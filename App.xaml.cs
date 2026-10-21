using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DragConvert;

public partial class App : Application
{
    private const string AppName = "DragConvert";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private OverlayWindow? _overlay;
    private InputHook? _hook;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "DragConvert.SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        _overlay = new OverlayWindow();
        _overlay.FilesDropped += OnFilesDropped;

        _hook = new InputHook();
        _hook.DragTriggered += (x, y) =>
            Dispatcher.BeginInvoke(new Action(() => _overlay.ShowAt(x, y)));
        _hook.Released += () =>
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(250); // beri waktu event Drop selesai diproses
                _overlay.HideMenu();
            }));

        // Klik kanan / scroll mouse saat menu muncul: ganti mode GAMBAR <-> DOKUMEN
        _hook.ModeSwitchRequested += () =>
            Dispatcher.BeginInvoke(new Action(() => _overlay?.ToggleMode()));

#if DEBUG
        // Mode dev: tekan Esc untuk menutup aplikasi (tidak aktif di build Release)
        _hook.EscPressed += () => Dispatcher.BeginInvoke(new Action(ExitApp));
#endif

        CreateTray();
    }

    private async void OnFilesDropped(string[] files, string format)
    {
        bool isDoc = Array.IndexOf(DocumentConverter.TargetFormats, format) >= 0;
        var result = await Task.Run(() => isDoc
            ? DocumentConverter.ConvertMany(files, format)
            : ImageConverter.ConvertMany(files, format));

        string msg = result.Converted > 0
            ? $"{result.Converted} file dikonversi ke {format}"
            : "Tidak ada file yang dikonversi";
        if (result.Skipped > 0) msg += $" ({result.Skipped} dilewati)";
        if (result.Failed > 0) msg += $"\nGagal: {result.Failed} - {result.LastError}";

        _tray?.ShowBalloonTip(3000, AppName, msg,
            result.Failed > 0 ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    private void CreateTray()
    {
        var autoStart = new Forms.ToolStripMenuItem("Mulai otomatis dengan Windows")
        {
            Checked = IsAutoStart(),
            CheckOnClick = true
        };
        autoStart.CheckedChanged += (_, _) => SetAutoStart(autoStart.Checked);

        var exit = new Forms.ToolStripMenuItem("Keluar");
        exit.Click += (_, _) => ExitApp();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(autoStart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        _tray = new Forms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "DragConvert - tahan Shift + drag file",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private static Drawing.Icon CreateIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 138, 61));
            g.FillEllipse(fill, 2, 2, 28, 28);
            using var pen = new Drawing.Pen(Drawing.Color.White, 3);
            g.DrawEllipse(pen, 9, 9, 14, 14);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static bool IsAutoStart()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enable) key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(AppName, false);
    }

    private void ExitApp()
    {
        _hook?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _mutex?.ReleaseMutex();
        Shutdown();
    }
}
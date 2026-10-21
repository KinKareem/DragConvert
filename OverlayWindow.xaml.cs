using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DragConvert;

public partial class OverlayWindow : Window
{
    private sealed record Mode(string Label, string[] Formats);

    // Urutan format searah jarum jam, mulai dari atas.
    private static readonly Mode[] Modes =
    {
        new("GAMBAR", new[] { "PNG", "JPG", "JPEG", "WEBP" }),
        new("DOKUMEN", new[] { "DOCX", "PDF", "TXT", "RTF" }),
    };

    private int _modeIndex; // mode terakhir diingat selama aplikasi berjalan

    private const double Cx = 110, Cy = 110, InnerR = 34, OuterR = 92, GapDeg = 3;
    private static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Display, Segoe UI");

    // Gaya "glass": gradasi putih transparan + garis tepi bercahaya
    private static Brush Glass(byte top, byte bottom) => Freeze(new LinearGradientBrush(
        Color.FromArgb(top, 255, 255, 255), Color.FromArgb(bottom, 255, 255, 255),
        new Point(0, 0), new Point(1, 1)));

    private static readonly Brush NormalBrush = Glass(0x48, 0x14);
    private static readonly Brush HoverBrush = Glass(0x96, 0x4A);
    private static readonly Brush EdgeBrush = Freeze(new LinearGradientBrush(
        Color.FromArgb(0xC0, 255, 255, 255), Color.FromArgb(0x24, 255, 255, 255),
        new Point(0, 0), new Point(1, 1)));
    private static readonly Brush HoverEdgeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 255, 255, 255)));

    public event Action<string[], string>? FilesDropped;

    public OverlayWindow()
    {
        InitializeComponent();
        BuildMenu();
        SourceInitialized += (_, _) =>
        {
            // Jangan ambil fokus dari Explorer & sembunyikan dari Alt+Tab
            var h = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    private static Point P(double r, double deg)
    {
        double rad = deg * Math.PI / 180.0;
        return new Point(Cx + r * Math.Cos(rad), Cy + r * Math.Sin(rad));
    }

    private void BuildMenu()
    {
        MenuCanvas.Children.Clear();
        var mode = Modes[_modeIndex];
        var formats = mode.Formats;

        var bg = new Ellipse
        {
            Width = 196,
            Height = 196,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0x70, 0x1C, 0x22, 0x30), Color.FromArgb(0x38, 0x10, 0x14, 0x1E),
                new Point(0, 0), new Point(1, 1)),
            Stroke = new LinearGradientBrush(
                Color.FromArgb(0x70, 255, 255, 255), Color.FromArgb(0x10, 255, 255, 255),
                new Point(0, 0), new Point(1, 1)),
            StrokeThickness = 1.2,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 2, Opacity = 0.35 }
        };
        Canvas.SetLeft(bg, 12);
        Canvas.SetTop(bg, 12);
        MenuCanvas.Children.Add(bg);

        double sweep = 360.0 / formats.Length;
        for (int i = 0; i < formats.Length; i++)
        {
            double start = -90 - sweep / 2 + i * sweep;
            double a0 = start + GapDeg / 2, a1 = start + sweep - GapDeg / 2;

            var fig = new PathFigure { StartPoint = P(OuterR, a0), IsClosed = true };
            fig.Segments.Add(new ArcSegment(P(OuterR, a1), new Size(OuterR, OuterR), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(P(InnerR, a1), true));
            fig.Segments.Add(new ArcSegment(P(InnerR, a0), new Size(InnerR, InnerR), 0, false, SweepDirection.Counterclockwise, true));

            string format = formats[i];
            var path = new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Fill = NormalBrush,
                Stroke = EdgeBrush,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                AllowDrop = true
            };
            path.DragEnter += (_, e) => { path.Fill = HoverBrush; path.Stroke = HoverEdgeBrush; SetEffect(e); };
            path.DragOver += (_, e) => SetEffect(e);
            path.DragLeave += (_, _) => { path.Fill = NormalBrush; path.Stroke = EdgeBrush; };
            path.Drop += (_, e) =>
            {
                path.Fill = NormalBrush;
                path.Stroke = EdgeBrush;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                    FilesDropped?.Invoke(files, format);
                e.Handled = true;
            };
            MenuCanvas.Children.Add(path);

            var mid = P((InnerR + OuterR) / 2, start + sweep / 2);
            var label = new TextBlock
            {
                Text = format,
                Width = 64,
                TextAlignment = TextAlignment.Center,
                FontFamily = UiFont,
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.7, Color = Colors.Black }
            };
            Canvas.SetLeft(label, mid.X - 32);
            Canvas.SetTop(label, mid.Y - 9);
            MenuCanvas.Children.Add(label);
        }

        // Lingkaran tengah: nama mode + indikator titik
        var center = new Ellipse
        {
            Width = (InnerR - 6) * 2,
            Height = (InnerR - 6) * 2,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0x80, 0x20, 0x26, 0x34), Color.FromArgb(0x50, 0x10, 0x14, 0x1E),
                new Point(0, 0), new Point(1, 1)),
            Stroke = EdgeBrush,
            StrokeThickness = 1.2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(center, Cx - (InnerR - 6));
        Canvas.SetTop(center, Cy - (InnerR - 6));
        MenuCanvas.Children.Add(center);

        var hint = new TextBlock
        {
            Text = mode.Label,
            Width = 52,
            TextAlignment = TextAlignment.Center,
            FontFamily = UiFont,
            Foreground = Brushes.White,
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black }
        };
        Canvas.SetLeft(hint, Cx - 26);
        Canvas.SetTop(hint, Cy - 10);
        MenuCanvas.Children.Add(hint);

        for (int i = 0; i < Modes.Length; i++)
        {
            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                IsHitTestVisible = false,
                Fill = new SolidColorBrush(Color.FromArgb(i == _modeIndex ? (byte)0xFF : (byte)0x55, 255, 255, 255))
            };
            Canvas.SetLeft(dot, Cx - (Modes.Length * 6 - 2) / 2.0 + i * 6);
            Canvas.SetTop(dot, Cy + 7);
            MenuCanvas.Children.Add(dot);
        }
    }

    /// <summary>Ganti mode GAMBAR &lt;-&gt; DOKUMEN.</summary>
    public void ToggleMode()
    {
        _modeIndex = (_modeIndex + 1) % Modes.Length;
        BuildMenu();

        var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
        };
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private static void SetEffect(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Tampilkan menu di tengah kursor. x,y dalam piksel fisik.</summary>
    public void ShowAt(int x, int y)
    {
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(Width * dpi.DpiScaleX);
        int h = (int)Math.Round(Height * dpi.DpiScaleY);

        var area = Forms.Screen.FromPoint(new Drawing.Point(x, y)).WorkingArea;
        int left = Math.Clamp(x - w / 2, area.Left, Math.Max(area.Left, area.Right - w));
        int top = Math.Clamp(y - h / 2, area.Top, Math.Max(area.Top, area.Bottom - h));

        Opacity = 0;
        Show();
        SetWindowPos(helper.Handle, new IntPtr(-1), left, top, w, h, SWP_NOACTIVATE);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    public void HideMenu()
    {
        if (IsVisible) Hide();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
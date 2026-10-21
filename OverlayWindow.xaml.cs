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
    // Urutan searah jarum jam, mulai dari atas. Tambah/kurangi format di sini.
    private static readonly string[] Formats = { "PNG", "JPG", "JPEG", "WEBP" };

    private const double Cx = 150, Cy = 150, InnerR = 48, OuterR = 130, GapDeg = 3;

    private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0x8A, 0x3D)));
    private static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB3, 0x47)));

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

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private static Point P(double r, double deg)
    {
        double rad = deg * Math.PI / 180.0;
        return new Point(Cx + r * Math.Cos(rad), Cy + r * Math.Sin(rad));
    }

    private void BuildMenu()
    {
        var bg = new Ellipse
        {
            Width = 276, Height = 276,
            Fill = new SolidColorBrush(Color.FromArgb(0xB3, 0x20, 0x20, 0x20)),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.5 }
        };
        Canvas.SetLeft(bg, 12);
        Canvas.SetTop(bg, 12);
        MenuCanvas.Children.Add(bg);

        double sweep = 360.0 / Formats.Length;
        for (int i = 0; i < Formats.Length; i++)
        {
            double start = -90 - sweep / 2 + i * sweep;
            double a0 = start + GapDeg / 2, a1 = start + sweep - GapDeg / 2;

            var fig = new PathFigure { StartPoint = P(OuterR, a0), IsClosed = true };
            fig.Segments.Add(new ArcSegment(P(OuterR, a1), new Size(OuterR, OuterR), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(P(InnerR, a1), true));
            fig.Segments.Add(new ArcSegment(P(InnerR, a0), new Size(InnerR, InnerR), 0, false, SweepDirection.Counterclockwise, true));

            string format = Formats[i];
            var path = new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Fill = NormalBrush,
                AllowDrop = true
            };
            path.DragEnter += (_, e) => { path.Fill = HoverBrush; SetEffect(e); };
            path.DragOver += (_, e) => SetEffect(e);
            path.DragLeave += (_, _) => path.Fill = NormalBrush;
            path.Drop += (_, e) =>
            {
                path.Fill = NormalBrush;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                    FilesDropped?.Invoke(files, format);
                e.Handled = true;
            };
            MenuCanvas.Children.Add(path);

            var mid = P((InnerR + OuterR) / 2, start + sweep / 2);
            var label = new TextBlock
            {
                Text = format, Width = 80, TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, mid.X - 40);
            Canvas.SetTop(label, mid.Y - 11);
            MenuCanvas.Children.Add(label);
        }

        var center = new Ellipse
        {
            Width = (InnerR - 6) * 2, Height = (InnerR - 6) * 2,
            Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(center, Cx - (InnerR - 6));
        Canvas.SetTop(center, Cy - (InnerR - 6));
        MenuCanvas.Children.Add(center);

        var hint = new TextBlock
        {
            Text = "DROP", Width = 60, TextAlignment = TextAlignment.Center,
            Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hint, Cx - 30);
        Canvas.SetTop(hint, Cy - 8);
        MenuCanvas.Children.Add(hint);
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

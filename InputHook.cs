using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DragConvert;

/// <summary>
/// Hook mouse global. Memicu event saat: tombol kiri ditahan + Shift ditahan + mouse bergeser,
/// dan klik awalnya terjadi di Explorer (jendela folder atau desktop).
/// </summary>
internal sealed class InputHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_ESCAPE = 0x1B;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int VK_SHIFT = 0x10;
    private const uint GA_ROOT = 2;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelProc _proc; // simpan referensi supaya tidak di-GC
    private readonly LowLevelProc _kbProc;
    private IntPtr _hook;
    private IntPtr _kbHook;
    private bool _pressed, _triggered;
    private POINT _start;

    public event Action<int, int>? DragTriggered;
    public event Action? Released;
    public event Action? EscPressed;

    public InputHook()
    {
        _proc = Callback;
        _kbProc = KeyboardCallback;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Gagal memasang mouse hook.");

        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam); // field pertama KBDLLHOOKSTRUCT
            if (vkCode == VK_ESCAPE) EscPressed?.Invoke();
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            switch (msg)
            {
                case WM_LBUTTONDOWN:
                    _pressed = true;
                    _triggered = false;
                    _start = info.pt;
                    break;

                case WM_MOUSEMOVE:
                    if (_pressed && !_triggered && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
                    {
                        int dx = info.pt.X - _start.X, dy = info.pt.Y - _start.Y;
                        if (dx * dx + dy * dy > 64 && IsExplorerAt(_start))
                        {
                            _triggered = true;
                            DragTriggered?.Invoke(info.pt.X, info.pt.Y);
                        }
                    }
                    break;

                case WM_LBUTTONUP:
                    if (_triggered) Released?.Invoke();
                    _pressed = false;
                    _triggered = false;
                    break;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsExplorerAt(POINT pt)
    {
        try
        {
            IntPtr h = WindowFromPoint(pt);
            if (h == IntPtr.Zero) return false;
            h = GetAncestor(h, GA_ROOT);
            GetWindowThreadProcessId(h, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (_kbHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string lpModuleName);
}
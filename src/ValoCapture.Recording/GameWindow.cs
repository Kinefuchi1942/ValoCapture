using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ValoCapture.Recording;

/// <param name="MonitorDeviceName">ウィンドウが表示されているモニター（\\.\DISPLAY1 など）。</param>
/// <param name="ClientRect">ゲーム画面（タイトルバーを除く）の範囲。モニター左上からの物理ピクセル。</param>
/// <param name="IsFullscreen">ゲーム画面がモニター全体を覆っているか。</param>
public sealed record GameWindowInfo(IntPtr Handle, string MonitorDeviceName, Rectangle ClientRect, bool IsFullscreen);

/// <summary>VALORANT のゲームウィンドウを探す。</summary>
public static partial class GameWindow
{
    private const string ProcessName = "VALORANT-Win64-Shipping";

    static GameWindow()
    {
        // 座標を物理ピクセルで扱うため（150% 表示などで値がずれないように）
        SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
    }

    /// <summary>ゲームウィンドウの情報。起動していない・最小化中なら null。</summary>
    public static GameWindowInfo? Find()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            using (p)
            {
                var hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero || IsIconic(hwnd)) continue;
                return Describe(hwnd);
            }
        }
        return null;
    }

    private static GameWindowInfo? Describe(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var client)) return Trace("GetClientRect");
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return Trace("ClientToScreen");

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(monitor, ref info)) return Trace("GetMonitorInfo");

        var mon = info.rcMonitor;
        // エンコーダーは偶数サイズが必要
        var rect = new Rectangle(
            origin.X - mon.Left,
            origin.Y - mon.Top,
            (client.Right - client.Left) & ~1,
            (client.Bottom - client.Top) & ~1);
        var fullscreen = rect.X <= 0 && rect.Y <= 0
            && rect.Right >= mon.Right - mon.Left - 1
            && rect.Bottom >= mon.Bottom - mon.Top - 1;

        return new GameWindowInfo(hwnd, info.szDevice, rect, fullscreen);
    }

    private static GameWindowInfo? Trace(string api)
    {
        Debug.WriteLine($"GameWindow: {api} failed (error {Marshal.GetLastPInvokeError()})");
        return null;
    }

    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
}

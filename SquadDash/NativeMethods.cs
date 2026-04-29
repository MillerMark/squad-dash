using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace SquadDash;

internal static class NativeMethods {
    private const int MONITOR_DEFAULTTONULL    = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW    = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref RECT rect, int dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    // DWMWA_WINDOW_CORNER_PREFERENCE = 33; DWMWCP_DONOTROUND = 1
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Opts the window out of Windows 11 DWM rounded corners.
    /// Must be called after the HWND is created (i.e., from SourceInitialized or later).
    /// No-op on Windows 10 where this attribute doesn't exist.
    /// </summary>
    public static void DisableRoundedCorners(nint hwnd) {
        try {
            int doNotRound = 1; // DWMWCP_DONOTROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref doNotRound, sizeof(int));
        }
        catch {
            // Silently ignore on older Windows where the attribute isn't supported.
        }
    }

    public static bool IsRectOnAnyMonitor(int left, int top, int right, int bottom) {
        var rect = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        return MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) != nint.Zero;
    }

    /// <summary>
    /// Returns the work area (excludes taskbar) for the monitor that contains
    /// the given physical-pixel point.  Falls back to the primary work area if
    /// the call fails.  Returned values are in physical pixels.
    /// </summary>
    public static Rect GetWorkAreaForPhysicalPoint(int x, int y) {
        var pt      = new POINT { x = x, y = y };
        var hMon    = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info    = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (hMon != nint.Zero && GetMonitorInfo(hMon, ref info)) {
            var wa = info.rcWork;
            return new Rect(wa.Left, wa.Top, wa.Right - wa.Left, wa.Bottom - wa.Top);
        }
        // Fallback: primary monitor work area (already in physical pixels at 96 dpi baseline)
        var primary = SystemParameters.WorkArea;
        return new Rect(primary.Left, primary.Top, primary.Width, primary.Height);
    }

    public static void AllowSetForegroundWindow(int processId) {
        if (processId <= 0)
            return;

        try {
            AllowSetForegroundWindow((uint)processId);
        }
        catch {
        }
    }

    public static bool TryActivateWindow(nint windowHandle) {
        if (windowHandle == nint.Zero)
            return false;

        try {
            ShowWindowAsync(windowHandle, IsIconic(windowHandle) ? SW_RESTORE : SW_SHOW);
            BringWindowToTop(windowHandle);
            return SetForegroundWindow(windowHandle);
        }
        catch {
            return false;
        }
    }

    public static bool TryActivateProcessMainWindow(int processId) {        if (processId <= 0)
            return false;

        try {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;

            try {
                process.WaitForInputIdle(250);
            }
            catch {
            }

            process.Refresh();
            var mainWindowHandle = process.MainWindowHandle;
            if (mainWindowHandle == nint.Zero)
                return false;

            AllowSetForegroundWindow(processId);
            return TryActivateWindow(mainWindowHandle);
        }
        catch {
            return false;
        }
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>
    /// WndProc hook that fixes the WindowStyle=None + WindowChrome maximize-over-taskbar bug.
    /// When WM_GETMINMAXINFO fires, constrain the maximized size to the work area of the
    /// monitor the window is on, so the taskbar is never covered.
    /// </summary>
    public static nint MaximizeWorkAreaHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
        if (msg != WM_GETMINMAXINFO)
            return nint.Zero;

        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (hMon == nint.Zero || !GetMonitorInfo(hMon, ref info))
            return nint.Zero;

        var wa = info.rcWork;
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition = new POINT { x = wa.Left, y = wa.Top };
        mmi.ptMaxSize     = new POINT { x = wa.Right - wa.Left, y = wa.Bottom - wa.Top };
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return nint.Zero;
    }
}

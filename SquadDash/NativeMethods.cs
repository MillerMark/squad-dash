using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SquadDash;

internal static class NativeMethods {
    private const int MONITOR_DEFAULTTONULL = 0;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref RECT rect, int dwFlags);

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

    public static bool TryActivateProcessMainWindow(int processId) {
        if (processId <= 0)
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
}

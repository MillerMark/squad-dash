using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

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

    /// <summary>
    /// Returns the actual on-screen bounds of <paramref name="window"/> in WPF logical (DIP) units.
    /// Unlike <see cref="Window.Left"/>/<see cref="Window.Top"/>, this is correct when the window
    /// is maximized — WPF's Left/Top return the restore position in that state.
    /// </summary>
    public static Rect GetActualWindowBoundsLogical(System.Windows.Window window) {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd != nint.Zero && GetWindowRect(hwnd, out RECT r)) {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            return new Rect(
                r.Left   / dpi.DpiScaleX,
                r.Top    / dpi.DpiScaleY,
                (r.Right  - r.Left) / dpi.DpiScaleX,
                (r.Bottom - r.Top)  / dpi.DpiScaleY);
        }
        return new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
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

    internal readonly record struct MaximizedWorkAreaBounds(int X, int Y, int Width, int Height);

    internal static MaximizedWorkAreaBounds ComputeMaximizedWorkAreaBounds(
        int monitorLeft,
        int monitorTop,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom) {
        return new MaximizedWorkAreaBounds(
            workLeft - monitorLeft,
            workTop - monitorTop,
            workRight - workLeft,
            workBottom - workTop);
    }

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
        var monitor = info.rcMonitor;
        var bounds = ComputeMaximizedWorkAreaBounds(
            monitor.Left,
            monitor.Top,
            wa.Left,
            wa.Top,
            wa.Right,
            wa.Bottom);

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition = new POINT { x = bounds.X, y = bounds.Y };
        mmi.ptMaxSize     = new POINT { x = bounds.Width, y = bounds.Height };
        Marshal.StructureToPtr(mmi, lParam, false);
        handled = true;
        return nint.Zero;
    }

    // ── GDI screen-capture ────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hDC, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hDC, nint hGdiObj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int w, int h,
                                      nint hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    /// <summary>
    /// Captures a region of the desktop (specified in physical/screen pixels) to a
    /// frozen WPF <see cref="BitmapSource"/>.  Unlike <c>RenderTargetBitmap</c> this
    /// captures everything visible on screen, including WPF <c>Popup</c> windows,
    /// tooltips, and other top-level HWNDs that are not part of the main window's
    /// visual tree.  Returns <c>null</c> if the capture fails or the region is empty.
    /// </summary>
    private static BitmapSource? CaptureScreenRegionPixels(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        const uint SRCCOPY = 0x00CC0020;

        var screenDC = GetDC(nint.Zero);
        if (screenDC == nint.Zero) return null;

        nint memDC  = nint.Zero;
        nint hBmp   = nint.Zero;
        nint oldBmp = nint.Zero;
        try
        {
            memDC  = CreateCompatibleDC(screenDC);
            hBmp   = CreateCompatibleBitmap(screenDC, width, height);
            oldBmp = SelectObject(memDC, hBmp);

            BitBlt(memDC, 0, 0, width, height, screenDC, x, y, SRCCOPY);

            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBmp,
                nint.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            if (oldBmp != nint.Zero && memDC != nint.Zero) SelectObject(memDC, oldBmp);
            if (hBmp   != nint.Zero) DeleteObject(hBmp);
            if (memDC  != nint.Zero) DeleteDC(memDC);
            ReleaseDC(nint.Zero, screenDC);
        }
    }

    /// <summary>
    /// Captures the area of <paramref name="window"/> (or a sub-region of it defined by
    /// <paramref name="selectionInDips"/>) from the live screen, including any floating
    /// WPF popups, tooltips, or other windows drawn on top.
    /// </summary>
    /// <param name="window">The WPF window whose screen position determines the base origin.</param>
    /// <param name="selectionInDips">
    /// Optional sub-region in WPF logical units (DIPs) relative to the window's client area.
    /// Pass <c>null</c> to capture the full window.
    /// </param>
    /// <returns>A frozen <see cref="BitmapSource"/>, or <c>null</c> on failure.</returns>
    public static BitmapSource? CaptureWindowRegion(Window window, Rect? selectionInDips = null)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero) return null;
        if (!GetWindowRect(hwnd, out RECT win)) return null;

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);

        int x, y, w, h;
        if (selectionInDips is { } sel && sel.Width > 0 && sel.Height > 0)
        {
            var cx = (int)Math.Round(sel.Left   * dpi.DpiScaleX);
            var cy = (int)Math.Round(sel.Top    * dpi.DpiScaleY);
            var cw = (int)Math.Round(sel.Width  * dpi.DpiScaleX);
            var ch = (int)Math.Round(sel.Height * dpi.DpiScaleY);
            var maxW = win.Right  - win.Left;
            var maxH = win.Bottom - win.Top;
            cx = Math.Max(0, cx);
            cy = Math.Max(0, cy);
            cw = Math.Max(1, Math.Min(cw, maxW - cx));
            ch = Math.Max(1, Math.Min(ch, maxH - cy));
            x  = win.Left + cx;
            y  = win.Top  + cy;
            w  = cw;
            h  = ch;
        }
        else
        {
            x = win.Left;
            y = win.Top;
            w = win.Right  - win.Left;
            h = win.Bottom - win.Top;
        }

        return CaptureScreenRegionPixels(x, y, w, h);
    }
}

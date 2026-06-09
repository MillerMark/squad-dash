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
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip,
        MonitorEnumProc lpfnEnum, nint dwData);

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor,
        ref RECT lprcMonitor, nint dwData);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static int GetCurrentNativeThreadId() =>
        unchecked((int)GetCurrentThreadId());

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
    /// the given window handle.  Falls back to the primary work area if the
    /// call fails.  Returned values are in physical pixels.
    /// </summary>
    public static Rect GetWorkAreaForWindow(nint hwnd) {
        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (hMon != nint.Zero && GetMonitorInfo(hMon, ref info)) {
            var wa = info.rcWork;
            return new Rect(wa.Left, wa.Top, wa.Right - wa.Left, wa.Bottom - wa.Top);
        }
        var primary = SystemParameters.WorkArea;
        return new Rect(primary.Left, primary.Top, primary.Width, primary.Height);
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

    /// <summary>
    /// Returns the full monitor rect (not work area) in physical pixels for the monitor
    /// containing the given physical-pixel point. Returns <c>Rect.Empty</c> on failure.
    /// </summary>
    public static Rect GetMonitorBoundsForPhysicalPoint(int x, int y)
    {
        var pt   = new POINT { x = x, y = y };
        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (hMon != nint.Zero && GetMonitorInfo(hMon, ref info))
        {
            var r = info.rcMonitor;
            return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        return Rect.Empty;
    }

    /// <summary>
    /// Enumerates monitors and returns the full monitor rect of the one whose physical-pixel
    /// bounds match the given values exactly. Returns <c>Rect.Empty</c> if none match.
    /// </summary>
    public static Rect FindMonitorByBounds(int left, int top, int width, int height)
    {
        Rect result = Rect.Empty;
        // Use a local variable to prevent the delegate from being GC'd during P/Invoke.
        MonitorEnumProc callback = (nint hMon, nint _, ref RECT _2, nint _3) =>
        {
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref info))
            {
                var r = info.rcMonitor;
                if (r.Left == left && r.Top == top &&
                    r.Right - r.Left == width && r.Bottom - r.Top == height)
                {
                    result = new Rect(r.Left, r.Top, width, height);
                    return false; // stop enumeration
                }
            }
            return true; // continue
        };
        EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        return result;
    }

    /// <summary>
    /// Returns <c>true</c> when the window's physical height is measurably less
    /// than the monitor work area height — indicating the window is in a constrained
    /// layout (Windows 11 Snap zone, top/bottom split, quadrant grid, etc.).
    /// <para>
    /// This geometry-only approach is used instead of checking <c>WS_MAXIMIZE</c>
    /// because <c>WindowStyle=None + WindowChrome</c> windows do not receive
    /// <c>WS_MAXIMIZE</c> reliably from the snap system, and dragging the divider
    /// between two snapped windows clears <c>WS_MAXIMIZE</c> even though the window
    /// remains in a partial-screen position.
    /// </para>
    /// <para>
    /// The threshold is 90 % of work-area height.  This catches half-height snap
    /// zones (≈ 50 %) and quadrant zones (≈ 50 %) while leaving full-height left/
    /// right snaps (≈ 100 %) unaffected — those windows are already at full height
    /// so the no-change path produces the same result.
    /// </para>
    /// </summary>
    /// <param name="hwnd">The window's HWND.</param>
    /// <param name="physWa">Physical-pixel work area for the window's monitor
    /// (from <see cref="GetWorkAreaForWindow"/>).</param>
    public static bool IsHeightConstrained(nint hwnd, Rect physWa)
    {
        if (hwnd == nint.Zero) return false;
        if (!GetWindowRect(hwnd, out RECT win)) return false;
        int physWindowH = win.Bottom - win.Top;
        return physWindowH < physWa.Height * 0.90;
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

    /// <summary>
    /// Returns the screen-pixel rect of the HWND that hosts <paramref name="visual"/>.
    /// Useful for getting the bounds of a ToolTip popup whose PresentationSource is
    /// a separate Popup HWND.  Returns null if the visual isn't connected to an HwndSource.
    /// </summary>
    public static Rect? TryGetVisualHwndScreenRect(System.Windows.Media.Visual visual)
    {
        try
        {
            var src = System.Windows.PresentationSource.FromVisual(visual)
                      as System.Windows.Interop.HwndSource;
            if (src is null || src.IsDisposed) return null;
            if (!GetWindowRect(src.Handle, out RECT r)) return null;
            return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        catch { return null; }
    }

    /// <summary>Returns the current Win32 cursor position in screen pixels.</summary>
    public static Point GetCursorScreenPos()
    {
        try
        {
            if (GetCursorPos(out POINT pt)) return new Point(pt.x, pt.y);
        }
        catch { }
        return default;
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

    // ── Keyboard state polling ────────────────────────────────────────────────

    // Virtual-key codes for left/right Ctrl
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT   = 0xA0;
    private const int VK_RSHIFT   = 0xA1;
    private const int VK_LMENU    = 0xA4;
    private const int VK_RMENU    = 0xA5;
    private const int VK_LWIN     = 0x5B;
    private const int VK_RWIN     = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Returns <c>true</c> if either Ctrl key is physically held down right now,
    /// regardless of which window has focus.
    /// </summary>
    public static bool IsCtrlPhysicallyDown()
        => ((GetAsyncKeyState(VK_LCONTROL) | GetAsyncKeyState(VK_RCONTROL)) & 0x8000) != 0;

    public static System.Windows.Input.ModifierKeys GetPhysicalModifierKeys()
    {
        var modifiers = System.Windows.Input.ModifierKeys.None;
        if (IsVirtualKeyDown(VK_LCONTROL) || IsVirtualKeyDown(VK_RCONTROL))
            modifiers |= System.Windows.Input.ModifierKeys.Control;
        if (IsVirtualKeyDown(VK_LSHIFT) || IsVirtualKeyDown(VK_RSHIFT))
            modifiers |= System.Windows.Input.ModifierKeys.Shift;
        if (IsVirtualKeyDown(VK_LMENU) || IsVirtualKeyDown(VK_RMENU))
            modifiers |= System.Windows.Input.ModifierKeys.Alt;
        if (IsVirtualKeyDown(VK_LWIN) || IsVirtualKeyDown(VK_RWIN))
            modifiers |= System.Windows.Input.ModifierKeys.Windows;
        return modifiers;
    }

    private static bool IsVirtualKeyDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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

        // Label the captured bitmap with the actual physical DPI of the monitor so
        // WPF renders it at the correct logical DIP size with 1:1 pixel mapping.
        // The physical pixel content is preserved 1:1 — no resampling occurs.
        var raw = CaptureScreenRegionPixels(x, y, w, h);
        return raw is null ? null : DpiHelper.SetPhysicalDpi(raw, 96.0 * dpi.DpiScaleX, 96.0 * dpi.DpiScaleY);
    }
}

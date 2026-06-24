using System;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace SquadDash;
public class WindowHelper {
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EX_STYLE = -20;
    private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;


    public static bool IsForegroundWindow(Window window) {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        return GetForegroundWindow() == hwnd;
    }

    public static void HideFromAltTab(Window window) {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, GWL_EX_STYLE, (GetWindowLong(hwnd, GWL_EX_STYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }
}
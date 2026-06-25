using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SquadDash.GuidedTours;

namespace SquadDash;

/// <summary>
/// Floating navigator window that shows the current step header and
/// Prev / Next / Close Tour controls during a guided tour.
/// </summary>
internal sealed class FrmGuidedTourNavigator : ChromedWindow
{
    private const string PositionKey = "GuidedTourNavigatorPosition";

    private readonly TextBlock _headerLabel;
    private readonly Button    _prevButton;
    private readonly Button    _nextButton;
    private readonly Button    _editStepButton;

    /// <summary>Fired when the user clicks "← Prev" or presses F2.</summary>
    public event EventHandler? PrevRequested;
    /// <summary>Fired when the user clicks "Next →" or presses F3.</summary>
    public event EventHandler? NextRequested;
    /// <summary>Fired when the user clicks "✕ Close Tour".</summary>
    public event EventHandler? CloseRequested;
    /// <summary>Fired when the user clicks "✎ Edit Step" (developer mode only).</summary>
    public event EventHandler? EditStepRequested;

    public FrmGuidedTourNavigator()
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        Title         = "Guided Tour";
        Width         = 300;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        Topmost       = true;

        // ── Content area ──────────────────────────────────────────────────────
        var contentArea = ApplyOuterBorder("AppSurface", "Guided Tour");

        _headerLabel = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(12, 10, 12, 8),
            FontWeight   = FontWeights.SemiBold,
        };
        _headerLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        _headerLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        _prevButton = MakeButton("← Prev");
        _prevButton.Click += (_, _) => PrevRequested?.Invoke(this, EventArgs.Empty);

        _nextButton = MakeButton("Next →");
        _nextButton.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);

        var closeButton = MakeButton("✕ Close Tour");
        closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        _editStepButton = MakeButton("✎ Edit Step");
        _editStepButton.Click      += (_, _) => EditStepRequested?.Invoke(this, EventArgs.Empty);
        _editStepButton.Visibility  = Visibility.Collapsed;

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(8, 0, 8, 10),
        };
        buttonRow.Children.Add(_prevButton);
        buttonRow.Children.Add(_nextButton);
        buttonRow.Children.Add(closeButton);
        buttonRow.Children.Add(_editStepButton);

        var stack = new StackPanel();
        stack.Children.Add(_headerLabel);
        stack.Children.Add(buttonRow);

        contentArea.Child = stack;

        // ── Position restore ─────────────────────────────────────────────────
        WindowStartupLocation = WindowStartupLocation.Manual;
        Loaded  += OnLoaded;
        Closing += OnClosing;

        // ── Keyboard shortcuts ────────────────────────────────────────────────
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shows or hides the "✎ Edit Step" button.
    /// Set to <c>true</c> only when the app is in developer mode.
    /// </summary>
    public bool IsEditModeVisible
    {
        get => _editStepButton.Visibility == Visibility.Visible;
        set => _editStepButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Updates the header to "Step N of N: {title}".</summary>
    public void UpdateStep(int stepIndex, int totalSteps, string title)
    {
        _headerLabel.Text = $"Step {stepIndex + 1} of {totalSteps}: {title}";
        _prevButton.IsEnabled = stepIndex > 0;
        _nextButton.IsEnabled = stepIndex < totalSteps - 1;
    }

    // ── Position save / restore ──────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (FloatingWindowPositionStore.Shared.TryRestore(PositionKey, this))
        {
            // Validate the restored position is fully on-screen
            if (!IsFullyOnScreen())
                PlaceAtDefaultPosition();
        }
        else
        {
            PlaceAtDefaultPosition();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        FloatingWindowPositionStore.Shared.Save(PositionKey, this);
    }

    private void PlaceAtDefaultPosition()
    {
        const double margin = 20;
        var owner  = Owner ?? Application.Current.MainWindow;
        var source = PresentationSource.FromVisual(owner);

        if (source?.CompositionTarget == null)
        {
            // Fallback: owner.Left/Top are already WPF logical units
            Left = owner.Left + owner.ActualWidth  - Width  - margin;
            Top  = owner.Top  + owner.ActualHeight - Height - margin;
            WindowStartupLocation = WindowStartupLocation.Manual;
            return;
        }

        var hwnd     = new WindowInteropHelper(owner).Handle;
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi       = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        // TransformFromDevice converts device (physical) pixels → WPF logical units,
        // correctly handling negative coordinates and per-monitor DPI.
        var transform   = source.CompositionTarget.TransformFromDevice;
        var bottomRight = transform.Transform(new System.Windows.Point(mi.rcWork.right, mi.rcWork.bottom));

        Left = bottomRight.X - Width  - margin;
        Top  = bottomRight.Y - Height - margin;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private bool IsFullyOnScreen()
    {
        var owner  = Owner ?? Application.Current.MainWindow;
        var source = PresentationSource.FromVisual(owner);
        if (source?.CompositionTarget == null) return false;

        var transform = source.CompositionTarget.TransformFromDevice;

        foreach (var screen in GetAllMonitorWorkAreas())
        {
            var topLeft     = transform.Transform(new System.Windows.Point(screen.left,  screen.top));
            var bottomRight = transform.Transform(new System.Windows.Point(screen.right, screen.bottom));

            if (Left >= topLeft.X     && Top >= topLeft.Y &&
                Left + Width  <= bottomRight.X &&
                Top  + Height <= bottomRight.Y)
                return true;
        }
        return false;
    }

    // ── Win32 multi-monitor helpers ──────────────────────────────────────────

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    private static System.Collections.Generic.List<RECT> GetAllMonitorWorkAreas()
    {
        var areas = new System.Collections.Generic.List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, ref _, _) =>
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
                areas.Add(mi.rcWork);
            return true;
        }, IntPtr.Zero);
        return areas;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { PrevRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        if (e.Key == Key.F3) { NextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static Button MakeButton(string content)
    {
        var btn = new Button
        {
            Content = content,
            Height  = 26,
            Margin  = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(8, 2, 8, 2),
        };
        btn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        return btn;
    }
}

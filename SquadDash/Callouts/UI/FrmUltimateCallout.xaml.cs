using System;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;


namespace SquadDash;
/// <summary>
/// Interaction logic for FrmUltimateCallout.xaml
/// </summary>
public partial class FrmUltimateCallout : Window, ICalloutWindow {
    public event EventHandler? RefreshTargetRect;
    public event EventHandler? AngleChanged;
    DispatcherTimer? waitingForMouseUpTimer;
    DispatcherTimer? calloutAnimationTimer;
    private const double indicatorMargin = 10d;
    SolidColorBrush calloutStrokeBrush = null!;
    SolidColorBrush calloutFillBrush = null!;
    System.Windows.Shapes.Path? _mainCalloutPath;
    System.Windows.Shapes.Path? _tourGlowPath;
    DropShadowEffect? _tourGlowEffect;

    static int _sessionTourHintAdvanceCount;

    /// <summary>Records a tour advance for session-level tracking (no-op when hints are disabled).</summary>
    public static void RecordTourAdvance() => _sessionTourHintAdvanceCount++;


    double idealCalloutWidth;

    CalloutTheme theme = CalloutTheme.Light;
    public CalloutTheme Theme {
        get => theme;
        set {
            if (theme == value)
                return;
            theme = value;
            LoadColorsForTheme();
        }
    }

    bool showDiagnostics;
    public bool ShowDiagnostics {
        get {
            return showDiagnostics;
        }
        set {
            if (showDiagnostics == value)
                return;
            showDiagnostics = value;
            RefreshLayout();
        }
    }

    /// <summary>
    /// When <c>true</c>, the callout ignores the global "close on activity" sweep and can
    /// only be dismissed by clicking its own × close button.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool IsSticky { get; set; }

    // ── Global registry (for auto-close sweep) ──────────────────────────────────
    private static readonly List<WeakReference<FrmUltimateCallout>> _openCallouts = new();
    private static readonly List<WeakReference<ContextMenu>> _openContextMenus = new();
    private static bool _contextMenuDragProtectionHooksInstalled;
    private static bool _protectingContextMenusForCalloutDrag;
    private static bool _contextMenuProtectedDragInProgress;
    private static WeakReference<FrmUltimateCallout>? _contextMenuProtectedDragCallout;
    private static readonly Dictionary<ContextMenu, bool> _protectedContextMenuOriginalStaysOpen = new();

    private static void EnsureContextMenuDragProtectionHooks()
    {
        if (_contextMenuDragProtectionHooksInstalled)
            return;

        _contextMenuDragProtectionHooksInstalled = true;
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnAnyContextMenuOpened),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.ClosedEvent,
            new RoutedEventHandler(OnAnyContextMenuClosed),
            handledEventsToo: true);
        InputManager.Current.PreProcessInput += OnPreProcessInputForContextMenuDragProtection;
    }

    private static void OnAnyContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        _openContextMenus.RemoveAll(r => !r.TryGetTarget(out _));
        if (!_openContextMenus.Any(r => r.TryGetTarget(out var existing) && ReferenceEquals(existing, menu)))
            _openContextMenus.Add(new WeakReference<ContextMenu>(menu));
    }

    private static void OnAnyContextMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            _protectedContextMenuOriginalStaysOpen.Remove(menu);

        _openContextMenus.RemoveAll(r => !r.TryGetTarget(out var existing)
            || !existing.IsOpen
            || ReferenceEquals(existing, sender));
    }

    private static void OnPreProcessInputForContextMenuDragProtection(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not MouseButtonEventArgs mouseArgs
            || mouseArgs.ChangedButton != MouseButton.Left)
            return;

        if (mouseArgs.RoutedEvent != Mouse.PreviewMouseDownEvent
            && mouseArgs.RoutedEvent != Mouse.MouseDownEvent
            && mouseArgs.RoutedEvent != Mouse.PreviewMouseDownOutsideCapturedElementEvent
            && mouseArgs.RoutedEvent != Mouse.PreviewMouseUpOutsideCapturedElementEvent
            && mouseArgs.RoutedEvent != Mouse.PreviewMouseUpEvent
            && mouseArgs.RoutedEvent != Mouse.MouseUpEvent
            && mouseArgs.RoutedEvent != UIElement.PreviewMouseLeftButtonUpEvent
            && mouseArgs.RoutedEvent != UIElement.MouseLeftButtonUpEvent
            && mouseArgs.RoutedEvent != UIElement.PreviewMouseLeftButtonDownEvent
            && mouseArgs.RoutedEvent != UIElement.MouseLeftButtonDownEvent)
            return;

        if (mouseArgs.ButtonState == MouseButtonState.Released)
        {
            if (!_protectingContextMenusForCalloutDrag)
                return;

            if (_contextMenuProtectedDragInProgress
                && _contextMenuProtectedDragCallout?.TryGetTarget(out var activeCallout) == true)
                activeCallout.EndRawMouseDrag();

            _contextMenuProtectedDragInProgress = false;
            _contextMenuProtectedDragCallout = null;

            mouseArgs.Handled = true;
            return;
        }

        if (mouseArgs.ButtonState != MouseButtonState.Pressed)
            return;

        if (!TryGetCalloutUnderCursor(out var callout))
        {
            RestoreContextMenusAfterCalloutDrag();
            return;
        }

        if (!callout.IsCursorOverDraggableCalloutSurface())
        {
            RestoreContextMenusAfterCalloutDrag();
            return;
        }

        ProtectOpenContextMenusForCalloutDrag();
        ProtectCapturedMenuForCalloutDragIfNeeded();
        callout.StartRawMouseDrag();
        mouseArgs.Handled = true;
    }

    private static bool TryGetCalloutUnderCursor(out FrmUltimateCallout callout)
    {
        callout = null!;
        _openCallouts.RemoveAll(r => !r.TryGetTarget(out var c) || !c.IsVisible);

        var cursor = NativeMethods.GetCursorScreenPos();
        for (int i = _openCallouts.Count - 1; i >= 0; i--)
        {
            if (!_openCallouts[i].TryGetTarget(out var c) || !c.IsVisible)
                continue;

            var local = c.PointFromScreen(cursor);
            if (local.X < 0 || local.Y < 0 || local.X > c.ActualWidth || local.Y > c.ActualHeight)
                continue;

            callout = c;
            return true;
        }

        return false;
    }

    private static void ProtectOpenContextMenusForCalloutDrag()
    {
        _openContextMenus.RemoveAll(r => !r.TryGetTarget(out var menu) || !menu.IsOpen);
        if (_openContextMenus.Count == 0)
            return;

        _protectingContextMenusForCalloutDrag = true;
        foreach (var weakMenu in _openContextMenus)
        {
            if (!weakMenu.TryGetTarget(out var menu) || !menu.IsOpen)
                continue;

            if (!_protectedContextMenuOriginalStaysOpen.ContainsKey(menu))
                _protectedContextMenuOriginalStaysOpen[menu] = menu.StaysOpen;

            if (!menu.StaysOpen)
                menu.StaysOpen = true;
        }
    }

    private static void ProtectCapturedMenuForCalloutDragIfNeeded()
    {
        if (_protectingContextMenusForCalloutDrag)
            return;

        if (!IsMouseCapturedByMenu())
            return;

        _protectingContextMenusForCalloutDrag = true;
    }

    private static void RestoreContextMenusAfterCalloutDrag()
    {
        if (!_protectingContextMenusForCalloutDrag)
            return;

        _protectingContextMenusForCalloutDrag = false;
        _contextMenuProtectedDragInProgress = false;
        _contextMenuProtectedDragCallout = null;
        foreach (var (menu, originalStaysOpen) in _protectedContextMenuOriginalStaysOpen.ToArray())
        {
            if (menu.IsOpen)
                menu.StaysOpen = originalStaysOpen;
        }

        _protectedContextMenuOriginalStaysOpen.Clear();
        _openContextMenus.RemoveAll(r => !r.TryGetTarget(out var menu) || !menu.IsOpen);
    }

    private static bool IsMouseCapturedByMenu()
    {
        if (Mouse.Captured is not DependencyObject captured)
            return false;

        for (DependencyObject? node = captured; node is not null; node = GetDependencyParent(node))
        {
            if (node is System.Windows.Controls.Primitives.MenuBase or MenuItem)
                return true;
        }

        return false;
    }

    /// <summary>Called from FinalizeAndShow to register this instance for auto-close sweeping.</summary>
    private static void RegisterCallout(FrmUltimateCallout callout)
    {
        EnsureContextMenuDragProtectionHooks();
        // Clean up dead references while we're here.
        _openCallouts.RemoveAll(r => !r.TryGetTarget(out _));
        _openCallouts.Add(new WeakReference<FrmUltimateCallout>(callout));
    }

    /// <summary>
    /// Closes all open non-sticky callouts. Called when user clicks or types in the main window.
    /// </summary>
    public static void CloseAllNonSticky()
    {
        var toClose = new List<FrmUltimateCallout>();
        foreach (var r in _openCallouts)
            if (r.TryGetTarget(out var c) && !c.IsSticky && c.IsVisible)
                toClose.Add(c);
        foreach (var c in toClose)
            c.Close();
        _openCallouts.RemoveAll(r => !r.TryGetTarget(out _));
    }

    /// <summary>
    /// Re-fetches theme brushes on all open callouts after a tint or theme change.
    /// Call from MainWindow.NotifyTintChanged().
    /// </summary>
    public static void NotifyTintChanged()
    {
        _openCallouts.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var r in _openCallouts)
            if (r.TryGetTarget(out var c) && c.IsVisible)
                c.LoadColorsForTheme();
    }

    /// <summary>
    /// Updates the Theme property and refreshes colors/styles on all open callouts
    /// after a light/dark theme switch. Call from MainWindow.ApplyTheme().
    /// </summary>
    public static void NotifyThemeChanged(bool isDark)
    {
        var newTheme = isDark ? CalloutTheme.Dark : CalloutTheme.Light;
        _openCallouts.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var r in _openCallouts)
        {
            if (!r.TryGetTarget(out var c) || !c.IsVisible) continue;
            // Setting Theme triggers LoadColorsForTheme via the property setter.
            // If theme hasn't changed we force a manual reload anyway to pick up
            // any new tint values that were applied as part of the theme swap.
            if (c.theme != newTheme)
                c.Theme = newTheme;
            else
                c.LoadColorsForTheme();
        }
    }

    public Color GlowColor {
        get => glowColor;
        set {
            if (glowColor == value)
                return;
            glowColor = value;
            glowHsl = new HueSatLight(glowColor);
            LoadColorsForTheme();
            RefreshLayout();
        }
    }

    SolidColorBrush GetBrushFromGlow(double saturation, double lightness) {
        var hueSatLight = new HueSatLight() { Hue = glowHsl.Hue, Saturation = saturation / 255.0, Lightness = lightness / 255.0 };
        return new SolidColorBrush(hueSatLight.AsRGB);
    }

    void InitializeColors() {
        calloutFillBrush   = ((Application.Current.Resources["CalloutBackground"] as SolidColorBrush) ?? new SolidColorBrush(Colors.White)).Clone();
        calloutStrokeBrush = ((Application.Current.Resources["CalloutBorder"]     as SolidColorBrush) ?? new SolidColorBrush(Colors.Gray)).Clone();
    }

    void LoadColorsForTheme() {
        InitializeColors();
        RefreshLayout();
    }

    protected virtual void OnRefreshTargetRect() {
        if (frameworkElementTarget == null)  // Only fire the event when we are *not* targeting a FrameworkElement.
            RefreshTargetRect?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshLayout() {
        InvalidateLayout();
        if (initializationComplete)
            LayoutEverything();
    }

    protected virtual void OnAngleChanged(object sender, EventArgs e) {
        AngleChanged?.Invoke(sender, e);
    }

    public CalloutOptions Options { get; set; } = new CalloutOptions();
    public FrmUltimateCallout() {
        InitializeComponent();
        InitializeColors();
        // Prevent clicks on the callout from activating this window.
        SourceInitialized += (_, _) =>
        {
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc_NoActivate);
        };
    }

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const IntPtr MA_NOACTIVATE = (nint)3;
    private HwndSource? _hwndSource;

    private IntPtr WndProc_NoActivate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }


    void InvalidateLayout() {
        layoutValid = false;
    }

    const double closeButtonEdgeSize = 22d;

    void PlaceCloseButton() {
        var closeButton = new Button();
        closeButton.SetResourceReference(Button.StyleProperty,      "PanelCloseButtonStyle");
        closeButton.SetResourceReference(Button.ForegroundProperty, "CalloutText");
        // Shadow CaptionButtonHover and HoverSurface locally so PanelCloseButtonStyle's
        // IsMouseOver and IsPressed triggers both use CalloutButtonHover.
        closeButton.SetResourceReference(Button.TagProperty, "CalloutButtonHover"); // forces resource tracking
        if (Application.Current.TryFindResource("CalloutButtonHover") is System.Windows.Media.Brush hoverBrush) {
            closeButton.Resources["CaptionButtonHover"] = hoverBrush;
            closeButton.Resources["HoverSurface"]       = hoverBrush;
        }
        closeButton.Content = "×";
        closeButton.FontSize = 14;
        closeButton.Width  = closeButtonEdgeSize;
        closeButton.Height = closeButtonEdgeSize;
        closeButton.Click += CloseButton_Click;
        _closeButton = closeButton;
        cvsCallout.Children.Add(closeButton);
        double rightEdge = calloutLeft + calloutWidth;
        Canvas.SetLeft(closeButton, rightEdge - Options.CornerRadius - closeButton.Width + 3);
        Canvas.SetTop(closeButton, calloutTop + Options.CornerRadius - 3);
    }

    double GetMinHeight() {
        return 2 * Options.CornerRadius + closeButtonEdgeSize;
    }

    /// <summary>
    /// Raised when the user explicitly clicks the × close button (as opposed to an auto-close sweep).
    /// Subscribe to record a "dismissed" state for hint tracking.
    /// </summary>
    public event EventHandler? UserDismissed;

    // ── Tour Mode ────────────────────────────────────────────────────────────────
    bool _isTourMode;
    TourCalloutNavigationOverlay? _tourOverlay;
    Button? _closeButton;
    bool _dragInProgress;
    CalloutSide _lastDangleSide = CalloutSide.Bottom;
    bool _isDangleActive;
    Func<int>? _tourNavAdvanceCountProvider;
    Action? _tourNavAdvanceRecorder;

    /// <summary>Fired when the user presses Enter while the callout is focused in tour mode.</summary>
    public event EventHandler? TourNextRequested;

    /// <summary>Fired when the tour overlay Prev button is clicked.</summary>
    public event EventHandler? TourPrevRequested;

    /// <summary>Fired when the user clicks the pencil edit button in the tour overlay (developer mode only).</summary>
    public event EventHandler? TourEditRequested;

    /// <summary>Fired when the user Alt+clicks the pencil button in the tour overlay (developer mode only).</summary>
    public event EventHandler? TourNewStepAfterRequested;

    /// <summary>Fired when the user Ctrl+clicks the pencil button in the tour overlay (developer mode only).</summary>
    public event EventHandler? TourNewStepBeforeRequested;

    /// <summary>Fired when the user clicks the delete (trash) button in the tour overlay (developer mode only).</summary>
    public event EventHandler? TourDeleteRequested;

    /// <summary>Fired when the callout animation finishes and the window has settled at its target position.</summary>
    public event EventHandler? Settled;

    /// <summary>Fired once per drag gesture when the user starts dragging the callout.</summary>
    public event EventHandler? DragStarted;

    public Func<int>? TourNavAdvanceCountProvider
    {
        get => _tourNavAdvanceCountProvider;
        set
        {
            _tourNavAdvanceCountProvider = value;
            ConfigureTourOverlayNextLabelState();
        }
    }

    public Action? TourNavAdvanceRecorder
    {
        get => _tourNavAdvanceRecorder;
        set
        {
            _tourNavAdvanceRecorder = value;
            ConfigureTourOverlayNextLabelState();
        }
    }

    /// <summary>Starts the reading-time glow nudge on the Next navigation button.</summary>
    public void StartNextButtonGlow() => _tourOverlay?.StartNextButtonGlow();

    /// <summary>Stops the reading-time glow nudge on the Next navigation button.</summary>
    public void StopNextButtonGlow() => _tourOverlay?.StopNextButtonGlow();

    /// <summary>
    /// When <c>true</c>, the callout is part of a guided-tour step.  A hint line is shown inside
    /// the callout and a floating Prev/Next overlay appears beside it after the callout settles.
    /// </summary>
    public bool IsTourMode
    {
        get => _isTourMode;
        set
        {
            if (_isTourMode == value) return;
            _isTourMode = value;
            if (_isTourMode) OnTourModeEnabled();
            else             OnTourModeDisabled();
        }
    }

    private bool _isTourFirstStep;

    public bool IsTourFirstStep
    {
        get => _isTourFirstStep;
        set
        {
            _isTourFirstStep = value;
            if (_tourOverlay is not null)
                _tourOverlay.IsFirstStep = value;
        }
    }

    private bool _isTourEditModeVisible;

    public bool IsTourEditModeVisible
    {
        get => _isTourEditModeVisible;
        set
        {
            _isTourEditModeVisible = value;
            if (_tourOverlay is not null)
                _tourOverlay.IsDevModeVisible = value;
        }
    }

    void OnTourModeEnabled()
    {
        this.KeyDown += Callout_KeyDown;
        Settled      += OnSettled_TourOverlay;
        DragStarted  += OnDragStarted_TourOverlay;

        _tourOverlay = new TourCalloutNavigationOverlay();
        ConfigureTourOverlayNextLabelState();
        _tourOverlay.IsFirstStep = _isTourFirstStep;
        _tourOverlay.IsDevModeVisible = _isTourEditModeVisible;
        _tourOverlay.NextClicked           += (_, _) => TourNextRequested?.Invoke(this, EventArgs.Empty);
        _tourOverlay.PrevClicked           += (_, _) => TourPrevRequested?.Invoke(this, EventArgs.Empty);
        _tourOverlay.EditClicked           += (_, _) => TourEditRequested?.Invoke(this, EventArgs.Empty);
        _tourOverlay.NewStepAfterClicked   += (_, _) => TourNewStepAfterRequested?.Invoke(this, EventArgs.Empty);
        _tourOverlay.NewStepBeforeClicked  += (_, _) => TourNewStepBeforeRequested?.Invoke(this, EventArgs.Empty);
        _tourOverlay.DeleteClicked         += (_, _) => TourDeleteRequested?.Invoke(this, EventArgs.Empty);

        if (_closeButton is not null)
            _closeButton.ToolTip = "Closes this callout and ends the guided tour.";

        if (initializationComplete)
            RefreshLayout();
    }

    void ConfigureTourOverlayNextLabelState()
    {
        _tourOverlay?.ConfigureNextLabelState(_tourNavAdvanceCountProvider, _tourNavAdvanceRecorder);
    }

    void OnTourModeDisabled()
    {
        this.KeyDown -= Callout_KeyDown;
        Settled      -= OnSettled_TourOverlay;
        DragStarted  -= OnDragStarted_TourOverlay;

        if (_closeButton is not null)
            _closeButton.ToolTip = null;

        CloseTourOverlay();

        if (initializationComplete)
            RefreshLayout();
    }

    void Callout_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TourNextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            TourPrevRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Returns the callout's full visual bounding box in WPF logical-pixel screen coordinates,
    /// including the dangle (pointer triangle) tip so that <see cref="TourCalloutNavigationOverlay.PositionNear"/>
    /// places navigation buttons clear of the pointer on all dangle sides.
    /// </summary>
    Rect GetCalloutScreenBounds()
    {
        var source = PresentationSource.FromVisual(cvsCallout);
        if (source?.CompositionTarget is { } ct)
        {
            double dpiX = ct.TransformToDevice.M11;
            double dpiY = ct.TransformToDevice.M22;

            Point physTL = cvsCallout.PointToScreen(new Point(calloutLeft,               calloutTop));
            Point physBR = cvsCallout.PointToScreen(new Point(calloutLeft + calloutWidth, calloutTop + calloutHeight));

            double left   = physTL.X / dpiX;
            double top    = physTL.Y / dpiY;
            double right  = physBR.X / dpiX;
            double bottom = physBR.Y / dpiY;

            // Nav buttons always go on the opposite side from the dangle, so the body rect
            // is all we need — aligning to the callout body edges, not the pointer tip.
            return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
        // Fallback when PresentationSource is unavailable (should be rare).
        return new Rect(Left + OutsideMargin, Top + OutsideMargin,
            Math.Max(0, calloutWidth), Math.Max(0, calloutHeight));
    }

    void OnSettled_TourOverlay(object? sender, EventArgs e)
    {
        if (_tourOverlay is null) return;
        _tourOverlay.EnsureLayout();
        _tourOverlay.PositionNear(GetCalloutScreenBounds(), _isDangleActive ? _lastDangleSide : CalloutSide.Top);
        _tourOverlay.FadeIn();
        if (_isTourMode)
            StartTourEntryAnimation();
    }

    void StartTourEntryAnimation() {
        if (_mainCalloutPath is null || _tourGlowPath is null || _tourGlowEffect is null) return;

        var skyBlue = glowColor;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromSeconds(1.8));

        // ── A. Glow fade-out ─────────────────────────────────────────────────────
        var blurAnim = new DoubleAnimation(28, 4, duration) { EasingFunction = ease };
        _tourGlowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);

        var opacityAnim = new DoubleAnimation(1.0, 0.0, duration) {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        var capturedGlowPath = _tourGlowPath;
        opacityAnim.Completed += (_, _) => {
            cvsCallout.Children.Remove(capturedGlowPath);
            capturedGlowPath.Effect = null;
        };
        _tourGlowPath.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

        // ── B. Border fade-out ───────────────────────────────────────────────────
        var normalColor = calloutStrokeBrush.Color;
        var thicknessAnim = new DoubleAnimation(3, 1, duration) { EasingFunction = ease };
        var capturedPath = _mainCalloutPath;
        thicknessAnim.Completed += (_, _) => {
            capturedPath.StrokeThickness = 1;
            capturedPath.Stroke = calloutStrokeBrush;
        };
        _mainCalloutPath.BeginAnimation(System.Windows.Shapes.Shape.StrokeThicknessProperty, thicknessAnim);

        var colorAnim = new ColorAnimation(skyBlue, normalColor, duration) { EasingFunction = ease };
        (_mainCalloutPath.Stroke as SolidColorBrush)?.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }

    /// <summary>
    /// Repositions the overlay to match the current callout position without animation.
    /// Called when the callout moves for non-drag reasons (e.g. target element repositioned).
    /// </summary>
    void RepositionTourOverlayNow()
    {
        if (_tourOverlay is null) return;
        _tourOverlay.PositionNear(GetCalloutScreenBounds(), _isDangleActive ? _lastDangleSide : CalloutSide.Top);
    }

    void OnDragStarted_TourOverlay(object? sender, EventArgs e) => _tourOverlay?.HideImmediate();

    void CloseTourOverlay()
    {
        if (_tourOverlay is null) return;
        var overlay = _tourOverlay;
        _tourOverlay = null;
        try { overlay.Close(); } catch { /* already closed */ }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        UserDismissed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    void CalculateDummyBounds() {
        calloutHeight = 200;
        calloutWidth = Options.Width;
        calloutTop = OutsideMargin;
        calloutLeft = OutsideMargin;
        Width = calloutWidth + OutsideMargin * 2;
        Height = calloutHeight + OutsideMargin * 2;
    }

    void CalculateBounds() {
        calloutHeight = calculatedHeight;
        if (idealCalloutWidth != 0)
            calloutWidth = idealCalloutWidth;
        else
            calloutWidth = Options.Width;
        calloutTop = OutsideMargin;
        calloutLeft = OutsideMargin;
        Width = calloutWidth + OutsideMargin * 2;
        Height = calloutHeight + OutsideMargin * 2;
    }

    void CreateCalloutFrame() {
        _mainCalloutPath = null;
        _tourGlowPath = null;
        _tourGlowEffect = null;

        // Main callout shape — added first, so shadow (inserted after) ends up at index 0 (behind)
        _mainCalloutPath = new System.Windows.Shapes.Path() {
            Stroke = calloutStrokeBrush,
            StrokeThickness = 1,
            Fill = calloutFillBrush
        };
        CreateCalloutGeometry(_mainCalloutPath);
        cvsCallout.Children.Insert(0, _mainCalloutPath);
        // Subtle drop shadow — inserted at 0 last, so it sits behind the main shape
        AddCalloutPathToBackOfCanvas(null, 0, new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), 5, 5);

        InitTourGlow();
    }

    void InitTourGlow() {
        if (!_isTourMode || _mainCalloutPath is null) return;

        var skyBlue = glowColor;

        // Glow layer — inserted at index 0, behind shadow and main shape
        _tourGlowPath = new System.Windows.Shapes.Path {
            Fill = null,
            Stroke = null,
            StrokeThickness = 0,
            Opacity = 1.0
        };
        CreateCalloutGeometry(_tourGlowPath);
        _tourGlowEffect = new DropShadowEffect {
            Color = skyBlue,
            ShadowDepth = 0,
            BlurRadius = 28,
            Opacity = 1.0,
            RenderingBias = RenderingBias.Performance
        };
        _tourGlowPath.Effect = _tourGlowEffect;
        cvsCallout.Children.Insert(0, _tourGlowPath);

        // Border — thick blue immediately (no animation yet)
        var localStroke = new SolidColorBrush(skyBlue);
        _mainCalloutPath.Stroke = localStroke;
        _mainCalloutPath.StrokeThickness = 3;
    }

    private void AddCalloutPathToBackOfCanvas(SolidColorBrush? calloutStrokeBrush, int thickness, SolidColorBrush calloutFillBrush, double offsetX = 0, double offsetY = 0) {
        System.Windows.Shapes.Path calloutPath = new System.Windows.Shapes.Path() {
            Stroke = calloutStrokeBrush,
            StrokeThickness = thickness,
            Fill = calloutFillBrush
        };
        CreateCalloutGeometry(calloutPath);
        // Place the callout in the back:
        cvsCallout.Children.Insert(0, calloutPath);
        if (offsetX != 0)
            Canvas.SetLeft(calloutPath, offsetX);
        if (offsetY != 0)
            Canvas.SetTop(calloutPath, offsetY);
    }

    private void CreateCalloutGeometry(System.Windows.Shapes.Path calloutPath) {
        CombinedGeometry combinedGeometry = new CombinedGeometry() { GeometryCombineMode = GeometryCombineMode.Union };
        calloutPath.Data = combinedGeometry;

        RectangleGeometry rectangleGeometry = new RectangleGeometry();
        rectangleGeometry.RadiusX = Options.CornerRadius;
        rectangleGeometry.RadiusY = Options.CornerRadius;

        Rect rect = new Rect();
        rect.Width = Math.Max(20, calloutWidth);
        rect.Height = calloutHeight;
        rect.Location = new Point(calloutLeft, calloutTop);
        rectangleGeometry.Rect = rect;

        combinedGeometry.Geometry1 = rectangleGeometry;

        StreamGeometry triangleGeometry = new StreamGeometry();

        using (StreamGeometryContext ctx = triangleGeometry.Open()) {
            ctx.BeginFigure(trianglePoint1, true, true);
            ctx.LineTo(trianglePoint2, true, true);
            ctx.LineTo(trianglePoint3, true, true);
        }

        triangleGeometry.Freeze();
        combinedGeometry.Geometry2 = triangleGeometry;
    }

    void CreateTemporaryMarkdownViewer() {
        UnloadMarkdownViewer(markdownViewer);
        CalculateDummyBounds();
        markdownViewer = LoadMarkdownViewer();
        cvsCallout.Children.Add(markdownViewer);
        markdownViewer.Tag = STR_TempMarkdown;
    }

    void LayoutText() {
        UnloadMarkdownViewer(markdownViewer);
        markdownViewer!.Height = topExtension + calloutHeight + bottomExtension;
        Canvas.SetLeft(markdownViewer, GetMarkdownLeft());
        Canvas.SetTop(markdownViewer, GetMarkdownTop());
        cvsCallout.Children.Add(markdownViewer);
    }

    private void UnloadMarkdownViewer(Control? markdownControl) {
        if (markdownControl != null)
            markdownControl.Loaded -= MarkdownViewer_Loaded;
    }

    void SetMarkDown(Control markdownControl, string markDownText) {
        if (markdownControl is SimpleMarkdownViewer simpleMarkdownViewer)
            simpleMarkdownViewer.Markdown = markDownText;
        else
            throw new Exception($"Unknown control type.");
    }

    private Control LoadMarkdownViewer() {
        CreateMarkdownViewer();
        markdownViewer!.Padding = new Thickness(0);
        LoadStyles(markdownViewer);
        SetMarkDown(markdownViewer, markDownText);
        markdownViewer.Margin = new Thickness(GetMarkdownMargin());
        markdownViewer.IsHitTestVisible = false;
        markdownViewer.Width = leftExtension + calloutWidth + rightExtension + GetMarkdownWidthAdjust();
        idealCalloutWidth = 0;
        markdownViewer.Loaded += MarkdownViewer_Loaded;
        return markdownViewer;
    }

    private double GetMarkdownTop() {
        return calloutTop + Options.CornerRadius - topExtension + GetMarkdownVerticalOffset();
    }

    const double leftExtension = 14d;
    const double topExtension = 16d;
    const double rightExtension = 2d;
    const double bottomExtension = 10d;
    const string STR_TempMarkdown = "Temp";


    private double GetMarkdownLeft() {
        return calloutLeft + Options.CornerRadius - leftExtension + GetMarkdownHorizontalOffset();
    }

    void ShowFlowDocumentDiagnostics(FlowDocument? flowDocument) {
        if (markdownViewer == null)
            return;

        double lowestBlockSoFar = 0;

        if (flowDocument != null)
            foreach (var b in flowDocument.Blocks) {
                Rect endCharacterRect = b.ElementEnd.GetCharacterRect(LogicalDirection.Forward);

                if (double.IsInfinity(endCharacterRect.Width) || double.IsInfinity(endCharacterRect.Height))
                    continue;

                if (endCharacterRect.Bottom > lowestBlockSoFar)
                    lowestBlockSoFar = endCharacterRect.Bottom;

                AddDiagnosticForBlock(endCharacterRect, Brushes.LightCoral, -1);
            }
    }

    double CalculateFlowDocumentHeight(FlowDocument flowDocument) {
        if (markdownViewer == null)
            return 0d;

        var lowestBlockSoFar = FlowDocumentHelper.GetLowestBlock(flowDocument);
        const double bottomMargin = 5;
        return Math.Max(GetMinHeight(), lowestBlockSoFar + bottomMargin) + GetExtraBottomMargin();
    }


    private void AddDiagnosticForBlock(Rect characterRect, SolidColorBrush strokeBrush, double offset) {
        if (double.IsInfinity(characterRect.Width) || double.IsInfinity(characterRect.Height))
            return;

        Rectangle blockRect = new Rectangle();
        blockRect.Width = Math.Max(10, characterRect.Width);
        blockRect.Height = characterRect.Height;
        blockRect.Stroke = strokeBrush;

        Canvas.SetLeft(blockRect, offset + characterRect.Left + calloutLeft + Options.CornerRadius - leftExtension);
        Canvas.SetTop(blockRect, offset + characterRect.Top + calloutTop + Options.CornerRadius - topExtension);
        cvsCallout.Children.Add(blockRect);
        //AddDiagnostic(blockRect);
    }

    /// <summary>
    /// Adds a figure to the layout to reserve space for the close button so words don't wrap behind it.
    /// </summary>
    private void ReserveSpaceForCloseButton(FlowDocument flowDocument) {
        if (flowDocument == null || flowDocument.Blocks.Count == 0)
            return;

        Block firstBlock = flowDocument.Blocks.First();
        if (firstBlock == null)
            return;

        if (!(firstBlock is Paragraph paragraph))
            return;

        double closeButtonMargin = 2d;

        Figure closeButtonFigure = new() {
            Width = new FigureLength(closeButtonEdgeSize * GetCloseButtonFigureHorizontalScale() + closeButtonMargin, FigureUnitType.Pixel),
            Height = new FigureLength(closeButtonEdgeSize  /* has no impact on height. */, FigureUnitType.Pixel),
            HorizontalAnchor = FigureHorizontalAnchor.PageRight,
            HorizontalOffset = GetMarkdownHorizontalOffset(),
            VerticalOffset = 0,   // has no impact on vertical position.
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };

        if (showDiagnostics) {
            closeButtonFigure.Background = Brushes.BlueViolet;
        }

        paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, closeButtonFigure);
    }

    double GetDistanceToIntersection(MyLine testLine, MyLine topLine) {
        Point intersection = testLine.GetSegmentIntersection(topLine);
        if (double.IsNaN(intersection.X))
            return double.MaxValue;
        double deltaX = intersection.X - targetCenter.X;
        double deltaY = intersection.Y - targetCenter.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    void SetCalloutSides(MyLine testLine, GuidelineIntersectionData data) {
        // Use the bounded Callout* sides (not full-window InnerWindow* lines) so that only the
        // side the testLine actually crosses returns a valid intersection. Window-wide lines can
        // both intersect the testLine near a corner, causing the wrong side to win by a tiny margin.
        data.CalloutDangleSide = SelectCalloutDangleSide(
            testLine, targetCenter,
            data.CalloutTop, data.CalloutLeft, data.CalloutRight, data.CalloutBottom);

        double topCalloutDistance    = GetDistanceToIntersection(testLine, data.CalloutTop);
        double leftCalloutDistance   = GetDistanceToIntersection(testLine, data.CalloutLeft);
        double rightCalloutDistance  = GetDistanceToIntersection(testLine, data.CalloutRight);
        double bottomCalloutDistance = GetDistanceToIntersection(testLine, data.CalloutBottom);

        double topTargetDistance = GetDistanceToIntersection(testLine, data.TargetTop);
        double leftTargetDistance = GetDistanceToIntersection(testLine, data.TargetLeft);
        double rightTargetDistance = GetDistanceToIntersection(testLine, data.TargetRight);
        double bottomTargetDistance = GetDistanceToIntersection(testLine, data.TargetBottom);

        double minTargetDistance = Min(topTargetDistance, leftTargetDistance, rightTargetDistance, bottomTargetDistance);

        if (minTargetDistance == topTargetDistance)
            data.TargetDangleSide = CalloutSide.Top;
        else if (minTargetDistance == rightTargetDistance)
            data.TargetDangleSide = CalloutSide.Right;
        else if (minTargetDistance == bottomTargetDistance)
            data.TargetDangleSide = CalloutSide.Bottom;
        else if (minTargetDistance == leftTargetDistance)
            data.TargetDangleSide = CalloutSide.Left;

        static string Fmt(double d) => d >= double.MaxValue ? "∞" : $"{d:F1}";
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"SetCalloutSides: callout distances top={Fmt(topCalloutDistance)} right={Fmt(rightCalloutDistance)} bottom={Fmt(bottomCalloutDistance)} left={Fmt(leftCalloutDistance)} → CalloutDangleSide={data.CalloutDangleSide} | " +
            $"target distances top={Fmt(topTargetDistance)} right={Fmt(rightTargetDistance)} bottom={Fmt(bottomTargetDistance)} left={Fmt(leftTargetDistance)} → TargetDangleSide={data.TargetDangleSide}");
    }

    private static double Min(params double[] args) => args.Min();

    /// <summary>
    /// Pure, testable side-selection logic. Returns the callout side whose bounded edge segment
    /// is closest to <paramref name="targetCenter"/> along <paramref name="testLine"/>.
    /// </summary>
    internal static CalloutSide SelectCalloutDangleSide(
        MyLine testLine, Point targetCenter,
        MyLine calloutTop, MyLine calloutLeft, MyLine calloutRight, MyLine calloutBottom)
    {
        static double dist(MyLine line, MyLine edge, Point origin) {
            Point pt = line.GetSegmentIntersection(edge);
            if (double.IsNaN(pt.X)) return double.MaxValue;
            double dx = pt.X - origin.X, dy = pt.Y - origin.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        double topDist    = dist(testLine, calloutTop,    targetCenter);
        double leftDist   = dist(testLine, calloutLeft,   targetCenter);
        double rightDist  = dist(testLine, calloutRight,  targetCenter);
        double bottomDist = dist(testLine, calloutBottom, targetCenter);
        double min = Min(topDist, leftDist, rightDist, bottomDist);
        if (min == topDist)    return CalloutSide.Top;
        if (min == rightDist)  return CalloutSide.Right;
        if (min == bottomDist) return CalloutSide.Bottom;
        return CalloutSide.Left;
    }

    GuidelineIntersectionData GetGuidelineIntersectionData(MyLine testLine, double windowLeft, double windowTop) {
        double calloutLeft = windowLeft + OutsideMargin;
        double calloutTop = windowTop + OutsideMargin;
        double calloutRight = calloutLeft + calloutWidth;
        double calloutBottom = calloutTop + calloutHeight;

        double targetLeft = targetCenter.X - TargetWidth / 2;
        double targetTop = targetCenter.Y - TargetHeight / 2;
        double targetRight = targetLeft + TargetWidth;
        double targetBottom = targetTop + TargetHeight;

        double windowRight = windowLeft + calloutWidth + 2 * OutsideMargin;
        double windowBottom = windowTop + calloutHeight + 2 * OutsideMargin;

        GuidelineIntersectionData guidelineIntersectionData = new GuidelineIntersectionData();

        guidelineIntersectionData.CalloutTop = MyLine.Horizontal(calloutLeft, calloutRight, calloutTop);
        guidelineIntersectionData.CalloutLeft = MyLine.Vertical(calloutLeft, calloutTop, calloutBottom);
        guidelineIntersectionData.CalloutRight = MyLine.Vertical(calloutRight, calloutTop, calloutBottom);
        guidelineIntersectionData.CalloutBottom = MyLine.Horizontal(calloutLeft, calloutRight, calloutBottom);

        guidelineIntersectionData.TargetTop = MyLine.Horizontal(targetLeft, targetRight, targetTop - Options.TargetSpacing);
        guidelineIntersectionData.TargetLeft = MyLine.Vertical(targetLeft + Options.TargetSpacing, targetTop, targetBottom);
        guidelineIntersectionData.TargetRight = MyLine.Vertical(targetRight - Options.TargetSpacing, targetTop, targetBottom);
        guidelineIntersectionData.TargetBottom = MyLine.Horizontal(targetLeft, targetRight, targetBottom + Options.TargetSpacing);

        const double contraction = 10d; // So dangle calculation works at close proximity to the target.
        double innerWindowMargin = indicatorMargin + contraction;
        guidelineIntersectionData.InnerWindowTop = MyLine.Horizontal(windowLeft, windowRight, windowTop + innerWindowMargin);
        guidelineIntersectionData.InnerWindowLeft = MyLine.Vertical(windowLeft + innerWindowMargin, windowTop, windowBottom);
        guidelineIntersectionData.InnerWindowRight = MyLine.Vertical(windowRight - innerWindowMargin, windowTop, windowBottom);
        guidelineIntersectionData.InnerWindowBottom = MyLine.Horizontal(windowLeft, windowRight, windowBottom - innerWindowMargin);

        SetCalloutSides(testLine, guidelineIntersectionData);

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetGuidelineIntersectionData: callout=({calloutLeft:F1},{calloutTop:F1})-({calloutRight:F1},{calloutBottom:F1}) " +
            $"target=({targetLeft:F1},{targetTop:F1})-({targetRight:F1},{targetBottom:F1}) " +
            $"innerWindow=({windowLeft + indicatorMargin + 10:F1},{windowTop + indicatorMargin + 10:F1})-({windowLeft + calloutWidth + 2*OutsideMargin - indicatorMargin - 10:F1},{windowTop + calloutHeight + 2*OutsideMargin - indicatorMargin - 10:F1})");

        return guidelineIntersectionData;
    }

    object diagnosticTag = new object();

    void AddDiagnostic(FrameworkElement element) {
        element.Tag = diagnosticTag;
        cvsCallout.Children.Add(element);
    }

    void ShowIntersectedSide(CalloutSide side) {
        const double indicatorThickness = 7d;
        Rectangle sideIndicator = new Rectangle();
        switch (side) {
            case CalloutSide.Left:
            case CalloutSide.Right:
                sideIndicator.Width = indicatorThickness;
                sideIndicator.Height = calloutHeight;
                Canvas.SetTop(sideIndicator, OutsideMargin);
                if (side == CalloutSide.Right)
                    Canvas.SetLeft(sideIndicator, calloutWidth + OutsideMargin - indicatorThickness);
                else
                    Canvas.SetLeft(sideIndicator, OutsideMargin);
                break;
            case CalloutSide.Top:
            case CalloutSide.Bottom:
                sideIndicator.Width = calloutWidth;
                sideIndicator.Height = indicatorThickness;
                Canvas.SetLeft(sideIndicator, OutsideMargin);
                if (side == CalloutSide.Bottom)
                    Canvas.SetTop(sideIndicator, calloutHeight + OutsideMargin - indicatorThickness);
                else
                    Canvas.SetTop(sideIndicator, OutsideMargin);
                break;
        }
        sideIndicator.Fill = Brushes.Blue;
        sideIndicator.Opacity = 0.25;
        AddDiagnostic(sideIndicator);
    }

    Point ScreenToCanvasPoint(Point screenPoint, double windowLeft, double windowTop) {
        return new Point(screenPoint.X - windowLeft, screenPoint.Y - windowTop);
    }

    private Point GetTriangleScreenPoint(GuidelineIntersectionData guidelineIntersectionData, Point triangleScreenPoint1, double angle) {
        Point rotatedScreenPt = MathEx.GetRotatedMyLineSegment(triangleScreenPoint1, calloutScreenCenter, angle).End;
        MyLine line = new MyLine(triangleScreenPoint1, rotatedScreenPt);

        Point intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
            CalloutSide.Right => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideRight),
            CalloutSide.Left => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideLeft),
            CalloutSide.Bottom => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideBottom),
            CalloutSide.Top => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideTop),
            _ => throw new NotImplementedException(),
        };

        if (double.IsNaN(intersectionPoint.X)) {
            // Try adjacent edges on one side...
            intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
                CalloutSide.Right => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideBottom),
                CalloutSide.Left => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideBottom),
                CalloutSide.Bottom => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideRight),
                CalloutSide.Top => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideRight),
                _ => throw new NotImplementedException(),
            };

            if (double.IsNaN(intersectionPoint.X)) {
                // Try adjacent edges on the other side...
                intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
                    CalloutSide.Right => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideTop),
                    CalloutSide.Left => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideTop),
                    CalloutSide.Bottom => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideLeft),
                    CalloutSide.Top => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideLeft),
                    _ => throw new NotImplementedException(),
                };
                if (double.IsNaN(intersectionPoint.X)) {
                    // Segment intersections all failed (pt1 too close to callout edge or inside callout).
                    // Fall back to full-line intersections before giving up with GetClosestConnectionPoint.
                    intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
                        CalloutSide.Right => line.GetIntersection(guidelineIntersectionData.CalloutInsideRight),
                        CalloutSide.Left => line.GetIntersection(guidelineIntersectionData.CalloutInsideLeft),
                        CalloutSide.Bottom => line.GetIntersection(guidelineIntersectionData.CalloutInsideBottom),
                        CalloutSide.Top => line.GetIntersection(guidelineIntersectionData.CalloutInsideTop),
                        _ => throw new NotImplementedException(),
                    };
                    if (double.IsNaN(intersectionPoint.X))
                        intersectionPoint = GetClosestConnectionPoint(rotatedScreenPt, guidelineIntersectionData);
                }
            }
        }

        rotatedScreenPt = intersectionPoint;

        return rotatedScreenPt;
    }

    Point GetClosestConnectionPoint(Point rotatedScreenPt, GuidelineIntersectionData data) {
        Point topConnector = data.CalloutTop.MidPoint;
        Point leftConnector = data.CalloutLeft.MidPoint;
        Point bottomConnector = data.CalloutBottom.MidPoint;
        Point rightConnector = data.CalloutRight.MidPoint;

        double topLength = (rotatedScreenPt - topConnector).Length;
        double leftLength = (rotatedScreenPt - leftConnector).Length;
        double bottomLength = (rotatedScreenPt - bottomConnector).Length;
        double rightLength = (rotatedScreenPt - rightConnector).Length;

        if (topLength < leftLength)
            if (topLength < bottomLength)
                if (topLength < rightLength)
                    return topConnector;
                else
                    return rightConnector;
            else if (bottomLength < rightLength)
                return bottomConnector;
            else
                return rightConnector;
        else if (leftLength < bottomLength)
            if (leftLength < rightLength)
                return leftConnector;
            else
                return rightConnector;
        else if (bottomLength < rightLength)
            return bottomConnector;
        else
            return rightConnector;
    }

    double windowLeft;
    double windowTop;

    GuidelineIntersectionData GetGuidelineIntersectionData(bool positionWindow = false) {
        CalculateWindowPosition(out MyLine testLine, out GuidelineIntersectionData guidelineIntersectionData);

        if (positionWindow)
            AdjustAngleForOnScreenPlacement(ref testLine, ref guidelineIntersectionData);

        if (positionWindow) {
            if (Options.AnimateAppearance) {
                Point halfwayPoint;
                if (Options.AnimationOffset.HasValue)
                    halfwayPoint = new Point(windowLeft + Options.AnimationOffset.Value.X,
                                             windowTop  + Options.AnimationOffset.Value.Y);
                else {
                    Vector vector = screenDanglePoint - targetCenter;
                    halfwayPoint = new Point(windowLeft, windowTop) + vector * 0.5;
                }
                AnimateFrom(halfwayPoint.X, halfwayPoint.Y);
                Left = halfwayPoint.X;
                Top = halfwayPoint.Y;
            }
            else {
                Left = windowLeft;
                Top = windowTop;
            }
        }
        else {
            windowLeft = Left;
            windowTop = Top;
        }

        calloutScreenCenter = new Point(windowLeft + calloutCenter.X, windowTop + calloutCenter.Y);

        GuidelineIntersectionData correctGuidelineIntersectionData = GetGuidelineIntersectionData(testLine, windowLeft, windowTop);
        GetTrianglePoints(correctGuidelineIntersectionData, guidelineIntersectionData.CalloutDangleSide, windowLeft, windowTop);

        if (double.IsNaN(trianglePoint1.X)) {
            CalculateWindowPosition(out testLine, out guidelineIntersectionData);
            calloutScreenCenter = new Point(windowLeft + calloutCenter.X, windowTop + calloutCenter.Y);

            correctGuidelineIntersectionData = GetGuidelineIntersectionData(testLine, windowLeft, windowTop);
            GetTrianglePoints(correctGuidelineIntersectionData, guidelineIntersectionData.CalloutDangleSide, windowLeft, windowTop);
        }

        return guidelineIntersectionData;
    }

    private void CalculateWindowPosition(out MyLine testLine, out GuidelineIntersectionData guidelineIntersectionData) {
        targetCenter = GetTargetCenter();

        const int almostInfiniteDistance = 222222;
        RotateCalloutToGetPosition(almostInfiniteDistance, out windowLeft, out windowTop);

        Point infiniteCalloutStartPos = GetTargetCenter(-almostInfiniteDistance);
        Point infiniteCalloutCenterPoint = MathEx.RotatePoint(infiniteCalloutStartPos, targetCenter, lastCalloutAngle);

        testLine = new MyLine(targetCenter, infiniteCalloutCenterPoint);
        guidelineIntersectionData = GetGuidelineIntersectionData(testLine, windowLeft, windowTop);
        //double distance = GetDistance(guidelineIntersectionData);

        //RotateCalloutToGetPosition(distance, guidelineIntersectionData.CalloutDangleSide, out windowLeft, out windowTop);
        calloutCenter = new Point(OutsideMargin + calloutWidth / 2, OutsideMargin + calloutHeight / 2);
        GetCalloutPosition(guidelineIntersectionData, out windowLeft, out windowTop);

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"CalculateWindowPosition: lastCalloutAngle={lastCalloutAngle:F1}° calloutSize=({calloutWidth:F1}×{calloutHeight:F1}) targetCenter=({targetCenter.X:F1},{targetCenter.Y:F1}) windowPos=({windowLeft:F1},{windowTop:F1}) calloutCenter=({calloutCenter.X:F1},{calloutCenter.Y:F1})");
    }

    /// <summary>
    /// After <see cref="CalculateWindowPosition"/> has set <see cref="windowLeft"/>/<see cref="windowTop"/>
    /// for the current <see cref="lastCalloutAngle"/>, checks whether the callout window fits within the
    /// monitor work area. If not, sweeps outward from the preferred angle in ±5° steps (up to ±180°)
    /// until a valid placement is found. Falls back to clamping when no angle keeps the callout on-screen
    /// (e.g. the target fills most of the screen).
    /// </summary>
    private void AdjustAngleForOnScreenPlacement(ref MyLine testLine, ref GuidelineIntersectionData guidelineIntersectionData, bool allowEdgeHugging = true) {
        double windowWidth  = Width;
        double windowHeight = Height;
        Rect workArea = GetWorkAreaForTargetLogical();

        const double edgeMargin = 20.0;
        bool isOnScreen = IsCalloutOnScreen(windowLeft, windowTop, windowWidth, windowHeight, workArea);
        bool isEdgeHugging = !allowEdgeHugging && (
            windowLeft < workArea.Left + edgeMargin ||
            windowTop < workArea.Top + edgeMargin ||
            windowLeft + windowWidth > workArea.Right - edgeMargin ||
            windowTop + windowHeight > workArea.Bottom - edgeMargin);

        if (isOnScreen && !isEdgeHugging)
            return;

        double requestedAngle = lastCalloutAngle;
        const double step = 5.0;
        const int maxSteps = 72;  // 72 × 5° = 360°

        for (int i = 0; i < maxSteps; i++) {
            double sign   = (i % 2 == 0) ? 1.0 : -1.0;
            double offset = ((i / 2) + 1) * step;
            lastCalloutAngle = requestedAngle + sign * offset;

            CalculateWindowPosition(out MyLine candidateLine, out GuidelineIntersectionData candidateData);

            if (IsCalloutOnScreen(windowLeft, windowTop, windowWidth, windowHeight, workArea)) {
                bool candidateEdgeHugging = !allowEdgeHugging && (
                    windowLeft < workArea.Left + edgeMargin ||
                    windowTop < workArea.Top + edgeMargin ||
                    windowLeft + windowWidth > workArea.Right - edgeMargin ||
                    windowTop + windowHeight > workArea.Bottom - edgeMargin);
                if (!candidateEdgeHugging) {
                    testLine = candidateLine;
                    guidelineIntersectionData = candidateData;
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"AdjustAngleForOnScreenPlacement: found on-screen angle {lastCalloutAngle:F1}° (requested {requestedAngle:F1}°) windowPos=({windowLeft:F1},{windowTop:F1})");
                    return;
                }
            }
        }

        // Fallback: restore the preferred angle, then clamp to keep the callout fully on-screen
        // even if the triangle tip ends up inside the target rect.
        lastCalloutAngle = requestedAngle;
        CalculateWindowPosition(out testLine, out guidelineIntersectionData);
        double clampedLeft = Math.Clamp(windowLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right  - windowWidth));
        double clampedTop  = Math.Clamp(windowTop,  workArea.Top,  Math.Max(workArea.Top,  workArea.Bottom - windowHeight));
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"AdjustAngleForOnScreenPlacement: no valid angle found — clamping from ({windowLeft:F1},{windowTop:F1}) to ({clampedLeft:F1},{clampedTop:F1})");
        windowLeft = clampedLeft;
        windowTop  = clampedTop;
    }

    private static bool IsCalloutOnScreen(double left, double top, double width, double height, Rect workArea)
        => left              >= workArea.Left
        && top               >= workArea.Top
        && left + width  <= workArea.Right
        && top  + height <= workArea.Bottom;

    /// <summary>
    /// Returns the work area of the monitor that contains the callout target, in logical DIPs.
    /// Falls back to <see cref="SystemParameters.WorkArea"/> if DPI information is unavailable.
    /// </summary>
    private Rect GetWorkAreaForTargetLogical() {
        var physCenter = GetTargetCenterPhysical();
        var physWa = NativeMethods.GetWorkAreaForPhysicalPoint((int)physCenter.X, (int)physCenter.Y);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is { } ct) {
            var tl = ct.TransformFromDevice.Transform(new Point(physWa.Left, physWa.Top));
            var br = ct.TransformFromDevice.Transform(new Point(physWa.Right, physWa.Bottom));
            return new Rect(tl, br);
        }
        return SystemParameters.WorkArea;
    }

    private Point GetTargetCenter(double verticalOffset = 0) {
        double hOffset = HorizontalPercentOffset * TargetWidth / 2;
        double vOffset = VerticalPercentOffset * TargetHeight / 2;
        return TargetClientPointToScreen(new Point(TargetWidth / 2 + hOffset, TargetHeight / 2 + vOffset + verticalOffset));
    }

    /// <summary>
    /// Returns the target center in physical screen pixels.
    /// Use only for Win32/GDI APIs such as <c>GetMonitorBoundsForPhysicalPoint</c> that require
    /// physical-pixel coordinates.  All WPF geometry should use <see cref="GetTargetCenter"/> instead.
    /// </summary>
    private Point GetTargetCenterPhysical() {
        double offset = HorizontalPercentOffset * TargetWidth / 2;
        double vOffset = VerticalPercentOffset * TargetHeight / 2;
        var clientCenter = new Point(TargetWidth / 2 + offset, TargetHeight / 2 + vOffset);
        if (frameworkElementTarget != null && frameworkElementTarget.IsVisible)
            return frameworkElementTarget.PointToScreen(clientCenter);
        // rectTarget is in logical coords; convert centre to physical
        var logicalCenter = new Point(rectTarget.X + clientCenter.X, rectTarget.Y + clientCenter.Y);
        var refVisual = (Visual?)targetParentWindow ?? (Visual?)frameworkElementTarget;
        var src = refVisual != null ? PresentationSource.FromVisual(refVisual) : null;
        if (src?.CompositionTarget is { } ct)
            return ct.TransformToDevice.Transform(logicalCenter);
        return logicalCenter;
    }

    private void RotateCalloutToGetPosition(double distance, out double windowLeft, out double windowTop) {
        Point calloutStartPos = GetTargetCenter(-distance);
        Point calloutCenterPoint = MathEx.RotatePoint(calloutStartPos, targetCenter, lastCalloutAngle);
        windowLeft = calloutCenterPoint.X - (OutsideMargin + calloutWidth / 2);
        windowTop = calloutCenterPoint.Y - (OutsideMargin + calloutHeight / 2);
    }

    double GetXSign() {
        // ![](5D631E255DF1F17130A1FB5820FE16E3.png)
        double angleDegrees = GetAngleDegrees();
        if (angleDegrees > 90 && angleDegrees <= 270)
            return 1;

        return -1;
    }

    double GetYSign() {
        // ![](7EB85C87527FE5FBB12762A9DD59A1B1.png)
        double angleDegrees = GetAngleDegrees();
        if (angleDegrees > 0 && angleDegrees <= 180)
            return 1;

        return -1;
    }

    Point GetCalloutDanglePointForHorizontalExit() {
        // ![](164BA7B27FE650FD419F6223A6677E33.png)

        double adjacentC = calloutWidth / 2 + Options.OuterMargin;
        double theta = GetTheta();
        double oppositeD = Math.Abs(adjacentC * Math.Tan(theta));

        return GetCalloutPoint(adjacentC, oppositeD);
    }

    Point GetCalloutDanglePointForVerticalExit() {
        // ![](9536BE665614588B86AA0DAF4F971BBB.png)
        double oppositeD = calloutHeight / 2 + Options.OuterMargin;
        double theta = GetTheta();
        double tanTheta = Math.Tan(theta);
        double adjacentC;
        if (tanTheta != 0)
            adjacentC = Math.Abs(oppositeD / tanTheta);
        else
            throw new Exception($"tanTheta was zero. We should never reach this point.");

        return GetCalloutPoint(adjacentC, oppositeD);
    }

    public double OutsideMargin {
        get => Options.OuterMargin + indicatorMargin;
    }

    private Point GetCalloutPoint(double adjacentC, double oppositeD) {
        double calloutX = OutsideMargin + calloutWidth / 2 + GetXSign() * adjacentC;
        double calloutY = OutsideMargin + calloutHeight / 2 + GetYSign() * oppositeD;
        return new Point(calloutX, calloutY);
    }

    private Point GetTargetPoint(double adjacentA, double oppositeB) {
        double screenX = targetCenter.X - GetXSign() * adjacentA;
        double screenY = targetCenter.Y - GetYSign() * oppositeB;
        return new Point(screenX, screenY);
    }

    Point GetScreenDanglePointForHorizontalExit() {
        // ![](473394D46C1D2A4F0FA89BEEE7DA7405.png)
        double offsetX = HorizontalPercentOffset / 2.0 + 0.5;
        double xSign = GetXSign();
        // xSign > 0: callout is to the left → dangle exits from LEFT edge
        //            distance from targetCenter to left edge = offsetX * TargetWidth
        // xSign < 0: callout is to the right → dangle exits from RIGHT edge
        //            distance from targetCenter to right edge = (1 - offsetX) * TargetWidth
        double targetHorizDist = (xSign > 0)
            ? offsetX * TargetWidth
            : (1.0 - offsetX) * TargetWidth;
        double adjacentA = targetHorizDist + Options.TargetSpacing;
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"ScreenDanglePoint(Horizontal): offsetX={offsetX:F3}, xSign={xSign:F0}, targetHorizDist={targetHorizDist:F1}, adjacentA={adjacentA:F1}, TargetWidth={TargetWidth:F1}");
        double theta = GetTheta();
        double oppositeB = Math.Abs(adjacentA * Math.Tan(theta));

        return GetTargetPoint(adjacentA, oppositeB);
    }

    Point GetScreenDanglePointForVerticalExit() {
        // ![](1DDD9F289F77FC56734B77A13828B6B0.png)
        double offsetY = VerticalPercentOffset / 2.0 + 0.5;
        double ySign = GetYSign();
        // ySign > 0: callout is above element → dangle exits from TOP edge
        //            distance from targetCenter to top edge = offsetY * TargetHeight
        // ySign < 0: callout is below element → dangle exits from BOTTOM edge
        //            distance from targetCenter to bottom edge = (1 - offsetY) * TargetHeight
        double targetVertDist = (ySign > 0)
            ? offsetY * TargetHeight
            : (1.0 - offsetY) * TargetHeight;
        double oppositeB = targetVertDist + Options.TargetSpacing;
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"ScreenDanglePoint(Vertical): offsetY={offsetY:F3}, ySign={ySign:F0}, targetVertDist={targetVertDist:F1}, oppositeB={oppositeB:F1}, TargetHeight={TargetHeight:F1}");
        double theta = GetTheta();
        double tanTheta = Math.Tan(theta);
        double adjacentA;
        if (tanTheta != 0)
            adjacentA = Math.Abs(oppositeB / tanTheta);
        else {
            throw new Exception($"tanTheta is zero. Should never reach this point.");
            //System.Diagnostics.Debugger.Break();
            //adjacentA = TargetWidth / 2 + Options.TargetSpacing;
        }

        return GetTargetPoint(adjacentA, oppositeB);
    }

    private double GetTheta() {
        return GetAngleDegrees() * Math.PI / 180;
    }

    private double GetAngleDegrees() {
        double angleDegrees = 90 - lastCalloutAngle;
        while (angleDegrees < 0)
            angleDegrees += 360;
        return angleDegrees % 360;
    }

    void PlaceGuidelineDiagnostics() {
        Point calloutCenterPoint = new Point(calloutWidth / 2 + OutsideMargin, calloutHeight / 2 + OutsideMargin);
        Line angleGuideline = MathEx.GetRotatedLine(calloutCenterPoint, lastCalloutAngle + 180);
        AddDiagnostic(angleGuideline);

        Rectangle outerMarginRect = new Rectangle();
        outerMarginRect.Width = calloutWidth + 2 * OutsideMargin;
        outerMarginRect.Height = calloutHeight + 2 * OutsideMargin;
        outerMarginRect.Stroke = Brushes.Purple;
        AddDiagnostic(outerMarginRect);

        AddDiagnosticCircle(Brushes.Red, closestIntersectingPoint);
        AddDiagnosticCircle(Brushes.Blue, calloutCenter);
    }

    private void AddDiagnosticCircle(SolidColorBrush fill, Point point) {
        Ellipse ellipse = new Ellipse();
        const double radius = 3d;
        const double diameter = 2 * radius;
        ellipse.Width = diameter;
        ellipse.Height = diameter;
        ellipse.Fill = fill;
        Canvas.SetLeft(ellipse, point.X - radius);
        Canvas.SetTop(ellipse, point.Y - radius);
        AddDiagnostic(ellipse);
    }

    void ShowTriangleDiagnostics() {
        System.Windows.Shapes.Path trianglePath = new System.Windows.Shapes.Path() {
            Stroke = new SolidColorBrush(Color.FromArgb(177, 140, 0, 0)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(44, 255, 0, 0))
        };
        StreamGeometry triangleGeometry = new StreamGeometry();
        trianglePath.Data = triangleGeometry;
        using (StreamGeometryContext ctx = triangleGeometry.Open()) {
            ctx.BeginFigure(trianglePoint1, true, true);
            ctx.LineTo(trianglePoint2, true, true);
            ctx.LineTo(trianglePoint3, true, true);
        }
        AddDiagnostic(trianglePath);
    }

    void LayoutEverything() {
        if (layoutValid)
            return;

        cvsCallout.Children.Clear();
        CreateTemporaryMarkdownViewer();

        layoutValid = true;
    }

    private void ResumeCalloutConstruction() {
        _mainCalloutPath = null;
        cvsCallout.Children.Clear();
        CalculateBounds();
        GuidelineIntersectionData guidelineIntersectionData = GetGuidelineIntersectionData(true);
        _lastDangleSide = guidelineIntersectionData.CalloutDangleSide;
        CreateCalloutFrame();
        PlaceCloseButton();
        LayoutText();

        if (showDiagnostics && markdownViewer is not null) {
            ShowFlowDocumentDiagnostics(GetDocument(markdownViewer));
        }

        ShowDiagnosticControls(guidelineIntersectionData);

        if (_isTourMode) { /* hint removed — nav overlay handles tour affordance */ }
    }

    void RemoveDiagnostics() {
        for (int i = cvsCallout.Children.Count - 1; i >= 0; i--)
            if (cvsCallout.Children[i] is FrameworkElement frameworkElement)
                if (frameworkElement.Tag == diagnosticTag)
                    cvsCallout.Children.RemoveAt(i);
    }

    private void ShowDiagnosticControls(GuidelineIntersectionData guidelineIntersectionData) {
        RemoveDiagnostics();
        if (!showDiagnostics)
            return;
        ShowIntersectedSide(guidelineIntersectionData.CalloutDangleSide);
        PlaceGuidelineDiagnostics();
        ShowTriangleDiagnostics();
    }

    void LoadStyles(Control markdownControl) {
        ResourceDictionary myResourceDictionary = new ResourceDictionary();
        string styleName;
        if (Theme == CalloutTheme.Light)
            styleName = "LightCalloutStyles";
        else if (Theme == CalloutTheme.Dark)
            styleName = "DarkCalloutStyles";
        else {
            // TODO: Add additional style resource loading here.
            return;
        }
        var assemblyName = typeof(FrmUltimateCallout).Assembly.GetName().Name;
        myResourceDictionary.Source = new Uri($"pack://application:,,,/{assemblyName};component/Callouts/Styles/{styleName}.xaml", UriKind.Absolute);

        markdownControl.Resources.MergedDictionaries.Add(myResourceDictionary);
    }

    Window? targetParentWindow;
    bool layoutValid;
    double calloutWidth;
    double calloutHeight;
    double calloutLeft;
    double calloutTop;
    string markDownText = string.Empty;
    FrameworkElement? frameworkElementTarget;
    Point targetCenter;
    Point trianglePoint1 = new Point(double.NaN, double.NaN);
    Point trianglePoint2 = new Point(double.NaN, double.NaN);
    Point trianglePoint3 = new Point(double.NaN, double.NaN);
    Point calloutScreenCenter;
    Point calloutCenter;
    double lastCalloutAngle;
    Point closestIntersectingPoint;
    Control? markdownViewer;
    double calculatedHeight;
    double targetParentLeft;
    double targetParentTop;
    double originalLeft;
    double originalTop;
    double deltaLeft;
    double deltaTop;
    bool animating;
    DateTime animationStartTime;
    Point screenDanglePoint;
    Rect rectTarget;

    void PointTo(FrameworkElement target) {
        frameworkElementTarget = target;
        // Popup children (separate HwndSource) have IsVisible=false even when rendered.
        // Use PresentationSource as an alternate "is rendered" check so PointToScreen works.
        bool isRendered = frameworkElementTarget.IsVisible
                       || (frameworkElementTarget.ActualWidth > 0
                           && System.Windows.PresentationSource.FromVisual(frameworkElementTarget) != null);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"PointTo: type={target.GetType().Name}, IsVisible={target.IsVisible}, "
            + $"ActualW={target.ActualWidth:F1}, ActualH={target.ActualHeight:F1}, isRendered={isRendered}");
        if (isRendered) {
            // Store in logical (DIP) coords so rectTarget is consistent with all other
            // WPF measurements.  PointToScreen returns physical pixels; convert back.
            Point physPos = frameworkElementTarget.PointToScreen(new Point(0, 0));
            Point logPos  = DpiHelper.PhysicalToLogical(frameworkElementTarget, physPos);
            rectTarget = new Rect(logPos.X, logPos.Y, frameworkElementTarget.ActualWidth, frameworkElementTarget.ActualHeight);
        }
        SetParentWindow(Window.GetWindow(target));
    }

    private void SetParentWindow(Window window) {
        // Popup children live in a separate HwndSource so Window.GetWindow() returns null.
        // Fall back to the main application window so the callout still has a valid owner.
        targetParentWindow = window ?? Application.Current.MainWindow;
        targetParentLeft = targetParentWindow.Left;
        targetParentTop = targetParentWindow.Top;
        this.Owner = targetParentWindow;
    }

    void PointTo(Rect targetRect) {
        frameworkElementTarget = null;
        rectTarget = targetRect;
    }

    private void TargetParentWindow_LocationChanged(object? sender, EventArgs e) {
        WindowsLocationChanged();
    }

    private void WindowsLocationChanged() {
        if (targetParentWindow == null)
            return;
        OnRefreshTargetRect();
        double deltaLeft = targetParentWindow.Left - targetParentLeft;
        double deltaTop = targetParentWindow.Top - targetParentTop;
        Left += deltaLeft;
        Top += deltaTop;
        targetParentLeft = targetParentWindow.Left;
        targetParentTop = targetParentWindow.Top;
    }

    void HookTargetParentWindowEvents() {
        if (targetParentWindow == null)
            return;
        targetParentWindow.LocationChanged += TargetParentWindow_LocationChanged;
        targetParentWindow.Closed += ParentWindow_Closed;
        targetParentWindow.Activated += TargetParentWindow_Activated;
        targetParentWindow.Deactivated += TargetParentWindow_Deactivated;
        targetParentWindow.StateChanged += TargetParentWindow_StateChanged;
    }

    private void TargetParentWindow_StateChanged(object? sender, EventArgs e) {
        // Maximize/restore changes every control's screen position, so a simple
        // delta-shift (WindowsLocationChanged) produces a wrong result.  Defer until
        // the new layout has been measured so PointToScreen returns the settled coords.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!initializationComplete) return;
            if (frameworkElementTarget is { IsVisible: true })
            {
                // Convert to logical (DIP) coords — same as PointTo(FrameworkElement).
                var physPos = frameworkElementTarget.PointToScreen(new Point(0, 0));
                var logPos  = DpiHelper.PhysicalToLogical(frameworkElementTarget, physPos);
                rectTarget = new Rect(logPos.X, logPos.Y,
                    frameworkElementTarget.ActualWidth, frameworkElementTarget.ActualHeight);
                ResumeCalloutConstruction();
            }
            else
            {
                WindowsLocationChanged();
            }
        });
    }

    private void TargetParentWindow_Deactivated(object? sender, EventArgs e) {
        CheckTopMostWindow();
    }

    private void TargetParentWindow_Activated(object? sender, EventArgs e) {
        CheckTopMostWindow();
    }

    private void UnhookTargetParentWindowEvents() {
        if (targetParentWindow == null)
            return;
        targetParentWindow.Closed -= ParentWindow_Closed;
        targetParentWindow.LocationChanged -= TargetParentWindow_LocationChanged;
        targetParentWindow.Activated -= TargetParentWindow_Activated;
        targetParentWindow.Deactivated -= TargetParentWindow_Deactivated;
        targetParentWindow.StateChanged -= TargetParentWindow_StateChanged;
    }

    bool initializationComplete = false;
    private const string CLR_SkyBlue = "#18b1fc";
    static Color glowColor = (Color)ColorConverter.ConvertFromString(CLR_SkyBlue);
    HueSatLight glowHsl = new HueSatLight(glowColor);
    double lastDragAngle = double.MinValue;

    void FinalizeAndShow() {
        HookTargetParentWindowEvents();
        LayoutEverything();
        initializationComplete = true;
        Show();
        RegisterCallout(this);
    }

    void SetAngle(double angle) {
        if (angle == double.MinValue) {
            angle = GetBestAngleToTarget();
        }
        Options.InitialAngle = angle;
        lastCalloutAngle = angle;
    }

    /// <summary>
    /// Returns the best angle to the specified target, based on the target center position in the screen.
    /// </summary>
    private double GetBestAngleToTarget() {
        // GetMonitorBoundsForPhysicalPoint requires physical-pixel coordinates.
        var physCenter = GetTargetCenterPhysical();
        Rect screenRect = NativeMethods.GetMonitorBoundsForPhysicalPoint((int)physCenter.X, (int)physCenter.Y);
        Point screenCenter = new Point(screenRect.X + screenRect.Width / 2, screenRect.Y + screenRect.Height / 2);

        if (physCenter.X < screenCenter.X)  // Target is left of screen center.
            if (physCenter.Y < screenCenter.Y)
                return 135;     // Above left
            else
                return 45;      // Below left
        else  // Target is right of screen center.
            if (physCenter.Y < screenCenter.Y)
                return 225;     // Above right
            else
                return 315;     // Below right
    }

    /// <summary>
    /// Returns the best horizontal angle for side placement, choosing whichever side of the
    /// target has more available screen space.
    /// Angle 180: tail points left  → callout body appears to the RIGHT of the target.
    /// Angle 0:   tail points right → callout body appears to the LEFT  of the target.
    /// </summary>
    private double GetBestSideAngle() {
        // GetMonitorBoundsForPhysicalPoint requires physical-pixel coordinates.
        var physCenter = GetTargetCenterPhysical();
        Rect screenRect = NativeMethods.GetMonitorBoundsForPhysicalPoint((int)physCenter.X, (int)physCenter.Y);
        double spaceLeft  = physCenter.X - screenRect.Left;
        double spaceRight = screenRect.Right - physCenter.X;
        return spaceRight >= spaceLeft ? 180 : 0;
    }

    public static FrmUltimateCallout ShowCallout(string markDownText, FrameworkElement target, double width = 200, double angle = double.MinValue, CalloutTheme theme = CalloutTheme.Light, double fontSize = 15, double horizontalPercentOffset = 0) {
        var frmUltimateCallout = CreateNewCallout(markDownText, width, theme, fontSize, horizontalPercentOffset);
        frmUltimateCallout.Options.TargetSpacing = fontSize / 2;
        frmUltimateCallout.PointTo(target);
        frmUltimateCallout.SetAngle(angle);
        frmUltimateCallout.FinalizeAndShow();

        return frmUltimateCallout;
    }

    public static FrmUltimateCallout ShowCallout(string markDownText, Rect target, Window parentWindow, double width = 200, double angle = double.MinValue, CalloutTheme theme = CalloutTheme.Light, double fontSize = 15, double horizontalPercentOffset = 0) {
        var frmUltimateCallout = CreateNewCallout(markDownText, width, theme, fontSize, horizontalPercentOffset);
        frmUltimateCallout.Options.TargetSpacing = fontSize / 2;
        frmUltimateCallout.PointTo(target);
        frmUltimateCallout.SetAngle(angle);
        frmUltimateCallout.SetParentWindow(parentWindow);
        frmUltimateCallout.FinalizeAndShow();

        return frmUltimateCallout;
    }

    /// <summary>
    /// Maps a <see cref="CalloutPlacement"/> to the callout angle convention used internally.
    /// Angle = direction the tail points (toward the target).
    /// Returns <c>double.MinValue</c> for <see cref="CalloutPlacement.Auto"/> to trigger auto-selection.
    /// </summary>
    internal static double PlacementToAngle(CalloutPlacement placement) => placement switch
    {
        CalloutPlacement.North     =>   0,  // tail from bottom of callout, body above target
        CalloutPlacement.NorthEast =>  45,
        CalloutPlacement.East      =>  90,  // tail from left of callout, body right of target
        CalloutPlacement.SouthEast => 135,
        CalloutPlacement.South     => 180,  // tail from top of callout, body below target
        CalloutPlacement.SouthWest => 225,
        CalloutPlacement.West      => 270,  // tail from right of callout, body left of target
        CalloutPlacement.NorthWest => 315,
        _                          => double.MinValue,  // Auto
    };

    /// <summary>
    /// Creates and shows a callout near <paramref name="target"/>.
    /// When <paramref name="placement"/> is <see cref="CalloutPlacement.Auto"/> (default), chooses
    /// the horizontal side with more screen space. Otherwise uses the specified preferred placement.
    /// </summary>
    public static FrmUltimateCallout? ShowCalloutBesideTarget(
        string markDownText,
        FrameworkElement target,
        double width = 300,
        CalloutTheme theme = CalloutTheme.Light,
        double fontSize = 15,
        CalloutPlacement placement = CalloutPlacement.Auto) {
        // Don't show callout against a target that isn't visible/rendered yet.
        // Popup children (separate HwndSource) return IsVisible=false even when open;
        // treat them as rendered if they have actual size and a valid PresentationSource.
        bool isRendered = target.IsVisible
                       || (target.ActualWidth > 0 && target.ActualHeight > 0
                           && System.Windows.PresentationSource.FromVisual(target) != null);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"ShowCalloutBesideTarget: type={target.GetType().Name}, IsVisible={target.IsVisible}, "
            + $"ActualW={target.ActualWidth:F1}, ActualH={target.ActualHeight:F1}, isRendered={isRendered}");
        if (!isRendered)
            return null;
        var callout = CreateNewCallout(markDownText, width, theme, fontSize);
        callout.PointTo(target);
        double angle = placement == CalloutPlacement.Auto
            ? callout.GetBestSideAngle()
            : PlacementToAngle(placement);
        callout.SetAngle(angle);
        // For vertical placements, override the animation start so the callout drops in from
        // the correct direction rather than sliding in from the side.
        switch (placement)
        {
            case CalloutPlacement.South:
                callout.Options.AnimationOffset = new System.Windows.Vector(0, -80); // start near button, drop down into position below
                break;
            case CalloutPlacement.North:
                callout.Options.AnimationOffset = new System.Windows.Vector(0, -80); // start above final, drop down to above-button position
                break;
            case CalloutPlacement.SouthEast:
            case CalloutPlacement.SouthWest:
                callout.Options.AnimationOffset = new System.Windows.Vector(0, -40);
                break;
            case CalloutPlacement.NorthEast:
            case CalloutPlacement.NorthWest:
                callout.Options.AnimationOffset = new System.Windows.Vector(0, -40);
                break;
        }
        callout.FinalizeAndShow();
        return callout;
    }

    /// <summary>Moves the callout to point at a new target element.</summary>
    public void Repoint(FrameworkElement target, double angle = double.MinValue)
    {
        PointTo(target);
        SetAngle(angle);
        FinalizeAndShow();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT_Centered { public int left, top, right, bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO_Centered
    {
        public uint cbSize;
        public RECT_Centered rcMonitor;
        public RECT_Centered rcWork;
        public uint dwFlags;
    }

    private const int VK_LBUTTON = 0x01;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern IntPtr MonitorFromWindow_Centered(IntPtr hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo_Centered(IntPtr hMonitor, ref MONITORINFO_Centered lpmi);

    /// <summary>
    /// Creates and shows a callout centered on the primary monitor (or the monitor containing
    /// <paramref name="ownerWindow"/>), with no dangle pointer.
    /// </summary>
    public static FrmUltimateCallout? ShowCalloutCenteredOnScreen(
        string markdownText,
        Window ownerWindow,
        double width    = 320,
        double fontSize = 15)
    {
        var callout = CreateNewCallout(markdownText, width, CalloutTheme.Light, fontSize);
        callout.Options.HideDangle = true;
        // No slide animation — we override position after layout (ContentRendered), and an
        // animation timer would fight the override if AnimateAppearance were true.
        callout.Options.AnimateAppearance = false;

        var source = PresentationSource.FromVisual(ownerWindow);
        Rect screenRect;
        if (source?.CompositionTarget is { } ct)
        {
            var hwnd     = new WindowInteropHelper(ownerWindow).Handle;
            var hMonitor = MonitorFromWindow_Centered(hwnd, 2u /* MONITOR_DEFAULTTONEAREST */);
            var mi       = new MONITORINFO_Centered { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO_Centered>() };
            GetMonitorInfo_Centered(hMonitor, ref mi);
            var transform   = ct.TransformFromDevice;
            var topLeft     = transform.Transform(new Point(mi.rcWork.left,  mi.rcWork.top));
            var bottomRight = transform.Transform(new Point(mi.rcWork.right, mi.rcWork.bottom));
            screenRect = new Rect(topLeft, bottomRight);
        }
        else
        {
            screenRect = new Rect(
                ownerWindow.Left, ownerWindow.Top,
                ownerWindow.ActualWidth, ownerWindow.ActualHeight);
        }

        double cx = screenRect.Left + screenRect.Width  / 2;
        double cy = screenRect.Top  + screenRect.Height / 2;
        // Virtual 1×1 target at screen center — satisfies internal state; dangle is suppressed.
        var virtualTarget = new Rect(cx - 0.5, cy - 0.5, 1, 1);
        callout.PointTo(virtualTarget);
        callout.SetAngle(135);  // Fixed angle avoids DPI-sensitive GetBestAngleToTarget (angle irrelevant since dangle is hidden)
        callout.SetParentWindow(ownerWindow);

        // ContentRendered fires after the first full layout pass (including ResumeCalloutConstruction),
        // so ActualWidth/ActualHeight are correct here — unlike Loaded which fires before markdown layout.
        // We also fire Settled here because AnimateAppearance=false means StopAnimationTimer is never
        // called, so Settled would never fire otherwise — and Settled is what makes the tour nav overlay appear.
        EventHandler? onRendered = null;
        onRendered = (_, _) =>
        {
            callout.ContentRendered -= onRendered;
            if (callout.ActualWidth > 0)
            {
                callout.Left = cx - callout.ActualWidth  / 2;
                callout.Top  = cy - callout.ActualHeight / 2;
            }
            callout.Settled?.Invoke(callout, EventArgs.Empty);
        };
        callout.ContentRendered += onRendered;

        callout.FinalizeAndShow();
        return callout;
    }

    private static FrmUltimateCallout CreateNewCallout(string markDownText, double width, CalloutTheme theme, double fontSize = 15, double horizontalPercentOffset = 0) {
        FrmUltimateCallout frmUltimateCallout = new FrmUltimateCallout();
        frmUltimateCallout.Options.Width = width;
        frmUltimateCallout.markDownText = markDownText;
        frmUltimateCallout.Theme = theme;
        frmUltimateCallout.FontSize = fontSize;
        frmUltimateCallout.HorizontalPercentOffset = horizontalPercentOffset;
        return frmUltimateCallout;
    }

    /// <summary>Updates the callout text and redraws in place.</summary>
    public void UpdateMarkdown(string newMarkDownText) {
        if (this.markDownText == newMarkDownText) return;
        this.markDownText = newMarkDownText;
        RefreshLayout();
    }

    public void MoveCallout(string markDownText, double angle, double width) {
        InvalidateLayout();
        lastCalloutAngle = angle;
        Options.InitialAngle = angle;
        Options.Width = width;
        if (this.markDownText != markDownText)
            this.markDownText = markDownText;
        LayoutEverything();
    }

    private void ParentWindow_Closed(object? sender, EventArgs e) {
        Close();
    }

    private void Callout_Closed(object sender, EventArgs e) {
        _hwndSource?.RemoveHook(WndProc_NoActivate);
        _hwndSource = null;
        EndRawMouseDrag();
        ThemeRevealWindowRegistry.Unregister(this);
        UnhookTargetParentWindowEvents();
        CloseTourOverlay();
    }


    // Manual drag state.  Do not use DragMove(), WPF Mouse.Capture(), or Win32 SetCapture()
    // here: all three disturb WPF Popup/ContextMenu ownership.  The HWND hook eats the initial
    // mouse-down and this timer polls the physical cursor until the button is released.
    private bool _isDragging;
    private Point _dragStartScreenPos;
    private double _dragStartWindowLeft;
    private double _dragStartWindowTop;
    private DispatcherTimer? _rawDragTimer;

    private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!IsCursorOverDraggableCalloutSurface())
            return;

        StartRawMouseDrag();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (_isDragging)
            MoveRawMouseDragToCursor();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Left)
            EndRawMouseDrag();
    }

    private void StartRawMouseDrag()
    {
        if (_isDragging)
            return;

        _dragStartScreenPos = GetCursorLogicalScreenPos();
        _dragStartWindowLeft = Left;
        _dragStartWindowTop = Top;
        _isDragging = true;
        if (_protectingContextMenusForCalloutDrag)
        {
            _contextMenuProtectedDragInProgress = true;
            _contextMenuProtectedDragCallout = new WeakReference<FrmUltimateCallout>(this);
        }

        _rawDragTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Input,
            RawDragTimer_Tick,
            Dispatcher);
        _rawDragTimer.Start();
    }

    private void RawDragTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDragging || !IsLeftMouseButtonPhysicallyDown())
        {
            EndRawMouseDrag();
            return;
        }

        MoveRawMouseDragToCursor();
    }

    private void MoveRawMouseDragToCursor()
    {
        var current = GetCursorLogicalScreenPos();
        Left = _dragStartWindowLeft + (current.X - _dragStartScreenPos.X);
        Top  = _dragStartWindowTop  + (current.Y - _dragStartScreenPos.Y);
    }

    private void EndRawMouseDrag()
    {
        if (!_isDragging && _rawDragTimer?.IsEnabled != true)
            return;

        _isDragging = false;
        _rawDragTimer?.Stop();
    }

    private Point GetCursorLogicalScreenPos() =>
        DpiHelper.PhysicalToLogical(this, NativeMethods.GetCursorScreenPos());

    private static bool IsLeftMouseButtonPhysicallyDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    private bool IsCursorOverDraggableCalloutSurface()
    {
        var cursor = NativeMethods.GetCursorScreenPos();
        if (_closeButton?.IsVisible == true && IsCursorOverElement(_closeButton, cursor))
            return false;

        // Don't start a drag when the cursor is over the nav overlay buttons.
        if (_tourOverlay?.IsVisible == true && IsCursorOverElement(_tourOverlay, cursor))
            return false;

        var local = PointFromScreen(cursor);
        if (local.X < 0 || local.Y < 0 || local.X > ActualWidth || local.Y > ActualHeight)
            return false;

        var hit = VisualTreeHelper.HitTest(this, local);
        if (hit?.VisualHit is not DependencyObject visualHit)
            return false;

        return !HasInteractiveAncestor(visualHit);
    }

    private static bool IsCursorOverElement(FrameworkElement element, Point screenPoint)
    {
        var local = element.PointFromScreen(screenPoint);
        return local.X >= 0
            && local.Y >= 0
            && local.X <= element.ActualWidth
            && local.Y <= element.ActualHeight;
    }

    private static bool HasInteractiveAncestor(DependencyObject current)
    {
        for (DependencyObject? node = current; node is not null; node = GetDependencyParent(node))
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.Primitives.RangeBase)
                return true;
        }

        return false;
    }

    private static DependencyObject? GetDependencyParent(DependencyObject node)
    {
        try
        {
            var visualParent = VisualTreeHelper.GetParent(node);
            if (visualParent is not null)
                return visualParent;
        }
        catch (InvalidOperationException)
        {
        }

        return node switch
        {
            FrameworkElement fe => fe.Parent,
            FrameworkContentElement fce => fce.Parent,
            _ => null
        };
    }

    private void Window_Activated(object sender, EventArgs e) {
        CheckTopMostWindow();
    }

    private void Window_Deactivated(object sender, EventArgs e) {
        CheckTopMostWindow();
    }

    void CheckTopMostWindow() {
        if (targetParentWindow != null)
        {
            Topmost = WindowHelper.IsForegroundWindow(targetParentWindow);
            if (_tourOverlay != null)
                _tourOverlay.Topmost = Topmost;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        WindowInteropHelper wndHelper = new WindowInteropHelper(this);
        WindowHelper.HideFromAltTab(this);
        ThemeRevealWindowRegistry.Register(this);
    }

    Point GetProperLocation(Point danglePoint, GuidelineIntersectionData data) {
        MyLine danglePointGuideline = new MyLine(calloutCenter, danglePoint);
        double calloutLeft = OutsideMargin;
        double calloutTop = OutsideMargin;
        double calloutRight = calloutLeft + calloutWidth;
        double calloutBottom = calloutTop + calloutHeight;

        // TODO: we might have similar code elsewhere.

        MyLine calloutTopLine = MyLine.Horizontal(calloutLeft, calloutRight, calloutTop);
        MyLine calloutBottomLine = MyLine.Horizontal(calloutLeft, calloutRight, calloutBottom);
        MyLine calloutLeftLine = MyLine.Vertical(calloutLeft, calloutTop, calloutBottom);
        MyLine calloutRightLine = MyLine.Vertical(calloutRight, calloutTop, calloutBottom);

        closestIntersectingPoint = danglePointGuideline.GetClosestIntersectingPoint(danglePoint, calloutTopLine, calloutBottomLine, calloutLeftLine, calloutRightLine);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetProperLocation: calloutCenter=({calloutCenter.X:F1},{calloutCenter.Y:F1}) calloutHeight={calloutHeight:F1} " +
            $"danglePoint=({danglePoint.X:F1},{danglePoint.Y:F1}) " +
            $"calloutBottom={calloutBottom:F1} closestIntersectingPoint=({closestIntersectingPoint.X:F1},{closestIntersectingPoint.Y:F1})");
        if (double.IsNaN(closestIntersectingPoint.X)) {
            SquadDashTrace.Write(TraceCategory.Callouts, "GetProperLocation: closestIntersectingPoint is NaN — returning danglePoint unchanged");
            return danglePoint;
        }

        MyLine guidelineToEdgeOfCallout = new MyLine(calloutCenter, closestIntersectingPoint);

        double length = guidelineToEdgeOfCallout.Length;
        double desiredLength = length + Options.OuterMargin;

        guidelineToEdgeOfCallout.MatchLength(desiredLength);

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetProperLocation: length={length:F1} desiredLength={desiredLength:F1} result=({guidelineToEdgeOfCallout.End.X:F1},{guidelineToEdgeOfCallout.End.Y:F1})");

        return guidelineToEdgeOfCallout.End;
    }

    void GetCalloutPosition(GuidelineIntersectionData data, out double windowLeft, out double windowTop) {
        Point danglePoint = data.CalloutDangleSide switch {
            CalloutSide.Left => GetCalloutDanglePointForHorizontalExit(),
            CalloutSide.Right => GetCalloutDanglePointForHorizontalExit(),
            CalloutSide.Top => GetCalloutDanglePointForVerticalExit(),
            CalloutSide.Bottom => GetCalloutDanglePointForVerticalExit(),
            _ => throw new NotImplementedException()
        };

        danglePoint = GetProperLocation(danglePoint, data);
        screenDanglePoint = data.TargetDangleSide switch {
            CalloutSide.Left => GetScreenDanglePointForHorizontalExit(),
            CalloutSide.Right => GetScreenDanglePointForHorizontalExit(),
            CalloutSide.Top => GetScreenDanglePointForVerticalExit(),
            CalloutSide.Bottom => GetScreenDanglePointForVerticalExit(),
            _ => throw new NotImplementedException()
        };

        windowLeft = screenDanglePoint.X - danglePoint.X;
        windowTop = screenDanglePoint.Y - danglePoint.Y;
    }

    void GetTrianglePoints(GuidelineIntersectionData data, CalloutSide previousCalloutSide, double windowLeft, double windowTop) {
        if (Options.HideDangle)
        {
            trianglePoint1 = calloutScreenCenter;
            trianglePoint2 = calloutScreenCenter;
            trianglePoint3 = calloutScreenCenter;
            _isDangleActive = false;
            return;
        }
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetTrianglePoints: entry — CalloutDangleSide={data.CalloutDangleSide} previousCalloutSide={previousCalloutSide} lastCalloutAngle={lastCalloutAngle:F1}° targetCenter=({targetCenter.X:F1},{targetCenter.Y:F1}) calloutScreenCenter=({calloutScreenCenter.X:F1},{calloutScreenCenter.Y:F1}) windowLeft={windowLeft:F1} windowTop={windowTop:F1}");
        MyLine guideline = MathEx.GetRotatedMyLine(targetCenter, lastCalloutAngle);
        // Use GetIntersection (infinite-line) rather than GetSegmentIntersection so that
        // corner cases where the guideline crosses the InnerWindow boundary just outside the
        // segment endpoints (e.g. target nearly level with callout, callout to the right) still
        // produce a valid pt1. GetProperLocation clamps the result to the callout body.
        Point pt1 = data.CalloutDangleSide switch {
            CalloutSide.Right => guideline.GetIntersection(data.InnerWindowRight),
            CalloutSide.Left => guideline.GetIntersection(data.InnerWindowLeft),
            CalloutSide.Bottom => guideline.GetIntersection(data.InnerWindowBottom),
            CalloutSide.Top => guideline.GetIntersection(data.InnerWindowTop),
            _ => throw new Exception($"Come on!!!")
        };

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetTrianglePoints: pt1 (raw, before offset) = ({pt1.X:F1},{pt1.Y:F1}) isNaN={double.IsNaN(pt1.X)||double.IsNaN(pt1.Y)}");
        double border = Options.OuterMargin;
        Point calloutUpperLeft = new Point(calloutScreenCenter.X - calloutWidth / 2 - border, calloutScreenCenter.Y - calloutHeight / 2 - border);
        Point calloutLowerRight = new Point(calloutScreenCenter.X + calloutWidth / 2 + border, calloutScreenCenter.Y + calloutHeight / 2 + border);

        double deltaLeft = Left - windowLeft;
        double deltaTop = Top - windowTop;

        Point adjustedCenter = targetCenter;

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetTrianglePoints: offset — this.Left={Left:F1} this.Top={Top:F1} windowLeft={windowLeft:F1} windowTop={windowTop:F1} deltaLeft={deltaLeft:F1} deltaTop={deltaTop:F1}");
        // Use windowLeft/windowTop (the calculated final position) rather than this.Left/this.Top
        // (the current animation position) so that the canvas-to-screen conversion is consistent
        // with the geometry computed in GetProperLocation and GetGuidelineIntersectionData.
        pt1.Offset(-windowLeft, -windowTop);
        SquadDashTrace.Write(TraceCategory.Callouts, $"GetTrianglePoints: pt1 (canvas, before GetProperLocation) = ({pt1.X:F1},{pt1.Y:F1}) calloutCenter=({calloutCenter.X:F1},{calloutCenter.Y:F1}) calloutHeight={calloutHeight:F1}");
        pt1 = GetProperLocation(pt1, data);
        pt1.Offset(windowLeft, windowTop);

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetTrianglePoints: pt1 (after GetProperLocation) = ({pt1.X:F1},{pt1.Y:F1}) distToTarget={(pt1 - targetCenter).Length:F1} indicatorMargin/2={indicatorMargin / 2:F1}");

        const double innerMargin = 10;
        if ((pt1 - targetCenter).Length < indicatorMargin / 2 || MathEx.IsBetween(targetCenter, calloutUpperLeft, calloutLowerRight, innerMargin)) {
            // Callout is over the target - no dangle needed!
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"GetTrianglePoints: NO DANGLE — callout is over/adjacent to target. distToTarget={(pt1 - targetCenter).Length:F1} indicatorMargin/2={indicatorMargin / 2:F1} isBetween={MathEx.IsBetween(targetCenter, calloutUpperLeft, calloutLowerRight, innerMargin)} trianglePoints collapsed to calloutScreenCenter=({calloutScreenCenter.X:F1},{calloutScreenCenter.Y:F1})");
            trianglePoint1 = calloutScreenCenter;
            trianglePoint2 = calloutScreenCenter;
            trianglePoint3 = calloutScreenCenter;
            _isDangleActive = false;
            return;
        }

        Point pt2 = GetTriangleScreenPoint(data, pt1, Options.DangleAngle / 2);
        Point pt3 = GetTriangleScreenPoint(data, pt1, -Options.DangleAngle / 2);

        adjustedCenter.Offset(-deltaLeft, -deltaTop);

        trianglePoint1 = ScreenToCanvasPoint(pt1, windowLeft, windowTop);
        trianglePoint2 = ScreenToCanvasPoint(pt2, windowLeft, windowTop);
        trianglePoint3 = ScreenToCanvasPoint(pt3, windowLeft, windowTop);

        // Clamp the base points (tp2, tp3) to the callout body. When the dangle tip is far
        // off-screen the fallback full-line intersection in GetTriangleScreenPoint can return
        // a point well outside the callout rectangle; clamping keeps the triangle visible.
        // Guard against a non-positive size (window not yet laid out) so Math.Clamp never
        // receives min > max and throws ArgumentException.
        double cbLeft   = OutsideMargin;
        double cbRight  = Math.Max(cbLeft, OutsideMargin + calloutWidth);
        double cbTop    = OutsideMargin;
        double cbBottom = Math.Max(cbTop,  OutsideMargin + calloutHeight);
        const double minDangleBase = 8.0;
        switch (data.CalloutDangleSide) {
            case CalloutSide.Top:
            case CalloutSide.Bottom: {
                double tp2x = Math.Clamp(trianglePoint2.X, cbLeft, cbRight);
                double tp3x = Math.Clamp(trianglePoint3.X, cbLeft, cbRight);
                if (Math.Abs(tp3x - tp2x) < minDangleBase) {
                    // Center the base around the tip's X projection, clamped to callout bounds
                    double tipX = Math.Clamp(trianglePoint1.X, cbLeft + minDangleBase / 2, cbRight - minDangleBase / 2);
                    tp2x = tipX - minDangleBase / 2;
                    tp3x = tipX + minDangleBase / 2;
                }
                trianglePoint2 = new Point(tp2x, trianglePoint2.Y);
                trianglePoint3 = new Point(tp3x, trianglePoint3.Y);
                break;
            }
            case CalloutSide.Left:
            case CalloutSide.Right: {
                double tp2y = Math.Clamp(trianglePoint2.Y, cbTop, cbBottom);
                double tp3y = Math.Clamp(trianglePoint3.Y, cbTop, cbBottom);
                if (Math.Abs(tp3y - tp2y) < minDangleBase) {
                    // Center the base around the tip's Y projection, clamped to callout bounds
                    double tipY = Math.Clamp(trianglePoint1.Y, cbTop + minDangleBase / 2, cbBottom - minDangleBase / 2);
                    tp2y = tipY - minDangleBase / 2;
                    tp3y = tipY + minDangleBase / 2;
                }
                trianglePoint2 = new Point(trianglePoint2.X, tp2y);
                trianglePoint3 = new Point(trianglePoint3.X, tp3y);
                break;
            }
        }

        // Enforce minimum triangle height (perpendicular distance from base to tip).
        const double minDangleHeight = 8.0;
        switch (data.CalloutDangleSide) {
            case CalloutSide.Bottom:
                if (trianglePoint1.Y - trianglePoint2.Y < minDangleHeight)
                    trianglePoint1 = new Point(trianglePoint1.X, trianglePoint2.Y + minDangleHeight);
                break;
            case CalloutSide.Top:
                if (trianglePoint2.Y - trianglePoint1.Y < minDangleHeight)
                    trianglePoint1 = new Point(trianglePoint1.X, trianglePoint2.Y - minDangleHeight);
                break;
            case CalloutSide.Left:
                if (trianglePoint2.X - trianglePoint1.X < minDangleHeight)
                    trianglePoint1 = new Point(trianglePoint2.X - minDangleHeight, trianglePoint1.Y);
                break;
            case CalloutSide.Right:
                if (trianglePoint1.X - trianglePoint2.X < minDangleHeight)
                    trianglePoint1 = new Point(trianglePoint2.X + minDangleHeight, trianglePoint1.Y);
                break;
        }

        _isDangleActive = true;
        _lastDangleSide = data.CalloutDangleSide;
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GetTrianglePoints: DANGLE drawn — tp1=({trianglePoint1.X:F1},{trianglePoint1.Y:F1}) tp2=({trianglePoint2.X:F1},{trianglePoint2.Y:F1}) tp3=({trianglePoint3.X:F1},{trianglePoint3.Y:F1})");
    }

    void MouseUpCheck(object? sender, EventArgs e) {
        if (GetMouseIsDown())
            return;

        _dragInProgress = false;
        waitingForMouseUpTimer?.Stop();
        ActivateParentWindow();
        StartAnimatingTowardTarget();
    }

    void WaitForMouseUp() {
        if (waitingForMouseUpTimer == null)
            waitingForMouseUpTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Input, MouseUpCheck, Dispatcher);

        waitingForMouseUpTimer.Start();
    }

    private void Window_LocationChanged(object sender, EventArgs e) {
        WindowPositionChanged();
    }

    private void WindowPositionChanged() {
        if (cvsCallout == null || cvsCallout.Children.Count == 0)
            return;

        double calloutCenterScreenX = Left + OutsideMargin + calloutWidth / 2;
        double calloutCenterScreenY = Top + OutsideMargin + calloutHeight / 2;
        Point calloutCenter = new Point(calloutCenterScreenX, calloutCenterScreenY);
        double angleDegrees = MathEx.GetAngleDegrees(targetCenter, calloutCenter) + 90;
        while (angleDegrees < 0)
            angleDegrees += 360;
        angleDegrees %= 360;
        if (angleDegrees != lastCalloutAngle) {
            for (int i = cvsCallout.Children.Count - 1; i >= 0; i--)
                if (cvsCallout.Children[i] is System.Windows.Shapes.Path)
                    cvsCallout.Children.RemoveAt(i);

            lastCalloutAngle = angleDegrees;
            GuidelineIntersectionData guidelineIntersectionData = GetGuidelineIntersectionData();
            CreateCalloutFrame();
            ShowDiagnosticControls(guidelineIntersectionData);
        }

        if (_isDragging || GetMouseIsDown()) {
            if (animating)
                StopAnimationTimer(fireSettled: false);

            if (!_dragInProgress)
            {
                _dragInProgress = true;
                DragStarted?.Invoke(this, EventArgs.Empty);
            }

            if (Options.AnimateBackAfterDrag)
                WaitForMouseUp();
        }
        else if (_isTourMode && _tourOverlay is { IsVisible: true })
        {
            // Non-drag position change (e.g. target element repositioned, window programmatically moved):
            // keep the overlay pinned to the callout.
            RepositionTourOverlayNow();
        }
    }

    private static bool GetMouseIsDown() {
        return System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed;
    }

    void StopAnimationTimer(bool fireSettled = true) {
        if (!animating)
            return;

        animating = false;
        calloutAnimationTimer?.Stop();

        // Force a final canvas re-render so that any transient NO-DANGLE state produced
        // during the last animated frame (e.g. the callout briefly overlapping the target
        // while settling) is corrected.  We invalidate the angle cache so that
        // WindowPositionChanged always re-runs CreateCalloutFrame with the settled position.
        // Guard against the drag case: StopAnimationTimer(fireSettled:false) is called from
        // inside WindowPositionChanged when the user is dragging, and re-entering would loop.
        if (!_isDragging && !GetMouseIsDown()) {
            lastCalloutAngle = double.NaN;
            WindowPositionChanged();
        }

        if (fireSettled)
            Settled?.Invoke(this, EventArgs.Empty);
    }

    const double PositionLimit = 2_000_000; // well within Int32 range, generous for any real screen layout

    static bool IsValidPosition(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= PositionLimit;

    void MoveWindowToFinalPosition() {
        var newLeft = originalLeft + deltaLeft;
        var newTop  = originalTop  + deltaTop;
        // Guard against overflow: WPF Window.Left/Top must be in Int32 range.
        if (!IsValidPosition(newLeft) || !IsValidPosition(newTop))
            return;
        Left = newLeft;
        Top  = newTop;
    }

    public double LastDragAngle { get => lastDragAngle; }

    void TriggerAngleChangedIfNeeded() {
        if (lastCalloutAngle != lastDragAngle) {
            bool shouldFireEvent = lastDragAngle != double.MinValue;
            lastDragAngle = lastCalloutAngle;

            if (shouldFireEvent)
                OnAngleChanged(this, EventArgs.Empty);
        }
    }
    void MoveTheCallout(object? sender, EventArgs e) {
        double timeSpanSinceAnimationStartMs = (DateTime.Now - animationStartTime).TotalMilliseconds;

        bool reachedEndOfAnimation = timeSpanSinceAnimationStartMs > Options.AnimationTimeMs;

        if (reachedEndOfAnimation) {
            TriggerAngleChangedIfNeeded();
            MoveWindowToFinalPosition();
            StopAnimationTimer();
            return;
        }

        double percentComplete = InOutQuadBlend(timeSpanSinceAnimationStartMs / Options.AnimationTimeMs);

        var animLeft = originalLeft + deltaLeft * percentComplete;
        var animTop  = originalTop  + deltaTop  * percentComplete;
        if (!IsValidPosition(animLeft) || !IsValidPosition(animTop))
            return;
        Left = animLeft;
        Top  = animTop;
    }

    double InOutQuadBlend(double t) {
        if (t <= 0.5f)
            return 2.0f * t * t;
        t -= 0.5f;
        return 2.0f * t * (1.0f - t) + 0.5f;
    }

    void ActivateParentWindow() {
        targetParentWindow?.Activate();
    }

    void StartAnimatingTowardTarget() {
        CalculateWindowPosition(out MyLine testLine, out GuidelineIntersectionData guidelineIntersectionData);
        AdjustAngleForOnScreenPlacement(ref testLine, ref guidelineIntersectionData, allowEdgeHugging: false);
        AnimateFrom(Left, Top);
    }

    /// <summary>
    /// Animates the window from the specified position to the position specified by windowLeft and windowTop.
    /// </summary>
    private void AnimateFrom(double left, double top) {
        originalLeft = left;
        originalTop = top;
        deltaLeft = windowLeft - originalLeft;
        deltaTop = windowTop - originalTop;
        animating = true;
        animationStartTime = DateTime.Now;
        if (calloutAnimationTimer == null)
            calloutAnimationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Input, MoveTheCallout, Dispatcher);
        calloutAnimationTimer.Start();
    }

    public void ForceRefresh() {
        WindowPositionChanged();  // Force a refresh of every calculation.
    }

    public void TargetMoved() {
        Point newTargetCenter = GetTargetCenter();
        double deltaX = newTargetCenter.X - targetCenter.X;
        double deltaY = newTargetCenter.Y - targetCenter.Y;
        Left += deltaX;
        Top += deltaY;
        targetCenter = newTargetCenter;
        if (deltaX == 0 && deltaY == 0)
            WindowPositionChanged();  // Force a refresh of every calculation.

    }

    FlowDocument? GetDocument(Control control) {
        if (control is SimpleMarkdownViewer simpleMarkdownViewer)
            return simpleMarkdownViewer.Document;

        return null;

    }

    private void MarkdownViewer_Loaded(object sender, RoutedEventArgs e) {
        Control? markdownControl = sender as Control;
        if (markdownControl == null)
            return;

        FlowDocument? flowDocument = GetDocument(markdownControl);

        if (flowDocument != null)
            SquadDashTrace.Write(TraceCategory.UI, $"[Callout] MarkdownViewer_Loaded: markdownControl.FontSize={markdownControl.FontSize:F1}, flowDocument.FontSize={flowDocument.FontSize:F1}, doc.Parent={flowDocument.Parent?.GetType().Name ?? "null"}");

        if (flowDocument != null) {
            ReserveSpaceForCloseButton(flowDocument);
            if ((string)markdownControl.Tag == STR_TempMarkdown) {
                calculatedHeight = CalculateFlowDocumentHeight(flowDocument);
                if (flowDocument.Parent is FlowDocumentScrollViewer flowDocumentScrollViewer) {
                    SquadDashTrace.Write(TraceCategory.UI, $"[Callout] MarkdownViewer_Loaded: FlowDocumentScrollViewer.FontSize={flowDocumentScrollViewer.FontSize:F1}");
                    double originalMarkdownWidth = markdownViewer!.Width;
                    double lastGoodWidth = markdownViewer.Width;
                    int numTries = 0;
                    while (numTries < 300 && markdownViewer.Width > 10) {
                        markdownViewer.Width -= 5;
                        flowDocument.PageWidth = markdownViewer.Width;
                        double newHeight = CalculateFlowDocumentHeight(flowDocument);
                        if (newHeight != calculatedHeight)
                            break;
                        lastGoodWidth = markdownViewer.Width;
                        numTries++;
                    }
                    double widthDelta = lastGoodWidth - originalMarkdownWidth;
                    if (widthDelta != 0) {
                        idealCalloutWidth = calloutWidth + widthDelta;
                    }
                    markdownViewer.Width = lastGoodWidth;
                    flowDocument.PageWidth = lastGoodWidth;
                    calculatedHeight = CalculateFlowDocumentHeight(flowDocument);
                }
                ResumeCalloutConstruction();
            }
        }
    }

    private void CreateMarkdownViewer() {
        markdownViewer = new SimpleMarkdownViewer();
        markdownViewer.FontSize = FontSize;
        SquadDashTrace.Write(TraceCategory.UI, $"[Callout] CreateMarkdownViewer: this.FontSize={FontSize:F1}, markdownViewer.FontSize={markdownViewer.FontSize:F1}");
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!UiRevealOverlay.IsAnyRevealActive) return;
        try
        {
            var screenPos = NativeMethods.GetCursorScreenPos();
            var localPos  = e.GetPosition(this);

            FrameworkElement? revealed = null;

            if (markdownViewer is not null)
            {
                var mvBounds = new Rect(
                    Canvas.GetLeft(markdownViewer),
                    Canvas.GetTop(markdownViewer),
                    markdownViewer.ActualWidth,
                    markdownViewer.ActualHeight);
                if (mvBounds.Contains(localPos))
                    revealed = markdownViewer;
            }

            if (revealed is null)
            {
                VisualTreeHelper.HitTest(this, null, r => {
                    if (r.VisualHit is FrameworkElement fe) { revealed = fe; return HitTestResultBehavior.Stop; }
                    return HitTestResultBehavior.Continue;
                }, new PointHitTestParameters(localPos));
            }

            if (revealed is not null)
                UiRevealOverlay.RevealFromExternalElement(revealed, screenPos);
        }
        catch { }
    }

    Point TargetClientPointToScreen(Point clientPoint) {
        if (frameworkElementTarget != null && frameworkElementTarget.IsVisible) {
            // PointToScreen returns physical pixels; convert to logical DIPs so that all
            // geometry (windowLeft/Top, triangle points, guideline intersections) lives in
            // the same coordinate system as WPF element sizes and Window.Left/Top.
            Point phys = frameworkElementTarget.PointToScreen(clientPoint);
            return DpiHelper.PhysicalToLogical(frameworkElementTarget, phys);
        }
        // rectTarget is already in logical coords (stored that way in PointTo / StateChanged).
        return new Point(rectTarget.X + clientPoint.X, rectTarget.Y + clientPoint.Y);
    }

    public void UpdateTargetRect(Rect targetRect) {
        rectTarget = targetRect;
    }

    public double TargetWidth {
        get {
            if (frameworkElementTarget != null && frameworkElementTarget.IsVisible)
                return frameworkElementTarget.ActualWidth;
            else
                return rectTarget.Width;
        }
    }

    public double TargetHeight {
        get {
            if (frameworkElementTarget != null && frameworkElementTarget.IsVisible)
                return frameworkElementTarget.ActualHeight;
            else
                return rectTarget.Height;
        }
    }

    /// <summary>
    /// The opacity of the glow when the dark theme is active.
    /// </summary>
    public double GlowOpacity { get; set; } = 0.2;

    /// <summary>
    /// The amount to shift the center of the target left or right (as a percentage of half the width).
    /// 0 has no shift. 1 shifts the center to the right by half the width, -1 shifts the center target to the left by the same amount.
    /// </summary>
    public double HorizontalPercentOffset { get; set; }

    /// <summary>
    /// The amount to shift the center of the target up or down (as a percentage of half the height).
    /// 0 has no shift. 1 shifts the center downward by half the height, -1 shifts upward by the same amount.
    /// </summary>
    public double VerticalPercentOffset { get; set; }
}

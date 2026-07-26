using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SquadDash.GuidedTours;

namespace SquadDash;

internal sealed class GuidedTourCoordinator
{
    internal GuidedTourCommandRegistry CommandRegistry { get; } = new();
    internal GuidedTourAdvanceTriggerRegistry AdvanceTriggerRegistry { get; } = new();
    internal GuidedTourContextRegistry ContextRegistry { get; } = new();
    internal List<Block> InjectedCoordinatorBlocks { get; } = new();
    internal Dictionary<string, FrameworkElement> NamedElements { get; } = new();
    internal List<(FrameworkElement El, Window Overlay, Action Reposition)> HighlightOverlays { get; } = new();
    internal HashSet<Window> HighlightTrackedWindows { get; } = new();
    internal DispatcherTimer? HighlightZTimer { get; set; }
    internal List<MenuItem> KeptOpenMenuItems { get; } = new();
    internal string? KeptOpenMenuPath { get; set; }
    internal bool MenuRecoveryRunning { get; set; }
    internal string? KeptOpenIntelliSenseTrigger { get; set; }
    internal bool IntelliSenseRecoveryRunning { get; set; }
    internal int MenuTrackingGeneration { get; set; }
    internal AgentStatusCard? FirstInactiveAgentCard { get; set; }
    internal AgentStatusCard? SimulatedAgentCard { get; set; }
    internal Dictionary<string, (AgentStatusCard Card, TranscriptThreadState Thread)> NamedDemoAgents { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, DispatcherTimer> DemoAgentSpinnerTimers { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal bool TypeItemIsSimulated { get; set; }
    internal bool PrefsWindowEnterLetThrough { get; set; }
    internal bool QuickReplyIntelliSenseEnterLetThrough { get; set; }
    internal UIElement? ShortcutTarget { get; set; }

    private Action? _quickReplySelected;
    internal event Action? QuickReplySelected { add => _quickReplySelected += value; remove => _quickReplySelected -= value; }
    internal void RaiseQuickReplySelected() => _quickReplySelected?.Invoke();

    private Action? _simulatedSendClicked;
    internal event Action? SimulatedSendClicked { add => _simulatedSendClicked += value; remove => _simulatedSendClicked -= value; }
    internal void RaiseSimulatedSendClicked() => _simulatedSendClicked?.Invoke();
    internal bool HasSimulatedSendClickedHandler => _simulatedSendClicked is not null;

    private EventHandler? _cycleCaseForward;
    internal event EventHandler? CycleCaseForward { add => _cycleCaseForward += value; remove => _cycleCaseForward -= value; }
    internal void RaiseCycleCaseForward(object sender) => _cycleCaseForward?.Invoke(sender, EventArgs.Empty);

    private EventHandler? _cycleCaseReverse;
    internal event EventHandler? CycleCaseReverse { add => _cycleCaseReverse += value; remove => _cycleCaseReverse -= value; }
    internal void RaiseCycleCaseReverse(object sender) => _cycleCaseReverse?.Invoke(sender, EventArgs.Empty);

    private EventHandler? _fullScreenTranscript;
    internal event EventHandler? FullScreenTranscript { add => _fullScreenTranscript += value; remove => _fullScreenTranscript -= value; }
    internal void RaiseFullScreenTranscript(object sender) => _fullScreenTranscript?.Invoke(sender, EventArgs.Empty);

    private EventHandler? _exitFullScreenTranscript;
    internal event EventHandler? ExitFullScreenTranscript { add => _exitFullScreenTranscript += value; remove => _exitFullScreenTranscript -= value; }
    internal void RaiseExitFullScreenTranscript(object sender) => _exitFullScreenTranscript?.Invoke(sender, EventArgs.Empty);

    private EventHandler? _fullScreenPromptPeek;
    internal event EventHandler? FullScreenPromptPeek { add => _fullScreenPromptPeek += value; remove => _fullScreenPromptPeek -= value; }
    internal void RaiseFullScreenPromptPeek(object sender) => _fullScreenPromptPeek?.Invoke(sender, EventArgs.Empty);

    private Action? _preferencesWindowShown;
    internal event Action? PreferencesWindowShown { add => _preferencesWindowShown += value; remove => _preferencesWindowShown -= value; }
    internal void RaisePreferencesWindowShown() => _preferencesWindowShown?.Invoke();

    private Action? _preferencesWindowClosed;
    internal event Action? PreferencesWindowClosed { add => _preferencesWindowClosed += value; remove => _preferencesWindowClosed -= value; }
    internal void RaisePreferencesWindowClosed() => _preferencesWindowClosed?.Invoke();

    private Action? _newQueueSlotAtFront;
    internal event Action? NewQueueSlotAtFront { add => _newQueueSlotAtFront += value; remove => _newQueueSlotAtFront -= value; }
    internal void RaiseNewQueueSlotAtFront() => _newQueueSlotAtFront?.Invoke();

    private Action? _environmentFontZoomed;
    internal event Action? EnvironmentFontZoomed { add => _environmentFontZoomed += value; remove => _environmentFontZoomed -= value; }
    internal void RaiseEnvironmentFontZoomed() => _environmentFontZoomed?.Invoke();

    private Action? _workspaceOpenedInExplorer;
    internal event Action? WorkspaceOpenedInExplorer { add => _workspaceOpenedInExplorer += value; remove => _workspaceOpenedInExplorer -= value; }
    internal void RaiseWorkspaceOpenedInExplorer() => _workspaceOpenedInExplorer?.Invoke();

    private Action? _allAttachmentsRemoved;
    internal event Action? AllAttachmentsRemoved { add => _allAttachmentsRemoved += value; remove => _allAttachmentsRemoved -= value; }
    internal void RaiseAllAttachmentsRemoved() => _allAttachmentsRemoved?.Invoke();

    private Action? _secondaryTranscriptCollapsedToOne;
    internal event Action? SecondaryTranscriptCollapsedToOne { add => _secondaryTranscriptCollapsedToOne += value; remove => _secondaryTranscriptCollapsedToOne -= value; }
    internal void RaiseSecondaryTranscriptCollapsedToOne() => _secondaryTranscriptCollapsedToOne?.Invoke();

    private Action<string>? _preferencePageSelected;
    internal event Action<string>? PreferencePageSelected { add => _preferencePageSelected += value; remove => _preferencePageSelected -= value; }
    internal void RaisePreferencePageSelected(string page) => _preferencePageSelected?.Invoke(page);

    internal GuidedTourCoordinator(
        Dispatcher dispatcher,
        Func<bool> isIntelliSensePopupOpen,
        Action clearIntelliSenseStateIfNeeded)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isIntelliSensePopupOpen);
        ArgumentNullException.ThrowIfNull(clearIntelliSenseStateIfNeeded);
        _dispatcher = dispatcher;
        _isIntelliSensePopupOpen = isIntelliSensePopupOpen;
        _clearIntelliSenseStateIfNeeded = clearIntelliSenseStateIfNeeded;
    }
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isIntelliSensePopupOpen;
    private readonly Action _clearIntelliSenseStateIfNeeded;

    internal void UnhighlightAllMenuItems()
    {
        HighlightZTimer?.Stop();
        HighlightZTimer = null;
        foreach (var (_, overlay, _) in HighlightOverlays)
            overlay.Close();
        // Unsubscribe visibility handlers before clearing the list
        foreach (var (el, _, _) in HighlightOverlays)
            el.IsVisibleChanged -= OnTourHighlightElementVisibilityChanged;
        HighlightOverlays.Clear();
        foreach (var w in HighlightTrackedWindows)
            w.LocationChanged -= OnTourHighlightWindowMoved;
        HighlightTrackedWindows.Clear();
    }

    internal void OnTourHighlightWindowMoved(object? sender, EventArgs e) =>
        RefreshTourHighlightRects();

    internal void OnTourHighlightElementVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) return;
        if (sender is not FrameworkElement el) return;
        _ = _dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(UiTimingConstants.TourCalloutVisibilitySettleMs);
            bool isRendered = el.IsVisible
                           || (el.ActualWidth > 0 && el.ActualHeight > 0
                               && PresentationSource.FromVisual(el) is not null);
            if (isRendered) return;

            el.IsVisibleChanged -= OnTourHighlightElementVisibilityChanged;
            var toRemove = HighlightOverlays.Where(r => ReferenceEquals(r.El, el)).ToList();
            foreach (var (_, overlay, _) in toRemove)
                overlay.Close();
            HighlightOverlays.RemoveAll(r => ReferenceEquals(r.El, el));

            if (HighlightOverlays.Count == 0)
            {
                HighlightZTimer?.Stop();
                HighlightZTimer = null;
                foreach (var w in HighlightTrackedWindows)
                    w.LocationChanged -= OnTourHighlightWindowMoved;
                HighlightTrackedWindows.Clear();
            }
        }, DispatcherPriority.Send);
    }

    internal void RefreshTourHighlightRects()
    {
        foreach (var (_, _, reposition) in HighlightOverlays)
            reposition();
    }

    internal void ReassertTourHighlightOverlays()
    {
        if (KeptOpenMenuItems.Any(menuItem => !menuItem.IsSubmenuOpen))
            RecoverKeptOpenTourMenuPath();
        if (KeptOpenIntelliSenseTrigger is not null && !_isIntelliSensePopupOpen())
            RecoverKeptOpenTourIntelliSense();
        RefreshTourHighlightRects();
        foreach (var (_, overlay, _) in HighlightOverlays)
            BringHighlightOverlayToFront(overlay);
    }

    // ── Win32 z-order helper ─────────────────────────────────────────────────
    // Using SetWindowPos with HWND_TOPMOST + SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE
    // re-asserts z-order WITHOUT triggering WPF's per-monitor DPI recalculation,
    // which is the side-effect that caused the overlay to shift when toggling
    // the managed Topmost property.
    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDst, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static readonly IntPtr HWND_TOPMOST_VALUE  = new(-1);
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int  RGN_DIFF       = 4;

    internal static void PositionTourHighlightOverlay(FrameworkElement el, Window overlay)
    {
        Point screenTL;
        Point screenBR;
        DpiScale targetDpi;
        try
        {
            screenTL = el.PointToScreen(new Point(0, 0));
            screenBR = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            targetDpi = VisualTreeHelper.GetDpi(el);
        }
        catch { return; }

        const double inset = 4.5; // pad (2) + stroke thickness (2.5), in target DIPs
        int insetX = Math.Max(1, (int)Math.Ceiling(inset * targetDpi.DpiScaleX));
        int insetY = Math.Max(1, (int)Math.Ceiling(inset * targetDpi.DpiScaleY));
        int left   = (int)Math.Floor(screenTL.X) - insetX;
        int top    = (int)Math.Floor(screenTL.Y) - insetY;
        int width  = Math.Max(1, (int)Math.Ceiling(screenBR.X) - left + insetX);
        int height = Math.Max(1, (int)Math.Ceiling(screenBR.Y) - top + insetY);

        var hwnd = new WindowInteropHelper(overlay).Handle;
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, HWND_TOPMOST_VALUE, left, top, width, height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        // If this is a MenuItem with an open submenu, punch out the submenu overlap
        // so the overlay rect doesn't obscure the submenu popup.
        if (TryGetSubmenuPopupScreenRect(el, out int pLeft, out int pTop, out int pRight, out int pBottom))
        {
            int ix      = Math.Max(0, pLeft - left);
            int iy      = Math.Max(0, pTop - top);
            int iRight  = Math.Min(width, pRight - left);
            int iBottom = Math.Min(height, pBottom - top);
            int iw      = iRight - ix;
            int ih      = iBottom - iy;

            if (iw > 0 && ih > 0)
            {
                SquadDashTrace.Write(TraceCategory.GuidedTour,
                    $"HighlightClip: punching out overlay region ix={ix},iy={iy},iw={iw},ih={ih}");
                var rgn  = CreateRectRgn(0, 0, width, height);
                var clip = CreateRectRgn(ix, iy, iRight, iBottom);
                CombineRgn(rgn, rgn, clip, RGN_DIFF);
                DeleteObject(clip);
                SetWindowRgn(hwnd, rgn, true);
                // Note: SetWindowRgn takes ownership of rgn — do NOT call DeleteObject(rgn).
                return;
            }
        }
        // No submenu overlap — clear any previously applied clip region.
        SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    /// <summary>
    /// Positions a highlight overlay using explicit screen pixel coordinates rather than
    /// deriving them from a single element.  Used for range highlights that span multiple
    /// elements.  DPI scale is taken from <paramref name="anchor"/>.
    /// </summary>
    internal static void PositionTourHighlightOverlayAtScreenRect(
        FrameworkElement anchor, Window overlay,
        double screenLeft, double screenTop, double screenRight, double screenBottom)
    {
        DpiScale dpi;
        try { dpi = VisualTreeHelper.GetDpi(anchor); }
        catch { return; }

        const double inset = 4.5;
        int insetX = Math.Max(1, (int)Math.Ceiling(inset * dpi.DpiScaleX));
        int insetY = Math.Max(1, (int)Math.Ceiling(inset * dpi.DpiScaleY));
        int left   = (int)Math.Floor(screenLeft)  - insetX;
        int top    = (int)Math.Floor(screenTop)   - insetY;
        int width  = Math.Max(1, (int)Math.Ceiling(screenRight)  - left + insetX);
        int height = Math.Max(1, (int)Math.Ceiling(screenBottom) - top  + insetY);

        var hwnd = new WindowInteropHelper(overlay).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST_VALUE, left, top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private static DateTime _clipLastTrace = DateTime.MinValue;

    private static bool TryGetSubmenuPopupScreenRect(FrameworkElement el,
        out int popupLeft, out int popupTop, out int popupRight, out int popupBottom)
    {
        popupLeft = popupTop = popupRight = popupBottom = 0;
        bool trace = (DateTime.UtcNow - _clipLastTrace).TotalSeconds >= 2;
        if (trace) _clipLastTrace = DateTime.UtcNow;

        if (el is not MenuItem mi || !mi.IsSubmenuOpen) return false;
        try
        {
            // Use FindTourMenuPopup which also tries "SubMenuPopup" and visual-tree fallback.
            var popup = FindTourMenuPopup(mi);
            if (popup is not { IsOpen: true })
            {
                if (trace) SquadDashTrace.Write(TraceCategory.GuidedTour,
                    $"HighlightClip: {mi.Name} IsSubmenuOpen=true but popup not found/open (popup={popup?.GetType().Name ?? "null"})");
                return false;
            }

            var child = popup.Child as FrameworkElement;
            if (child is null || child.ActualWidth == 0 || child.ActualHeight == 0)
            {
                if (trace) SquadDashTrace.Write(TraceCategory.GuidedTour,
                    $"HighlightClip: {mi.Name} popup open but child not rendered (child={child?.GetType().Name ?? "null"} W={child?.ActualWidth} H={child?.ActualHeight})");
                return false;
            }

            var tl = child.PointToScreen(new Point(0, 0));
            var br = child.PointToScreen(new Point(child.ActualWidth, child.ActualHeight));
            popupLeft   = (int)Math.Floor(tl.X);
            popupTop    = (int)Math.Floor(tl.Y);
            popupRight  = (int)Math.Ceiling(br.X);
            popupBottom = (int)Math.Ceiling(br.Y);
            return true;
        }
        catch (Exception ex)
        {
            if (trace) SquadDashTrace.Write(TraceCategory.GuidedTour,
                $"HighlightClip: exception for {mi?.Name}: {ex.Message}");
            return false;
        }
    }

    private static void BringHighlightOverlayToFront(Window overlay)
    {
        var hwnd = new WindowInteropHelper(overlay).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST_VALUE, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    internal static Popup? FindTourMenuPopup(MenuItem menuItem)
    {
        menuItem.ApplyTemplate();
        return menuItem.Template?.FindName("SubMenuPopup", menuItem) as Popup
            ?? menuItem.Template?.FindName("PART_Popup", menuItem) as Popup
            ?? VisualTreeSearch.FindChild<Popup>(menuItem);
    }

    internal static FrameworkElement? GetRenderedTourMenuPopupChild(MenuItem menuItem)
    {
        var popup = FindTourMenuPopup(menuItem);
        if (!menuItem.IsSubmenuOpen || popup is not { IsOpen: true, Child: FrameworkElement child })
            return null;

        return child.ActualWidth > 0 && child.ActualHeight > 0 && PresentationSource.FromVisual(child) is not null
            ? child
            : null;
    }

    internal async Task<FrameworkElement?> OpenTourMenuItemAsync(MenuItem menuItem, string name)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            menuItem.ApplyTemplate();
            menuItem.UpdateLayout();
            if (GetRenderedTourMenuPopupChild(menuItem) is { } existingChild)
                return existingChild;

            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler submenuOpened = (_, _) => opened.TrySetResult(true);
            var popupBeforeOpen = FindTourMenuPopup(menuItem);
            EventHandler popupOpened = (_, _) => opened.TrySetResult(true);
            menuItem.SubmenuOpened += submenuOpened;
            if (popupBeforeOpen is not null)
                popupBeforeOpen.Opened += popupOpened;

            try
            {
                // Recover from the inconsistent state where IsSubmenuOpen is true but the
                // Popup HWND was never created (or was already dismissed).
                if (menuItem.IsSubmenuOpen)
                {
                    menuItem.IsSubmenuOpen = false;
                    await menuItem.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                }

                menuItem.IsSubmenuOpen = true;
                await Task.WhenAny(opened.Task, Task.Delay(UiTimingConstants.TourMenuOpenTimeoutMs));
                await menuItem.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                await menuItem.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // Require the popup to remain open across a short settling interval. This
                // catches opens that WPF immediately cancels because menu focus/capture moved.
                await Task.Delay(UiTimingConstants.TourMenuSettleMs);
                if (GetRenderedTourMenuPopupChild(menuItem) is { } child)
                    return child;
            }
            finally
            {
                menuItem.SubmenuOpened -= submenuOpened;
                if (popupBeforeOpen is not null)
                    popupBeforeOpen.Opened -= popupOpened;
            }

            SquadDashTrace.Write(TraceCategory.UI,
                $"[TourOpenMenu] '{name}' did not reach a rendered-open state (attempt {attempt}/{maxAttempts}).");
            menuItem.IsSubmenuOpen = false;
            await Task.Delay(UiTimingConstants.TourBetweenAttemptsMs);
        }

        return null;
    }

    internal void StopKeepingTourMenusOpen()
    {
        MenuTrackingGeneration++;
        ClearTourMenuTracking(closeMenus: true);
    }

    internal void StopKeepingTourIntelliSenseOpen()
    {
        KeptOpenIntelliSenseTrigger = null;
        _clearIntelliSenseStateIfNeeded();
    }

    internal void ClearTourMenuTracking(bool closeMenus)
    {
        foreach (var menuItem in KeptOpenMenuItems)
            menuItem.SubmenuClosed -= OnKeptOpenTourMenuClosed;
        if (closeMenus)
            for (int i = KeptOpenMenuItems.Count - 1; i >= 0; i--)
                KeptOpenMenuItems[i].IsSubmenuOpen = false;
        KeptOpenMenuItems.Clear();
        KeptOpenMenuPath = null;
    }

    internal void KeepTourMenuPathOpen(string path, IReadOnlyList<MenuItem> menuItems)
    {
        ClearTourMenuTracking(closeMenus: false);
        KeptOpenMenuPath = path;
        foreach (var menuItem in menuItems)
        {
            if (KeptOpenMenuItems.Contains(menuItem)) continue;
            KeptOpenMenuItems.Add(menuItem);
            menuItem.SubmenuClosed += OnKeptOpenTourMenuClosed;
        }
    }

    private void OnKeptOpenTourMenuClosed(object sender, RoutedEventArgs e) =>
        RecoverKeptOpenTourMenuPath();

    internal void RecoverKeptOpenTourIntelliSense()
    {
        if (IntelliSenseRecoveryRunning || KeptOpenIntelliSenseTrigger is null) return;
        if (_isIntelliSensePopupOpen()) return;
        string trigger = KeptOpenIntelliSenseTrigger;
        IntelliSenseRecoveryRunning = true;
        _ = _dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (KeptOpenIntelliSenseTrigger != trigger) return;
                SquadDashTrace.Write(TraceCategory.UI,
                    $"[TourIntelliSense] IntelliSense closed during tour step; reopening (trigger={trigger}).");
                var commandName = trigger == "slash" ? "ShowSlashIntelliSense" : "ShowAtIntelliSense";
                _ = CommandRegistry.ExecuteAsync(commandName);
            }
            finally
            {
                IntelliSenseRecoveryRunning = false;
            }
        }, DispatcherPriority.Send);
    }

    internal void RecoverKeptOpenTourMenuPath()
    {
        if (MenuRecoveryRunning || string.IsNullOrWhiteSpace(KeptOpenMenuPath)) return;
        string path = KeptOpenMenuPath;
        int generation = MenuTrackingGeneration;
        MenuRecoveryRunning = true;
        _ = _dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!string.Equals(KeptOpenMenuPath, path, StringComparison.Ordinal)) return;
                SquadDashTrace.Write(TraceCategory.UI,
                    $"[TourOpenMenu] Menu path '{path}' closed during its tour step; reopening it.");
                await CommandRegistry.ExecuteAsync($"OpenMenu: {path}");
                if (MenuTrackingGeneration != generation)
                {
                    ClearTourMenuTracking(closeMenus: true);
                    return;
                }
                ReassertTourHighlightOverlays();
            }
            finally
            {
                MenuRecoveryRunning = false;
            }
        }, DispatcherPriority.Send);
    }
}

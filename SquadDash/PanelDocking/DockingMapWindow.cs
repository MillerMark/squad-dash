#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SquadDash.PanelDocking;

internal sealed class DockingMapWindow : Window
{
    private readonly DockingMapViewModel _viewModel;
    private readonly PanelDockingService _dockingService;
    private readonly string _workspacePath;
    private readonly Brush? _hoverBrush;
    private readonly (DockZone Zone, int Order, SyntheticInsertKind InsertKind)? _targetSlot;

    private Window? _previewOverlay;
    private Window? _dimOverlay;

    public DockingMapWindow(
        DockingMapViewModel viewModel,
        PanelDockingService dockingService,
        string workspacePath,
        ResourceDictionary appResources,
        Brush? hoverBrush = null,
        (DockZone Zone, int Order, SyntheticInsertKind InsertKind)? targetSlot = null)
    {
        _viewModel      = viewModel;
        _dockingService = dockingService;
        _workspacePath  = workspacePath;
        _hoverBrush     = hoverBrush;
        _targetSlot     = targetSlot;

        WindowStyle      = WindowStyle.None;
        AllowsTransparency = true;
        Background       = Brushes.Transparent;
        Topmost          = true;
        ShowInTaskbar    = false;
        ResizeMode       = ResizeMode.NoResize;
        SizeToContent    = SizeToContent.WidthAndHeight;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        // Wire Deactivated only after the first Activated — wiring it in the constructor
        // causes an immediate Close() because WPF fires WM_ACTIVATE during Show() itself.
        void OnFirstActivated(object? s, EventArgs e)
        {
            Activated   -= OnFirstActivated;
            Deactivated += (_, _) => { if (IsVisible) Dispatcher.InvokeAsync(Close); };
        }
        Activated += OnFirstActivated;

        // Close the preview overlay whenever this window closes.
        Closed += (_, _) =>
        {
            _previewOverlay?.Close();
            _previewOverlay = null;

            _dimOverlay?.Close();
            _dimOverlay = null;

            if (Owner is { } closingOwner)
            {
                closingOwner.LocationChanged -= OnOwnerPositionChanged;
                closingOwner.SizeChanged     -= OnOwnerSizeChanged;
            }
        };

        BuildUI(appResources);

        // Log a layout snapshot so the full panel state is visible in the Docking trace
        // before any slot-hover events fire.
        var sourcePanelId = viewModel.Slots.FirstOrDefault()?.SourcePanelId ?? "(unknown)";
        _dockingService.LogLayoutSnapshot(sourcePanelId);
    }

    private void BuildUI(ResourceDictionary appResources)
    {
        bool isDark = AgentStatusCard.IsDarkTheme;

        // Grounding = the "ground" of the theme (black in dark, white in light).
        // Polar     = the contrasting pole (white in dark, black in light).
        Color groundingColor = isDark ? Colors.Black : Colors.White;
        Color polarColor     = isDark ? Colors.White : Colors.Black;

        // Use AppSurface (tinted, matches window background) and InputBorder (matches PromptBorder style).
        Color bgColor = appResources.Contains("AppSurface") && appResources["AppSurface"] is SolidColorBrush appsurface
            ? appsurface.Color
            : (isDark ? Color.FromRgb(26, 23, 20) : Color.FromRgb(244, 239, 231));

        Color borderColor = appResources.Contains("InputBorder") && appResources["InputBorder"] is SolidColorBrush ib
            ? ib.Color
            : (isDark ? Color.FromRgb(62, 54, 48) : Color.FromRgb(216, 204, 186));

        var root = new Border
        {
            Background      = new SolidColorBrush(bgColor),
            BorderBrush     = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(PopupPadding),
            Effect          = new DropShadowEffect
            {
                BlurRadius  = 8,
                Opacity     = 0.35,
                ShadowDepth = 2,
                Direction   = 270,
            }
        };

        var canvas = new Canvas
        {
            Width  = _viewModel.PopupWidth,
            Height = _viewModel.PopupHeight,
        };

        foreach (var slot in _viewModel.Slots)
        {
            var el = BuildSlotElement(slot, groundingColor, polarColor, isDark);
            Canvas.SetLeft(el, slot.X);
            Canvas.SetTop(el,  slot.Y);
            canvas.Children.Add(el);
        }

        // ── Section labels ───────────────────────────────────────────────────
        const double LabelWidth = 60;
        if (_viewModel.HasLeftSection)
            canvas.Children.Add(MakeSectionLabel("Left:", _viewModel.LeftSectionCenterX, LabelWidth, polarColor));
        canvas.Children.Add(MakeSectionLabel("Top:", _viewModel.TopSectionCenterX, LabelWidth, polarColor));
        if (_viewModel.HasRightSection)
            canvas.Children.Add(MakeSectionLabel("Right:", _viewModel.RightSectionCenterX, LabelWidth, polarColor));

        root.Child = canvas;
        Content    = root;
    }

    private const double PopupPadding = 6;

    private UIElement BuildSlotElement(SlotButtonViewModel slot, Color groundingColor, Color polarColor, bool isDark)
    {
        // ── Separator (decorative vertical pill) ────────────────────────────
        if (slot.IsSeparator)
        {
            return new Border
            {
                Width        = slot.Width,
                Height       = slot.Height,
                Background   = MakeBrush(polarColor, isDark ? 0.15 : 0.30),
                CornerRadius = new CornerRadius(slot.Width / 2.0),
            };
        }

        // ── Source panel (non-interactive "you are here" tile) ──────────────
        if (slot.IsSourcePanel)
        {
            return new Border
            {
                Width           = slot.Width,
                Height          = slot.Height,
                Background      = MakeBrush(groundingColor, 0.20),
                BorderBrush     = MakeBrush(polarColor, 0.10),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(3),
            };
        }

        // ── Target button (interactive drop target) ─────────────────────────
        bool isPlaybackTarget = _targetSlot.HasValue
            && slot.InsertKind == _targetSlot.Value.InsertKind
            && slot.TargetZone  == _targetSlot.Value.Zone
            && slot.TargetOrder == _targetSlot.Value.Order;

        var normalBg     = isPlaybackTarget
            ? MakeBrush(Colors.Orange, 0.25)
            : MakeBrush(groundingColor, 0.70);
        var normalBorder = isPlaybackTarget
            ? MakeBrush(Colors.Orange, 0.85)
            : MakeBrush(polarColor, 0.10);
        var hoverBg      = MakeBrush(groundingColor, 0.90);
        var hoverBorder  = MakeBrush(polarColor,     0.50);

        var border = new Border
        {
            Width           = slot.Width,
            Height          = slot.Height,
            Background      = normalBg,
            BorderBrush     = normalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Cursor          = Cursors.Hand,
        };

        border.MouseEnter += (_, _) =>
        {
            border.Background  = hoverBg;
            border.BorderBrush = hoverBorder;
            OnSlotHover(slot);
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background  = normalBg;
            border.BorderBrush = normalBorder;
            HidePreview();
        };
        if (isPlaybackTarget)
            border.ToolTip = ToolTipHelper.MakeThemedToolTip($"Playback target: {DockingLayoutEngine.GetZoneDisplayName(slot.TargetZone)} @ {slot.TargetOrder}");
        border.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                _dockingService.MovePanel(slot.SourcePanelId, slot.TargetZone, slot.TargetOrder, slot.InsertKind);
                if (!string.IsNullOrEmpty(_workspacePath))
                    _dockingService.SaveLayout(_workspacePath);
            }
            catch { /* swallow — best effort */ }
            Close();
        };

        return border;
    }

    private UIElement MakeSectionLabel(string text, double centerX, double width, Color polarColor)
    {
        double fontSize = Application.Current?.TryFindResource("FontSizeXSmall") is double d ? d : 10.0;
        var lbl = new TextBlock
        {
            Text          = text,
            Width         = width,
            TextAlignment = TextAlignment.Center,
            FontSize      = fontSize,
            Foreground    = MakeBrush(polarColor, 0.45),
        };
        Canvas.SetLeft(lbl, centerX - width / 2);
        Canvas.SetTop(lbl, 0);
        return lbl;
    }

    private static SolidColorBrush MakeBrush(Color color, double opacity) =>
        new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B));

    private void OnSlotHover(SlotButtonViewModel slot)
    {
        var rect = _dockingService.GetSlotScreenRect(slot);
        if (rect.IsEmpty)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"OnSlotHover: rect is Empty — hiding preview. zone={slot.TargetZone} order={slot.TargetOrder} src={slot.SourcePanelId}");
            HidePreview(); return;
        }
        SquadDashTrace.Write(TraceCategory.Docking,
            $"OnSlotHover: showing preview rect={rect} zone={slot.TargetZone} order={slot.TargetOrder}");
        ShowPreview(rect);
    }

    private void ShowPreview(Rect screenRect)
    {
        if (_hoverBrush is null) return;
        EnsurePreviewOverlay();
        _previewOverlay!.Left   = screenRect.Left;
        _previewOverlay!.Top    = screenRect.Top;
        _previewOverlay!.Width  = Math.Max(screenRect.Width,  1);
        _previewOverlay!.Height = Math.Max(screenRect.Height, 1);
        if (!_previewOverlay.IsVisible)
            _previewOverlay.Show();
        // Re-assert topmost so the docking map stays above the preview overlay.
        Topmost = false;
        Topmost = true;
    }

    private void HidePreview() => _previewOverlay?.Hide();

    private void EnsurePreviewOverlay()
    {
        if (_previewOverlay is not null) return;

        Color accent = _hoverBrush is SolidColorBrush scb ? scb.Color : Color.FromRgb(100, 160, 255);

        // Layer 1 — outer bloom: large negative margin, very blurry, low opacity
        Color bloom = BoostColor(accent, targetSaturation: 0.9, targetLightness: 0.50);
        var outerBloom = new Border
        {
            BorderThickness = new Thickness(12),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(100, bloom.R, bloom.G, bloom.B)),
            CornerRadius    = new CornerRadius(14),
            Margin          = new Thickness(-18),
            Effect          = new System.Windows.Media.Effects.BlurEffect { Radius = 18 },
        };

        // Layer 2 — mid halo: medium border, full saturation, medium blur
        Color mid = BoostColor(accent, targetSaturation: 1.0, targetLightness: 0.58);
        var midHalo = new Border
        {
            BorderThickness = new Thickness(4),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(220, mid.R, mid.G, mid.B)),
            CornerRadius    = new CornerRadius(9),
            Margin          = new Thickness(-6),
            Effect          = new System.Windows.Media.Effects.BlurEffect { Radius = 6 },
        };

        // Layer 3 — inner ring: crisp near-white tinted border, no blur, light accent fill
        Color hot  = BoostColor(accent, targetSaturation: 0.6, targetLightness: 0.88);
        Color fill = BoostColor(accent, targetSaturation: 1.0, targetLightness: 0.50);
        var innerRing = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(255, hot.R, hot.G, hot.B)),
            Background      = new SolidColorBrush(Color.FromArgb(60, fill.R, fill.G, fill.B)),
            CornerRadius    = new CornerRadius(6),
        };

        var stack = new Grid { ClipToBounds = false };
        stack.Children.Add(outerBloom);
        stack.Children.Add(midHalo);
        stack.Children.Add(innerRing);

        _previewOverlay = new Window
        {
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = Brushes.Transparent,
            Topmost               = true,
            ShowInTaskbar         = false,
            ResizeMode            = ResizeMode.NoResize,
            ShowActivated         = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content               = stack,
        };
    }

    private static Color BoostColor(Color c, double targetSaturation = 1.0, double targetLightness = 0.55)
    {
        var (h, _, _) = RgbToHsl(c);
        return HslToRgb(h, targetSaturation, targetLightness, 255);
    }

    /// <summary>
    /// Derives a border brush from the fill brush — same hue, but shifted toward
    /// higher contrast against the background (brighter in dark theme, darker in light theme).
    /// </summary>
    internal static SolidColorBrush? DeriveBorderBrush(Brush? fillBrush)
    {
        if (fillBrush is not SolidColorBrush scb) return null;
        var c = scb.Color;
        var (h, s, l) = RgbToHsl(c);
        double perceivedL = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        double newL = perceivedL < 0.45
            ? Math.Min(l + 0.30, 0.95)   // dark theme: brighter
            : Math.Max(l - 0.25, 0.05);  // light theme: darker
        double newS = Math.Min(s + 0.10, 1.0);
        return new SolidColorBrush(HslToRgb(h, newS, newL, 210));
    }

    private static (double H, double S, double L) RgbToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l   = (max + min) / 2.0;
        if (max == min) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if      (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else               h = (r - g) / d + 4;
        return (h / 6.0, s, l);
    }

    private static Color HslToRgb(double h, double s, double l, byte a = 255)
    {
        if (s == 0) { byte v = (byte)(l * 255); return Color.FromArgb(a, v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromArgb(a,
            (byte)(Hue2Rgb(p, q, h + 1.0 / 3) * 255),
            (byte)(Hue2Rgb(p, q, h)            * 255),
            (byte)(Hue2Rgb(p, q, h - 1.0 / 3) * 255));
    }

    private static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private void ShowDimOverlay()
    {
        var owner = Owner;
        if (owner is null) return;

        var bounds = GetOwnerLogicalRect(owner);

        _dimOverlay = new Window
        {
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            Topmost               = false,
            ShowInTaskbar         = false,
            ResizeMode            = ResizeMode.NoResize,
            ShowActivated         = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Owner                 = owner,
            Left                  = bounds.Left,
            Top                   = bounds.Top,
            Width                 = bounds.Width,
            Height                = bounds.Height,
        };

        owner.LocationChanged += OnOwnerPositionChanged;
        owner.SizeChanged     += OnOwnerSizeChanged;

        _dimOverlay.Show();
        MakeDimOverlayClickThrough(_dimOverlay);
    }

    private void OnOwnerPositionChanged(object? sender, EventArgs e)
    {
        if (_dimOverlay is null || Owner is null) return;
        var bounds = GetOwnerLogicalRect(Owner);
        _dimOverlay.Left   = bounds.Left;
        _dimOverlay.Top    = bounds.Top;
        _dimOverlay.Width  = bounds.Width;
        _dimOverlay.Height = bounds.Height;
    }

    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_dimOverlay is null || Owner is null) return;
        var bounds = GetOwnerLogicalRect(Owner);
        _dimOverlay.Left   = bounds.Left;
        _dimOverlay.Top    = bounds.Top;
        _dimOverlay.Width  = bounds.Width;
        _dimOverlay.Height = bounds.Height;
    }

    /// <summary>
    /// Returns the owner window's bounds in WPF logical units (DIPs) by reading the true
    /// physical rect via Win32 GetWindowRect and converting through the DPI transform.
    /// This is correct on high-DPI monitors where Window.Left/Top can differ from the
    /// physical screen position when DPI scaling is active.
    /// </summary>
    private static Rect GetOwnerLogicalRect(Window owner)
    {
        var helper = new WindowInteropHelper(owner);
        if (helper.Handle != IntPtr.Zero && GetWindowRect(helper.Handle, out DimOverlayRECT r))
        {
            var src = PresentationSource.FromVisual(owner);
            if (src?.CompositionTarget is { } ct)
            {
                var transform   = ct.TransformFromDevice;
                var topLeft     = transform.Transform(new Point(r.Left, r.Top));
                var bottomRight = transform.Transform(new Point(r.Right, r.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }
        // Fallback: use WPF logical properties directly
        return new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out DimOverlayRECT rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DimOverlayRECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

    private static void MakeDimOverlayClickThrough(Window w)
    {
        const int GWL_EXSTYLE       = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        var helper = new WindowInteropHelper(w);
        helper.EnsureHandle();
        int style = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        SetWindowLong(helper.Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// Opens the popup positioned so the source panel slot is centered on the given screen point.
    /// Clamps to keep the window fully within the active monitor's working area.
    /// </summary>
    public void ShowAtScreenPoint(Point clickScreenPoint)
    {
        // clickScreenPoint is in physical device pixels (from PointToScreen on the owner window).
        // Window.Left/Top are in WPF logical units (DIPs). Convert physical → logical using the
        // owner window's DPI transform so positioning is correct on high-DPI monitors.
        double sx = 1, sy = 1;
        if (Owner is { } owner)
        {
            var src = PresentationSource.FromVisual(owner);
            if (src?.CompositionTarget is { } ct)
            {
                var m = ct.TransformToDevice;
                sx = m.M11 > 0 ? m.M11 : 1;
                sy = m.M22 > 0 ? m.M22 : 1;
            }
        }
        double logicalX = clickScreenPoint.X / sx;
        double logicalY = clickScreenPoint.Y / sy;

        Left = logicalX - _viewModel.SourceSlotCenterX;
        Top  = logicalY - _viewModel.SourceSlotCenterY;

        SquadDashTrace.Write(TraceCategory.Docking,
            $"ShowAtScreenPoint: clickPhys=({clickScreenPoint.X:F0},{clickScreenPoint.Y:F0}) " +
            $"dpiScale=({sx:F3},{sy:F3}) clickLogical=({logicalX:F0},{logicalY:F0}) " +
            $"srcCenter=({_viewModel.SourceSlotCenterX:F0},{_viewModel.SourceSlotCenterY:F0}) " +
            $"→ Left={Left:F0} Top={Top:F0}");

        ShowDimOverlay();
        Show();

        // Clamp to monitor work area using the project's existing helper
        WindowPlacementHelper.EnsureOnScreen(this);
    }

    /// <summary>
    /// Opens the popup with its top edge at <paramref name="panelScreenTop"/> + 40px,
    /// centered horizontally over the panel. Used by docking test playback where the
    /// anchor should be the panel border, not a mouse click point.
    /// </summary>
    public void ShowAtPanelTopCenter(double panelScreenCenterX, double panelScreenTop)
    {
        // Set Top before Show so WPF uses it as the initial position during SizeToContent layout.
        Top  = panelScreenTop + 40;
        ShowDimOverlay();
        Show();
        // ActualWidth is valid after Show() — center horizontally now.
        Left = panelScreenCenterX - ActualWidth / 2;
        WindowPlacementHelper.EnsureOnScreen(this);
    }
}

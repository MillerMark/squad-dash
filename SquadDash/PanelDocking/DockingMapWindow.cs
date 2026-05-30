#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SquadDash.PanelDocking;

internal sealed class DockingMapWindow : Window
{
    private readonly DockingMapViewModel _viewModel;
    private readonly PanelDockingService _dockingService;
    private readonly string _workspacePath;
    private readonly Brush? _hoverBrush;

    private Window? _previewOverlay;

    public DockingMapWindow(
        DockingMapViewModel viewModel,
        PanelDockingService dockingService,
        string workspacePath,
        ResourceDictionary appResources,
        Brush? hoverBrush = null)
    {
        _viewModel      = viewModel;
        _dockingService = dockingService;
        _workspacePath  = workspacePath;
        _hoverBrush     = hoverBrush;

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
        Closed += (_, _) => { _previewOverlay?.Close(); _previewOverlay = null; };

        BuildUI(appResources);

        // Log a layout snapshot so the full panel state is visible in the Docking trace
        // before any slot-hover events fire.
        var sourcePanelId = viewModel.Slots.FirstOrDefault()?.SourcePanelId ?? "(unknown)";
        _dockingService.LogLayoutSnapshot(sourcePanelId);
    }

    private void BuildUI(ResourceDictionary appResources)
    {
        var root = new Border
        {
            Background = appResources.Contains("ChromeSurface")
                ? (Brush)appResources["ChromeSurface"]
                : new SolidColorBrush(Color.FromRgb(40, 40, 44)),
            CornerRadius = new CornerRadius(4),
            Padding  = new Thickness(PopupPadding),
            Width    = _viewModel.PopupWidth,
            Height   = _viewModel.PopupHeight,
            Effect   = new DropShadowEffect
            {
                BlurRadius  = 8,
                Opacity     = 0.35,
                ShadowDepth = 2,
                Direction   = 270,
            }
        };

        var canvas = new Canvas
        {
            Width  = _viewModel.PopupWidth  - PopupPadding * 2,
            Height = _viewModel.PopupHeight - PopupPadding * 2,
        };

        foreach (var slot in _viewModel.Slots)
        {
            var btn = BuildSlotButton(slot, appResources);
            Canvas.SetLeft(btn, slot.X);
            Canvas.SetTop(btn,  slot.Y);
            canvas.Children.Add(btn);
        }

        root.Child = canvas;
        Content    = root;
    }

    private const double PopupPadding = 8;

    private Button BuildSlotButton(SlotButtonViewModel slot, ResourceDictionary appResources)
    {
        var btn = new Button
        {
            Width     = slot.Width,
            Height    = slot.Height,
            IsEnabled = !slot.IsSourcePanel,
            FontSize  = 11,
        };

        if (appResources.Contains("DockSlotButtonStyle"))
            btn.Style = (Style)appResources["DockSlotButtonStyle"];

        Brush chromeSurface = GetRes(appResources, "ChromeSurface", Brushes.DimGray);
        Brush labelText     = GetRes(appResources, "LabelText",     Brushes.White);
        Brush subtleText    = GetRes(appResources, "SubtleText",    Brushes.DarkGray);
        Brush panelBorder   = GetRes(appResources, "RosterPanelBorder", Brushes.SlateGray);

        btn.Background      = chromeSurface;
        btn.BorderBrush     = panelBorder;
        btn.BorderThickness = new Thickness(1);

        if (slot.IsSourcePanel)
        {
            btn.Foreground = subtleText;
        }
        else
        {
            btn.Foreground = labelText;
            btn.Click += (_, _) =>
            {
                try
                {
                    _dockingService.MovePanel(slot.SourcePanelId, slot.TargetZone, slot.TargetOrder);
                    if (!string.IsNullOrEmpty(_workspacePath))
                        _dockingService.SaveLayout(_workspacePath);
                }
                catch { /* swallow — best effort */ }
                Close();
            };

            // Show/hide a semi-transparent overlay on the main window at the target location.
            btn.MouseEnter += (_, _) => OnSlotHover(slot);
            btn.MouseLeave += (_, _) => HidePreview();
        }

        return btn;
    }

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

        var previewBorder = new Border
        {
            Background      = _hoverBrush,
            BorderBrush     = DeriveBorderBrush(_hoverBrush),
            BorderThickness = new Thickness(2),
            CornerRadius    = new CornerRadius(6),
        };

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
            Content               = previewBorder,
        };
    }

    /// <summary>
    /// Derives a border brush from the fill brush — same hue, but shifted toward
    /// higher contrast against the background (brighter in dark theme, darker in light theme).
    /// </summary>
    private static SolidColorBrush? DeriveBorderBrush(Brush? fillBrush)
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

    private static Brush GetRes(ResourceDictionary r, string key, Brush fallback) =>
        r.Contains(key) ? (Brush)r[key] : fallback;

    /// <summary>
    /// Opens the popup positioned so the source panel slot is centered on the given screen point.
    /// Clamps to keep the window fully within the active monitor's working area.
    /// </summary>
    public void ShowAtScreenPoint(Point clickScreenPoint)
    {
        // Position so source slot center aligns with click point
        Left = clickScreenPoint.X - _viewModel.SourceSlotCenterX;
        Top  = clickScreenPoint.Y - _viewModel.SourceSlotCenterY;

        Show();

        // Clamp to monitor work area using the project's existing helper
        WindowPlacementHelper.EnsureOnScreen(this);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Base class for SquadDash popup windows that use the custom owner-drawn chrome
/// (WindowStyle.None + WindowChrome + themed outer border) instead of the
/// standard OS title bar.
/// </summary>
internal class ChromedWindow : Window {

    /// <summary>
    /// Height of the floating close (✕) button.  Subclasses should ensure their
    /// top-most content row starts at least this many pixels below the border edge
    /// so the close button does not cover interactive controls.
    /// </summary>
    protected const double CloseButtonHeight = 34;

    // Hover border state — populated by ApplyOuterBorder, consumed by the WndProc hook.
    private Border?          _hoverBorderInstance;
    private SolidColorBrush? _hoverBrushInstance;
    private bool             _hoverActive;

    private const int WM_MOUSEMOVE  = 0x0200;
    private const int WM_MOUSELEAVE = 0x02A3;

    /// <summary>
    /// Applies the standard SquadDash custom chrome to the window.
    /// </summary>
    /// <param name="captionHeight">
    /// Height of the drag-to-move caption strip at the top. Default 36px suits
    /// windows with a full header row. Use a smaller value (e.g. 28) for compact
    /// ToolWindow-style popups.
    /// </param>
    /// <param name="resizeMode">Defaults to CanResizeWithGrip.</param>
    /// <param name="resizeBorderThickness">
    /// Width of the invisible resize hit-test border. Default 6. Pass 0 for
    /// non-resizable windows (ResizeMode.NoResize) to suppress the hit-test region.
    /// </param>
    protected ChromedWindow(
        double     captionHeight          = 36,
        ResizeMode resizeMode             = ResizeMode.CanResizeWithGrip,
        double     resizeBorderThickness  = 6) {

        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        ResizeMode         = resizeMode;

        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight         = captionHeight,
            ResizeBorderThickness = new Thickness(resizeBorderThickness),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.DisableRoundedCorners(handle);
            // Hook Win32 messages to get reliable mouse-enter/leave for the entire
            // window including the WindowChrome caption zone (which suppresses WPF
            // hit-test events and causes Window.MouseEnter to miss fast entries).
            HwndSource.FromHwnd(handle)?.AddHook(HoverWndProc);
        };
    }

    private IntPtr HoverWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEMOVE:
                if (!_hoverActive && _hoverBorderInstance != null && _hoverBrushInstance != null)
                {
                    _hoverActive = true;
                    _hoverBorderInstance.BorderBrush = _hoverBrushInstance;
                }
                // Re-arm the leave notification every move so tracking stays active.
                NativeMethods.TrackMouseLeave(hwnd);
                break;

            case WM_MOUSELEAVE:
                _hoverActive = false;
                if (_hoverBorderInstance != null)
                    _hoverBorderInstance.BorderBrush = Brushes.Transparent;
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Creates the standard themed outer border, sets it as the window Content,
    /// and returns it so the subclass can set its Child.
    /// Call this once in the subclass constructor after setting window dimensions.
    /// </summary>
    /// <param name="backgroundResource">
    /// Resource key for the border background. Defaults to <c>"AppSurface"</c>.
    /// Pass a different key (e.g. <c>"PopupSurface"</c>) for windows that use
    /// an alternative surface colour.
    /// </param>
    /// <param name="titleText">
    /// When non-null, a visible title bar row is rendered at the top of the window
    /// containing this text (left-aligned) and the close button (right-aligned).
    /// The returned border is the <em>inner content area</em> below the title bar,
    /// not the outer border itself.  The <see cref="WindowChrome.CaptionHeight"/>
    /// should be set to <see cref="CloseButtonHeight"/> so the title bar is the
    /// drag zone.
    /// </param>
    protected Border ApplyOuterBorder(string backgroundResource = "AppSurface", string? titleText = null) {
        var outerBorder = new Border {
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(4),
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty,  backgroundResource);
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        // Thin 1px overlay border that lights up when the mouse is over the window.
        // Driven by Win32 WM_MOUSEMOVE/WM_MOUSELEAVE via HoverWndProc for reliability
        // across the entire window including the WindowChrome caption zone.
        var hoverBorder = new Border {
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(4),
            IsHitTestVisible = false,
            BorderBrush      = Brushes.Transparent,
        };

        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) => {
            Loaded -= loadedHandler;
            try {
                var glowColor = ((SolidColorBrush)FindResource("WindowBorderGlow")).Color;
                var restBrush = (SolidColorBrush)FindResource("PanelBorder");
                WindowOpenGlow.Animate(outerBorder, glowColor, restBrush);

                // Wire up the Win32 hover tracking fields.
                _hoverBorderInstance = hoverBorder;
                _hoverBrushInstance  = new SolidColorBrush(Color.FromArgb(128, glowColor.R, glowColor.G, glowColor.B));
            }
            catch { /* silently skip if resources are unavailable (designer / test context) */ }
        };
        Loaded += loadedHandler;

        var closeBtnText= new TextBlock {
            Text                = "\u2715",
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        closeBtnText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");

        var closeBtn = new Button {
            Width   = 38,
            Padding = new Thickness(0),
            ToolTip = "Close",
            Content = closeBtnText,
        };
        closeBtn.SetResourceReference(Button.StyleProperty, "CaptionCloseButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        closeBtn.Click += (_, _) => Close();

        if (titleText is not null)
        {
            // Visible title bar: title text (left) + close button (right), separated from
            // content by a 1px bottom border.  The title row is the drag zone — do NOT
            // mark it hit-test-visible in chrome.
            var titleLabel = new TextBlock {
                VerticalAlignment = VerticalAlignment.Center,
                Padding           = new Thickness(10, 0, 8, 0),
                FontWeight        = FontWeights.SemiBold,
                TextTrimming      = TextTrimming.CharacterEllipsis,
            };
            titleLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });
            titleLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            titleLabel.SetResourceReference(TextBlock.ForegroundProperty,  "LabelText");

            closeBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            closeBtn.VerticalAlignment   = VerticalAlignment.Stretch;

            var titleRow = new DockPanel { Height = CloseButtonHeight };
            DockPanel.SetDock(closeBtn, Dock.Right);
            titleRow.Children.Add(closeBtn);
            titleRow.Children.Add(titleLabel);

            var titleBar = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
            titleBar.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
            titleBar.Child = titleRow;

            var contentHolder = new Border();
            WindowChrome.SetIsHitTestVisibleInChrome(contentHolder, true);

            var layout = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(titleBar, Dock.Top);
            layout.Children.Add(titleBar);
            layout.Children.Add(contentHolder);

            outerBorder.Child = layout;

            var overlay = new Grid();
            overlay.Children.Add(outerBorder);
            overlay.Children.Add(hoverBorder);
            Content = overlay;
            return contentHolder;
        }

        // Original path: floating close button overlay (no visible title row).
        closeBtn.Width               = 38;
        closeBtn.Height              = CloseButtonHeight;
        closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
        closeBtn.VerticalAlignment   = VerticalAlignment.Top;

        var grid = new Grid();
        grid.Children.Add(outerBorder);
        grid.Children.Add(hoverBorder);
        grid.Children.Add(closeBtn);
        Content = grid;
        return outerBorder;
    }
}

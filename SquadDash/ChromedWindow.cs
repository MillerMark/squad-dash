using System.Windows;
using System.Windows.Controls;
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
    /// Applies the standard SquadDash custom chrome to the window.
    /// </summary>
    /// <param name="captionHeight">
    /// Height of the drag-to-move caption strip at the top. Default 36px suits
    /// windows with a full header row. Use a smaller value (e.g. 28) for compact
    /// ToolWindow-style popups.
    /// </param>
    /// <param name="resizeMode">Defaults to CanResizeWithGrip.</param>
    /// <param name="resizeBorderThickness">
    /// Width of the invisible resize hit-test border. Default 4. Pass 0 for
    /// non-resizable windows (ResizeMode.NoResize) to suppress the hit-test region.
    /// </param>
    protected ChromedWindow(
        double     captionHeight          = 36,
        ResizeMode resizeMode             = ResizeMode.CanResizeWithGrip,
        double     resizeBorderThickness  = 4) {

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

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);
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
    protected Border ApplyOuterBorder(string backgroundResource = "AppSurface") {
        var border = new Border {
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(4),
        };
        border.SetResourceReference(Border.BackgroundProperty,    backgroundResource);
        border.SetResourceReference(Border.BorderBrushProperty,   "PanelBorder");
        Content = border;
        return border;
    }
}

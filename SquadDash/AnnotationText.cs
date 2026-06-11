using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Runtime state for a single text label annotation placed on the annotation canvas.
/// Used by <see cref="ClipboardImageEditorWindow"/>.
/// </summary>
internal sealed class AnnotationText
{
    /// <summary>Position and approximate rendered size in canvas logical coordinates.</summary>
    public Rect Bounds { get; set; }

    /// <summary>The annotation text content.</summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Current rendered font size (possibly shrunk from <see cref="MaxFontSize"/> to keep
    /// text within canvas bounds).
    /// </summary>
    public double FontSize { get; set; } = MaxFontSize;

    /// <summary>Text foreground colour.</summary>
    public Color TextColor { get; set; } = Colors.White;

    /// <summary>Maximum font size (pt) used when text fits comfortably.</summary>
    public const double MaxFontSize = 18.0;

    /// <summary>Minimum font size (pt) — text will not shrink below this.</summary>
    public const double MinFontSize = 12.0;

    /// <summary>The primary TextBlock shown on the canvas when not being edited.</summary>
    public TextBlock? Display { get; set; }

    /// <summary>Background fill color. <see cref="Colors.Transparent"/> means no background.</summary>
    public Color BackgroundColor { get; set; } = Colors.Black;
}

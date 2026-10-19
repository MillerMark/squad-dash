using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash;

/// <summary>
/// Immutable description of the colors an embedded code-region editor should paint with for the
/// current theme. Produced by an <see cref="IRegionEditorPaletteSource"/> and pushed onto the
/// editor through <see cref="IRegionCodeEditor.ApplyPalette"/> on every theme switch.
/// <para>
/// AvalonEdit's surface/text/highlight colors are NOT driven by WPF <c>DynamicResource</c>, so
/// the theme must be re-applied imperatively (see the AvalonEdit integration decision,
/// AVALONEDIT-20261014-001). Highlight colors may be keyed by grammar/color name, color name,
/// or the semantic role keys supplied by <see cref="RegionHighlightingPalettes"/>.
/// </para>
/// </summary>
public sealed record RegionEditorPalette(
    Brush Surface,
    Brush Text,
    IReadOnlyDictionary<string, HighlightingColor> HighlightColors);

/// <summary>
/// Produces a <see cref="RegionEditorPalette"/> from the currently active theme. Kept pluggable
/// (per the AvalonEdit integration contract) so a future host can supply its own palette without
/// touching the region's input/nav/copy logic. The shipped implementation is
/// <see cref="ThemeResourcePaletteSource"/>.
/// </summary>
public interface IRegionEditorPaletteSource
{
    RegionEditorPalette CreatePalette();
}

/// <summary>
/// Default palette source: reads the active theme's <c>CodeSurface</c>/<c>CodeText</c> brushes
/// directly from the application resources at call time. Because it reads the resources that are
/// live AFTER a theme dictionary swap (rather than relying on deferred
/// <see cref="FrameworkElement.SetResourceReference"/> resolution), the push is correct
/// synchronously from inside <c>MainWindow.ApplyTheme</c>.
/// </summary>
public sealed class ThemeResourcePaletteSource : IRegionEditorPaletteSource
{
    private readonly RegionHighlightingEngine _highlighting;

    public ThemeResourcePaletteSource(RegionHighlightingEngine? highlighting = null) =>
        _highlighting = highlighting ?? RegionHighlightingEngine.Default;

    public RegionEditorPalette CreatePalette()
    {
        var app = Application.Current;
        var surface = (app?.TryFindResource("CodeSurface") as Brush) ?? Brushes.Transparent;
        var text = (app?.TryFindResource("CodeText") as Brush) ?? SystemColors.ControlTextBrush;
        var isDark = surface is SolidColorBrush brush && RegionHighlightingPalettes.IsDark(brush.Color);
        return new RegionEditorPalette(surface, text, _highlighting.CreatePaletteColors(isDark));
    }
}

/// <summary>
/// Narrow seam abstracting the editor control hosted inside an
/// <see cref="EmbeddedCodeRegionBlock"/>, so the region's mouse/caret/selection, fenced
/// copy/cut, boundary-exit navigation, content-sync and focus logic depend on this interface
/// rather than on a concrete WPF editor type. The shipped implementation
/// (<see cref="AvalonEditRegionEditor"/>) wraps ICSharpCode AvalonEdit's
/// <c>TextEditor</c>. This is the pluggability boundary from the AvalonEdit integration
/// decision — it exists so a future reuse (e.g. a Markdown-highlighting host) is possible, not
/// as an invitation to build one now.
/// </summary>
public interface IRegionCodeEditor
{
    /// The hosted WPF control, used as the editor-border child and for visual-tree hit-testing.
    Control Control { get; }

    /// Full editor text (the serialization source of truth mirrored into
    /// <see cref="EmbeddedCodeRegionBlock.Content"/>).
    string Text { get; set; }

    /// Caret offset within <see cref="Text"/>.
    int CaretOffset { get; set; }

    /// Start offset of the current selection (caret offset when the selection is empty).
    int SelectionStart { get; }

    /// Length of the current selection; set to 0 to collapse the selection.
    int SelectionLength { get; set; }

    /// <summary>
    /// Returns the directed selection endpoints (fixed anchor, moving end), or the caret twice
    /// when collapsed. A whole-word selection may contain the physical caret inside its range;
    /// the moving endpoint must still cover the full selection rather than stop at that caret.
    /// </summary>
    (int anchorOffset, int caretOffset) GetSelectionOffsets();

    /// <summary>
    /// Restores a selection inside the editor from explicit local <paramref name="anchorOffset"/>
    /// (the fixed end) and <paramref name="caretOffset"/> (the moving end), preserving direction:
    /// the active caret lands at <paramref name="caretOffset"/> whether it is the lower or higher
    /// of the two. Equal offsets collapse to a caret with no highlight. Offsets are clamped to the
    /// editor's text length. This is the in-region half of the single-authority prompt undo/redo
    /// restore path (PROMPTUNDO-20261015): <see cref="PromptUndoManager.ApplySnapshot"/> uses it to
    /// re-establish a selection that lives (wholly or partly) inside this region.
    /// </summary>
    void RestoreSelection(int anchorOffset, int caretOffset);

    /// Text currently selected (empty string when nothing is selected).
    string SelectedText { get; }

    /// Total length of <see cref="Text"/>.
    int TextLength { get; }

    /// Whether keyboard focus is currently within the editor control.
    bool IsKeyboardFocusWithin { get; }

    /// Whether the editor's actual text-input surface (AvalonEdit's <c>TextArea</c>) genuinely
    /// owns OS keyboard focus. Distinct from <see cref="IsKeyboardFocusWithin"/> for diagnostics:
    /// the visible caret can be rendered while real focus has leaked back to the host prompt.
    bool IsTextInputSurfaceFocused { get; }

    /// Maximum rendered height of the editor control.
    double MaxHeight { get; set; }

    /// Point size the editor renders its text at. Driven by the host so an embedded code region
    /// scales together with the surrounding prompt text when the user Ctrl+wheels the prompt font
    /// size (see <c>MainWindow.ApplyPromptFontSize</c>). Only the size changes — the monospace
    /// font family is left untouched.
    double EditorFontSize { get; set; }

    /// Removes the current selection from the document (used by the fenced Cut path).
    void RemoveSelectedText();

    /// Removes a range from the document and leaves the caret at the removal offset.
    void DeleteText(int offset, int length);

    /// Inserts <paramref name="text"/> at the caret (replacing the current selection, if any)
    /// and advances the caret to the end of the inserted text. Used by the voice-dictation path
    /// so recognized text lands inside the focused region instead of the host prompt. Mirrors
    /// how <see cref="RemoveSelectedText"/> mutates the document directly.
    void InsertText(string text);

    /// Routes logical + keyboard focus into the editor's text-input surface. Safe to call before
    /// the control has been rendered (sets logical focus too), mirroring the TextBox-era behavior.
    void FocusEditor();

    /// Maps a point in the editor control's coordinate space to a text offset (best effort).
    int GetOffsetFromPoint(Point pointInControl);

    /// Applies surface/text and syntax colors, invalidating the active rendering immediately.
    void ApplyPalette(RegionEditorPalette palette);

    /// Updates the persistent picker token. Only an empty token permits content auto-detection.
    void SetLanguage(string fenceToken);

    string? HighlightingName { get; }
    event EventHandler HighlightingChanged;

    /// Raised before a document update, while the pre-edit selection is still intact.
    event EventHandler TextChanging;

    /// Raised after a text-changing document update completes, including its caret/selection moves
    /// (drives <see cref="EmbeddedCodeRegionBlock"/> content sync).
    event EventHandler TextChanged;
}

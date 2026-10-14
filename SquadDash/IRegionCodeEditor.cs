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
/// AVALONEDIT-20261014-001). <see cref="Surface"/>/<see cref="Text"/> are used in Stage 1;
/// <see cref="HighlightColors"/> is carried for the later syntax-highlighting task and is empty
/// until then.
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
    private static readonly IReadOnlyDictionary<string, HighlightingColor> NoHighlightColors =
        new Dictionary<string, HighlightingColor>();

    public RegionEditorPalette CreatePalette()
    {
        var app = Application.Current;
        var surface = (app?.TryFindResource("CodeSurface") as Brush) ?? Brushes.Transparent;
        var text = (app?.TryFindResource("CodeText") as Brush) ?? SystemColors.ControlTextBrush;
        return new RegionEditorPalette(surface, text, NoHighlightColors);
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

    /// Text currently selected (empty string when nothing is selected).
    string SelectedText { get; }

    /// Total length of <see cref="Text"/>.
    int TextLength { get; }

    /// Whether keyboard focus is currently within the editor control.
    bool IsKeyboardFocusWithin { get; }

    /// Maximum rendered height of the editor control.
    double MaxHeight { get; set; }

    /// Removes the current selection from the document (used by the fenced Cut path).
    void RemoveSelectedText();

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

    /// Applies <paramref name="palette"/> to the editor's own surface/text colors.
    void ApplyPalette(RegionEditorPalette palette);

    /// Raised whenever the editor text changes (drives <see cref="EmbeddedCodeRegionBlock"/>
    /// content sync).
    event EventHandler TextChanged;
}

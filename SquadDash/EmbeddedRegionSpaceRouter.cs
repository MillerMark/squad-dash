using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Pure decision for whether an unmodified Space keystroke must be inserted authoritatively into
/// the focused embedded code region BEFORE WPF's text-input pipeline can divert it to the host.
/// <para>
/// Background: the host prompt is a <see cref="System.Windows.Controls.RichTextBox"/> that hosts
/// each region's editor inside a <c>BlockUIContainer</c>. WPF's <c>TextBoxBase</c> text editor
/// special-cases Space in its own input/editing pipeline, so even when the nested AvalonEdit
/// <c>TextArea</c> genuinely owns OS keyboard focus, the Space keystroke's TextInput is consumed by
/// the host <c>RichTextBox</c> and inserted into the host FlowDocument — it never reaches the
/// nested editor's <c>OnTextInput</c>. (Printable letters are NOT special-cased this way, which is
/// why letters already insert into the region correctly.) Handling the Space at the region editor's
/// <c>PreviewKeyDown</c> (and marking it handled) stops WPF from ever raising the diverted TextInput
/// for that keystroke, so the space lands in the region with no double-insert and no host leak.
/// </para>
/// <para>
/// Extracted as a pure predicate (mirroring <see cref="EmbeddedRegionInputRouter"/>) so the
/// decision is unit-testable without a live WPF key event.
/// </para>
/// </summary>
public static class EmbeddedRegionSpaceRouter
{
    /// <param name="key">The pressed key.</param>
    /// <param name="modifiers">The active keyboard modifiers at key-down time.</param>
    /// <param name="regionHasFocus">Whether keyboard focus is genuinely inside THIS region (so a
    /// stale/unfocused region never swallows a Space).</param>
    /// <returns><c>true</c> when the region must insert a literal space itself and mark the key
    /// handled; <c>false</c> to leave the keystroke untouched (lets Shift+Space / modified combos,
    /// and Space with no region focused, behave exactly as before).</returns>
    public static bool ShouldInsertSpaceIntoRegion(Key key, ModifierKeys modifiers, bool regionHasFocus)
        => key == Key.Space && modifiers == ModifierKeys.None && regionHasFocus;
}

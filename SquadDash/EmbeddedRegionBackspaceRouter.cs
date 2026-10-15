using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Pure decision for whether a Backspace keystroke in an embedded code region should remove the
/// ENTIRE region from the host prompt (rather than deleting a character inside it).
/// <para>
/// The region is removed only when it is completely empty (<c>textLength == 0</c>) and the user
/// presses a plain, unmodified Backspace — matching the intuitive "backspace at the start of an
/// empty block deletes the block" behavior, so an accidentally-opened or emptied-out region can
/// be dismissed with a single keystroke and typing continues where the region used to be. Any
/// text in the region, or any modifier (Ctrl/Alt/Shift) on the Backspace, leaves the keystroke
/// untouched for normal in-region editing. Delete (and every other key) is never a removal.
/// </para>
/// <para>
/// Extracted as a pure predicate (mirroring <see cref="EmbeddedRegionSpaceRouter"/>) so the
/// decision is unit-testable without a live WPF key event.
/// </para>
/// </summary>
public static class EmbeddedRegionBackspaceRouter
{
    /// <param name="key">The pressed key.</param>
    /// <param name="modifiers">The active keyboard modifiers at key-down time.</param>
    /// <param name="textLength">The region editor's current text length.</param>
    /// <returns><c>true</c> only for an unmodified <see cref="Key.Back"/> on a truly empty region
    /// (so the region should be removed and the key marked handled); <c>false</c> otherwise (so
    /// Backspace does normal character deletion and every other key/modifier is left alone).</returns>
    public static bool ShouldRemoveEmptyRegionOnBackspace(Key key, ModifierKeys modifiers, int textLength)
        => key == Key.Back && modifiers == ModifierKeys.None && textLength == 0;
}

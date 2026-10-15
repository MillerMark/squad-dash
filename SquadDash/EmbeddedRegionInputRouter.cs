namespace SquadDash;

/// <summary>
/// What the host prompt <see cref="System.Windows.Controls.RichTextBox"/> should do with a
/// printable <c>PreviewTextInput</c> keystroke (space or any other character) given where
/// keyboard focus currently lives relative to the embedded code regions.
/// </summary>
public enum HostTextInputDecision
{
    /// Let WPF's normal routing proceed. Either there is no text, or focus is genuinely inside a
    /// region (so the tunneling event will reach that region's editor natively), or no region is
    /// active (ordinary host typing).
    LetProceed,

    /// Keyboard focus has leaked back to the host prompt while an embedded region is still the
    /// active region (the AvalonEdit caret is visible inside it, but the host owns OS focus). The
    /// host must NOT insert the character itself — it must route it into the active region's
    /// editor and re-assert that region's focus.
    RouteToActiveRegion,
}

/// <summary>
/// Pure routing decision extracted from <c>MainWindow.PromptTextBox_PreviewTextInput</c> so the
/// "should the host insert this character, or defer to the focused/active embedded region?"
/// logic is unit-testable without a live window.
/// <para>
/// Background: the host <see cref="System.Windows.Controls.RichTextBox"/>'s
/// <c>PreviewTextInput</c> is a tunneling handler on the route from the window root to whatever
/// element owns keyboard focus. When an AvalonEdit <c>TextArea</c> genuinely owns focus, a space
/// tunnels through the host (which does nothing) and reaches the TextArea, which inserts it
/// correctly. The reported bug is that focus leaks back to the host while the region's caret is
/// still visibly blinking — so the tunnel route ends at the host and the host inserts the space
/// OUTSIDE the region. This predicate detects that leak so the caller can redirect the character
/// into the active region instead (belt-and-suspenders on top of the focus-ownership fix).
/// </para>
/// </summary>
public static class EmbeddedRegionInputRouter
{
    /// <param name="hasText">The <c>TextCompositionEventArgs.Text</c> is non-empty (a printable
    /// character such as a space was produced).</param>
    /// <param name="focusInsideRegion">Keyboard focus currently resolves to an element inside an
    /// embedded region (the non-leaking case — the event will reach that region natively).</param>
    /// <param name="hostHasFocus">The host prompt control itself currently owns keyboard focus
    /// (the precise leak scenario — the character would otherwise land in the host).</param>
    /// <param name="activeRegionAttached">There is a tracked "active" region (the one the user
    /// most recently entered) that is still attached to the host document.</param>
    public static HostTextInputDecision DecideHostTextInput(
        bool hasText,
        bool focusInsideRegion,
        bool hostHasFocus,
        bool activeRegionAttached)
    {
        if (!hasText)
            return HostTextInputDecision.LetProceed;

        // Focus genuinely inside a region: let the tunneling event reach that region's editor
        // natively. Intercepting here would be redundant and could bypass native input handling.
        if (focusInsideRegion)
            return HostTextInputDecision.LetProceed;

        // The leak: the host owns focus but the user is still working in an active region. Route
        // the character into that region instead of letting the host insert it adjacent.
        if (hostHasFocus && activeRegionAttached)
            return HostTextInputDecision.RouteToActiveRegion;

        return HostTextInputDecision.LetProceed;
    }
}

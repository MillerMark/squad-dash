using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// A <see cref="RichTextBox"/> that caps the auto-scroll distance that WPF applies during
/// mouse drag-selection.
///
/// <para>
/// WPF's built-in behaviour: when the cursor leaves the viewport during a drag, the
/// internal <c>IScrollInfo</c> implementation scrolls by an amount proportional to how far
/// outside the bounds the cursor is.  On a tall transcript this can jump hundreds of lines
/// in a single mouse-move event, making precise text selection impossible.
/// </para>
///
/// <para>
/// The fix: override <see cref="OnMouseMove"/>, let the base class run its normal selection
/// and scroll logic, then clamp any resulting vertical-offset change to at most
/// <see cref="MaxScrollPerMovePx"/> device-independent pixels.
/// </para>
/// </summary>
internal sealed class ThrottledScrollRichTextBox : RichTextBox
{
    /// <summary>
    /// Maximum pixels the viewport may scroll in a single <see cref="OnMouseMove"/> call
    /// while a left-button drag is in progress.  ~40 px ≈ 2-3 typical text lines.
    /// </summary>
    private const double MaxScrollPerMovePx = 40.0;

    public ThrottledScrollRichTextBox()
    {
        ReadOnlyTextInputGuard.DisableInputMethod(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            base.OnMouseMove(e);
            return;
        }

        var before = VerticalOffset;
        base.OnMouseMove(e);
        var after = VerticalOffset;

        var delta = after - before;
        if (Math.Abs(delta) > MaxScrollPerMovePx)
            ScrollToVerticalOffset(before + Math.Sign(delta) * MaxScrollPerMovePx);
    }
}

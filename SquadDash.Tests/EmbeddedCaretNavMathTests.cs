using System.Windows.Input;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class EmbeddedCaretNavMathTests
{
    // Multi-line sample used throughout: three lines "abc", "de", "fghi".
    //  index: a0 b1 c2 \n3 d4 e5 \n6 f7 g8 h9 i10   → length 11
    private const string MultiLine = "abc\nde\nfghi";

    // ---- Empty region (L == 0): first line == last line ----

    [Test]
    public void Empty_Up_ExitsBackward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("", 0, 0, Key.Up), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [Test]
    public void Empty_Down_ExitsForward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("", 0, 0, Key.Down), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [Test]
    public void Empty_Left_ExitsBackward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("", 0, 0, Key.Left), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [Test]
    public void Empty_Right_ExitsForward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("", 0, 0, Key.Right), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    // ---- Single-line region: first line == last line ----

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void SingleLine_Up_AlwaysExitsBackward(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", caret, 0, Key.Up), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void SingleLine_Down_AlwaysExitsForward(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", caret, 0, Key.Down), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [Test]
    public void SingleLine_Left_AtStart_ExitsBackward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", 0, 0, Key.Left), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void SingleLine_Left_NotAtStart_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", caret, 0, Key.Left), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    [Test]
    public void SingleLine_Right_AtEnd_ExitsForward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", 3, 0, Key.Right), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SingleLine_Right_NotAtEnd_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", caret, 0, Key.Right), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    // ---- Up: first-line caret ALWAYS exits backward, regardless of column (the regression) ----

    [TestCase(0)]   // column 0
    [TestCase(1)]   // non-zero column
    [TestCase(2)]
    [TestCase(3)]   // end of first line, just before the newline
    public void Up_OnFirstLine_AlwaysExitsBackward_RegardlessOfColumn(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, caret, 0, Key.Up), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [TestCase(4)]   // middle line "de" start
    [TestCase(5)]   // middle line middle
    [TestCase(6)]   // middle line end (before second newline)
    [TestCase(7)]   // last line start
    [TestCase(9)]   // last line middle
    [TestCase(11)]  // last line end
    public void Up_NotOnFirstLine_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, caret, 0, Key.Up), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    // ---- Down: last-line caret ALWAYS exits forward, regardless of column (the regression) ----

    [TestCase(7)]   // last line "fghi" start (column 0)
    [TestCase(9)]   // last line middle
    [TestCase(11)]  // last line end
    public void Down_OnLastLine_AlwaysExitsForward_RegardlessOfColumn(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, caret, 0, Key.Down), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [TestCase(0)]   // first line start
    [TestCase(2)]   // first line middle
    [TestCase(3)]   // first line end
    [TestCase(4)]   // middle line start
    [TestCase(5)]   // middle line middle
    [TestCase(6)]   // middle line end
    public void Down_NotOnLastLine_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, caret, 0, Key.Down), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    // ---- Left / Right on multi-line text are purely about absolute caret index ----

    [Test]
    public void MultiLine_Left_AtIndexZero_ExitsBackward() =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, 0, 0, Key.Left), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [TestCase(1)]
    [TestCase(4)]   // start of a later line, but index > 0 → pass through (TextBox moves across newline)
    [TestCase(11)]
    public void MultiLine_Left_NotIndexZero_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, caret, 0, Key.Left), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    [Test]
    public void MultiLine_Right_AtEnd_ExitsForward() =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, 11, 0, Key.Right), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [TestCase(0)]
    [TestCase(3)]   // end of first line, but index < L → pass through
    [TestCase(10)]
    public void MultiLine_Right_NotAtEnd_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, caret, 0, Key.Right), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    // ---- selectionLength > 0 → PassThrough for every arrow key, at any position ----

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Up)]
    [TestCase(Key.Down)]
    public void Selection_AtStart_PassesThrough_ForEveryArrow(Key key) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, 0, 2, key), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Up)]
    [TestCase(Key.Down)]
    public void Selection_AtEnd_PassesThrough_ForEveryArrow(Key key) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, 11, 3, key), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Up)]
    [TestCase(Key.Down)]
    public void Selection_SingleLine_PassesThrough_ForEveryArrow(Key key) =>
        Assert.That(EmbeddedCaretNavMath.Decide("abc", 0, 3, key), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    // ---- Trailing newline: last line is empty ("a\n" → lines "a" and "") ----
    //  index: a0 \n1  → length 2. Caret 2 is on the empty last line; caret 0..1 is on the first line.

    [Test]
    public void TrailingNewline_Down_OnEmptyLastLine_ExitsForward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("a\n", 2, 0, Key.Down), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [Test]
    public void TrailingNewline_Up_OnEmptyLastLine_PassesThrough() =>
        Assert.That(EmbeddedCaretNavMath.Decide("a\n", 2, 0, Key.Up), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    [TestCase(0)]
    [TestCase(1)]
    public void TrailingNewline_Up_OnFirstLine_ExitsBackward(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide("a\n", caret, 0, Key.Up), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    [TestCase(0)]   // before the newline → first line, not last → pass through
    [TestCase(1)]
    public void TrailingNewline_Down_OnFirstLine_PassesThrough(int caret) =>
        Assert.That(EmbeddedCaretNavMath.Decide("a\n", caret, 0, Key.Down), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));

    [Test]
    public void TrailingNewline_Right_AtEnd_ExitsForward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("a\n", 2, 0, Key.Right), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitForward));

    [Test]
    public void TrailingNewline_Left_AtStart_ExitsBackward() =>
        Assert.That(EmbeddedCaretNavMath.Decide("a\n", 0, 0, Key.Left), Is.EqualTo(EmbeddedCaretNavMath.Decision.ExitBackward));

    // ---- Non-arrow keys always pass through ----

    [TestCase(Key.A)]
    [TestCase(Key.Enter)]
    [TestCase(Key.Home)]
    [TestCase(Key.End)]
    public void NonArrowKey_PassesThrough(Key key) =>
        Assert.That(EmbeddedCaretNavMath.Decide(MultiLine, 5, 0, key), Is.EqualTo(EmbeddedCaretNavMath.Decision.PassThrough));
}

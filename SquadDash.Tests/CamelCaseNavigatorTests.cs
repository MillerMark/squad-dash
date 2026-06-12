using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CamelCaseNavigatorTests {

    // ── MoveRight ────────────────────────────────────────────────────────────

    [Test]
    public void MoveRight_EmptyString_StaysAtZero() {
        Assert.That(CamelCaseNavigator.MoveRight("", 0), Is.EqualTo(0));
    }

    [Test]
    public void MoveRight_SingleWordNoCamelCase_MovesToEndOfWord() {
        // "hello" — no uppercase transition, moves to end (5)
        Assert.That(CamelCaseNavigator.MoveRight("hello", 0), Is.EqualTo(5));
    }

    [Test]
    public void MoveRight_FromStart_StopsAtCamelBoundary() {
        // "helloWorld" — 'o'→'W' transition at index 5
        Assert.That(CamelCaseNavigator.MoveRight("helloWorld", 0), Is.EqualTo(5));
    }

    [Test]
    public void MoveRight_AlreadyAtBoundary_MovesToNextBoundaryOrWordEnd() {
        // "helloWorld" caret=5 (on 'W') — no further l→u in "World", moves to end (10)
        Assert.That(CamelCaseNavigator.MoveRight("helloWorld", 5), Is.EqualTo(10));
    }

    [Test]
    public void MoveRight_ThreeHumps_SecondBoundary() {
        // "helloWorldFoo" caret=5 (on 'W') — 'd'→'F' transition at index 10
        Assert.That(CamelCaseNavigator.MoveRight("helloWorldFoo", 5), Is.EqualTo(10));
    }

    [Test]
    public void MoveRight_ThreeHumps_ThirdBoundaryIsWordEnd() {
        // "helloWorldFoo" caret=10 (on 'F') — no further transition, moves to end (13)
        Assert.That(CamelCaseNavigator.MoveRight("helloWorldFoo", 10), Is.EqualTo(13));
    }

    [TestCase("oneTwoThreeFour", 0,  3,  TestName = "MoveRight_MultipleHumps_FirstBoundary")]
    [TestCase("oneTwoThreeFour", 3,  6,  TestName = "MoveRight_MultipleHumps_SecondBoundary")]
    [TestCase("oneTwoThreeFour", 6,  11, TestName = "MoveRight_MultipleHumps_ThirdBoundary")]
    [TestCase("oneTwoThreeFour", 11, 15, TestName = "MoveRight_MultipleHumps_FourthBoundaryIsWordEnd")]
    public void MoveRight_MultipleHumps_EachStep(string text, int caret, int expected) {
        Assert.That(CamelCaseNavigator.MoveRight(text, caret), Is.EqualTo(expected));
    }

    [Test]
    public void MoveRight_AtEndOfString_StaysAtEnd() {
        Assert.That(CamelCaseNavigator.MoveRight("hello", 5), Is.EqualTo(5));
    }

    [Test]
    public void MoveRight_AtEndOfCamelCaseString_StaysAtEnd() {
        Assert.That(CamelCaseNavigator.MoveRight("helloWorld", 10), Is.EqualTo(10));
    }

    [Test]
    public void MoveRight_CaretAtStartOfFirstWord_MovesToWordEnd() {
        // "hello World" caret=0 — no camelCase in "hello", moves to 5 (position of space)
        Assert.That(CamelCaseNavigator.MoveRight("hello World", 0), Is.EqualTo(5));
    }

    [Test]
    public void MoveRight_CaretAtWhitespace_SkipsToNextWordAndScans() {
        // "hello World" caret=5 (space) — skip whitespace, scan "World", no l→u, moves to 11
        Assert.That(CamelCaseNavigator.MoveRight("hello World", 5), Is.EqualTo(11));
    }

    [Test]
    public void MoveRight_AllCapsWord_MovesToWordEnd() {
        // "HTML" — no lowercase→uppercase transition, moves to 4
        Assert.That(CamelCaseNavigator.MoveRight("HTML", 0), Is.EqualTo(4));
    }

    [Test]
    public void MoveRight_AllCapsWordInSentence_MovesToEndOfWord() {
        // "parse HTML" caret=6 (start of "HTML") — no l→u in "HTML", moves to 10
        Assert.That(CamelCaseNavigator.MoveRight("parse HTML", 6), Is.EqualTo(10));
    }

    [Test]
    public void MoveRight_MixedUpperRunThenLower_FirstLowerToUpperBoundary() {
        // "parseHTMLDocument" caret=0 — 'e'(4)→'H'(5) is the first l→u boundary
        Assert.That(CamelCaseNavigator.MoveRight("parseHTMLDocument", 0), Is.EqualTo(5));
    }

    [Test]
    public void MoveRight_AfterAllCapsRun_NoFurtherLowerToUpperBoundary() {
        // "parseHTMLDocument" caret=5 — 'L'→'D' is upper→upper (not a boundary), moves to end (17)
        Assert.That(CamelCaseNavigator.MoveRight("parseHTMLDocument", 5), Is.EqualTo(17));
    }

    [Test]
    public void MoveRight_WordWithNumbers_MovesToFirstCamelBoundary() {
        // "getUser123Name" caret=0 — 't'(2)→'U'(3) is first l→u boundary
        Assert.That(CamelCaseNavigator.MoveRight("getUser123Name", 0), Is.EqualTo(3));
    }

    [Test]
    public void MoveRight_CaretAfterNumberRun_DigitToUpperIsNotStrictBoundary() {
        // "getUser123Name" caret=3 — digit '3' is not lower, so '3'→'N' is not a l→u boundary;
        // no further l→u found, moves to end (14)
        Assert.That(CamelCaseNavigator.MoveRight("getUser123Name", 3), Is.EqualTo(14));
    }

    [Test]
    public void MoveRight_MultipleWhitespaceBeforeWord_SkipsAllWhitespace() {
        // "hello   World" caret=5 — skip three spaces, scan "World", no l→u, moves to 13
        Assert.That(CamelCaseNavigator.MoveRight("hello   World", 5), Is.EqualTo(13));
    }

    [Test]
    public void MoveRight_WhitespaceBetweenCamelWords_SkipsAndFindsBoundary() {
        // "foo helloWorld" caret=4 (space) — skip space, scan "helloWorld", 'o'→'W' at 10
        Assert.That(CamelCaseNavigator.MoveRight("foo helloWorld", 4), Is.EqualTo(10));
    }

    // ── MoveLeft ─────────────────────────────────────────────────────────────

    [Test]
    public void MoveLeft_EmptyString_StaysAtZero() {
        Assert.That(CamelCaseNavigator.MoveLeft("", 0), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_AtStartOfString_StaysAtZero() {
        Assert.That(CamelCaseNavigator.MoveLeft("hello", 0), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_AtStartOfCamelWord_StaysAtZero() {
        Assert.That(CamelCaseNavigator.MoveLeft("helloWorld", 0), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_FromEndOfCamelWord_MovesToBoundary() {
        // "helloWorld" caret=10 — 'o'(4)→'W'(5) is the last l→u boundary before 10
        Assert.That(CamelCaseNavigator.MoveLeft("helloWorld", 10), Is.EqualTo(5));
    }

    [Test]
    public void MoveLeft_FromBoundary_MovesToWordStart() {
        // "helloWorld" caret=5 (on 'W') — no l→u boundary before 5 in same word, moves to 0
        Assert.That(CamelCaseNavigator.MoveLeft("helloWorld", 5), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_ThreeHumps_LastBoundary() {
        // "oneTwoThree" caret=11 (end) — last l→u is 'o'(5)→'T'(6), moves to 6
        Assert.That(CamelCaseNavigator.MoveLeft("oneTwoThree", 11), Is.EqualTo(6));
    }

    [Test]
    public void MoveLeft_ThreeHumps_MiddleBoundary() {
        // "oneTwoThree" caret=6 (on 'T') — last l→u before 6 is 'e'(2)→'T'(3), moves to 3
        Assert.That(CamelCaseNavigator.MoveLeft("oneTwoThree", 6), Is.EqualTo(3));
    }

    [Test]
    public void MoveLeft_ThreeHumps_FirstBoundaryThenWordStart() {
        // "oneTwoThree" caret=3 (on 'T') — no l→u boundary before 3, moves to 0
        Assert.That(CamelCaseNavigator.MoveLeft("oneTwoThree", 3), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_TwoWords_FromEndOfSecondWord_MovesToWordStart() {
        // "hello World" caret=11 — "World" has no l→u, moves to start of "World" (6)
        Assert.That(CamelCaseNavigator.MoveLeft("hello World", 11), Is.EqualTo(6));
    }

    [Test]
    public void MoveLeft_TwoWords_FromStartOfSecondWord_MovesToEndOrBoundaryOfFirstWord() {
        // "hello World" caret=6 (start of "World") — jump to end of "hello",
        // no l→u in "hello", moves to start of "hello" (0)
        Assert.That(CamelCaseNavigator.MoveLeft("hello World", 6), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_CaretAtWhitespaceBetweenWords_TreatedAsWordBoundary() {
        // "hello World" caret=5 (space) — treats as boundary, moves to end of "hello" scan → 0
        Assert.That(CamelCaseNavigator.MoveLeft("hello World", 5), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_TwoWords_CamelCaseInFirstWord_FindsBoundaryAfterCross() {
        // "helloWorld foo" caret=14 — scan "foo": no l→u, start of "foo"=11
        Assert.That(CamelCaseNavigator.MoveLeft("helloWorld foo", 14), Is.EqualTo(11));
    }

    [Test]
    public void MoveLeft_TwoWords_FromStartOfSecondPlainWord_MovesToCamelBoundaryInFirst() {
        // "helloWorld foo" caret=11 (start of "foo") — jump to end of "helloWorld",
        // last l→u is 'o'(4)→'W'(5), moves to 5
        Assert.That(CamelCaseNavigator.MoveLeft("helloWorld foo", 11), Is.EqualTo(5));
    }

    [Test]
    public void MoveLeft_AllCapsWord_MovesToWordStart() {
        // "HTML" — no l→u boundary, moves to 0
        Assert.That(CamelCaseNavigator.MoveLeft("HTML", 4), Is.EqualTo(0));
    }

    [Test]
    public void MoveLeft_AllCapsWordInSentence_MovesToStartOfWord() {
        // "parse HTML" caret=10 — scan "HTML" backward: no l→u, start of "HTML"=6
        Assert.That(CamelCaseNavigator.MoveLeft("parse HTML", 10), Is.EqualTo(6));
    }

    [Test]
    public void MoveLeft_WordWithNumbers_MovesToCamelBoundary() {
        // "getUser123" caret=10 — 't'(2)→'U'(3) is the l→u boundary; scanned backward from 9 (no l→u in "123"), moves to 3
        Assert.That(CamelCaseNavigator.MoveLeft("getUser123", 10), Is.EqualTo(3));
    }

    [Test]
    public void MoveLeft_FromMiddleOfWord_FindsNearestPrecedingBoundary() {
        // "oneTwoThreeFour" caret=13 (inside "Four") — last l→u before 13 is 'e'(10)→'F'(11)
        Assert.That(CamelCaseNavigator.MoveLeft("oneTwoThreeFour", 13), Is.EqualTo(11));
    }

    [Test]
    public void MoveLeft_MixedUpperRun_NoBoundaryInUpperRun_MovesToPrecedingBoundary() {
        // "parseHTMLDocument" caret=9 (on 'D') — scan backward: 'e'(4)→'H'(5) is l→u boundary
        Assert.That(CamelCaseNavigator.MoveLeft("parseHTMLDocument", 9), Is.EqualTo(5));
    }

    [Test]
    public void MoveLeft_MixedUpperRun_FromBoundary_MovesToWordStart() {
        // "parseHTMLDocument" caret=5 (on 'H') — no l→u boundary before 5, moves to 0
        Assert.That(CamelCaseNavigator.MoveLeft("parseHTMLDocument", 5), Is.EqualTo(0));
    }
}

using System.Threading;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SmoothDictationHelperTests {

    // ── Apply (pure string transform) ────────────────────────────────────────

    [Test]
    public void Apply_NoSentenceBreaks_ReturnsOriginal() {
        var input = "hello world this is a test";
        Assert.That(SmoothDictationHelper.Apply(input), Is.EqualTo(input));
    }

    [Test]
    public void Apply_EmptyString_ReturnsEmpty() {
        Assert.That(SmoothDictationHelper.Apply(""), Is.EqualTo(""));
    }

    [Test]
    public void Apply_SingleSentenceBreak_MergesWithLowercase() {
        // ". T" → " t"
        var result = SmoothDictationHelper.Apply("first sentence. Then the next.");
        Assert.That(result, Is.EqualTo("first sentence then the next."));
    }

    [Test]
    public void Apply_MultipleSentenceBreaks_MergesAll() {
        // All four sentence-break patterns are collapsed — no exception for the last one.
        var result = SmoothDictationHelper.Apply("one. Two. Three. Done.");
        Assert.That(result, Is.EqualTo("one two three done."));
    }

    [Test]
    public void Apply_PronounI_IsPreserved() {
        // ". I " — "I" followed by a space is the standalone pronoun, keep it.
        var result = SmoothDictationHelper.Apply("hello. I went there.");
        Assert.That(result, Is.EqualTo("hello. I went there."));
    }

    [Test]
    public void Apply_PronounI_AtEndOfString_IsPreserved() {
        var result = SmoothDictationHelper.Apply("agreed. I");
        Assert.That(result, Is.EqualTo("agreed. I"));
    }

    [Test]
    public void Apply_CapitalIFollowedByLetter_IsLowercased() {
        // "It" — the "I" is followed by 't', so it's not the standalone pronoun.
        var result = SmoothDictationHelper.Apply("done. It worked fine.");
        Assert.That(result, Is.EqualTo("done it worked fine."));
    }

    [Test]
    public void Apply_MultipleSpacesBetweenSentences_StillMerges() {
        // The regex matches one-or-more whitespace chars between period and capital.
        var result = SmoothDictationHelper.Apply("first.  Second sentence.");
        Assert.That(result, Is.EqualTo("first  second sentence."));
    }

    [Test]
    public void Apply_PronounI_FollowedByApostrophe_IsPreserved() {
        // "I'm" — apostrophe is not a letter, so the lone "I" rule applies.
        var result = SmoothDictationHelper.Apply("hello. I'm going.");
        Assert.That(result, Is.EqualTo("hello. I'm going."));
    }

    [Test]
    public void Apply_MixOfPronounAndNonPronoun_HandlesCorrectly() {
        var result = SmoothDictationHelper.Apply("done. They came. I agreed. The end.");
        // ". They" → " they"; ". I " → preserved; ". The" → " the"
        Assert.That(result, Is.EqualTo("done they came. I agreed the end."));
    }

    // ── ApplyToTextBox ───────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToTextBox_NoSelection_TransformsFullText() {
        // All sentence breaks are collapsed: ". Two" and ". Three" both get merged.
        var tb = new TextBox { Text = "one. Two. Three." };
        var changed = SmoothDictationHelper.ApplyToTextBox(tb);
        Assert.That(changed, Is.True);
        Assert.That(tb.Text, Is.EqualTo("one two three."));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToTextBox_WithSelection_TransformsOnlySelection() {
        // Selection starts after the first period so only the inner break is merged.
        var tb = new TextBox { Text = "prefix. Start. Middle. End suffix." };
        // Select " Start. Middle." (the space after the first period through the second period).
        tb.SelectionStart  = 7;  // space after first '.'
        tb.SelectionLength = 15; // " Start. Middle."
        SmoothDictationHelper.ApplyToTextBox(tb);
        // ". Middle" within the selection → " middle"; the outer "prefix." is untouched.
        Assert.That(tb.Text, Does.StartWith("prefix. Start middle."));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToTextBox_NoMatchInText_ReturnsFalse() {
        var tb = new TextBox { Text = "no sentence breaks here" };
        var changed = SmoothDictationHelper.ApplyToTextBox(tb);
        Assert.That(changed, Is.False);
        Assert.That(tb.Text, Is.EqualTo("no sentence breaks here"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToTextBox_NoSelection_PreservesCaretPosition() {
        var tb = new TextBox { Text = "hello. World is great." };
        tb.CaretIndex = 7; // inside "World"
        SmoothDictationHelper.ApplyToTextBox(tb);
        // After transform: "hello world is great." — caret should be clamped within new length
        Assert.That(tb.CaretIndex, Is.LessThanOrEqualTo(tb.Text.Length));
    }
}

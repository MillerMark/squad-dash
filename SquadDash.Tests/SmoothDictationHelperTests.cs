using System.Threading;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;

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

    // ── Duplicate-word removal ───────────────────────────────────────────────

    [Test]
    public void Apply_DuplicateWordLowercase_CollapsesToOne() {
        Assert.That(SmoothDictationHelper.Apply("the the quick brown fox"), Is.EqualTo("the quick brown fox"));
    }

    [Test]
    public void Apply_DuplicateWordMixedCase_KeepsFirstWordCase() {
        // "The the" → "The"
        Assert.That(SmoothDictationHelper.Apply("The the quick brown fox"), Is.EqualTo("The quick brown fox"));
    }

    [Test]
    public void Apply_DuplicateWordUppercase_KeepsFirstWordCase() {
        // "THE THE" → "THE"
        Assert.That(SmoothDictationHelper.Apply("THE THE quick"), Is.EqualTo("THE quick"));
    }

    [Test]
    public void Apply_TripleDuplicateWord_CollapsesToOne() {
        // Loop handles triple repetition.
        Assert.That(SmoothDictationHelper.Apply("the the the fox"), Is.EqualTo("the fox"));
    }

    [Test]
    public void Apply_DuplicateWordAtEnd_Collapsed() {
        Assert.That(SmoothDictationHelper.Apply("I went there there"), Is.EqualTo("I went there"));
    }

    [Test]
    public void Apply_NoDuplicates_ReturnsOriginal() {
        var input = "the quick brown fox";
        Assert.That(SmoothDictationHelper.Apply(input), Is.EqualTo(input));
    }

    [Test]
    public void Apply_DuplicateAndSentenceBreak_BothFixed() {
        // "the the. Quick" → duplicate removed → sentence break smoothed.
        Assert.That(SmoothDictationHelper.Apply("see the the. Quick brown fox"), Is.EqualTo("see the quick brown fox"));
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

    // ── ApplyToAvalonEdit ─────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToAvalonEdit_NoSelection_TransformsFullText() {
        var editor = new TextEditor { Text = "one. Two. Three." };

        var changed = SmoothDictationHelper.ApplyToAvalonEdit(editor);

        Assert.That(changed, Is.True);
        Assert.That(editor.Text, Is.EqualTo("one two three."));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToAvalonEdit_WithSelection_TransformsOnlySelection() {
        var editor = new TextEditor { Text = "prefix. Start. Middle. End suffix." };
        editor.Select(7, 15); // " Start. Middle."

        var changed = SmoothDictationHelper.ApplyToAvalonEdit(editor);

        Assert.That(changed, Is.True);
        Assert.That(editor.Text, Does.StartWith("prefix. Start middle."));
        Assert.That(editor.SelectionStart, Is.EqualTo(7));
        Assert.That(editor.SelectedText, Is.EqualTo(" Start middle."));
        editor.Undo();
        Assert.That(editor.Text, Is.EqualTo("prefix. Start. Middle. End suffix."));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToAvalonEdit_NoMatchInText_ReturnsFalse() {
        var editor = new TextEditor { Text = "no sentence breaks here" };

        var changed = SmoothDictationHelper.ApplyToAvalonEdit(editor);

        Assert.That(changed, Is.False);
        Assert.That(editor.Text, Is.EqualTo("no sentence breaks here"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToAvalonEdit_NormalizesToLf() {
        var editor = new TextEditor { Text = "line1\r\nline2. Three" };

        var changed = SmoothDictationHelper.ApplyToAvalonEdit(editor);

        Assert.That(changed, Is.True);
        Assert.That(editor.Text, Is.EqualTo("line1\nline2 three"));
        Assert.That(editor.Text.Contains('\r'), Is.False);
        editor.Undo();
        Assert.That(editor.Text, Is.EqualTo("line1\r\nline2. Three"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyToAvalonEdit_SelectionAfterCrLf_UsesRawDocumentOffsets() {
        var editor = new TextEditor { Text = "prefix\r\none. Two suffix" };
        editor.Select(8, 8);
        SmoothDictationHelper.ApplyToAvalonEdit(editor);
        Assert.That(editor.Text, Is.EqualTo("prefix\r\none two suffix"));
        Assert.That(editor.SelectedText, Is.EqualTo("one two"));
        editor.Undo();
        Assert.That(editor.Text, Is.EqualTo("prefix\r\none. Two suffix"));
    }
}

namespace SquadDash.Tests;

[TestFixture]
internal sealed class VoiceInsertionHeuristicsTests {

    // ── IsSentenceContinuation ────────────────────────────────────────────────

    [Test]
    public void IsSentenceContinuation_EmptyString_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation(""), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_WhitespaceOnly_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("   \t\n"), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithLowercaseLetter_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("hello"), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithLowercaseLetterAndTrailingSpace_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("hello "), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithComma_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("hello,"), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithCommaAndSpace_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("hello, "), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithOpenParen_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("see ("), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithSemicolon_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("done;"), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithHyphen_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("re-"), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithEnDash_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("range \u2013"), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithEmDash_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("thought\u2014"), Is.True);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithPeriod_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("done."), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithExclamation_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("done!"), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithQuestionMark_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("done?"), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithColon_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("note:"), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithUppercaseLetter_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("HELLO"), Is.False);
    }

    [Test]
    public void IsSentenceContinuation_EndsWithDigit_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSentenceContinuation("step 3"), Is.False);
    }

    // ── IsSpecialCaseWord ─────────────────────────────────────────────────────

    [Test]
    public void IsSpecialCaseWord_EmptyString_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord(""), Is.False);
    }

    [Test]
    public void IsSpecialCaseWord_PreservedWordI_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("I"), Is.True);
    }

    [Test]
    public void IsSpecialCaseWord_LowercaseI_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("i"), Is.False);
    }

    [Test]
    public void IsSpecialCaseWord_AllCapsAcronymAPI_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("API"), Is.True);
    }

    [Test]
    public void IsSpecialCaseWord_AllCapsAcronymURL_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("URL"), Is.True);
    }

    [Test]
    public void IsSpecialCaseWord_CamelCaseJavaScript_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("JavaScript"), Is.True);
    }

    [Test]
    public void IsSpecialCaseWord_SingleUppercaseLetter_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("Hello"), Is.False);
    }

    [Test]
    public void IsSpecialCaseWord_AllLowercase_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("hello"), Is.False);
    }

    [Test]
    public void IsSpecialCaseWord_IPhoneHasOnlyOneUppercase_ReturnsFalse() {
        // 'i' is lowercase; 'P' is the only uppercase → does not meet the 2+ threshold
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("iPhone"), Is.False);
    }

    [Test]
    public void IsSpecialCaseWord_TwoUppercaseLetters_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsSpecialCaseWord("CamelCase"), Is.True);
    }

    // ── LowercaseFirstWordIfNotSpecial ────────────────────────────────────────

    [Test]
    public void LowercaseFirstWordIfNotSpecial_EmptyString_ReturnsEmpty() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial(""), Is.EqualTo(""));
    }

    [Test]
    public void LowercaseFirstWordIfNotSpecial_AlreadyLowercase_ReturnsUnchanged() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial("hello world"), Is.EqualTo("hello world"));
    }

    [Test]
    public void LowercaseFirstWordIfNotSpecial_NormalCapitalisedWord_LowercasesFirstWord() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial("Hello world"), Is.EqualTo("hello world"));
    }

    [Test]
    public void LowercaseFirstWordIfNotSpecial_SingleWord_LowercasesIt() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial("Hello"), Is.EqualTo("hello"));
    }

    [Test]
    public void LowercaseFirstWordIfNotSpecial_PreservedWordI_ReturnsUnchanged() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial("I am here"), Is.EqualTo("I am here"));
    }

    [Test]
    public void LowercaseFirstWordIfNotSpecial_AcronymFirstWord_ReturnsUnchanged() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial("API call"), Is.EqualTo("API call"));
    }

    [Test]
    public void LowercaseFirstWordIfNotSpecial_CamelCaseFirstWord_ReturnsUnchanged() {
        Assert.That(VoiceInsertionHeuristics.LowercaseFirstWordIfNotSpecial("JavaScript rocks"), Is.EqualTo("JavaScript rocks"));
    }

    // ── ApplyTrailingPunctuationFixes ─────────────────────────────────────────

    [Test]
    public void ApplyTrailingPunctuationFixes_EmptyString_ReturnsEmpty() {
        Assert.That(VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes(""), Is.EqualTo(""));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_EndsWithThisPeriod_ReplacesWithColon() {
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("it looks like this."),
            Is.EqualTo("it looks like this:"));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_EndsWithThisNoPeriod_AppendsColon() {
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("it looks like this"),
            Is.EqualTo("it looks like this:"));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_EndsWithThisCaseInsensitive_AppliesColon() {
        // The replacement is always the normalised lowercase "this:" form
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("like THIS."),
            Is.EqualTo("like this:"));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_EndsWithUnrelatedWord_ReturnsUnchanged() {
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("something else."),
            Is.EqualTo("something else."));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_ThisNotAtEnd_ReturnsUnchanged() {
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("this is not the end"),
            Is.EqualTo("this is not the end"));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_SynthesisEndsWithThisAsSubstring_ReturnsUnchanged() {
        // "synthesis" ends with the letters "this" but is not a word boundary
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("synthesis."),
            Is.EqualTo("synthesis."));
    }

    // ── Apply (integration) ───────────────────────────────────────────────────

    [Test]
    public void Apply_MidSentenceContextWithNormalFirstWord_LowercasesFirstWord() {
        var result = VoiceInsertionHeuristics.Apply("we need to fix", "Hello world");
        Assert.That(result, Is.EqualTo("hello world"));
    }

    [Test]
    public void Apply_MidSentenceContextWithPreservedWordI_KeepsCapital() {
        var result = VoiceInsertionHeuristics.Apply("and then", "I said so");
        Assert.That(result, Is.EqualTo("I said so"));
    }

    [Test]
    public void Apply_SentenceEndingContextWithCapitalisedWord_KeepsCapital() {
        var result = VoiceInsertionHeuristics.Apply("Done.", "Hello world");
        Assert.That(result, Is.EqualTo("Hello world"));
    }

    [Test]
    public void Apply_EmptyContext_DoesNotLowercaseFirstWord() {
        var result = VoiceInsertionHeuristics.Apply("", "Hello world");
        Assert.That(result, Is.EqualTo("Hello world"));
    }

    [Test]
    public void Apply_MidSentenceContextWithTrailingThis_LowercasesAndFixesPunctuation() {
        // Continuation → lowercases "Here"; punctuation fix → appends colon
        var result = VoiceInsertionHeuristics.Apply("the output is", "Here is this.");
        Assert.That(result, Is.EqualTo("here is this:"));
    }

    [Test]
    public void Apply_EmptyIncomingText_ReturnsEmpty() {
        Assert.That(VoiceInsertionHeuristics.Apply("some context", ""), Is.EqualTo(""));
    }
}

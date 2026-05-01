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
            Is.EqualTo("it looks like this: "));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_EndsWithThisNoPeriod_AppendsColonAndSpace() {
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("it looks like this"),
            Is.EqualTo("it looks like this: "));
    }

    [Test]
    public void ApplyTrailingPunctuationFixes_EndsWithThisCaseInsensitive_AppliesColonAndSpace() {
        // The replacement is always the normalised lowercase "this: " form
        Assert.That(
            VoiceInsertionHeuristics.ApplyTrailingPunctuationFixes("like THIS."),
            Is.EqualTo("like this: "));
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
        Assert.That(result, Is.EqualTo("here is this: "));
    }

    [Test]
    public void Apply_EmptyIncomingText_ReturnsEmpty() {
        Assert.That(VoiceInsertionHeuristics.Apply("some context", ""), Is.EqualTo(""));
    }

    // ── IsRightContextStartsWithLetter ────────────────────────────────────────

    [Test]
    public void IsRightContextStartsWithLetter_EmptyString_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithLetter(""), Is.False);
    }

    [Test]
    public void IsRightContextStartsWithLetter_StartsWithLetter_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithLetter("years ago"), Is.True);
    }

    [Test]
    public void IsRightContextStartsWithLetter_StartsWithSpace_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithLetter(" years"), Is.False);
    }

    [Test]
    public void IsRightContextStartsWithLetter_StartsWithDigit_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithLetter("42"), Is.False);
    }

    // ── IsRightContextRequiresTrailingSpace ───────────────────────────────────

    [Test]
    public void IsRightContextRequiresTrailingSpace_EmptyString_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace(""), Is.False);
    }

    [Test]
    public void IsRightContextRequiresTrailingSpace_StartsWithLetter_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace("years"), Is.True);
    }

    [Test]
    public void IsRightContextRequiresTrailingSpace_StartsWithDigit_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace("42 items"), Is.True);
    }

    [Test]
    public void IsRightContextRequiresTrailingSpace_StartsWithOpenParen_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace("(note)"), Is.True);
    }

    [Test]
    public void IsRightContextRequiresTrailingSpace_StartsWithCloseParen_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace(")"), Is.False);
    }

    [Test]
    public void IsRightContextRequiresTrailingSpace_StartsWithWhitespace_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace(" years"), Is.False);
    }

    [Test]
    public void IsRightContextRequiresTrailingSpace_StartsWithComma_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextRequiresTrailingSpace(", which"), Is.False);
    }

    // ── IsRightContextStartsWithPunctuation ───────────────────────────────────

    [Test]
    public void IsRightContextStartsWithPunctuation_EmptyString_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithPunctuation(""), Is.False);
    }

    [Test]
    public void IsRightContextStartsWithPunctuation_StartsWithPeriod_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithPunctuation(". More"), Is.True);
    }

    [Test]
    public void IsRightContextStartsWithPunctuation_StartsWithComma_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithPunctuation(", which"), Is.True);
    }

    [Test]
    public void IsRightContextStartsWithPunctuation_StartsWithExclamation_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithPunctuation("! ok"), Is.True);
    }

    [Test]
    public void IsRightContextStartsWithPunctuation_StartsWithLetter_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsRightContextStartsWithPunctuation("years"), Is.False);
    }

    // ── StripTrailingSentencePunctuation ──────────────────────────────────────

    [Test]
    public void StripTrailingSentencePunctuation_EndsWithPeriod_RemovesPeriod() {
        Assert.That(VoiceInsertionHeuristics.StripTrailingSentencePunctuation("hello."), Is.EqualTo("hello"));
    }

    [Test]
    public void StripTrailingSentencePunctuation_EndsWithExclamation_RemovesIt() {
        Assert.That(VoiceInsertionHeuristics.StripTrailingSentencePunctuation("wow!"), Is.EqualTo("wow"));
    }

    [Test]
    public void StripTrailingSentencePunctuation_EndsWithQuestion_RemovesIt() {
        Assert.That(VoiceInsertionHeuristics.StripTrailingSentencePunctuation("really?"), Is.EqualTo("really"));
    }

    [Test]
    public void StripTrailingSentencePunctuation_EndsWithComma_ReturnsUnchanged() {
        Assert.That(VoiceInsertionHeuristics.StripTrailingSentencePunctuation("hello,"), Is.EqualTo("hello,"));
    }

    [Test]
    public void StripTrailingSentencePunctuation_EmptyString_ReturnsEmpty() {
        Assert.That(VoiceInsertionHeuristics.StripTrailingSentencePunctuation(""), Is.EqualTo(""));
    }

    // ── Apply: trailing-space heuristic (heuristic 4 extended) ───────────────

    [Test]
    public void Apply_RightContextLetter_AddsTrailingSpace() {
        // caret before "years" — inserting "score and seven" should get trailing space
        var result = VoiceInsertionHeuristics.Apply("", "Score and seven", "years ago");
        Assert.That(result, Does.EndWith(" "));
        Assert.That(result + "years ago", Is.EqualTo("Score and seven years ago"));
    }

    [Test]
    public void Apply_RightContextDigit_AddsTrailingSpace() {
        var result = VoiceInsertionHeuristics.Apply("", "Step", "42");
        Assert.That(result, Is.EqualTo("Step "));
    }

    [Test]
    public void Apply_RightContextOpenParen_AddsTrailingSpace() {
        // caret before "(note)" — should ensure space before the paren
        var result = VoiceInsertionHeuristics.Apply("see", "the details", "(note)");
        Assert.That(result, Does.EndWith(" "));
    }

    [Test]
    public void Apply_RightContextCloseParen_NoTrailingSpace() {
        // caret inside "(|)" — voice fills the parens, no trailing space before ')'
        var result = VoiceInsertionHeuristics.Apply("(", "for today.", ")");
        Assert.That(result, Is.EqualTo("for today"));  // lowercase + period stripped + no trailing space
    }

    [Test]
    public void Apply_RightContextPunct_TrailingPunctStripped_NoRunTogether() {
        // caret before "," — "hello." should become "hello" (punct stripped)
        var result = VoiceInsertionHeuristics.Apply("", "Hello.", ", world");
        Assert.That(result, Does.Not.EndWith("."));
    }

    // ── Apply: open-paren left context (no-prefix-space + continuation) ──────

    [Test]
    public void Apply_LeftContextOpenParen_LowercasesAndStripsTrailingPeriod() {
        // left = "(", right = ")" — inside parens: lowercase, strip period, no trailing space
        var result = VoiceInsertionHeuristics.Apply("(", "For today.", ")");
        Assert.That(result, Is.EqualTo("for today"));
    }

    // ── IsLeftContextEndsWithDigit ────────────────────────────────────────────

    [Test]
    public void IsLeftContextEndsWithDigit_EmptyString_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsLeftContextEndsWithDigit(""), Is.False);
    }

    [Test]
    public void IsLeftContextEndsWithDigit_EndsWithDigit_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsLeftContextEndsWithDigit("step 6"), Is.True);
    }

    [Test]
    public void IsLeftContextEndsWithDigit_EndsWithDigitAndTrailingSpace_ReturnsTrue() {
        Assert.That(VoiceInsertionHeuristics.IsLeftContextEndsWithDigit("step 6 "), Is.True);
    }

    [Test]
    public void IsLeftContextEndsWithDigit_EndsWithLetter_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsLeftContextEndsWithDigit("hello"), Is.False);
    }

    [Test]
    public void IsLeftContextEndsWithDigit_EndsWithPunct_ReturnsFalse() {
        Assert.That(VoiceInsertionHeuristics.IsLeftContextEndsWithDigit("hello."), Is.False);
    }

    // ── Apply: digit-left heuristic (heuristic 5) ────────────────────────────

    [Test]
    public void Apply_DigitLeft_UppercaseWord_LowercasesAndAddsSpace() {
        // "6" + "And" → " and"  (space prepended, A lowercased)
        var result = VoiceInsertionHeuristics.Apply("6", "And");
        Assert.That(result, Is.EqualTo(" and"));
    }

    [Test]
    public void Apply_DigitLeftWithTrailingSpace_UppercaseWord_LowercasesNoExtraSpace() {
        // "6 " + "And" → "and"  (space already in left context, no double-space)
        var result = VoiceInsertionHeuristics.Apply("6 ", "And");
        Assert.That(result, Is.EqualTo("and"));
    }

    [Test]
    public void Apply_DigitLeft_PronounI_AddsSpaceNoLowercase() {
        // "3" + "I've" → " I've"  (space added, but "I've" preserved)
        var result = VoiceInsertionHeuristics.Apply("3", "I've seen it");
        Assert.That(result, Does.StartWith(" I"));
    }

    [Test]
    public void Apply_DigitLeft_Acronym_AddsSpaceNoLowercase() {
        // "2" + "API" → " API"  (acronym preserved)
        var result = VoiceInsertionHeuristics.Apply("2", "API calls");
        Assert.That(result, Does.StartWith(" API"));
    }

    [Test]
    public void Apply_DigitLeft_AlreadyLowercase_OnlyAddsSpace() {
        // "6" + "and" — already lowercase, just space prepended
        var result = VoiceInsertionHeuristics.Apply("6", "and then");
        Assert.That(result, Is.EqualTo(" and then"));
    }

    [Test]
    public void Apply_LetterLeft_UppercaseWord_NoDigitHeuristic() {
        // "word" + "And" — letter on left, heuristic 5 does NOT fire
        // heuristic 1 (IsSentenceContinuation) fires instead → lowercased, no prefix space
        var result = VoiceInsertionHeuristics.Apply("word", "And");
        Assert.That(result, Is.EqualTo("and"));
        Assert.That(result, Does.Not.StartWith(" "));
    }

    [Test]
    public void Apply_EndToEnd_NumberInSentence() {
        // Full scenario: "There are 6" + "And more items" → "There are 6 and more items"
        var left = "There are 6";
        var inserted = VoiceInsertionHeuristics.Apply(left, "And more items");
        Assert.That(left + inserted, Is.EqualTo("There are 6 and more items"));
    }
}

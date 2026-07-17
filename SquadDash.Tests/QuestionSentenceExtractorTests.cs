namespace SquadDash.Tests;

[TestFixture]
internal sealed class QuestionSentenceExtractorTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<(int Start, int End)> Ranges(string text)
        => QuestionSentenceExtractor.ExtractQuestionSentenceRanges(text);

    // ─── basic cases ──────────────────────────────────────────────────────────

    [Test]
    public void NoQuestionMark_ReturnsEmpty()
    {
        Assert.That(Ranges("Hello World"), Is.Empty);
    }

    [Test]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.That(Ranges(""), Is.Empty);
    }

    [Test]
    public void SingleQuestionMark_Only_ReturnsZeroZero()
    {
        // "?" — the only character; sentence starts and ends at index 0.
        var r = Ranges("?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0], Is.EqualTo((0, 0)));
    }

    [Test]
    public void SingleQuestionMark_MidSentence()
    {
        // "Hello? World" — sentence begins at 0, ? at 5.
        var r = Ranges("Hello? World");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(0));
        Assert.That(r[0].End,   Is.EqualTo(5));
    }

    [Test]
    public void SingleQuestionMark_AtStart()
    {
        // "? what" — the ? is the very first character.
        var r = Ranges("? what");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0], Is.EqualTo((0, 0)));
    }

    [Test]
    public void SingleQuestionMark_AtEnd()
    {
        // "Is this right?" — sentence covers the full string (indices 0–13).
        var r = Ranges("Is this right?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(0));
        Assert.That(r[0].End,   Is.EqualTo(13));
    }

    // ─── multiple questions ───────────────────────────────────────────────────

    [Test]
    public void TwoSeparateSentences_ReturnsTwoRanges()
    {
        // "Is it A? Is it B?" — each ? begins a new sentence (? is a boundary).
        // Sentence 1: "Is it A?"  → (0, 7)
        // Sentence 2: "Is it B?"  → (9, 16)
        var r = Ranges("Is it A? Is it B?");
        Assert.That(r, Has.Count.EqualTo(2));
        Assert.That(r[0], Is.EqualTo((0, 7)));
        Assert.That(r[1], Is.EqualTo((9, 16)));
    }

    [Test]
    public void TwoQuestionsInSameSentence_MergesWhenStartAdjacent()
    {
        // "A?B?" — no whitespace between the two sentences; sentenceStart(2nd) == prevEnd+1 → merge.
        var r = Ranges("A?B?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(0));
        Assert.That(r[0].End,   Is.EqualTo(3));
    }

    [Test]
    public void TwoQuestionsWithSpaceBetween_ReturnsTwoRanges()
    {
        // "Is it A? Or B?" — sentenceStart of "Or B?" is 9, prevEnd+1 is 8 → distinct ranges.
        var r = Ranges("Is it A? Or B?");
        Assert.That(r, Has.Count.EqualTo(2));
        Assert.That(r[0], Is.EqualTo((0, 7)));
        Assert.That(r[1], Is.EqualTo((9, 13)));
    }

    [Test]
    public void ThreeQuestions_ReturnsThreeRanges()
    {
        // "Why? What? How?" — every ? acts as a sentence boundary for the next.
        // (0,3) "Why?" | (5,9) "What?" | (11,14) "How?"
        var r = Ranges("Why? What? How?");
        Assert.That(r, Has.Count.EqualTo(3));
        Assert.That(r[0], Is.EqualTo((0,  3)));
        Assert.That(r[1], Is.EqualTo((5,  9)));
        Assert.That(r[2], Is.EqualTo((11, 14)));
    }

    [Test]
    public void MixedSentencesWithSpaceGaps_ReturnsThreeRanges()
    {
        // "First question? Second A? Or B?"
        // Each ? has sentenceStart > prevEnd+1, so no merging → three ranges.
        // "First question?" (0,14) | "Second A?" (16,24) | "Or B?" (26,30)
        var r = Ranges("First question? Second A? Or B?");
        Assert.That(r, Has.Count.EqualTo(3));
        Assert.That(r[0], Is.EqualTo((0,  14)));
        Assert.That(r[1], Is.EqualTo((16, 24)));
        Assert.That(r[2], Is.EqualTo((26, 30)));
    }

    [Test]
    public void AdjacentDoubleQuestionMark_MergesIntoOneRange()
    {
        // "Really??" — the second ? immediately follows the first; they merge.
        var r = Ranges("Really??");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(0));
        Assert.That(r[0].End,   Is.EqualTo(7));
    }

    // ─── boundary cases ───────────────────────────────────────────────────────

    [Test]
    public void LeadingWhitespace_SentenceStartSkipsWhitespace()
    {
        // "  Is this right?" — two leading spaces; sentence start skips to index 2.
        var r = Ranges("  Is this right?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(2));
        Assert.That(r[0].End,   Is.EqualTo(15));
    }

    [Test]
    public void SentenceBoundaryAtPeriod_StartsAfterPeriodAndSpace()
    {
        // "Done. Is this right?" — '.' at 4 marks the boundary; sentence starts at 6 ('I').
        var r = Ranges("Done. Is this right?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(6));
        Assert.That(r[0].End,   Is.EqualTo(19));
    }

    [Test]
    public void SentenceBoundaryAtExclamation_StartsAfterExclamationAndSpace()
    {
        // "Done! Is this right?" — '!' at 4 marks the boundary; sentence starts at 6 ('I').
        var r = Ranges("Done! Is this right?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(6));
        Assert.That(r[0].End,   Is.EqualTo(19));
    }

    [Test]
    public void SentenceBoundaryAtNewline_StartsImmediatelyAfterNewline()
    {
        // "Done\nIs this right?" — '\n' at 4; 'I' follows with no space → start at 5.
        var r = Ranges("Done\nIs this right?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(5));
        Assert.That(r[0].End,   Is.EqualTo(18));
    }

    [Test]
    public void SentenceBoundaryAtCarriageReturnNewline_SkipsBothChars()
    {
        // "Done\r\nIs this right?" — '\r' at 4 is the boundary; sentenceStart=5 is '\n'
        // (whitespace), so the loop advances to 6 ('I').
        var r = Ranges("Done\r\nIs this right?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(6));
        Assert.That(r[0].End,   Is.EqualTo(19));
    }

    [Test]
    public void WhitespaceOnlyBeforeQuestion_SentenceStartAtQuestion()
    {
        // "   ?" — three spaces then '?'; whitespace-skip advances until sentenceStart == qIdx.
        var r = Ranges("   ?");
        Assert.That(r, Has.Count.EqualTo(1));
        Assert.That(r[0].Start, Is.EqualTo(3));
        Assert.That(r[0].End,   Is.EqualTo(3));
    }

    [Test]
    public void MultipleQuestionsWithLeadingWhitespace_FirstSentenceStartSkipsWhitespace()
    {
        // "  Why? How?" — first ? at 5, second at 10.
        // "  Why?" → sentenceStart skips 2 spaces → start=2, end=5
        // " How?" → boundary '?' at 5, sentenceStart=6, skip ' '→7, end=10
        var r = Ranges("  Why? How?");
        Assert.That(r, Has.Count.EqualTo(2));
        Assert.That(r[0], Is.EqualTo((2, 5)));
        Assert.That(r[1], Is.EqualTo((7, 10)));
    }
}

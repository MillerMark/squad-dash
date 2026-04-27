using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MarkdownDocumentRendererCommitHashTests {

    // --- Regression: the exact reported bug ---

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenHashIsSuffixOfEnglishWord() {
        // "cceeded" inside "succeeded" must never be detected as a commit hash.
        const string text = "succeeded";
        // 'c' first appears at index 1 (su[c]ceeded). Check every position.
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadCommitHash(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadCommitHash_ReturnsFalse_ForFullRegressionSentence() {
        // Regression guard: scan every character position in the exact sentence from the bug report.
        const string text = "Build succeeded — only warnings (MSB3277 assembly conflicts), no CS errors.";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadCommitHash(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False,
            "No substring of the sentence should be detected as a commit hash.");
    }

    // --- Negative: preceding letter/digit boundary ---

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenHashIsPrefixedByNonHexLetter() {
        // text[0] = 'u' (non-hex letter), scanning starts at index 1 where the hex run begins.
        const string text = "uabcdef1234";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 1, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenPrecededByHexLetter() {
        // text[0] = 'a' which is both a hex char and a letter — still a letter/digit boundary.
        const string text = "aabcdef1";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 1, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenPrecededByDigit() {
        // text[0] = '1' (digit), scan at index 1.
        const string text = "1abcdef1";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 1, out _, out _);
        Assert.That(result, Is.False);
    }

    // --- Negative: trailing letter/digit boundary ---

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenFollowedByNonHexLetter() {
        // "abcdef12" (8 hex chars) immediately followed by 'x' — rejected because 'x' is a letter.
        const string text = "abcdef12x";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 0, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenFollowedByDigit() {
        // Exactly 7 hex chars followed by '9' makes it 8 total — the '9' is a hex digit so
        // the while loop consumes it. Result is an 8-char hash followed by end-of-string, so
        // this is actually a *valid* hash. Use a non-hex digit continuation to truly test the
        // trailing-digit boundary: 40 hex chars + one more digit → length 41 → too long.
        var fortyOneHex = new string('a', 41);
        var result = MarkdownDocumentRenderer.TryReadCommitHash(fortyOneHex, 0, out _, out _);
        Assert.That(result, Is.False);
    }

    // --- Negative: length constraints ---

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenTooShort() {
        // 6-char hex string, standalone.
        const string sixHex = "abcdef";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(sixHex, 0, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenTooLong() {
        // 41-char hex string, no trailing chars.
        var fortyOneHex = new string('b', 41);
        var result = MarkdownDocumentRenderer.TryReadCommitHash(fortyOneHex, 0, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadCommitHash_ReturnsFalse_WhenStartIndexHasNonHexChar() {
        // 'x' is not a hex char — zero hex chars consumed, length = 0 → too short.
        var result = MarkdownDocumentRenderer.TryReadCommitHash("xyz", 0, out _, out _);
        Assert.That(result, Is.False);
    }

    // --- Positive cases ---

    [Test]
    public void TryReadCommitHash_ReturnsTrue_ForMinimumLengthHash() {
        // Exactly 7 hex chars, standalone.
        const string text = "abcdef1";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 0, out _, out var hash);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(hash, Is.EqualTo("abcdef1"));
        });
    }

    [Test]
    public void TryReadCommitHash_ReturnsTrue_ForFullLengthSha1() {
        // 40-char hex string (max valid SHA-1 length).
        var sha1 = new string('a', 39) + "f";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(sha1, 0, out _, out var hash);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(hash, Is.EqualTo(sha1));
        });
    }

    [Test]
    public void TryReadCommitHash_ReturnsTrue_AtStartOfString() {
        // startIndex = 0 means no preceding char — boundary check is skipped.
        const string text = "1a2b3c4";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 0, out _, out var hash);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(hash, Is.EqualTo("1a2b3c4"));
        });
    }

    [Test]
    public void TryReadCommitHash_ReturnsTrue_WhenPrecededBySpace() {
        // Space is not a letter or digit, so the start-boundary is satisfied.
        const string text = " abc1234f";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 1, out _, out var hash);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(hash, Is.EqualTo("abc1234f"));
        });
    }

    [Test]
    public void TryReadCommitHash_ReturnsTrue_WhenPrecededByColon() {
        // Colon is not a letter or digit — valid start boundary.
        const string text = "commit:abc1234f";
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 7, out _, out var hash);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(hash, Is.EqualTo("abc1234f"));
        });
    }

    [Test]
    public void TryReadCommitHash_SetsCorrectHashAndNextIndex() {
        // Verify both out parameters are set correctly for a hash embedded in a sentence.
        const string text = "see abc1234f for details";
        //                       ^7     ^15
        var result = MarkdownDocumentRenderer.TryReadCommitHash(text, 4, out var nextIndex, out var hash);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(hash, Is.EqualTo("abc1234f"));
            Assert.That(nextIndex, Is.EqualTo(12)); // 4 + 8 chars
        });
    }
}

using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PushNotificationServiceTests {

    // ── ExtractNotificationJson ───────────────────────────────────────────────

    [Test]
    public void ExtractNotificationJson_NullInput_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractNotificationJson(null), Is.Null);
    }

    [Test]
    public void ExtractNotificationJson_WhitespaceInput_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractNotificationJson("   "), Is.Null);
    }

    [Test]
    public void ExtractNotificationJson_NoJsonPresent_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractNotificationJson("All done, no JSON here."), Is.Null);
    }

    [Test]
    public void ExtractNotificationJson_ValidJson_ReturnsNotificationText() {
        var result = PushNotificationService.ExtractNotificationJson("""{"notification": "Build succeeded"}""");
        Assert.That(result, Is.EqualTo("Build succeeded"));
    }

    [Test]
    public void ExtractNotificationJson_CaseInsensitiveKey_ReturnsNotificationText() {
        var result = PushNotificationService.ExtractNotificationJson("""{"Notification": "Tests passed"}""");
        Assert.That(result, Is.EqualTo("Tests passed"));
    }

    [Test]
    public void ExtractNotificationJson_EmbeddedInLargerText_ReturnsNotificationText() {
        var response = """
            I finished reviewing the code. Summary: {"notification": "Review complete"} — all looks good.
            """;
        Assert.That(PushNotificationService.ExtractNotificationJson(response), Is.EqualTo("Review complete"));
    }

    [Test]
    public void ExtractNotificationJson_WhitespacePaddedJson_ReturnsNotificationText() {
        var result = PushNotificationService.ExtractNotificationJson("""{ "notification" : "Deployed to staging" }""");
        Assert.That(result, Is.EqualTo("Deployed to staging"));
    }

    [Test]
    public void ExtractNotificationJson_EmptyNotificationValue_ReturnsEmptyString() {
        var result = PushNotificationService.ExtractNotificationJson("""{"notification": ""}""");
        Assert.That(result, Is.EqualTo(""));
    }

    // ── BuildFallbackSummary ──────────────────────────────────────────────────

    [Test]
    public void BuildFallbackSummary_NullInput_ReturnsNull() {
        Assert.That(PushNotificationService.BuildFallbackSummary(null), Is.Null);
    }

    [Test]
    public void BuildFallbackSummary_WhitespaceInput_ReturnsNull() {
        Assert.That(PushNotificationService.BuildFallbackSummary("   "), Is.Null);
    }

    [Test]
    public void BuildFallbackSummary_AllStopWords_ReturnsNull() {
        Assert.That(PushNotificationService.BuildFallbackSummary("the a an and or but"), Is.Null);
    }

    [Test]
    public void BuildFallbackSummary_CapitalizesFirstWord() {
        var result = PushNotificationService.BuildFallbackSummary("refactor authentication service");
        Assert.That(result, Does.StartWith("R"));
    }

    [Test]
    public void BuildFallbackSummary_StripsMixedStopWords() {
        // "the", "and", "a" are stop words; "fix", "login", "bug" should survive
        var result = PushNotificationService.BuildFallbackSummary("fix the login and a bug");
        Assert.That(result, Is.EqualTo("Fix login bug"));
    }

    [Test]
    public void BuildFallbackSummary_TakesAtMostSevenMeaningfulWords() {
        var prompt = "refactor authentication service rebuild pipeline deploy staging production release candidate";
        var result = PushNotificationService.BuildFallbackSummary(prompt);
        var wordCount = result!.Split(' ').Length;
        Assert.That(wordCount, Is.LessThanOrEqualTo(7));
    }

    [Test]
    public void BuildFallbackSummary_FiltersWordsShorterThanTwoChars() {
        // Single-char tokens like "x" or "y" should be excluded
        var result = PushNotificationService.BuildFallbackSummary("x y deploy pipeline");
        Assert.That(result, Is.EqualTo("Deploy pipeline"));
    }

    [Test]
    public void BuildFallbackSummary_PlainPrompt_ReturnsMeaningfulSummary() {
        var result = PushNotificationService.BuildFallbackSummary("update the README with installation instructions");
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("README").Or.Contain("update").IgnoreCase);
    }

    // ── ExtractGitCommitSha ───────────────────────────────────────────────────

    [Test]
    public void ExtractGitCommitSha_NoOutputs_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractGitCommitSha([]), Is.Null);
    }

    [Test]
    public void ExtractGitCommitSha_NullAndEmptyOutputs_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractGitCommitSha([null, "", "  "]), Is.Null);
    }

    [Test]
    public void ExtractGitCommitSha_NoCommitInOutput_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractGitCommitSha(["Build succeeded\n0 errors"]), Is.Null);
    }

    [Test]
    public void ExtractGitCommitSha_TypicalGitCommitOutput_ReturnsSha() {
        var output = "[main d5ca047] Fix F11 fullscreen disappear\n 1 file changed, 4 insertions(+), 1 deletion(-)";
        var result = PushNotificationService.ExtractGitCommitSha([output]);
        Assert.That(result, Is.EqualTo("d5ca047"));
    }

    [Test]
    public void ExtractGitCommitSha_MultipleOutputs_ReturnsFirstCommitSha() {
        string?[] outputs = [
            "Running tests...\nAll passed.",
            "[feature/xyz abc1234] Add feature\n 2 files changed",
            "[main def5678] Follow-up fix\n 1 file changed"
        ];
        var result = PushNotificationService.ExtractGitCommitSha(outputs);
        Assert.That(result, Is.EqualTo("abc1234"));
    }

    [Test]
    public void ExtractGitCommitSha_OutputWithLongSha_ReturnsFullMatch() {
        var output = "[main abcdef1234567] Fix something";
        var result = PushNotificationService.ExtractGitCommitSha([output]);
        Assert.That(result, Is.EqualTo("abcdef1234567"));
    }
}

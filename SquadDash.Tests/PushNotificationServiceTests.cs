using System.Collections.Generic;
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

    // ── ExtractSquadashPayload ────────────────────────────────────────────────

    [Test]
    public void ExtractSquadashPayload_NullInput_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractSquadashPayload(null), Is.Null);
    }

    [Test]
    public void ExtractSquadashPayload_NoPayload_ReturnsNull() {
        Assert.That(PushNotificationService.ExtractSquadashPayload("All done, no commands here."), Is.Null);
    }

    [Test]
    public void ExtractSquadashPayload_CommandOnly_ParsesCommand() {
        var result = PushNotificationService.ExtractSquadashPayload(
            """{"squadash": {"command": "stop_loop"}}""");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Command, Is.EqualTo("stop_loop"));
        Assert.That(result.Notification, Is.Null);
    }

    [Test]
    public void ExtractSquadashPayload_NotificationOnly_ParsesNotification() {
        var result = PushNotificationService.ExtractSquadashPayload(
            """{"squadash": {"notification": "All RC tasks done"}}""");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Command, Is.Null);
        Assert.That(result.Notification, Is.EqualTo("All RC tasks done"));
    }

    [Test]
    public void ExtractSquadashPayload_CommandAndNotification_ParsesBoth() {
        var result = PushNotificationService.ExtractSquadashPayload(
            """{"squadash": {"command": "stop_loop", "notification": "All tasks complete"}}""");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Command, Is.EqualTo("stop_loop"));
        Assert.That(result.Notification, Is.EqualTo("All tasks complete"));
    }

    [Test]
    public void ExtractSquadashPayload_EmbeddedInLargerText_Parses() {
        var response = "Work complete.\n{\"squadash\": {\"command\": \"stop_loop\", \"notification\": \"Done\"}}\nThat's all.";
        var result = PushNotificationService.ExtractSquadashPayload(response);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Command, Is.EqualTo("stop_loop"));
        Assert.That(result.Notification, Is.EqualTo("Done"));
    }

    // ── ExtractNotificationJson handles unified squadash format ───────────────

    [Test]
    public void ExtractNotificationJson_UnifiedFormat_ReturnsNotificationText() {
        var result = PushNotificationService.ExtractNotificationJson(
            """{"squadash": {"command": "stop_loop", "notification": "All done"}}""");
        Assert.That(result, Is.EqualTo("All done"));
    }

    [Test]
    public void ExtractNotificationJson_LegacyFormatStillWorks() {
        var result = PushNotificationService.ExtractNotificationJson(
            """{"notification": "Legacy format still works"}""");
        Assert.That(result, Is.EqualTo("Legacy format still works"));
    }

    // ── BuildAugmentedPrompt ──────────────────────────────────────────────────

    [Test]
    public void BuildAugmentedPrompt_NoCommands_ReturnsOriginalInstructions() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", null);
        Assert.That(result, Is.EqualTo("Do work."));
    }

    [Test]
    public void BuildAugmentedPrompt_EmptyCommands_ReturnsOriginalInstructions() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", new List<string>());
        Assert.That(result, Is.EqualTo("Do work."));
    }

    [Test]
    public void BuildAugmentedPrompt_StopLoopCommand_InjectsReferenceBlock() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", ["stop_loop"]);
        Assert.That(result, Does.Contain("Do work."));
        Assert.That(result, Does.Contain("SquadDash loop commands"));
        Assert.That(result, Does.Contain("stop_loop"));
        Assert.That(result, Does.Contain("HOST_COMMAND_JSON"));
    }

    [Test]
    public void BuildAugmentedPrompt_MultipleCommands_AllAppear() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", ["stop_loop", "start_loop"]);
        Assert.That(result, Does.Contain("stop_loop"));
        Assert.That(result, Does.Contain("start_loop"));
    }
}

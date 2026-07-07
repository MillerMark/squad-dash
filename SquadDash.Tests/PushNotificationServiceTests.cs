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

    // ── CollectAgentTurnOutputsSince ─────────────────────────────────────────

    [Test]
    public void CollectAgentTurnOutputsSince_EmptyThreadList_ReturnsEmpty() {
        var result = PushNotificationService.CollectAgentTurnOutputsSince(
            [],
            DateTimeOffset.UtcNow);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CollectAgentTurnOutputsSince_TurnStartedExactlyAtCutoff_IsIncluded() {
        var cutoff = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var tool = MakeTool("[main abc1234] Fix bug\n 1 file");
        var turn = MakeTurn(startedAt: cutoff, tools: [tool]);

        var result = PushNotificationService.CollectAgentTurnOutputsSince([[turn]], cutoff).ToList();
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Does.Contain("abc1234"));
    }

    [Test]
    public void CollectAgentTurnOutputsSince_TurnStartedBeforeCutoff_IsExcluded() {
        var cutoff = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var tool = MakeTool("[main oldsha1] Old commit\n 1 file");
        var oldTurn = MakeTurn(startedAt: cutoff.AddSeconds(-1), tools: [tool]);

        var result = PushNotificationService.CollectAgentTurnOutputsSince([[oldTurn]], cutoff).ToList();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CollectAgentTurnOutputsSince_MixedOldAndNewTurns_OnlyReturnsNewOutputs() {
        var cutoff = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var oldTool = MakeTool("[main old1111] Prior turn commit");
        var oldTurn = MakeTurn(startedAt: cutoff.AddMinutes(-5), tools: [oldTool]);

        var newTool = MakeTool("[main new2222] Current turn commit");
        var newTurn = MakeTurn(startedAt: cutoff.AddSeconds(1), tools: [newTool]);

        var result = PushNotificationService.CollectAgentTurnOutputsSince([[oldTurn, newTurn]], cutoff).ToList();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Does.Contain("new2222"));
        Assert.That(result, Has.None.Contain("old1111"));
    }

    [Test]
    public void CollectAgentTurnOutputsSince_MultipleThreads_OnlyCurrentTurnOutputsIncluded() {
        var cutoff = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);

        // Thread A: one old turn (before cutoff) and one new turn (after cutoff)
        var oldToolA = MakeTool("[main stale1] stale commit from thread A");
        var oldTurnA = MakeTurn(startedAt: cutoff.AddMinutes(-10), tools: [oldToolA]);

        var newToolA = MakeTool("[main fresh1] new commit from thread A");
        var newTurnA = MakeTurn(startedAt: cutoff, tools: [newToolA]);

        // Thread B: one old turn only
        var oldToolB = MakeTool("[main stale2] stale commit from thread B");
        var oldTurnB = MakeTurn(startedAt: cutoff.AddMinutes(-2), tools: [oldToolB]);

        var result = PushNotificationService
            .CollectAgentTurnOutputsSince([[oldTurnA, newTurnA], [oldTurnB]], cutoff)
            .ToList();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Does.Contain("fresh1"));
    }

    [Test]
    public void CollectAgentTurnOutputsSince_TurnWithNullTools_DoesNotThrow() {
        var cutoff = DateTimeOffset.UtcNow;
        var turnWithNullTools = MakeTurn(startedAt: cutoff, tools: null);

        Assert.DoesNotThrow(() => {
            var result = PushNotificationService
                .CollectAgentTurnOutputsSince([[turnWithNullTools]], cutoff)
                .ToList();
            Assert.That(result, Is.Empty);
        });
    }

    // Regression test: the bug that caused the same old SHA to appear in every turn's
    // approval entry after the first agent commit.
    [Test]
    public void ExtractGitCommitInfo_WithOldTurnFilteredOut_ReturnsNullForCurrentTurn() {
        var mainTurnStart = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        // Simulate an old saved turn (from a previous main turn) that has a git commit
        var staleCommitOutput = "[main abc0001] Commit from earlier session turn";
        var staleTool = MakeTool(staleCommitOutput);
        var staleTurn = MakeTurn(startedAt: mainTurnStart.AddMinutes(-30), tools: [staleTool]);

        // Current main turn has no commits of its own
        var currentTurnOutputs = new List<string?>();

        // Gather agent outputs using the time-gated helper
        var agentOutputs = PushNotificationService.CollectAgentTurnOutputsSince(
            [[staleTurn]], mainTurnStart);

        var allOutputs = currentTurnOutputs.Concat(agentOutputs);
        var result = PushNotificationService.ExtractGitCommitInfo(allOutputs);

        // The stale commit must not bleed into the current turn
        Assert.That(result, Is.Null,
            "Stale commit SHA from a prior turn must not appear in the current turn's commit detection.");
    }

    [Test]
    public void ExtractGitCommitInfo_NewAgentTurnWithinCurrentTurn_CommitIsDetected() {
        var mainTurnStart = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        // Agent committed DURING the current main turn
        var freshCommitOutput = "[main cafe999] Fresh commit during this turn";
        var freshTool = MakeTool(freshCommitOutput);
        var freshTurn = MakeTurn(startedAt: mainTurnStart.AddSeconds(5), tools: [freshTool]);

        var agentOutputs = PushNotificationService.CollectAgentTurnOutputsSince(
            [[freshTurn]], mainTurnStart);

        var result = PushNotificationService.ExtractGitCommitInfo(agentOutputs);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.CommitSha, Is.EqualTo("cafe999"));
        Assert.That(result.CommitMessage, Is.EqualTo("Fresh commit during this turn"));
    }

    [Test]
    public void ExtractGitCommitInfo_NativeGitOutput_UsesApprovalGroupFromResponse() {
        const string response = """
            Committed: abc1234

            APPROVAL_GROUP_JSON:
            {"sha":"abc1234","group":"UI Polish"}
            """;

        var result = PushNotificationService.ExtractGitCommitInfo(
            ["[main abc1234] Refine approval transcript labels"],
            response);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.CommitSha, Is.EqualTo("abc1234"));
        Assert.That(result.CommitMessage, Is.EqualTo("Refine approval transcript labels"));
        Assert.That(result.FeatureGroup, Is.EqualTo("UI Polish"));
    }

    [Test]
    public void ExtractGitCommitInfo_AgentBacktickCommit_UsesApprovalGroupFromResponse() {
        const string response = """
            Committed as `def5678`.

            APPROVAL_GROUP_JSON:
            {"sha":"def5678","group":"Loop Reliability"}
            """;

        var result = PushNotificationService.ExtractGitCommitInfo([], response);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.CommitSha, Is.EqualTo("def5678"));
        Assert.That(result.FeatureGroup, Is.EqualTo("Loop Reliability"));
    }

    [Test]
    public void ExtractGitCommitInfo_PlainCommitted_UsesPrettyPrintedApprovalGroupFromResponse() {
        const string response = """
            Committed: c38333d

            APPROVAL_GROUP_JSON:
            {
              "group": "Guided Tour",
              "sha": "c38333d"
            }
            """;

        var result = PushNotificationService.ExtractGitCommitInfo([], response);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.CommitSha, Is.EqualTo("c38333d"));
        Assert.That(result.FeatureGroup, Is.EqualTo("Guided Tour"));
    }

    [Test]
    public void ExtractGitCommitInfo_NativeGitOutput_UsesFencedApprovalGroupFromResponse() {
        const string response = """
            Committed: abc1234

            APPROVAL_GROUP_JSON:
            ```json
            {
              "sha": "abc1234",
              "group": "Approval Categorization"
            }
            ```
            """;

        var result = PushNotificationService.ExtractGitCommitInfo(
            ["[main abc1234] Categorize approval commits"],
            response);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FeatureGroup, Is.EqualTo("Approval Categorization"));
    }

    [Test]
    public void BuildMissingDelegatedCommitCompletionNote_ReadAgentReportedCommitAndCoordinatorOmittedSha_ReturnsNote() {
        var commitInfo = new GitCommitInfo("35f5087", "feat: multi-select + copy/cut/paste in Guided Tour Step Editor");
        const string coordinatorResponse = "Lyra is implementing multi-select + copy/cut/paste. Here's the auto-save spec for your inbox:";
        const string readAgentOutput = "Build ✅ · Tests ✅ (2653 passed) · Committed as `35f5087`.\n\n(Full response provided to agent)";

        var note = PushNotificationService.BuildMissingDelegatedCommitCompletionNote(
            commitInfo,
            coordinatorResponse,
            [readAgentOutput]);

        Assert.That(note, Is.Not.Null);
        Assert.That(note!, Does.Contain("35f5087"));
        Assert.That(note, Does.Contain("delegated agent"));
    }

    [Test]
    public void BuildMissingDelegatedCommitCompletionNote_CoordinatorAlreadyMentionedSha_ReturnsNull() {
        var commitInfo = new GitCommitInfo("35f5087", null);
        const string coordinatorResponse = "Build passed and committed as `35f5087`.";
        const string readAgentOutput = "Build passed. Tests passed. Committed as `35f5087`.";

        var note = PushNotificationService.BuildMissingDelegatedCommitCompletionNote(
            commitInfo,
            coordinatorResponse,
            [readAgentOutput]);

        Assert.That(note, Is.Null);
    }

    [Test]
    public void BuildMissingDelegatedCommitCompletionNote_NoReadAgentCompletion_ReturnsNull() {
        var commitInfo = new GitCommitInfo("35f5087", null);

        var note = PushNotificationService.BuildMissingDelegatedCommitCompletionNote(
            commitInfo,
            "Done.",
            ["Some unrelated tool output"]);

        Assert.That(note, Is.Null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TranscriptToolRecord MakeTool(string? outputText) =>
        new TranscriptToolRecord(
            ToolCallId:    null,
            Descriptor:    new ToolTranscriptDescriptor("git"),
            ArgsJson:      null,
            StartedAt:     DateTimeOffset.UtcNow,
            FinishedAt:    null,
            ProgressText:  null,
            OutputText:    outputText,
            DetailContent: null,
            IsCompleted:   true,
            Success:       true);

    private static TranscriptTurnRecord MakeTurn(
        DateTimeOffset startedAt,
        IReadOnlyList<TranscriptToolRecord>? tools)
    {
        return new TranscriptTurnRecord(
            StartedAt:        startedAt,
            CompletedAt:      null,
            Prompt:           string.Empty,
            ThinkingText:     string.Empty,
            ResponseText:     string.Empty,
            ThinkingCollapsed: false,
            Tools:            tools ?? []);
    }

    [Test]
    public void BuildAugmentedPrompt_NoCommands_ReturnsOriginalInstructions(){
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

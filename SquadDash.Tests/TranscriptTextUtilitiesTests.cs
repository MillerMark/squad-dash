namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptTextUtilitiesTests {

    [Test]
    public void FormatThinkingText_SuffixSplit_FusesSpaceSeparatedSuffixes() {
        // The SuffixSplitRegex removes whitespace between a 4+-letter root and a recognised
        // grammatical suffix, repairing thinking-text that arrives with spurious mid-word spaces.
        Assert.Multiple(() => {
            Assert.That(TranscriptTextUtilities.FormatThinkingText("comput ing"),    Is.EqualTo("computing"));
            Assert.That(TranscriptTextUtilities.FormatThinkingText("organiz ation"), Is.EqualTo("organization"));
            Assert.That(TranscriptTextUtilities.FormatThinkingText("nation ality"),  Is.EqualTo("nationality"));
            Assert.That(TranscriptTextUtilities.FormatThinkingText("manage ment"),   Is.EqualTo("management"));
            // Short roots (< 4 letters) must not be fused.
            Assert.That(TranscriptTextUtilities.FormatThinkingText("go ing"),        Is.EqualTo("go ing"));
        });
    }

    [Test]
    public void SanitizeResponseText_InlineInboxSentinelMention_DoesNotStripText() {
        const string text = "The parser only accepts a bare `INBOX_MESSAGE_JSON:` line at the end.";

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo(text));
    }

    [Test]
    public void SanitizeResponseText_BacktickInlineWithJson_DoesNotStripOrLeaveOrphanBacktick() {
        // An inline code span like `INBOX_MESSAGE_JSON: { "from": "argus-weld" }` is NOT a real
        // block — it should be left fully intact, not stripped to a lone backtick.
        const string text = "See this example: `INBOX_MESSAGE_JSON: { \"from\": \"argus-weld\" }` for details.";

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo(text));
    }

    [Test]
    public void SanitizeResponseText_BacktickInlineFollowedByTopLevelBlock_StripsOnlyBlock() {
        // When there is BOTH an inline backtick mention AND a real top-level block, only the
        // real block should be stripped. The inline mention should survive.
        const string text = """
            Example syntax: `INBOX_MESSAGE_JSON: {...}` — use this format.

            INBOX_MESSAGE_JSON:
            { "subject": "Real", "from": "argus-weld", "body": "Done", "attachments": [] }
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Does.Contain("`INBOX_MESSAGE_JSON:"));
        Assert.That(sanitized, Does.Not.Contain("\"subject\": \"Real\""));
    }

    /// <summary>
    /// Previously the parser stripped everything after a valid inbox block. It should now keep
    /// trailing visible prose while still removing the machine-readable JSON payload.
    /// </summary>
    [Test]
    public void SanitizeResponseText_CodeFencedInboxBlockWithTrailingText_PreservesTrailingText() {
        const string text = """
            Example:

            ```json
            INBOX_MESSAGE_JSON:
            { "subject": "Example", "from": "", "body": "", "attachments": [] }
            ```

            The real response continues here.
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("""
            Example:

            The real response continues here.
            """));
    }

    [Test]
    public void SanitizeResponseText_TopLevelPartialInboxBlock_StripsWhileStreaming() {
        const string text = """
            Report ready.

            INBOX_MESSAGE_JSON:
            {
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Report ready."));
    }

    [Test]
    public void SanitizeResponseText_FencedInboxBlockAtEnd_StripsDeliveredPayload() {
        const string text = """
            Report ready.

            ```
            INBOX_MESSAGE_JSON:
            {
              "subject": "README report",
              "from": "argus-weld",
              "body": "Done",
              "attachments": []
            }
            ```
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Report ready."));
    }

    [Test]
    public void SanitizeResponseText_TasksJsonBlock_StripsPayloadButPreservesVisibleText()
    {
        const string text = """
            Here is the proposed plan.

            TASKS_JSON:

            ```json
            {
              "groupId": "PLAN-20260725",
              "groupTitle": "Plan",
              "branch": "feature/plan",
              "summary": "Summary",
              "tasks": [{ "id": "PLAN-20260725-001", "description": "First", "dependsOn": [], "priority": "high" }]
            }
            ```

            This trailing explanation remains visible.
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("""
            Here is the proposed plan.

            This trailing explanation remains visible.
            """));
    }

    [Test]
    public void SanitizeResponseText_PartialTopLevelTasksJson_StripsWhileStreaming()
    {
        const string text = "Plan ready.\n\nTASKS_JSON:\n{\n  \"groupId\": \"PLAN-20260725\",";
        Assert.That(TranscriptTextUtilities.SanitizeResponseText(text), Is.EqualTo("Plan ready."));
    }

    [Test]
    public void SanitizeResponseText_DecomposeStepResult_StripsHostPayload()
    {
        const string text = """
            Implemented and verified the assigned step.

            DECOMPOSE_STEP_RESULT_JSON:
            {"groupId":"PLAN-20260725","taskId":"PLAN-20260725-001","revision":"abc",
             "status":"complete","commit":"abcdef1","summary":"Done","remainingWork":[],
             "verification":{"status":"passed","command":"dotnet test","summary":"Passed"}}
            """;

        Assert.That(
            TranscriptTextUtilities.SanitizeResponseText(text),
            Is.EqualTo("Implemented and verified the assigned step."));
    }

    [Test]
    public void SanitizeResponseText_FencedTasksJsonExample_RemainsVisible()
    {
        const string text = """
            Example only:
            ```json
            TASKS_JSON:
            { "groupId": "PLAN-20260725" }
            ```
            """;
        Assert.That(TranscriptTextUtilities.SanitizeResponseText(text), Is.EqualTo(text));
    }

    [Test]
    public void SanitizeResponseText_InboxMessageJsonFileBlock_StripsDeliveredPayload() {
        const string text = """
            Report ready.

            INBOX_MESSAGE_JSON_FILE:
            { "path": ".squad/tmp/agent-artifacts/inbox.json" }
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Report ready."));
    }

    [Test]
    public void SanitizeResponseText_TopLevelApprovalGroupBlock_StripsFromDisplay() {
        const string text = """
            Committed: abc1234

            APPROVAL_GROUP_JSON:
            {"sha":"abc1234","group":"UI Polish"}
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Committed: abc1234"));
    }

    [Test]
    public void SanitizeResponseText_ApprovalGroupBlockWithTrailingText_StripsMachineBlock() {
        const string text = """
            Committed: abc1234

            APPROVAL_GROUP_JSON:
            {"sha":"abc1234","group":"UI Polish"}

            trailing accidental text
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Committed: abc1234"));
    }

    [Test]
    public void SanitizeResponseText_ApprovalGroupBeforeQuickReplies_PreservesQuickRepliesForRenderer() {
        const string text = """
            Want CassianRook to continue, or would you like to review the code first?

            APPROVAL_GROUP_JSON:
            {"sha":"86f4988","group":"GitHub Copilot Integration"}

            QUICK_REPLIES_JSON:
            [
              {
                "label": "Continue — implement VS account provider next",
                "routeMode": "start_named_agent",
                "targetAgent": "cassian-rook",
                "reason": "CassianRook owns the AI model integration."
              }
            ]

            <system_notification>{"notification":"Copilot provider committed."}</system_notification>
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);
        var parsed = QuickReplyOptionParser.TryExtractWithMetadata(
            sanitized,
            out var body,
            out QuickReplyOptionMetadata[] options);

        Assert.That(parsed, Is.True);
        Assert.That(body, Is.EqualTo("Want CassianRook to continue, or would you like to review the code first?"));
        Assert.That(options, Has.Length.EqualTo(1));
        Assert.That(options[0].Label, Is.EqualTo("Continue — implement VS account provider next"));
        Assert.That(options[0].RouteMode, Is.EqualTo("start_named_agent"));
        Assert.That(options[0].TargetAgent, Is.EqualTo("cassian-rook"));
    }

    [Test]
    public void MergeStreamingAndFinalResponse_AppendsTail_WhenFinalStartsWithStreamedText() {
        const string streamed = "Okay, now I have everything I need to solve the problem.";
        const string final = """
            Okay, now I have everything I need to solve the problem.

            ## Findings

            The report continues with the actual analysis.
            """;

        var merged = TranscriptTextUtilities.MergeStreamingAndFinalResponse(
            streamed,
            final,
            out var tail);

        Assert.Multiple(() => {
            Assert.That(merged, Is.EqualTo(final));
            Assert.That(tail, Does.Contain("## Findings"));
        });
    }

    [Test]
    public void MergeStreamingAndFinalResponse_UsesLongerFinal_WhenStreamedTextIsIncomplete() {
        const string streamed = "Short partial opening.";
        const string final = "Full final response with more detail, but a slightly different opening.";

        var merged = TranscriptTextUtilities.MergeStreamingAndFinalResponse(
            streamed,
            final,
            out var tail);

        Assert.Multiple(() => {
            Assert.That(merged, Is.EqualTo(final));
            Assert.That(tail, Is.Null);
        });
    }

    [Test]
    public void EnsureResponseParagraphBreak_AppendsBlankLineAfterText() {
        var builder = new System.Text.StringBuilder("Finished a sentence.");

        TranscriptTextUtilities.EnsureResponseParagraphBreak(builder);

        Assert.That(builder.ToString(), Is.EqualTo("Finished a sentence.\n\n"));
    }

    [Test]
    public void EnsureResponseParagraphBreak_CompletesSingleTrailingNewline() {
        var builder = new System.Text.StringBuilder("Finished a sentence.\n");

        TranscriptTextUtilities.EnsureResponseParagraphBreak(builder);

        Assert.That(builder.ToString(), Is.EqualTo("Finished a sentence.\n\n"));
    }

    [Test, Apartment(System.Threading.ApartmentState.STA)]
    public void GetSanitizedTurnResponseText_UsesSegmentBreaks_WhenFlatBuilderIsFused() {
        var thread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Coordinator",
            DateTimeOffset.UtcNow);
        var turn = new TranscriptTurnView(
            thread,
            "prompt",
            DateTimeOffset.UtcNow,
            new System.Windows.Documents.Section(),
            Array.Empty<System.Windows.Documents.Block>());
        var first = new TranscriptResponseEntry(turn, 1, new System.Windows.Documents.Section());
        first.RawTextBuilder.Append("Doing this myself because it's a quick code fix with a small, well-defined scope.");
        var second = new TranscriptResponseEntry(turn, 3, new System.Windows.Documents.Section());
        second.RawTextBuilder.Append("Let me read the relevant lines from all three files:");
        turn.ResponseEntries.Add(first);
        turn.ResponseEntries.Add(second);
        turn.ResponseTextBuilder.Append("Doing this myself because it's a quick code fix with a small, well-defined scope.Let me read the relevant lines from all three files:");

        var sanitized = TranscriptTextUtilities.GetSanitizedTurnResponseText(turn);

        Assert.That(
            sanitized,
            Is.EqualTo("Doing this myself because it's a quick code fix with a small, well-defined scope.\n\nLet me read the relevant lines from all three files:"));
    }

    [Test]
    public void SanitizeResponseText_RepairsObviousFusedProseBoundaries() {
        const string text = "The fix: update the helper.Now I'll run tests.ApplyQueueTabActiveState:Now add the helper method:Committed";

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(
            sanitized,
            Is.EqualTo("The fix: update the helper. Now I'll run tests. ApplyQueueTabActiveState: Now add the helper method: Committed"));
    }

    [Test]
    public void SanitizeResponseText_DoesNotRepairInsideInlineCodeOrFencedCode() {
        const string text = """
            Keep `helper.Now` unchanged.

            ```
            ApplyQueueTabActiveState:Now
            ```

            But repair outside:Now.
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Does.Contain("`helper.Now`"));
        Assert.That(sanitized, Does.Contain("ApplyQueueTabActiveState:Now"));
        Assert.That(sanitized, Does.Contain("outside: Now."));
    }

    [Test]
    public void SanitizeResponseText_DoesNotRepairUrlsPathsOrNumbers() {
        const string text = "Open https://example.com/a.B and C:\\Temp\\file. Version 1.2:3 stays. Then fix:Now.";

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(
            sanitized,
            Is.EqualTo("Open https://example.com/a.B and C:\\Temp\\file. Version 1.2:3 stays. Then fix: Now."));
    }
}

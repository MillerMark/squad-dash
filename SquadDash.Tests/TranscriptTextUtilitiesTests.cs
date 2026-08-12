namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptTextUtilitiesTests {

    [TestCase("TASKS_JSON")]
    [TestCase("TASKS_JSON:")]
    public void BuildProtocolJsonClipboardText_IncludesOneProtocolMarkerColon(string marker) {
        var result = TranscriptTextUtilities.BuildProtocolJsonClipboardText(
            marker,
            "{\n  \"groupId\": \"CALC-20260806\"\n}");

        Assert.That(result, Is.EqualTo(
            $"TASKS_JSON:{Environment.NewLine}{{\n  \"groupId\": \"CALC-20260806\"\n}}"));
    }

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

        Assert.That(sanitized.ReplaceLineEndings("\n"), Is.EqualTo("""
            Example:

            The real response continues here.
            """.ReplaceLineEndings("\n")));
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

        Assert.That(sanitized.ReplaceLineEndings("\n"), Is.EqualTo("""
            Here is the proposed plan.

            This trailing explanation remains visible.
            """.ReplaceLineEndings("\n")));
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
    public void SanitizeResponseText_InlineDecomposeStepResult_StripsAcceptedHostPayload()
    {
        const string text = """
            I’m starting the fresh attempt now. DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "MODELPROF-20260810",
              "taskId": "MODELPROF-20260810-005",
              "revision": "abc",
              "status": "failed",
              "summary": "The provider launch failed.",
              "remainingWork": []
            }
            """;

        Assert.That(
            TranscriptTextUtilities.SanitizeResponseText(text),
            Is.EqualTo("I’m starting the fresh attempt now."));
    }

    [Test]
    public void InspectableProtocolJson_InlineDecomposeStepResult_IsRetainedAsMetadata()
    {
        const string text = """
            I’m starting the fresh attempt now. DECOMPOSE_STEP_RESULT_JSON:
            { "groupId": "MODELPROF-20260810", "summary": "Failed" }
            """;

        var blocks = TranscriptTextUtilities.ExtractInspectableProtocolJsonBlocks(text);

        Assert.Multiple(() => {
            Assert.That(blocks, Has.Count.EqualTo(1));
            Assert.That(blocks[0].Marker, Is.EqualTo("DECOMPOSE_STEP_RESULT_JSON"));
            Assert.That(blocks[0].Json, Does.Contain("MODELPROF-20260810"));
        });
    }

    [Test]
    public void SanitizeResponseText_InlineCodeDecomposeMarker_RemainsVisible()
    {
        const string text = "Explain `DECOMPOSE_STEP_RESULT_JSON: { \"groupId\": \"example\" }` to the user.";

        Assert.That(TranscriptTextUtilities.SanitizeResponseText(text), Is.EqualTo(text));
    }

    [Test]
    public void InspectableProtocolJson_RetainsStrippedPayloadAsMetadata()
    {
        const string text = """
            Implemented and verified the assigned step.

            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "PLAN-20260725",
              "summary": "A closing brace inside a string: } is preserved"
            }
            """;

        var blocks = TranscriptTextUtilities.ExtractInspectableProtocolJsonBlocks(text);

        Assert.Multiple(() =>
        {
            Assert.That(blocks, Has.Count.EqualTo(1));
            Assert.That(blocks[0].Marker, Is.EqualTo("DECOMPOSE_STEP_RESULT_JSON"));
            Assert.That(blocks[0].Json, Does.Contain("\"groupId\": \"PLAN-20260725\""));
            Assert.That(blocks[0].Json, Does.Contain("A closing brace inside a string: } is preserved"));
        });
    }

    [Test]
    public void SanitizeResponseText_PlanValidationResult_StripsPayloadAndPreservesAssessment()
    {
        const string text = """
            All three assertions passed.

            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "PLAN-VAL-003",
              "planId": "PLAN-001",
              "passed": true,
              "summary": "Ready for the live soak.",
              "assertionEvidence": [
                { "assertion": "Build passes.", "passed": true, "evidence": "0 errors." }
              ],
              "validatedCommit": "98353d5755d1406d52a013e9024684457c4e73c5"
            }
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("All three assertions passed."));
    }

    [Test]
    public void SanitizeResponseText_PartialPlanValidationResult_HidesStreamingPayload()
    {
        const string text = "Validation complete.\n\nPLAN_VALIDATION_RESULT_JSON:\n{\n  \"validationId\": \"V1\",";

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Validation complete."));
    }

    [Test]
    public void SanitizeResponseText_PlanGateResponse_StripsPayloadAndPreservesVisibleText()
    {
        const string text = """
            The requested correction is understood.

            PLAN_GATE_RESPONSE_JSON:
            {"planId":"PLAN-1","gateId":"GATE-1","revision":"rev","requestVersion":2,
             "disposition":"request-rework","taskIds":["TASK-1"],"instructions":"Wire the runtime."}
            """;

        Assert.That(
            TranscriptTextUtilities.SanitizeResponseText(text),
            Is.EqualTo("The requested correction is understood."));
    }

    [Test]
    public void SanitizeResponseText_PartialPlanGateResponse_HidesStreamingPayload()
    {
        const string text = "Classifying the requested correction.\n\nPLAN_GATE_RESPONSE_JSON:\n{\n  \"planId\": \"PLAN-1\",";

        Assert.That(
            TranscriptTextUtilities.SanitizeResponseText(text),
            Is.EqualTo("Classifying the requested correction."));
    }

    [Test]
    public void SanitizeResponseText_PlanRecoveryAssessment_StripsCompleteAndStreamingPayloads()
    {
        const string complete = """
            Recovery evidence assessed.

            PLAN_RECOVERY_ASSESSMENT_JSON:
            { "planId": "PLAN-1", "classification": "partial", "remainingWork": ["Wire it."] }

            The recovery workflow will continue.
            """;
        const string streaming =
            "Recovery evidence assessed.\n\nPLAN_RECOVERY_ASSESSMENT_JSON:\n{\n  \"planId\": \"PLAN-1\",";

        Assert.Multiple(() =>
        {
            Assert.That(
                TranscriptTextUtilities.SanitizeResponseText(complete).ReplaceLineEndings("\n"),
                Is.EqualTo("Recovery evidence assessed.\n\nThe recovery workflow will continue."));
            Assert.That(
                TranscriptTextUtilities.SanitizeResponseText(streaming),
                Is.EqualTo("Recovery evidence assessed."));
        });
    }

    [Test]
    public void SanitizeResponseText_InlinePlanRecoveryAssessment_StripsPayloadAndKeepsJsonInspectable()
    {
        const string text = "Review complete. PLAN_RECOVERY_ASSESSMENT_JSON:\n" +
            "{ \"planId\": \"PLAN-1\", \"classification\": \"complete\" }";

        var blocks = TranscriptTextUtilities.ExtractInspectableProtocolJsonBlocks(text);

        Assert.Multiple(() =>
        {
            Assert.That(TranscriptTextUtilities.SanitizeResponseText(text), Is.EqualTo("Review complete."));
            Assert.That(blocks, Has.Count.EqualTo(1));
            Assert.That(blocks[0].Marker, Is.EqualTo("PLAN_RECOVERY_ASSESSMENT_JSON"));
            Assert.That(blocks[0].Json, Does.Contain("\"classification\": \"complete\""));
        });
    }

    [Test]
    public void SanitizeResponseText_RecoveryOptionsAndGateApproval_StripsHostPayloads()
    {
        const string recoveryOptions = """
            Recovery choices prepared.

            PLAN_RECOVERY_OPTIONS_JSON:
            { "planId": "PLAN-1", "actions": [{ "action": "resume", "label": "Resume" }] }
            """;
        const string gateApproval = """
            Approval recorded.

            PLAN_GATE_APPROVAL_JSON:
            { "planId": "PLAN-1", "gateId": "GATE-1", "approved": true }
            """;

        Assert.Multiple(() =>
        {
            Assert.That(TranscriptTextUtilities.SanitizeResponseText(recoveryOptions),
                Is.EqualTo("Recovery choices prepared."));
            Assert.That(TranscriptTextUtilities.SanitizeResponseText(gateApproval),
                Is.EqualTo("Approval recorded."));
        });
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
        Assert.That(TranscriptTextUtilities.SanitizeResponseText(text).ReplaceLineEndings("\n"), Is.EqualTo(text.ReplaceLineEndings("\n")));
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
    public void SanitizeResponseText_MultipleInboxMessageJsonFileBlocks_StripsEveryPayload()
    {
        const string text = """
            Reports ready.

            INBOX_MESSAGE_JSON_FILE:
            { "path": ".squad/tmp/agent-artifacts/lyra.json" }

            INBOX_MESSAGE_JSON_FILE:
            { "path": ".squad/tmp/agent-artifacts/vesper.json" }
            """;

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Reports ready."));
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

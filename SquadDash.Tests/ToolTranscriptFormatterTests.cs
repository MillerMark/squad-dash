namespace SquadDash.Tests;

[TestFixture]
internal sealed class ToolTranscriptFormatterTests {
    [Test]
    public void BuildRunningText_PrefersDescriptionForPowerShell() {
        var descriptor = new ToolTranscriptDescriptor(
            "powershell",
            Description: "Check git status and recent commits",
            Command: "git --no-pager status && git --no-pager log --oneline -5");

        var text = ToolTranscriptFormatter.BuildRunningText(descriptor);

        Assert.That(text, Is.EqualTo("Running PowerShell: Check git status and recent commits..."));
    }

    [Test]
    public void BuildCompletedText_UsesConciseContextWithoutRanPrefix() {
        var descriptor = new ToolTranscriptDescriptor(
            "report_intent",
            Intent: "Checking repo status");

        var text = ToolTranscriptFormatter.BuildCompletedText(descriptor, success: true);

        Assert.That(text, Is.EqualTo("Checking repo status."));
    }

    [Test]
    public void BuildFailurePreview_TruncatesAfterConfiguredLineLimit() {
        var text = string.Join("\n", Enumerable.Range(1, 22).Select(index => $"line {index}"));

        var preview = ToolTranscriptFormatter.BuildFailurePreview(text, maxLines: 20);

        Assert.Multiple(() => {
            Assert.That(preview, Does.Contain("line 20"));
            Assert.That(preview, Does.Not.Contain("line 21"));
            Assert.That(preview.EndsWith("...", StringComparison.Ordinal), Is.True);
        });
    }

    [Test]
    public void BuildDetailContent_IncludesToolPayloadTimingAndOutput() {
        var detail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("powershell", Description: "Check repo"),
            "{\n  \"command\": \"git status\"\n}",
            "fatal: not a git repository",
            new DateTimeOffset(2026, 4, 6, 14, 24, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 4, 6, 14, 24, 3, TimeSpan.FromHours(-4)),
            "Running git status",
            IsCompleted: true,
            Success: false);

        var content = ToolTranscriptFormatter.BuildDetailContent(detail);

        Assert.Multiple(() => {
            Assert.That(content, Does.Contain("[TOOL] powershell"));
            Assert.That(content, Does.Contain("\"command\": \"git status\""));
            Assert.That(content, Does.Contain("Started: 2026-04-06 14:24:00 -04:00"));
            Assert.That(content, Does.Contain("Finished: 2026-04-06 14:24:03 -04:00"));
            Assert.That(content, Does.Contain("Error Output"));
            Assert.That(content, Does.Contain("fatal: not a git repository"));
        });
    }

    [Test]
    public void BuildDetailContent_ShowsRunningStatus_WhenToolHasNotCompleted() {
        var detail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("powershell", Description: "Check repo"),
            "{\n  \"command\": \"git status\"\n}",
            null,
            new DateTimeOffset(2026, 4, 6, 14, 24, 0, TimeSpan.FromHours(-4)),
            null,
            "Running git status",
            IsCompleted: false,
            Success: false);

        var content = ToolTranscriptFormatter.BuildDetailContent(detail);

        Assert.Multiple(() => {
            Assert.That(content, Does.Contain("Finished: (still running)"));
            Assert.That(content, Does.Contain("Status: Running"));
            Assert.That(content, Does.Not.Contain("Status: Failed"));
        });
    }

    [Test]
    public void BuildAgentTurnStartMarker_UsesSingleLineTaskAndTimestamp() {
        var startedAt = new DateTimeOffset(2026, 4, 9, 14, 44, 57, TimeSpan.FromHours(-4));

        var marker = ToolTranscriptFormatter.BuildAgentTurnStartMarker(
            "Fix stale session after idle.\nInvestigate transcript persistence.",
            startedAt);

        Assert.That(
            marker,
            Is.EqualTo("Starting Fix stale session after idle. Investigate transcript persistence at 2026-04-09 14:44:57 -04:00"));
    }

    [Test]
    public void StripSystemNotifications_RemovesInternalControlBlocks() {
        var text = """
            Here is the result.

            <system_notification>
            Agent "explore-agent-cards" (explore) has completed successfully.
            Use read_agent with agent_id "explore-agent-cards" to retrieve the full results.
            </system_notification>

            Final summary.
            """;

        var stripped = ToolTranscriptFormatter.StripSystemNotifications(text);

        Assert.Multiple(() => {
            Assert.That(stripped, Does.Contain("Here is the result."));
            Assert.That(stripped, Does.Contain("Final summary."));
            Assert.That(stripped, Does.Not.Contain("system_notification"));
            Assert.That(stripped, Does.Not.Contain("read_agent"));
        });
    }

    [Test]
    public void BuildCompletedText_ForGlobReportsMatchedFileCount() {
        var descriptor = new ToolTranscriptDescriptor(
            "glob",
            DisplayText: "**/OpenAiModelInfo.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: true,
            outputText: @"C:\Repo\OpenAiModelInfo.cs");

        Assert.That(text, Is.EqualTo("🔎**/OpenAiModelInfo.cs -- 1 file found"));
    }

    [Test]
    public void BuildRunningText_ForViewUsesUntruncatedRelativePath() {
        var descriptor = new ToolTranscriptDescriptor(
            "view",
            DisplayText: @"..\Foundation\Engine\DevExpress.CodeRush.Foundation\OpenAI\ModelData\OpenAiModelData.cs");

        var text = ToolTranscriptFormatter.BuildRunningText(descriptor);

        Assert.That(text, Is.EqualTo(@"👀..\Foundation\Engine\DevExpress.CodeRush.Foundation\OpenAI\ModelData\OpenAiModelData.cs"));
    }

    [Test]
    public void TryBuildEditDiffSummary_CountsAddedAndRemovedLines() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"..\Foundation\Engine\OpenAiModelData.cs");
        var output = """
            diff --git a/file b/file
            index 0000000..1111111 100644
            --- a/file
            +++ b/file
            @@ -1,2 +1,4 @@
            -old line
            +new line
            +another line
             context
            """;

        var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);

        Assert.Multiple(() => {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.DisplayName, Is.EqualTo("OpenAiModelData.cs"));
            Assert.That(summary.AddedLineCount, Is.EqualTo(2));
            Assert.That(summary.RemovedLineCount, Is.EqualTo(1));
            Assert.That(summary.IsNewFile, Is.False);
            Assert.That(summary.IsDeletedFile, Is.False);
        });
    }

    [Test]
    public void TryBuildEditDiffSummary_DetectsNewFile() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"..\Foundation\Engine\MyNewFile.cs");
        var output = """
            diff --git a/dev/null b/file
            new file mode 100644
            --- /dev/null
            +++ b/file
            @@ -0,0 +1,2 @@
            +line one
            +line two
            """;

        var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);

        Assert.Multiple(() => {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.IsNewFile, Is.True);
            Assert.That(summary.AddedLineCount, Is.EqualTo(2));
            Assert.That(summary.RemovedLineCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryBuildEditDiffSummary_ForApplyPatch_ExtractsFileNameFromDiffOutput() {
        var descriptor = new ToolTranscriptDescriptor("apply_patch");
        var output = """
            diff --git a/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs b/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs
            index 0000000..1111111 100644
            --- a/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs
            +++ b/D:/Drive/Source/SquadDash-public/SquadDash/PlanViewerWindow.cs
            @@ -1,2 +1,3 @@
            -old line
            +new line
            +another line
            """;

        var summary = ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output);

        Assert.Multiple(() => {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.DisplayName, Is.EqualTo("PlanViewerWindow.cs"));
            Assert.That(summary.AddedLineCount, Is.EqualTo(2));
            Assert.That(summary.RemovedLineCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryExtractEditTargetPath_ReadsFreeformApplyPatchHeader() {
        var patch = """
            *** Begin Patch
            *** Update File: D:\Drive\Source\SquadDash-public\SquadDash\PlanViewerWindow.cs
            @@
            -old line
            +new line
            *** End Patch
            """;

        var path = ToolTranscriptFormatter.TryExtractEditTargetPath(patch);

        Assert.That(path, Is.EqualTo(@"D:\Drive\Source\SquadDash-public\SquadDash\PlanViewerWindow.cs"));
    }

    [TestCase("edit")]
    [TestCase("apply_patch")]
    [TestCase("APPLY_PATCH")]
    public void IsEditToolName_RecognizesFileEditTools(string toolName) {
        Assert.That(ToolTranscriptFormatter.IsEditToolName(toolName), Is.True);
    }

    [Test]
    public void BuildCompletedText_ForFailedEdit_AppendsErrorReasonFromOutputText() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"SquadDash\MainWindow.xaml.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: "Multiple matches found\nCode: failure");

        Assert.That(text, Is.EqualTo(@"✏️SquadDash\MainWindow.xaml.cs failed -- Multiple matches found"));
    }

    [Test]
    public void BuildCompletedText_ForFailedEdit_OmitsReasonWhenOutputTextIsEmpty() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"SquadDash\MainWindow.xaml.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: null);

        Assert.That(text, Is.EqualTo(@"✏️SquadDash\MainWindow.xaml.cs failed."));
    }

    [Test]
    public void BuildCompletedText_ForFailedEdit_OmitsReasonWhenOutputIsOnlyCodeLine() {
        var descriptor = new ToolTranscriptDescriptor(
            "edit",
            DisplayText: @"SquadDash\MainWindow.xaml.cs");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: "Code: failure");

        Assert.That(text, Is.EqualTo(@"✏️SquadDash\MainWindow.xaml.cs failed."));
    }

    [Test]
    public void BuildCompletedText_ForFailedGenericTool_AppendsErrorReasonFromOutputText() {
        var descriptor = new ToolTranscriptDescriptor(
            "read_agent",
            Description: "some-agent-id");

        var text = ToolTranscriptFormatter.BuildCompletedText(
            descriptor,
            success: false,
            outputText: "Agent not found\nCode: failure");

        Assert.That(text, Is.EqualTo("some-agent-id failed -- Agent not found."));
    }

    [Test]
    public void ResolveToolIconKey_ForRg_UsesGrepIcon() {
        var iconKey = ToolTranscriptFormatter.ResolveToolIconKey("rg");

        Assert.That(iconKey, Is.EqualTo("ToolIcon_grep"));
    }

    [Test]
    public void BuildRipGrepDisplayText_WithSingleScope_UsesPatternDashScope() {
        var text = ToolTranscriptFormatter.BuildRipGrepDisplayText(
            "class PlanTaskStatus|record PlanTaskStatus|…",
            [@"SquadDash"]);

        Assert.That(text, Is.EqualTo("class PlanTaskStatus|record PlanTaskStatus|… — SquadDash"));
    }

    [Test]
    public void BuildRipGrepDisplayText_WithMultipleScopes_UsesFirstScopeAndAdditionalCount() {
        var text = ToolTranscriptFormatter.BuildRipGrepDisplayText(
            "pattern",
            [@"SquadDash\Screens", @"SquadDash\Tests", @"SquadDash\Assets"]);

        Assert.That(text, Is.EqualTo("pattern — Screens +2"));
    }

    [Test]
    public void BuildRipGrepDisplayText_WithoutScopes_FallsBackToPatternOnly() {
        Assert.Multiple(() => {
            Assert.That(
                ToolTranscriptFormatter.BuildRipGrepDisplayText("pattern", null),
                Is.EqualTo("pattern"));
            Assert.That(
                ToolTranscriptFormatter.BuildRipGrepDisplayText("pattern", []),
                Is.EqualTo("pattern"));
        });
    }

    [Test]
    public void ClassifyToolActivityKind_ForRg_IsReading() {
        var activity = ToolTranscriptFormatter.ClassifyToolActivityKind("rg");

        Assert.That(activity, Is.EqualTo(SpinnerActivityKind.Reading));
    }

    [Test]
    public void ResolveToolIconKey_ForDelegateRosterAgent_UsesTaskIcon() {
        var iconKey = ToolTranscriptFormatter.ResolveToolIconKey("delegate_roster_agent");

        Assert.That(iconKey, Is.EqualTo("ToolIcon_task"));
    }

    [Test]
    public void BuildRunningText_ForDelegateRosterAgent_UsesAgentIdentityOnly() {
        var descriptor = new ToolTranscriptDescriptor(
            "delegate_roster_agent",
            DisplayText: "@arjun-sen");

        var text = ToolTranscriptFormatter.BuildRunningText(descriptor);

        Assert.Multiple(() => {
            Assert.That(text, Is.EqualTo("🤖 @arjun-sen"));
            Assert.That(text, Does.Not.Contain("SQUADDASH_AGENT_ASSIGNMENT_JSON"));
            Assert.That(text, Does.Not.Contain("Delegate to"));
            Assert.That(text, Does.Not.Contain("Running"));
        });
    }

    [Test]
    public void BuildDetailContent_ForRgAndDelegateRosterAgent_PreservesToolNameAndArguments() {
        var rgDetail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("rg", DisplayText: "pattern — SquadDash"),
            "{\n  \"pattern\": \"pattern\",\n  \"paths\": [\"SquadDash\", \"SquadDash.Tests\"]\n}",
            null,
            new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 8, 19, 15, 0, 1, TimeSpan.FromHours(-4)),
            null,
            IsCompleted: true,
            Success: true);
        var delegateDetail = new ToolTranscriptDetail(
            new ToolTranscriptDescriptor("delegate_roster_agent", DisplayText: "@arjun-sen"),
            "{\n  \"agent_handle\": \"arjun-sen\",\n  \"prompt\": \"SQUADDASH_AGENT_ASSIGNMENT_JSON:\\n{...}\\nassignment\"\n}",
            null,
            new DateTimeOffset(2026, 8, 19, 15, 1, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 8, 19, 15, 1, 1, TimeSpan.FromHours(-4)),
            null,
            IsCompleted: true,
            Success: true);

        var rgContent = ToolTranscriptFormatter.BuildDetailContent(rgDetail);
        var delegateContent = ToolTranscriptFormatter.BuildDetailContent(delegateDetail);

        Assert.Multiple(() => {
            Assert.That(rgContent, Does.Contain("[TOOL] rg"));
            Assert.That(rgContent, Does.Contain("\"pattern\": \"pattern\""));
            Assert.That(rgContent, Does.Contain("\"paths\": [\"SquadDash\", \"SquadDash.Tests\"]"));
            Assert.That(delegateContent, Does.Contain("[TOOL] delegate_roster_agent"));
            Assert.That(delegateContent, Does.Contain("\"agent_handle\": \"arjun-sen\""));
            Assert.That(delegateContent, Does.Contain("\"prompt\": \"SQUADDASH_AGENT_ASSIGNMENT_JSON"));
        });
    }
}

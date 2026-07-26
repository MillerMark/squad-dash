using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DecomposeStepResultParserTests
{
    [Test]
    public void CompleteResult_WithCommitAndPassedVerification_Parses()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId":"PLAN-20260725",
              "taskId":"PLAN-20260725-001",
              "revision":"abc123",
              "status":"complete",
              "commit":"abcdef1",
              "summary":"Implemented the step.",
              "remainingWork":[],
              "verification":{"status":"passed","command":"dotnet test","summary":"All tests passed."}
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True, error);
        Assert.That(result!.TaskId, Is.EqualTo("PLAN-20260725-001"));
    }

    [Test]
    public void CompleteResult_WithoutPassedVerification_IsRejected()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {"groupId":"PLAN-20260725","taskId":"PLAN-20260725-001","revision":"abc123",
             "status":"complete","commit":"abcdef1","summary":"Done","remainingWork":[],
             "verification":{"status":"not-run","command":null,"summary":null}}
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("passed verification"));
    }

    [Test]
    public void PartialResult_WithoutRemainingWork_IsRejected()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {"groupId":"PLAN-20260725","taskId":"PLAN-20260725-001","revision":"abc123",
             "status":"partial","commit":null,"summary":"Started","remainingWork":[],"verification":null}
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("remaining work"));
    }
}

[TestFixture]
internal sealed class DecomposeRecoveryDecisionParserTests
{
    [TestCase("retry-as-written")]
    [TestCase("replan-failed-task")]
    public void SupportedAction_Parses(string action)
    {
        var text = $"DECOMPOSE_RECOVERY_JSON:\n{{\"groupId\":\"PLAN-20260725\",\"revision\":\"abc\",\"action\":\"{action}\"}}";
        Assert.That(DecomposeRecoveryDecisionParser.TryParse(text, out var decision), Is.True);
        Assert.That(decision!.Action, Is.EqualTo(action));
    }

    [Test]
    public void UnsupportedAction_IsRejected() =>
        Assert.That(DecomposeRecoveryDecisionParser.TryParse(
            "DECOMPOSE_RECOVERY_JSON:\n{\"groupId\":\"PLAN-20260725\",\"revision\":\"abc\",\"action\":\"skip\"}",
            out _), Is.False);
}

[TestFixture]
internal sealed class DecomposeWorktreePolicyTests
{
    [Test]
    public void TasksFileOnly_IsAllowed()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            " M .squad/tasks.md\n", [".squad/tasks.md"], out var disallowed), Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    [Test]
    public void SourceAndTasksChanges_AreRejected()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            " M .squad/tasks.md\n M SquadDash/MainWindow.xaml.cs\n",
            [".squad/tasks.md"],
            out var disallowed), Is.False);
        Assert.That(disallowed, Is.EqualTo(new[] { "SquadDash/MainWindow.xaml.cs" }));
    }

    [Test]
    public void ConfirmedPaths_MatchRegardlessOfOrderSlashOrCase()
    {
        Assert.That(DecomposeWorktreePolicy.MatchesConfirmedPaths(
            ["SquadDash/ScreenshotService.cs", "SquadDash\\MainWindow.xaml.cs"],
            ["squaddash/mainwindow.xaml.cs", "SquadDash/ScreenshotService.cs"]), Is.True);
    }

    [Test]
    public void ConfirmedPaths_RejectAddedOrRemovedFiles()
    {
        Assert.That(DecomposeWorktreePolicy.MatchesConfirmedPaths(
            ["SquadDash/MainWindow.xaml.cs", "SquadDash/ScreenshotService.cs"],
            ["SquadDash/MainWindow.xaml.cs"]), Is.False);
    }
}

[TestFixture]
internal sealed class DecomposeRevisionTests
{
    private static DecomposedTaskGroup MakeRevision()
    {
        const string group = "PLAN-20260725";
        return new DecomposedTaskGroup(group, "Plan", "refactor/plan", "Summary",
        [
            new($"{group}-001", "Large parent", [], "high", "Large parent"),
            new($"{group}-002", "Downstream", [$"{group}-001"], "high", "Downstream"),
            new($"{group}-003", "First replacement", [], "high", "First replacement", $"{group}-001"),
            new($"{group}-004", "Terminal replacement", [$"{group}-003"], "high", "Terminal replacement", $"{group}-001"),
        ]);
    }

    [Test]
    public void Normalize_RewiresDownstreamToTerminalReplacement()
    {
        Assert.That(DecomposePlanRevision.TryNormalize(MakeRevision(), out var normalized, out var error), Is.True, error);
        Assert.That(normalized.Tasks.Single(task => task.Id.EndsWith("-002", StringComparison.Ordinal)).DependsOn,
            Is.EqualTo(new[] { "PLAN-20260725-004" }));
    }

    [Test]
    public void ValidateRevision_OnlyAllowsBlockedParentToBeSuperseded()
    {
        var proposal = MakeRevision();
        var existing = proposal with { Tasks = proposal.Tasks.Take(2).ToArray() };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out var error), Is.True, error);

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out error), Is.False);
        Assert.That(error, Does.Contain("not currently failed or partial"));
    }

    [Test]
    public void ReplaceGroup_MarksParentSupersededAndPersistsRevision()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"tasks_{Guid.NewGuid():N}.md");
        try
        {
            var original = MakeRevision() with { Tasks = MakeRevision().Tasks.Take(2).ToArray() };
            var writer = new DecomposedTasksWriter();
            writer.WriteGroup(path, original, "old");
            Assert.That(DecomposePlanRevision.TryNormalize(MakeRevision(), out var revised, out _), Is.True);

            Assert.That(writer.ReplaceGroup(path, revised, "new-revision"), Is.True);

            var text = File.ReadAllText(path);
            var parsed = TasksPanelParser.Parse(File.ReadAllLines(path));
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("- [>] **[PLAN-20260725-001]**"));
                Assert.That(parsed.DecomposeGroups["PLAN-20260725"].HostRevision, Is.EqualTo("new-revision"));
                Assert.That(parsed.OpenGroups.SelectMany(group => group.Items).Any(item => item.TaskId == "PLAN-20260725-001"), Is.False);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void PartialStatus_DoesNotUnlockDependentTask()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"tasks_{Guid.NewGuid():N}.md");
        try
        {
            const string groupId = "PLAN-20260725";
            var group = new DecomposedTaskGroup(groupId, "Plan", "refactor/plan", "Summary",
            [
                new($"{groupId}-001", "First", [], "high", "First"),
                new($"{groupId}-002", "Second", [$"{groupId}-001"], "high", "Second"),
            ]);
            var writer = new DecomposedTasksWriter();
            writer.WriteGroup(path, group, "revision");
            writer.MarkTaskPartial(path, $"{groupId}-001", "abcdef1", "Partly done", ["Finish migration"]);
            var runner = new CodeHealthGroupRunner(writer, path);

            Assert.That(runner.TrackFirstEligibleStep(groupId), Is.EqualTo(DecomposeGroupExecutionState.Blocked));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Parser_ReadsAdjacentLegacyRevisionMetadata()
    {
        var parsed = TasksPanelParser.Parse(
        [
            "<!-- decompose-group: PLAN-20260725 | branch: refactor/plan -->",
            "<!-- decompose-revision: abc123 -->",
            "**[PLAN-20260725] Plan**",
            "> Summary",
            "- [ ] **[PLAN-20260725-001]** First",
            "  Group: PLAN-20260725 | Branch: refactor/plan | Priority: high",
            "  description: First",
            "  dependsOn: (none)",
        ]);

        Assert.That(parsed.DecomposeGroups["PLAN-20260725"].HostRevision, Is.EqualTo("abc123"));
    }
}

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
    [TestCase("assess-and-continue")]
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
    public void RepositoryRelativePath_TrimsGitCommandLineEnding()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "SquadDash", "repo");
        var tasksPath = Path.Combine(repositoryRoot, ".squad", "tasks.md");

        var relative = DecomposeWorktreePolicy.GetRepositoryRelativePath(
            repositoryRoot + "\r\n",
            tasksPath);

        Assert.That(relative, Is.EqualTo(".squad/tasks.md"));
    }

    [Test]
    public void RepositoryRelativePath_RejectsPathOutsideRepository()
    {
        var parent = Path.Combine(Path.GetTempPath(), "SquadDash");
        var repositoryRoot = Path.Combine(parent, "repo");
        var outsidePath = Path.Combine(parent, "other", "tasks.md");

        Assert.That(
            DecomposeWorktreePolicy.GetRepositoryRelativePath(repositoryRoot, outsidePath),
            Is.Null);
    }

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

    // ─── HasOnlyAllowedChanges: content-aware cases ───────────────────────────

    [Test]
    public void HasOnlyAllowedChanges_EmptyStatus_ReturnsTrue()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            string.Empty, [], out var disallowed), Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    [Test]
    public void HasOnlyAllowedChanges_NullStatus_ReturnsTrue()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            null, [], out var disallowed), Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    [Test]
    public void HasOnlyAllowedChanges_UntrackedFile_ReturnsFalse()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            "?? newfile.txt\n", [], out var disallowed), Is.False);
        Assert.That(disallowed, Has.Count.EqualTo(1));
    }

    [Test]
    public void HasOnlyAllowedChanges_StagedChange_ReturnsFalse()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            "M  src/Program.cs\n", [], out var disallowed), Is.False);
        Assert.That(disallowed, Has.Count.EqualTo(1));
    }

    [Test]
    public void HasOnlyAllowedChanges_RenamedFile_ChecksDestinationPath()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            "R  old/path.cs -> new/path.cs\n", ["new/path.cs"], out var disallowed), Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    [Test]
    public void HasOnlyAllowedChanges_AllowedPathIsCaseInsensitive()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            " M .SQUAD/Tasks.MD\n", [".squad/tasks.md"], out var disallowed), Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    [Test]
    public void HasOnlyAllowedChanges_MultipleDisallowedPaths_ReturnsAllDisallowed()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            " M src/A.cs\n M src/B.cs\n M src/C.cs\n", [], out var disallowed), Is.False);
        Assert.That(disallowed, Has.Count.EqualTo(3));
    }

    [Test]
    public void HasOnlyAllowedChanges_PlanJsonAllowed_ReturnsTrue()
    {
        Assert.That(DecomposeWorktreePolicy.HasOnlyAllowedChanges(
            " M .squad/plans/plan-123.json\n",
            [".squad/plans/plan-123.json"],
            out var disallowed), Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    // ─── FilterMetadataOnlyAsync ──────────────────────────────────────────────

    [Test]
    public async Task FilterMetadataOnlyAsync_EmptyList_ReturnsEmpty()
    {
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            [], _ => Task.FromResult(string.Empty));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task FilterMetadataOnlyAsync_EmptyDiff_FiltersPath()
    {
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            ["src/Program.cs"],
            _ => Task.FromResult(string.Empty));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task FilterMetadataOnlyAsync_NonEmptyDiff_RetainsPath()
    {
        const string fakeDiff = ":100644 100644 abc123 def456 M\tsrc/Program.cs";
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            ["src/Program.cs"],
            _ => Task.FromResult(fakeDiff));
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("src/Program.cs"));
    }

    [Test]
    public async Task FilterMetadataOnlyAsync_MixedResults_FiltersOnlyMetadataOnly()
    {
        const string fakeDiff = ":100644 100644 abc123 def456 M\tsrc/Real.cs";
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            ["src/StatCacheOnly.cs", "src/Real.cs"],
            cmd => cmd.Contains("StatCacheOnly")
                ? Task.FromResult(string.Empty)
                : Task.FromResult(fakeDiff));
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("src/Real.cs"));
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
        Assert.That(error, Does.Contain("not currently eligible for replanning"));
    }

    [Test]
    public void ValidateRevision_RejectsRewritingExistingPendingTaskWithoutReplacement()
    {
        var existing = MakeRevision() with { Tasks = MakeRevision().Tasks.Take(2).ToArray() };
        var rewritten = existing with
        {
            Tasks =
            [
                existing.Tasks[0],
                existing.Tasks[1] with { Title = "Silently changed task" },
            ],
        };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            rewritten,
            existing,
            new HashSet<string>(["PLAN-20260725-002"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out var error), Is.False);
        Assert.That(error, Does.Contain("keep existing tasks unchanged"));
    }

    [Test]
    public void ValidateRevision_RejectsChangingExistingTaskProofContract()
    {
        var existing = MakeRevision() with { Tasks = MakeRevision().Tasks.Take(2).ToArray() };
        var rewritten = existing with
        {
            Tasks =
            [
                existing.Tasks[0] with
                {
                    ProofRequirements =
                    [
                        new DecomposedTaskProofRequirement(
                            "invented-proof",
                            "automated-test",
                            "A proof contract added after approval."),
                    ],
                },
                existing.Tasks[1],
            ],
        };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            rewritten,
            existing,
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out var error), Is.False);
        Assert.That(error, Does.Contain("keep existing tasks unchanged"));
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

using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DecomposeStepResultParserTests
{
    [Test]
    public void StructuredTestCounts_AreAcceptedWhenArithmeticIsExact()
    {
        var text = DecomposeStepResultParser.Marker + "\n" + """
            {
              "groupId":"PLAN-1","taskId":"TASK-1","revision":"rev-1",
              "status":"complete","completionKind":"changed","commit":"abc1234",
              "summary":"Done.","remainingWork":[],
              "verification":{"status":"passed","command":"dotnet test","summary":"All passed.",
                "tests":{"total":780,"passed":780,"failed":0,"skipped":0}}
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True, error);
        Assert.That(result?.Verification?.Tests?.Passed, Is.EqualTo(780));
    }

    [Test]
    public void StructuredTestCounts_AreRejectedWhenArithmeticIsInconsistent()
    {
        var text = DecomposeStepResultParser.Marker + "\n" + """
            {
              "groupId":"PLAN-1","taskId":"TASK-1","revision":"rev-1",
              "status":"complete","completionKind":"changed","commit":"abc1234",
              "summary":"Done.","remainingWork":[],
              "verification":{"status":"passed","command":"dotnet test","summary":"All passed.",
                "tests":{"total":780,"passed":779,"failed":0,"skipped":0}}
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("total must equal passed plus failed plus skipped"));
    }

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
        Assert.That(result.CompletedWork, Is.EqualTo(new[] { "Implemented the step." }),
            "Legacy paragraph-only envelopes must become one item without sentence parsing.");
    }

    [Test]
    public void CompleteResult_FollowedByRequiredInboxDelivery_ParsesBothProtocols()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId":"PLAN-20260725",
              "taskId":"PLAN-20260725-001",
              "revision":"abc123",
              "status":"complete",
              "commit":"abcdef1",
              "summary":"Prepared and delivered the manual test kit.",
              "remainingWork":[],
              "verification":{"status":"passed","command":"test -f runbook.md","summary":"Runbook exists."}
            }

            INBOX_MESSAGE_JSON:
            {
              "subject":"Manual test kit",
              "from":"coordinator",
              "body":"Open [the fixture](app://open-path:samples/fixture).",
              "attachments":[]
            }
            """;

        Assert.Multiple(() =>
        {
            Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var resultError),
                Is.True, resultError);
            Assert.That(result!.TaskId, Is.EqualTo("PLAN-20260725-001"));
            Assert.That(InboxMessageParser.TryExtract(text, out _, out var inbox), Is.True);
            Assert.That(inbox!.Subject, Is.EqualTo("Manual test kit"));
        });
    }

    [Test]
    public void CompleteResult_WithStructuredCompletedWork_PreservesItemBoundariesAndSentences()
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
              "completedWork":[" Added the production path. It handles retries. ","Added focused tests."],
              "remainingWork":[],
              "verification":{"status":"passed","command":"dotnet test","summary":"All tests passed."}
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True, error);
        Assert.That(result!.CompletedWork, Is.EqualTo(new[]
        {
            "Added the production path. It handles retries.",
            "Added focused tests.",
        }));
    }

    [Test]
    public void CompleteResult_TrimsCommitBeforeHostValidation()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId":"PLAN-20260725",
              "taskId":"PLAN-20260725-001",
              "revision":"abc123",
              "status":"complete",
              "commit":"  d77372ade488034a121e57051ab65abb7ab1bf91  ",
              "summary":"Implemented the step.",
              "remainingWork":[],
              "verification":{"status":"passed","command":"dotnet test","summary":"All tests passed."}
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True, error);
        Assert.That(result!.Commit, Is.EqualTo("d77372ade488034a121e57051ab65abb7ab1bf91"));
    }

    [Test]
    public void VerifiedNoOp_WithSnapshotAndRequirementEvidence_Parses()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId":"PLAN-20260725",
              "taskId":"PLAN-20260725-001",
              "revision":"abc123",
              "status":"complete",
              "completionKind":"verified-no-op",
              "commit":null,
              "evaluatedCommit":"abcdef1",
              "summary":"The requested behavior already exists.",
              "remainingWork":[],
              "noOpEvidence":[{
                "requirement":"Pass max reasoning through model profiles",
                "observation":"The profile stores an unconstrained string and the request pipeline forwards it unchanged.",
                "locations":["SquadDash/ModelProfile.cs:ReasoningEffort","SquadDash/SquadSdkProcess.cs:NormalizeReasoningEffort"]
              }],
              "verification":{"status":"passed","command":"dotnet test","summary":"Existing behavior and tests passed."}
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(result!.IsVerifiedNoOp, Is.True);
            Assert.That(result.Commit, Is.Null);
            Assert.That(result.VerificationTargetCommit, Is.EqualTo("abcdef1"));
            Assert.That(result.NoOpEvidence, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void VerifiedNoOp_WithoutRequirementEvidence_IsRejected()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {"groupId":"PLAN-20260725","taskId":"PLAN-20260725-001","revision":"abc123",
             "status":"complete","completionKind":"verified-no-op","commit":null,
             "evaluatedCommit":"abcdef1","summary":"Already done","remainingWork":[],
             "noOpEvidence":[],
             "verification":{"status":"passed","command":"dotnet test","summary":"Passed"}}
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("requirement-scoped noOpEvidence"));
    }

    [Test]
    public void VerifiedNoOp_WithRedundantTaskCommit_IsNormalizedBeforeHostValidation()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {"groupId":"PLAN-20260725","taskId":"PLAN-20260725-001","revision":"abc123",
             "status":"complete","completionKind":"verified-no-op","commit":"abcdef1",
             "evaluatedCommit":"abcdef1","summary":"Already done","remainingWork":[],
             "noOpEvidence":[{"requirement":"R","observation":"O","locations":["src/A.cs"]}],
             "verification":{"status":"passed","command":"dotnet test","summary":"Passed"}}
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(result!.IsVerifiedNoOp, Is.True);
            Assert.That(result.Commit, Is.Null);
            Assert.That(result.EvaluatedCommit, Is.EqualTo("abcdef1"));
        });
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

    [Test]
    public void IdentityValidation_ReportsEveryMismatchIncludingAuthoritativeRevision()
    {
        var result = new DecomposeStepResult(
            "OLD-PLAN", "OLD-TASK", "old-revision", "complete", "abcdef1", "Done", [],
            new DecomposeStepVerification("passed", "dotnet test", "Passed"));

        var diagnostics = DecomposeStepResultContractDiagnostics.ValidateIdentity(
            result, "CURRENT-PLAN", "CURRENT-TASK", "current-revision");
        var combined = DecomposeStepResultContractDiagnostics.Combine(diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Has.Count.EqualTo(3));
            Assert.That(combined, Does.Contain("OLD-PLAN").And.Contain("CURRENT-PLAN"));
            Assert.That(combined, Does.Contain("OLD-TASK").And.Contain("CURRENT-TASK"));
            Assert.That(combined, Does.Contain("old-revision").And.Contain("current-revision"));
            Assert.That(combined, Does.Contain("3 independent validation problems"));
        });
    }
}

[TestFixture]
internal sealed class DecomposeRecoveryDecisionParserTests
{
    [TestCase("retry-as-written")]
    [TestCase("replan-failed-task")]
    [TestCase("assess-and-continue")]
    [TestCase("accept-step-complete")]
    [TestCase("resume-verification")]
    [TestCase("continue-working")]
    [TestCase("request-corrections")]
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
    public void HasOnlyAllowedChanges_HostOwnedSquadRuntimeFiles_DoNotBlockRecovery()
    {
        const string status = """
             M .squad/diagnostics/recent-prompts.json
             M .squad/logs/plan-execution.ndjson
            ?? .squad/plans/PLAN-1.json
            ?? .squad/tmp/agent-artifacts/result.json
            ?? .squad/archive/agent-artifacts/2026-09-28/result.json
             M .squad/instructions/decompose-planning.md
             M .squad/loop-executing-plan.md
            """;

        Assert.That(
            DecomposeWorktreePolicy.HasOnlyAllowedChanges(status, [], out var disallowed),
            Is.True);
        Assert.That(disallowed, Is.Empty);
    }

    [TestCase(" M .squad/team.md")]
    [TestCase(" M .squad/routing.md")]
    [TestCase(" M .squad/decisions.md")]
    [TestCase("?? .squad/notes/user-note.md")]
    public void HasOnlyAllowedChanges_UserOwnedSquadFilesStillBlockRecovery(string status)
    {
        Assert.That(
            DecomposeWorktreePolicy.HasOnlyAllowedChanges(status, [], out var disallowed),
            Is.False);
        Assert.That(disallowed, Has.Count.EqualTo(1));
    }

    [Test]
    public void Normalize_AllowsOneForOneReplacementAndRewiresDownstream()
    {
        var proposal = MakeRevision() with
        {
            Tasks = MakeRevision().Tasks.Where(task => !task.Id.EndsWith("-004", StringComparison.Ordinal)).ToArray(),
        };

        Assert.That(DecomposePlanRevision.TryNormalize(proposal, out var normalized, out var error), Is.True, error);
        Assert.That(normalized.Tasks.Single(task => task.Id.EndsWith("-002", StringComparison.Ordinal)).DependsOn,
            Is.EqualTo(new[] { "PLAN-20260725-003" }));
    }

    [Test]
    public void ValidateRevision_AllowsAnyExplicitlyReplaceableParentToBeSuperseded()
    {
        var proposal = MakeRevision();
        var existing = proposal with { Tasks = proposal.Tasks.Take(2).ToArray() };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out var error), Is.True, error);

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out error), Is.False);
        Assert.That(error, Does.Contain("cannot be superseded"));
    }

    [Test]
    public void ValidateRevision_AllowsRewritingUntouchedPendingTaskInPlace()
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
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(["PLAN-20260725-002"], StringComparer.Ordinal),
            out var error), Is.True, error);
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
            new HashSet<string>(StringComparer.Ordinal),
            out var error), Is.False);
        Assert.That(error, Does.Contain("after work or review had begun"));
    }

    [Test]
    public void ValidateRevision_AllowsEditingAndReplacingSamePendingTask()
    {
        var existing = MakeRevision() with { Tasks = MakeRevision().Tasks.Take(2).ToArray() };
        var proposal = MakeRevision() with
        {
            Tasks =
            [
                existing.Tasks[0] with { Title = "Changed parent" },
                existing.Tasks[1],
                .. MakeRevision().Tasks.Skip(2),
            ],
        };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            out var error), Is.True, error);
    }

    [Test]
    public void ValidateRevision_AllowsEditingUntouchedTaskWhileReplacingDifferentBlockedTask()
    {
        var existing = MakeRevision() with { Tasks = MakeRevision().Tasks.Take(2).ToArray() };
        var proposal = MakeRevision() with
        {
            Tasks =
            [
                existing.Tasks[0],
                existing.Tasks[1] with { Title = "Clarified untouched task" },
                .. MakeRevision().Tasks.Skip(2),
            ],
        };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(["PLAN-20260725-002"], StringComparer.Ordinal),
            out var error), Is.True, error);
    }

    [Test]
    public void ValidateRevision_RejectsSupersedingCompletedTaskButAllowsFollowUp()
    {
        var proposal = MakeRevision();
        var existing = proposal with { Tasks = proposal.Tasks.Take(2).ToArray() };

        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            proposal,
            existing,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out var error), Is.False);
        Assert.That(error, Does.Contain("Completed task PLAN-20260725-001 cannot be superseded"));

        var followUp = existing with
        {
            Tasks =
            [
                .. existing.Tasks,
                new DecomposedSubTask(
                    "PLAN-20260725-005",
                    "Correct completed behavior",
                    ["PLAN-20260725-001"],
                    "high",
                    "Corrective follow-up"),
            ],
        };
        Assert.That(DecomposePlanRevision.TryValidateAgainstPersisted(
            followUp,
            existing,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(["PLAN-20260725-001"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out error), Is.True, error);
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
    public void ReplaceGroup_ApprovedRevisionResetsOnlyExplicitlyReopenedTask()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"tasks_{Guid.NewGuid():N}.md");
        try
        {
            const string firstId = "PLAN-20260725-001";
            const string secondId = "PLAN-20260725-002";
            var original = MakeRevision() with { Tasks = MakeRevision().Tasks.Take(2).ToArray() };
            var writer = new DecomposedTasksWriter();
            writer.WriteGroup(path, original, "old-revision");
            writer.MarkTaskComplete(path, firstId, "abc1111", "Preserved completion");
            writer.MarkTaskComplete(path, secondId, "abc2222", "Completion to reopen");

            var revised = original with
            {
                Tasks = original.Tasks.Select(task => task.Id == secondId
                    ? task with { Description = "Revised second step" }
                    : task).ToArray(),
            };
            Assert.That(writer.ReplaceGroup(
                path, revised, "new-revision", new HashSet<string>([secondId], StringComparer.Ordinal)), Is.True);

            var text = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain($"- [x] **[{firstId}]**"));
                Assert.That(text, Does.Contain($"- [ ] **[{secondId}]**"));
                Assert.That(text, Does.Contain("Revised second step"));
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

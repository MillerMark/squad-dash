using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class RecoveryActionValidationTests
{
    [TestCase("6b206c0", "6b206c0a1f25d0a5b04f6f856c262591827466aa")]
    [TestCase("6B206C0A1F25D0A5B04F6F856C262591827466AA", "6b206c0")]
    [TestCase("6b206c0a1f25d0a5b04f6f856c262591827466aa", "6b206c0a1f25d0a5b04f6f856c262591827466aa")]
    public void CommitIdentifiersMatch_AbbreviatedAndFullSha_ReturnsTrue(string left, string right)
    {
        Assert.That(RecoveryCommitValidator.CommitIdentifiersMatch(left, right), Is.True);
    }

    [TestCase("6b206c", "6b206c0a1f25d0a5b04f6f856c262591827466aa")]
    [TestCase("not-a-sha", "not-a-sha")]
    [TestCase("not-a-sha", "not-a-sha-extension")]
    [TestCase("6b206c0", "7b206c0a1f25d0a5b04f6f856c262591827466aa")]
    public void CommitIdentifiersMatch_UnsafeOrDifferentIdentifier_ReturnsFalse(string left, string right)
    {
        Assert.That(RecoveryCommitValidator.CommitIdentifiersMatch(left, right), Is.False);
    }

    [Test]
    public void FindRecordedCandidate_ShortRecordedSha_ResolvesFullRangeEntry()
    {
        var expected = new RecoveryCommitRangeEntry(
            "6b206c0a1f25d0a5b04f6f856c262591827466aa",
            "Add focused dependency edit coverage corrections");

        var resolved = RecoveryCommitValidator.FindRecordedCandidate(
            [
                new RecoveryCommitRangeEntry(
                    "577abf583b624db984314010d7e26ec6b45f4c6d",
                    "Add dependency and approval-boundary editing tests"),
                expected,
            ],
            "6b206c0");

        Assert.That(resolved, Is.SameAs(expected));
    }

    [Test]
    public void FindRecordedCandidate_AmbiguousPrefix_ReturnsNull()
    {
        var resolved = RecoveryCommitValidator.FindRecordedCandidate(
            [
                new RecoveryCommitRangeEntry("abcdefa000000000", "First"),
                new RecoveryCommitRangeEntry("abcdefa111111111", "Second"),
            ],
            "abcdefa");

        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void CandidateCommitsMatch_PreservesCheckedReviewAcrossShaExpansion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlanHumanReviewChecklist.CandidateCommitsMatch(
                "6b206c0",
                "6b206c0a1f25d0a5b04f6f856c262591827466aa"), Is.True);
            Assert.That(PlanHumanReviewChecklist.CandidateCommitsMatch(
                "577abf5+6b206c0",
                "577abf583b624db984314010d7e26ec6b45f4c6d+6b206c0a1f25d0a5b04f6f856c262591827466aa"),
                Is.True);
            Assert.That(PlanHumanReviewChecklist.CandidateCommitsMatch(
                "577abf5+6b206c0",
                "577abf583b624db984314010d7e26ec6b45f4c6d+7b206c0a1f25d0a5b04f6f856c262591827466aa"),
                Is.False);
        });
    }

    [Test]
    public void ParseCommitRange_ParsesOldestToNewestRows()
    {
        var entries = RecoveryCommitValidator.ParseCommitRange(
            "4bf1c1c000000000\tInitial matrix\n" +
            "b6d69d9000000000\tComplete matrix\n");

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => entry.Commit),
                Is.EqualTo(new[] { "4bf1c1c000000000", "b6d69d9000000000" }));
            Assert.That(entries[1].Subject, Is.EqualTo("Complete matrix"));
        });
    }

    [TestCase("not-a-row")]
    [TestCase("nothex00\tSubject")]
    public void ParseCommitRange_MalformedRow_Throws(string log)
    {
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryCommitValidator.ParseCommitRange(log));
    }

    [Test]
    public void FindNewestRecordedCommit_ReturnsNewestReachableCompletedTaskCommit()
    {
        var resolved = RecoveryCommitValidator.FindNewestRecordedCommit(
            ["ccccccc000000000", "bbbbbbb000000000", "aaaaaaa000000000"],
            ["aaaaaaa", "bbbbbbb"]);

        Assert.That(resolved, Is.EqualTo("bbbbbbb000000000"));
    }

    [Test]
    public void FindNewestRecordedCommit_IgnoresUnrecordedHistory()
    {
        var resolved = RecoveryCommitValidator.FindNewestRecordedCommit(
            ["ccccccc000000000", "bbbbbbb000000000"],
            ["aaaaaaa"]);

        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void FindNewestRecordedCommit_DoesNotResolveAmbiguousAbbreviation()
    {
        var resolved = RecoveryCommitValidator.FindNewestRecordedCommit(
            ["abcdefa000000000", "abcdefb000000000"],
            ["abcdef"]);

        Assert.That(resolved, Is.Null);
    }

    // ── ExtractSingleCandidateCommit ─────────────────────────────────────────

    [Test]
    public void ExtractSingleCandidateCommit_OneCommit_ReturnsSha()
    {
        const string log = "a1b2c3d Add feature X";
        var sha = RecoveryCommitValidator.ExtractSingleCandidateCommit(log);
        Assert.That(sha, Is.EqualTo("a1b2c3d"));
    }

    [Test]
    public void ExtractSingleCandidateCommit_ZeroCommits_ReturnsNull()
    {
        var sha = RecoveryCommitValidator.ExtractSingleCandidateCommit(string.Empty);
        Assert.That(sha, Is.Null);
    }

    [Test]
    public void ExtractSingleCandidateCommit_MultipleCommits_ThrowsInvalidOperation()
    {
        const string log = "a1b2c3d First commit\nb4c5d6e Second commit";
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryCommitValidator.ExtractSingleCandidateCommit(log));
    }

    [Test]
    public void ExtractSingleCandidateCommit_WhitespaceOnly_ReturnsNull()
    {
        var sha = RecoveryCommitValidator.ExtractSingleCandidateCommit("   \n\t\n  ");
        Assert.That(sha, Is.Null);
    }

    // ── FindDownstreamCompletedDependents ────────────────────────────────────

    [Test]
    public void FindDownstreamCompletedDependents_NoDependents_ReturnsEmpty()
    {
        var tasks = new List<PlanTask>
        {
            MakeTask("T-02", dependsOn: ["T-99"], status: PlanTaskStatus.Complete),
        };
        var result = RecoveryCommitValidator.FindDownstreamCompletedDependents(tasks, "T-01");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindDownstreamCompletedDependents_CompletedDependent_ReturnsIt()
    {
        var tasks = new List<PlanTask>
        {
            MakeTask("T-02", dependsOn: ["T-01"], status: PlanTaskStatus.Complete),
        };
        var result = RecoveryCommitValidator.FindDownstreamCompletedDependents(tasks, "T-01");
        Assert.That(result, Is.EqualTo(new[] { "T-02" }));
    }

    [Test]
    public void FindDownstreamCompletedDependents_PendingDependent_ReturnsEmpty()
    {
        var tasks = new List<PlanTask>
        {
            MakeTask("T-02", dependsOn: ["T-01"], status: PlanTaskStatus.Pending),
        };
        var result = RecoveryCommitValidator.FindDownstreamCompletedDependents(tasks, "T-01");
        Assert.That(result, Is.Empty);
    }

    // ── ContainsOnlyNonHostChanges ───────────────────────────────────────────

    [Test]
    public void ContainsOnlyNonHostChanges_AllHostOwned_ReturnsFalse()
    {
        var changedPaths  = new[] { ".squad/tasks.md", ".squad/plans/plan.json" };
        var hostOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".squad/tasks.md", ".squad/plans/plan.json" };
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(changedPaths, hostOwnedPaths);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_MixedSourceAndHostOwned_ReturnsFalse()
    {
        var changedPaths  = new[] { ".squad/tasks.md", "src/Feature.cs" };
        var hostOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".squad/tasks.md" };
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(changedPaths, hostOwnedPaths);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_SourceOnly_ReturnsTrue()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            ["src/Feature.cs", "tests/FeatureTests.cs"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_DirectoryPrefixMatchesDescendant_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            [".squad/plans/plan.json"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_SimilarPrefixOutsideDirectory_ReturnsTrue()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            [".squad-tools/Feature.cs"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_EmptyChanges_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/tasks.md" });
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsNonHostChanges_StateOnly_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ContainsNonHostChanges(
            [".squad/decisions/inbox/review.md", "Package/.squad/plans/plan.json"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".squad/", "Package/.squad/" });
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsNonHostChanges_MixedTaskAndState_ReturnsTrue()
    {
        var result = RecoveryCommitValidator.ContainsNonHostChanges(
            [
                ".squad/decisions/inbox/review.md",
                "Package/.squad/plans/plan.json",
                "Plugins/Web/Feature.cs",
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".squad/", "Package/.squad/" });
        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsNonHostChanges_EmptyChanges_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ContainsNonHostChanges(
            [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRecoverBroadenedInterruptedRange_StateOnlyExactRangeAndSourceInBroaderRange_ReturnsTrue()
    {
        var result = RecoveryCommitValidator.ShouldRecoverBroadenedInterruptedRange(
            [".squad/decisions/inbox/review.md"],
            [
                ".squad/decisions/inbox/review.md",
                "Plugins/Web/DevExpress.CodeAnalysis.Web.Features/Blazor/AiGen/Tests/AiGenBlazorIntegrationTests.cs",
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRecoverBroadenedInterruptedRange_ExactRangeAlreadyContainsSource_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ShouldRecoverBroadenedInterruptedRange(
            ["Plugins/Web/Feature.cs"],
            ["Plugins/Web/Feature.cs", ".squad/decisions/inbox/review.md"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });

        Assert.That(result, Is.False);
    }

    [Test]
    public void InterruptedStepAcceptance_ReportsFailuresWithoutModalDialogs()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var handler = ExtractMethod(
            source,
            "private async Task<bool> AcceptInterruptedStepAfterReviewAsync(",
            "private Plan? ApplyAndPublishValidationHumanReviewRequired(");

        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain("ReportPlanActionIssue("));
            Assert.That(handler, Does.Not.Contain("MessageBox.Show("));
            Assert.That(handler, Does.Not.Contain("UIErrorHelper."));
        });
    }

    [Test]
    public void HumanReviewValidation_ReportsFailuresWithoutModalDialogs()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var handler = ExtractMethod(
            source,
            "private async Task<bool> ValidateHumanReviewStepAcceptanceAsync(",
            "private void ReconcileDecomposeRecoveryInboxMessages(");

        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain("ReportPlanActionIssue("));
            Assert.That(handler, Does.Not.Contain("MessageBox.Show("));
            Assert.That(handler, Does.Not.Contain("UIErrorHelper."));
        });
    }

    [Test]
    public void PlanViewer_UsesInlineStatusForAcceptanceAndRecoveryFailures()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var contentBuilder = ExtractMethod(
            source,
            "private void BuildContent(",
            "private PlanTaskCorrectionActivityState ResolveTaskCorrectionActivity(");

        Assert.Multiple(() =>
        {
            Assert.That(contentBuilder, Does.Contain("EnsureActionStatus("));
            Assert.That(contentBuilder, Does.Contain("ShowActionStatus("));
            Assert.That(contentBuilder, Does.Not.Contain("UIErrorHelper.ShowError(\"Complete Review\""));
            Assert.That(contentBuilder, Does.Not.Contain("UIErrorHelper.ShowError(\"Accept Completed Step\""));
            Assert.That(contentBuilder, Does.Not.Contain("UIErrorHelper.ShowError(\"Plan Recovery\""));
        });
    }

    [Test]
    public void RecoveryOptions_ClearBeforeAgentWorkAndReportImmediateFailureInTranscript()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var panel = ExtractMethod(
            source,
            "private void AppendRecoveryOptionsPanel(",
            "private async Task<bool> ExecuteRecoveryOptionAsync(");

        Assert.Multiple(() =>
        {
            Assert.That(panel, Does.Contain("panel.Visibility = Visibility.Collapsed;"));
            Assert.That(panel, Does.Contain("await ExecuteRecoveryOptionAsync("));
            Assert.That(panel, Does.Not.Contain("panel.Visibility = Visibility.Visible;"));
        });
    }

    [TestCase(
        "private async Task<bool> AdoptOrphanCommitAsync(",
        "private async Task<bool> RevertAndRetryAsync(")]
    [TestCase(
        "private async Task<bool> RevertAndRetryAsync(",
        "private async Task ReplanWithCurrentStateAsync(")]
    public void DestructiveRecoveryActions_UseOnlyTheirAuthorizationDialog(
        string startMarker,
        string endMarker)
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var handler = ExtractMethod(source, startMarker, endMarker);

        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain("ReportPlanActionIssue("));
            Assert.That(CountOccurrences(handler, "MessageBox.Show("), Is.EqualTo(1));
            Assert.That(handler, Does.Not.Contain("UIErrorHelper."));
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing marker: {startMarker}");
        Assert.That(end, Is.GreaterThan(start), $"Missing marker: {endMarker}");
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(pathParts)}'.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static PlanTask MakeTask(
        string taskId,
        IReadOnlyList<string> dependsOn,
        string status) =>
        new(
            TaskId:      taskId,
            Title:       null,
            Description: "Test task",
            DependsOn:   dependsOn,
            Priority:    "medium",
            Status:      status);
}

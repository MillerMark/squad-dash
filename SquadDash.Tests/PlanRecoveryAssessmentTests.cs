namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryAssessmentTests
{
    private const string Prefix = """
        PLAN_RECOVERY_ASSESSMENT_JSON:
        """;

    [Test]
    public void CompleteAssessment_WithPassedVerificationAndAttributedCommit_Parses()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"bbbbbbbb",
             "classification":"complete","summary":"Task is complete.","remainingWork":[],
             "verification":{"status":"passed","command":"dotnet test","summary":"All tests passed."},
             "commits":[{"commit":"bbbbbbbb","relation":"task","reason":"Implements the task."}]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(response!.Classification, Is.EqualTo(PlanRecoveryClassification.Complete));
            Assert.That(response.Commits.Single().Relation, Is.EqualTo(PlanRecoveryCommitRelation.Task));
        });
    }

    [Test]
    public void Assessment_WithTrailingCommasAndComments_ParsesTolerantly()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"bbbbbbbb",
             "classification":"inconclusive","summary":"Needs review.",
             "remainingWork":[],"verification":null,
             "commits":[
               {"commit":"bbbbbbbb","relation":"unknown","reason":"Unclear.",},
             ], // repairable JSON style emitted by some models
            }
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error), Is.True, error);
        Assert.That(response!.Classification, Is.EqualTo(PlanRecoveryClassification.Inconclusive));
    }

    [Test]
    public void InconclusiveAssessment_PreservesChronologicalSupportingCommits()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"dddddddd",
             "classification":"inconclusive","summary":"Older work may implement the step.",
             "remainingWork":[],"verification":null,
             "commits":[{"commit":"dddddddd","relation":"unrelated","reason":"Recovery infrastructure."}],
             "supportingCommits":[
               {"commit":"bbbbbbbb","relation":"task","reason":"Introduced the feature."},
               {"commit":"cccccccc","relation":"unknown","reason":"May refine the feature."}
             ]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(response!.SupportingCommits!.Select(commit => commit.Commit),
                Is.EqualTo(new[] { "bbbbbbbb", "cccccccc" }));
            Assert.That(response.SupportingCommits![1].Relation, Is.EqualTo(PlanRecoveryCommitRelation.Unknown));
        });
    }

    [Test]
    public void MarkerlessAssessment_NormalizesNullCollectionsAndClassificationCasing()
    {
        var text = """
            ```json
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"bbbbbbbb",
             "classification":" Inconclusive ","summary":"Needs review.",
             "remainingWork":null,"verification":null,"commits":null}
            ```
            """;

        var parsed = PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(response!.Classification, Is.EqualTo(PlanRecoveryClassification.Inconclusive));
            Assert.That(response.RemainingWork, Is.Empty);
            Assert.That(response.Commits, Is.Empty);
        });
    }

    [Test]
    public void CompleteAssessment_WithoutPassedVerification_IsRejected()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"bbbbbbbb",
             "classification":"complete","summary":"Looks complete.","remainingWork":[],
             "verification":{"status":"not_run","command":null,"summary":"Not run."},
             "commits":[]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("passed verification"));
    }

    [Test]
    public void PartialAssessment_WithoutRemainingWork_IsRejected()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"bbbbbbbb",
             "classification":"partial","summary":"Some work exists.","remainingWork":[],
             "verification":null,"commits":[]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("remaining work"));
    }

    [Test]
    public void UnknownCommitRelation_IsRejected()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","planId":"PLAN-1","taskId":"TASK-1",
             "revision":"rev-1","baselineCommit":"aaaaaaaa","assessedHead":"bbbbbbbb",
             "classification":"inconclusive","summary":"Cannot tell.","remainingWork":[],
             "verification":null,
             "commits":[{"commit":"bbbbbbbb","relation":"maybe","reason":"Unclear."}]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("valid relation"));
    }

    [Test]
    public void MatchesRequest_RejectsStaleHead()
    {
        var response = Response(
            PlanRecoveryClassification.Inconclusive,
            [Commit("bbbbbbbb", PlanRecoveryCommitRelation.Unknown)]) with
        {
            AssessedHead = "stale-head",
        };

        Assert.That(PlanRecoveryAssessmentValidator.MatchesRequest(
            response, "assessment-1", "PLAN-1", "TASK-1", "rev-1", "aaaaaaaa", "bbbbbbbb"), Is.False);
    }

    [Test]
    public void CommitCoverage_RejectsOmittedCommit()
    {
        var response = Response(
            PlanRecoveryClassification.Partial,
            [Commit("bbbbbbbb", PlanRecoveryCommitRelation.Task)]) with
        {
            RemainingWork = ["Finish tests"],
        };

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, ["bbbbbbbb", "cccccccc"], out _, out var error), Is.False);
        Assert.That(error, Does.Contain("every commit"));
    }

    [Test]
    public void CommitCoverage_ReportsMistypedAndMissingCommitIdsBeforeGitResolution()
    {
        var response = Response(
            PlanRecoveryClassification.NotStarted,
            [Commit("dddddddd", PlanRecoveryCommitRelation.Unrelated)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, ["bbbbbbbb", "cccccccc"], out _, out var error), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(error, Does.Contain("Missing: bbbbbbbb, cccccccc"));
            Assert.That(error, Does.Contain("Not in the captured range: dddddddd"));
        });
    }

    [TestCase("0123456789abcdef0123456789abcdef01234567", true)]
    [TestCase("0123456", true)]
    [TestCase("012345", false)]
    [TestCase("not-a-commit", false)]
    [TestCase("0123456\" --help", false)]
    public void SafeGitCommitIdentifier_AllowsOnlyBoundedHexadecimalIds(
        string value,
        bool expected)
    {
        Assert.That(
            PlanRecoveryAssessmentValidator.IsSafeGitCommitIdentifier(value),
            Is.EqualTo(expected));
    }

    [Test]
    public void CommitCoverage_RejectsCompleteWithoutAttributedCommit()
    {
        var response = Response(
            PlanRecoveryClassification.Complete,
            [Commit("bbbbbbbb", PlanRecoveryCommitRelation.Unrelated)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, ["bbbbbbbb"], out _, out var error), Is.False);
        Assert.That(error, Does.Contain("without identifying"));
    }

    [Test]
    public void CommitCoverage_PreservesHistoryOrderForAttributedCommits()
    {
        var response = Response(
            PlanRecoveryClassification.Partial,
            [
                Commit("cccccccc", PlanRecoveryCommitRelation.Mixed),
                Commit("bbbbbbbb", PlanRecoveryCommitRelation.Task),
                Commit("dddddddd", PlanRecoveryCommitRelation.Unrelated),
            ]) with
        {
            RemainingWork = ["Finish tests"],
        };

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response,
            ["bbbbbbbb", "cccccccc", "dddddddd"],
            out var attributed,
            out var error), Is.True, error);
        Assert.That(attributed, Is.EqualTo(new[] { "bbbbbbbb", "cccccccc" }));
    }

    [Test]
    public void PlanEvidence_RejectsCompleteWhileIndependentVerificationIsUnresolved()
    {
        var task = new PlanTask(
            "TASK-1", "Task", "Task", [], "high", PlanTaskStatus.HumanReviewRequired,
            VerificationHistory:
            [
                new PlanTaskVerificationReport(
                    PlanTaskVerificationVerdict.HumanReviewRequired,
                    "Production approval actions were not guarded.", [], ["Missing action guard"],
                    "No production action test.", ["Guard approve and reject."], "bbbbbbbb",
                    DateTimeOffset.UtcNow),
            ]);
        var plan = new Plan(
            "PLAN-1", "rev-1", PlanSource.Inbox, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/plan", "Summary", [task], [], new PlanProgress(0, 1),
            new PlanTimestamps(DateTimeOffset.UtcNow));
        var response = Response(
            PlanRecoveryClassification.Complete,
            [Commit("bbbbbbbb", PlanRecoveryCommitRelation.Task)]);

        var valid = PlanRecoveryAssessmentValidator.TryValidateAgainstPlanEvidence(
            plan, response, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("verification remains unresolved"));
            Assert.That(error, Does.Contain("Production approval actions were not guarded"));
        });
    }

    [Test]
    public void PlanEvidence_AllowsPartialWhileIndependentVerificationIsUnresolved()
    {
        var task = new PlanTask(
            "TASK-1", "Task", "Task", [], "high", PlanTaskStatus.HumanReviewRequired,
            VerificationHistory:
            [
                new PlanTaskVerificationReport(
                    PlanTaskVerificationVerdict.ReworkRequired, "Missing guard", [], ["Missing guard"],
                    "Tests incomplete", ["Add guard"], "bbbbbbbb", DateTimeOffset.UtcNow),
            ]);
        var plan = new Plan(
            "PLAN-1", "rev-1", PlanSource.Inbox, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/plan", "Summary", [task], [], new PlanProgress(0, 1),
            new PlanTimestamps(DateTimeOffset.UtcNow));
        var response = Response(
            PlanRecoveryClassification.Partial,
            [Commit("bbbbbbbb", PlanRecoveryCommitRelation.Task)]) with
        {
            RemainingWork = ["Add guard"],
        };

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateAgainstPlanEvidence(
            plan, response, out var error), Is.True, error);
    }

    [Test]
    public void PlanEvidence_AllowsCompleteAfterLaterVerificationAcceptsTheWork()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new PlanTask(
            "TASK-1", "Task", "Task", [], "high", PlanTaskStatus.HumanReviewRequired,
            VerificationHistory:
            [
                new PlanTaskVerificationReport(
                    PlanTaskVerificationVerdict.ReworkRequired, "Missing guard", [], ["Missing guard"],
                    "Tests incomplete", ["Add guard"], "bbbbbbbb", now.AddMinutes(-1)),
                new PlanTaskVerificationReport(
                    PlanTaskVerificationVerdict.Accepted, "Guard and tests verified.", [], [],
                    "Tests passed", [], "cccccccc", now),
            ]);
        var plan = new Plan(
            "PLAN-1", "rev-1", PlanSource.Inbox, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/plan", "Summary", [task], [], new PlanProgress(0, 1),
            new PlanTimestamps(now));
        var response = Response(
            PlanRecoveryClassification.Complete,
            [Commit("bbbbbbbb", PlanRecoveryCommitRelation.Task)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateAgainstPlanEvidence(
            plan, response, out var error), Is.True, error);
    }

    [Test]
    public void RepositoryChangeRetryPolicy_AllowsExactlyOneAutomaticReassessment()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryAssessmentRetryPolicy.CanRetryRepositoryChange(0), Is.True);
            Assert.That(PlanRecoveryAssessmentRetryPolicy.CanRetryRepositoryChange(1), Is.False);
            Assert.That(PlanRecoveryAssessmentRetryPolicy.CanRetryRepositoryChange(2), Is.False);
        });
    }

    private static PlanRecoveryCommitAssessment Commit(string sha, string relation) =>
        new(sha, relation, "Evidence");

    private static PlanRecoveryAssessmentResponse Response(
        string classification,
        IReadOnlyList<PlanRecoveryCommitAssessment> commits) =>
        new(
            "assessment-1",
            "PLAN-1",
            "TASK-1",
            "rev-1",
            "aaaaaaaa",
            "bbbbbbbb",
            classification,
            "Summary",
            [],
            new DecomposeStepVerification("passed", "dotnet test", "Passed"),
            commits);
}

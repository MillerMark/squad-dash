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
            {"recoveryAssessmentId":"assessment-1","classification":"complete",
             "summary":"Task is complete.","remainingWork":[],
             "verification":{"status":"passed","command":"dotnet test","summary":"All tests passed."},
             "commits":[{"commitId":"c001","relation":"task","reason":"Implements the task."}]}
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
            {"recoveryAssessmentId":"assessment-1","classification":"inconclusive","summary":"Needs review.",
             "remainingWork":[],"verification":null,
             "commits":[
               {"commitId":"c001","relation":"unknown","reason":"Unclear.",},
             ], // repairable JSON style emitted by some models
            }
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error), Is.True, error);
        Assert.That(response!.Classification, Is.EqualTo(PlanRecoveryClassification.Inconclusive));
    }

    [Test]
    public void UnrelatedCommit_MayOmitReasonForCompactResponse()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","classification":"not_started",
             "summary":"No current task work exists.","remainingWork":[],"verification":null,
             "commits":[{"commitId":"c001","relation":"unrelated"}]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error), Is.True, error);
        Assert.That(response!.Commits.Single().Reason, Does.Contain("unrelated"));
    }

    [Test]
    public void RelevantCommit_MustExplainItsRelation()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","classification":"inconclusive",
             "summary":"Potential task work exists.","remainingWork":[],"verification":null,
             "commits":[{"commitId":"c001","relation":"unknown"}]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("require a reason"));
    }

    [Test]
    public void InconclusiveAssessment_PreservesChronologicalSupportingCommits()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","classification":"inconclusive",
             "summary":"Older work may implement the step.",
             "remainingWork":[],"verification":null,
             "commits":[{"commitId":"c001","relation":"unrelated","reason":"Recovery infrastructure."}],
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
            {"recoveryAssessmentId":"assessment-1","classification":" Inconclusive ",
             "summary":"Needs review.",
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
            {"recoveryAssessmentId":"assessment-1","classification":"complete",
             "summary":"Looks complete.","remainingWork":[],
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
            {"recoveryAssessmentId":"assessment-1","classification":"partial",
             "summary":"Some work exists.","remainingWork":[],
             "verification":null,"commits":[]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("remaining work"));
    }

    [Test]
    public void UnknownCommitRelation_IsRejected()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","classification":"inconclusive",
             "summary":"Cannot tell.","remainingWork":[],
             "verification":null,
             "commits":[{"commitId":"c001","relation":"maybe","reason":"Unclear."}]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("valid relation"));
    }

    [Test]
    public void SupersededCommitRelation_ParsesForRevisedTaskEvidence()
    {
        var text = Prefix + """
            {"recoveryAssessmentId":"assessment-1","classification":"not_started",
             "summary":"Only replaced work exists.","remainingWork":[],
             "verification":null,
             "commits":[{"commitId":"c001","relation":"superseded","reason":"Implements the old specification."}]}
            """;

        Assert.That(PlanRecoveryAssessmentParser.TryParse(text, out var response, out var error), Is.True, error);
        Assert.That(response!.Commits.Single().Relation, Is.EqualTo(PlanRecoveryCommitRelation.Superseded));
    }

    [Test]
    public void MatchesRequest_RejectsWrongAssessmentNonce()
    {
        var response = Response(
            PlanRecoveryClassification.Inconclusive,
            [Commit("c001", PlanRecoveryCommitRelation.Unknown)]);

        Assert.That(PlanRecoveryAssessmentValidator.MatchesRequest(
            response, "another-assessment"), Is.False);
    }

    [Test]
    public void CommitReferences_UseDeterministicShortIdsWithoutChangingShas()
    {
        var references = References("aaaaaaaa", "bbbbbbbb", "cccccccc");

        Assert.Multiple(() =>
        {
            Assert.That(references.Select(reference => reference.Id),
                Is.EqualTo(new[] { "c001", "c002", "c003" }));
            Assert.That(references.Select(reference => reference.Commit),
                Is.EqualTo(new[] { "aaaaaaaa", "bbbbbbbb", "cccccccc" }));
        });
    }

    [Test]
    public void EvidenceLog_PairsShortIdsWithRealShasForReadOnlyGitTools()
    {
        const string first = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string second = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var formatted = MainWindow.FormatPlanRecoveryCommitLog(
            $"{first}\tFirst commit\n{second}\tSecond commit",
            References(first, second));

        Assert.That(formatted, Is.EqualTo(
            $"c001\t{first}\tFirst commit\nc002\t{second}\tSecond commit"));
    }

    [Test]
    public void CompactResponse_SerializesOnlyNonceAndSemanticAssessmentData()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Response(
            PlanRecoveryClassification.NotStarted,
            [Commit("c001", PlanRecoveryCommitRelation.Unrelated)]));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"recoveryAssessmentId\":\"assessment-1\""));
            Assert.That(json, Does.Contain("\"commitId\":\"c001\""));
            Assert.That(json, Does.Not.Contain("planId"));
            Assert.That(json, Does.Not.Contain("taskId"));
            Assert.That(json, Does.Not.Contain("revision"));
            Assert.That(json, Does.Not.Contain("baselineCommit"));
            Assert.That(json, Does.Not.Contain("assessedHead"));
        });
    }

    [Test]
    public void CommitCoverage_RejectsOmittedCommit()
    {
        var response = Response(
            PlanRecoveryClassification.Partial,
            [Commit("c001", PlanRecoveryCommitRelation.Task)]) with
        {
            RemainingWork = ["Finish tests"],
        };

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, References("bbbbbbbb", "cccccccc"), out _, out var error), Is.False);
        Assert.That(error, Does.Contain("every commit"));
    }

    [Test]
    public void CommitCoverage_ReportsMistypedAndMissingCommitIdsBeforeGitResolution()
    {
        var response = Response(
            PlanRecoveryClassification.NotStarted,
            [Commit("c003", PlanRecoveryCommitRelation.Unrelated)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, References("bbbbbbbb", "cccccccc"), out _, out var error), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(error, Does.Contain("Missing: c001, c002"));
            Assert.That(error, Does.Contain("Not in the captured range: c003"));
        });
    }

    [Test]
    public void CommitCoverage_AggregatesListAndClassificationErrorsForSingleRepair()
    {
        var response = Response(
            PlanRecoveryClassification.NotStarted,
            [
                Commit("c001", PlanRecoveryCommitRelation.Task),
                Commit("c003", PlanRecoveryCommitRelation.Unrelated),
            ]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, References("bbbbbbbb", "cccccccc"), out _, out var error), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(error, Does.Contain("Missing: c002"));
            Assert.That(error, Does.Contain("Not in the captured range: c003"));
            Assert.That(error, Does.Contain("not started"));
            Assert.That(error, Does.Contain("superseded"));
        });
    }

    [Test]
    public void CommitCoverage_NotStartedAllowsSupersededOlderSpecification()
    {
        var response = Response(
            PlanRecoveryClassification.NotStarted,
            [Commit("c001", PlanRecoveryCommitRelation.Superseded)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, References("bbbbbbbb"), out var attributed, out var error), Is.True, error);
        Assert.That(attributed, Is.Empty);
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
    public void ErrorReportLink_RoundTripsDetailedValidationFindings()
    {
        var report = PlanRecoveryAssessmentErrorReport.Build(
            "PLAN-1",
            "TASK-2",
            "assessment-3",
            "Missing commit abc. Classification contradicted relation task.");

        var target = PlanRecoveryAssessmentErrorReport.CreateLinkTarget(report);
        var decoded = PlanRecoveryAssessmentErrorReport.TryDecodeLinkTarget(target, out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(target, Does.StartWith(PlanRecoveryAssessmentErrorReport.LinkPrefix));
            Assert.That(restored, Is.EqualTo(report));
            Assert.That(restored, Does.Contain("Plan: PLAN-1"));
            Assert.That(restored, Does.Contain("Task: TASK-2"));
            Assert.That(restored, Does.Contain("Missing commit abc"));
            Assert.That(restored, Does.Contain("requested one corrected structured response"));
        });
    }

    [TestCase("https://example.com")]
    [TestCase("app://plan-recovery-assessment-error/not-base64!")]
    public void ErrorReportLink_RejectsUnrelatedOrMalformedTargets(string target)
    {
        Assert.That(
            PlanRecoveryAssessmentErrorReport.TryDecodeLinkTarget(target, out _),
            Is.False);
    }

    [Test]
    public void CommitCoverage_RejectsCompleteWithoutAttributedCommit()
    {
        var response = Response(
            PlanRecoveryClassification.Complete,
            [Commit("c001", PlanRecoveryCommitRelation.Unrelated)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response, References("bbbbbbbb"), out _, out var error), Is.False);
        Assert.That(error, Does.Contain("without identifying"));
    }

    [Test]
    public void CommitCoverage_PreservesHistoryOrderForAttributedCommits()
    {
        var response = Response(
            PlanRecoveryClassification.Partial,
            [
                Commit("c002", PlanRecoveryCommitRelation.Mixed),
                Commit("c001", PlanRecoveryCommitRelation.Task),
                Commit("c003", PlanRecoveryCommitRelation.Unrelated),
            ]) with
        {
            RemainingWork = ["Finish tests"],
        };

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateCommitCoverage(
            response,
            References("bbbbbbbb", "cccccccc", "dddddddd"),
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
            [Commit("c001", PlanRecoveryCommitRelation.Task)]);

        var valid = PlanRecoveryAssessmentValidator.TryValidateAgainstPlanEvidence(
            plan, "TASK-1", response, out var error);

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
            [Commit("c001", PlanRecoveryCommitRelation.Task)]) with
        {
            RemainingWork = ["Add guard"],
        };

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateAgainstPlanEvidence(
            plan, "TASK-1", response, out var error), Is.True, error);
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
            [Commit("c001", PlanRecoveryCommitRelation.Task)]);

        Assert.That(PlanRecoveryAssessmentValidator.TryValidateAgainstPlanEvidence(
            plan, "TASK-1", response, out var error), Is.True, error);
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

    private static PlanRecoveryCommitAssessment Commit(string commitId, string relation) =>
        new(commitId, relation, "Evidence");

    private static IReadOnlyList<PlanRecoveryCommitReference> References(params string[] commits) =>
        PlanRecoveryAssessmentValidator.CreateCommitReferences(commits);

    private static PlanRecoveryAssessmentResponse Response(
        string classification,
        IReadOnlyList<PlanRecoveryCommitAssessment> commits) =>
        new(
            "assessment-1",
            classification,
            "Summary",
            [],
            new DecomposeStepVerification("passed", "dotnet test", "Passed"),
            commits);
}

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

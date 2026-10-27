namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanProofCoverageReviewTests
{
    [Test]
    public void Prompt_CoversHumanProofAndCrossAgentOwnershipInOneReview()
    {
        const string checkId = "check-123";
        const string candidateHash = "abcdef0123456789";

        var prompt = PlanHumanProofCoverageReview.BuildPrompt(checkId, candidateHash);
        var normalized = prompt.ReplaceLineEndings(" ");

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Does.Contain(PlanHumanProofCoverageReview.Marker));
            Assert.That(normalized, Does.Contain("human-observable promise"));
            Assert.That(normalized, Does.Contain("headless tests do not establish direct human observation"));
            Assert.That(normalized, Does.Contain("invalid human-only task shell"));
            Assert.That(normalized, Does.Contain("must not create a separate operator runbook"));
            Assert.That(normalized, Does.Contain("durable approval Inbox message"));
            Assert.That(normalized, Does.Contain("sample project"));
            Assert.That(normalized, Does.Contain("Do not classify a task as a human-only shell merely because"));
            Assert.That(normalized, Does.Contain("only when the task explicitly asks AI to perform or claim the human interaction"));
            Assert.That(normalized, Does.Contain("Do not report implementation-quality"));
            Assert.That(normalized, Does.Contain(checkId));
            Assert.That(normalized, Does.Contain(candidateHash));
            Assert.That(normalized, Does.Contain("report all uncovered human-proof"));
            Assert.That(normalized, Does.Contain("single assigned primary"));
            Assert.That(normalized, Does.Contain("cross-agent ownership requirements in one issues array"));
            Assert.That(normalized, Does.Contain("create or run tests"));
            Assert.That(normalized, Does.Contain("separate dependent task assigned to that agent"));
            Assert.That(normalized, Does.Contain("collaborates"));
            Assert.That(normalized, Does.Contain("subordinate read-only research helpers"));
            Assert.That(normalized, Does.Contain("alongside the host's aggregated structural diagnostics"));
        });
    }

    [Test]
    public void Parser_AcceptsRevisionRequiredWithSpecificIssue()
    {
        var response = PlanHumanProofCoverageReview.Marker + """
            {
              "checkId":"check-1",
              "candidateHash":"abcdef0123456789",
              "status":"revision-required",
              "summary":"One live outcome is uncovered.",
              "issues":[{
                "targetId":"PLAN-1-007",
                "observableOutcome":"Retry shows progress in the live dialog.",
                "reason":"Only source and headless evidence are declared.",
                "recommendation":"Add one live UI observation checkpoint after Step 7."
              }]
            }
            """;

        Assert.That(PlanHumanProofCoverageReview.TryParse(response, out var payload, out var error),
            Is.True, error);
        Assert.That(payload!.Issues, Has.Count.EqualTo(1));
        Assert.That(PlanHumanProofCoverageReview.BuildRepairDiagnostic(payload),
            Does.Contain("PLAN-1-007"));
    }

    [Test]
    public void StartPolicy_CoalescesDuplicateCandidateAndPreservesDifferentActiveReview()
    {
        var active = new PendingPlanHumanProofCoverageReview(
            "check-1",
            "candidate-a",
            null,
            "TASKS_JSON:\n{}",
            [],
            0);

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanHumanProofCoverageReview.DecideStart(null, "candidate-a"),
                Is.EqualTo(PlanHumanProofCoverageStartDecision.Start));
            Assert.That(
                PlanHumanProofCoverageReview.DecideStart(active, "candidate-a"),
                Is.EqualTo(PlanHumanProofCoverageStartDecision.CoalesceSameCandidate));
            Assert.That(
                PlanHumanProofCoverageReview.DecideStart(active, "candidate-b"),
                Is.EqualTo(PlanHumanProofCoverageStartDecision.PreserveActiveReview));
        });
    }

    [Test]
    public void RepairDiagnostic_DescribesCrossAgentOwnershipAsPlanContractFailure()
    {
        var payload = new PlanHumanProofCoverageReviewPayload(
            "check-1",
            "abcdef0123456789",
            "revision-required",
            "One task assigns required testing to another agent.",
            [new PlanHumanProofCoverageIssue(
                "PLAN-1-002",
                "BruceBanner owns the tests while Shuri is primary.",
                "The required worker proof is assigned only through prose.",
                "Create a dependent BruceBanner testing task.")]);

        var diagnostic = PlanHumanProofCoverageReview.BuildRepairDiagnostic(payload);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic, Does.StartWith("Semantic plan-contract validation failed."));
            Assert.That(diagnostic, Does.Contain("PLAN-1-002"));
            Assert.That(diagnostic, Does.Contain("dependent BruceBanner testing task"));
        });
    }

    [Test]
    public void Parser_RejectsReadyStatusWithIssues()
    {
        var response = PlanHumanProofCoverageReview.Marker + """
            {
              "checkId":"check-1",
              "candidateHash":"abcdef0123456789",
              "status":"ready",
              "summary":"Contradictory.",
              "issues":[{
                "targetId":"PLAN-1-007",
                "observableOutcome":"Live outcome.",
                "reason":"Missing.",
                "recommendation":"Add checkpoint."
              }]
            }
            """;

        Assert.That(PlanHumanProofCoverageReview.TryParse(response, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("ready only when"));
    }
}

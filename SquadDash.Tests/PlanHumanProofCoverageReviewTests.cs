namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanProofCoverageReviewTests
{
    [Test]
    public void Prompt_IsFocusedOnMissingHumanProofCoverage()
    {
        var group = StagedPlanEditorTests.MakeGroup();
        var plan = new PendingDecomposePlan(
            PendingDecomposePlanStore.ComputeRevision(group), group, DateTimeOffset.UtcNow);

        var prompt = PlanHumanProofCoverageReview.BuildPrompt(plan);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(PlanHumanProofCoverageReview.Marker));
            Assert.That(prompt, Does.Contain("human-observable promise"));
            Assert.That(prompt, Does.Contain("headless tests do not establish direct human observation"));
            Assert.That(prompt, Does.Contain("Do not report implementation-quality"));
            Assert.That(prompt, Does.Contain(plan.Revision));
        });
    }

    [Test]
    public void Parser_AcceptsRevisionRequiredWithSpecificIssue()
    {
        var response = PlanHumanProofCoverageReview.Marker + """
            {
              "groupId":"PLAN-1",
              "baseRevision":"rev-1",
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
    public void Parser_RejectsReadyStatusWithIssues()
    {
        var response = PlanHumanProofCoverageReview.Marker + """
            {
              "groupId":"PLAN-1",
              "baseRevision":"rev-1",
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

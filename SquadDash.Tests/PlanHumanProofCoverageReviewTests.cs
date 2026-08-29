namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanProofCoverageReviewTests
{
    [Test]
    public void Prompt_IsFocusedOnMissingHumanProofCoverage()
    {
        const string checkId = "check-123";
        const string candidateHash = "abcdef0123456789";

        var prompt = PlanHumanProofCoverageReview.BuildPrompt(checkId, candidateHash);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(PlanHumanProofCoverageReview.Marker));
            Assert.That(prompt, Does.Contain("human-observable promise"));
            Assert.That(prompt, Does.Contain("headless tests do not establish direct human observation"));
            Assert.That(prompt, Does.Contain("Do not report implementation-quality"));
            Assert.That(prompt, Does.Contain(checkId));
            Assert.That(prompt, Does.Contain(candidateHash));
            Assert.That(prompt, Does.Contain("report all uncovered human-proof"));
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

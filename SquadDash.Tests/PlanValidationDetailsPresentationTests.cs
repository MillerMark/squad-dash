using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanValidationDetailsPresentationTests
{
    [Test]
    public void Build_MapsCanonicalEvidenceToExactApprovedAssertions()
    {
        var completedAt = DateTimeOffset.Parse("2026-09-27T12:00:00Z");
        var validation = new PlanValidationNode(
            "PLAN-VAL-002",
            "Coordinate safety",
            "Validate coordinate output.",
            ["PLAN-001"],
            ["PLAN-002"],
            ["Finite inputs stay finite.", "Labels remain readable."],
            [],
            "evidence",
            [],
            true,
            PlanValidationStatus.Failed,
            Summary: "One assertion failed.",
            Evidence:
            [
                "[PASS] Labels remain readable.: All label cases passed.",
                "[FAIL] Finite inputs stay finite.: A finite coordinate produced Infinity.",
            ],
            ValidatedCommit: "8b57f39",
            CompletedAt: completedAt);
        var plan = MakePlan(validation);

        var result = PlanValidationDetailsPresentationBuilder.Build(plan, validation);

        Assert.Multiple(() =>
        {
            Assert.That(result.ValidationId, Is.EqualTo("PLAN-VAL-002"));
            Assert.That(result.Summary, Is.EqualTo("One assertion failed."));
            Assert.That(result.ValidatedCommit, Is.EqualTo("8b57f39"));
            Assert.That(result.CompletedAt, Is.EqualTo(completedAt));
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Assertions[0].Assertion, Is.EqualTo("Finite inputs stay finite."));
            Assert.That(result.Assertions[0].Passed, Is.False);
            Assert.That(result.Assertions[0].Evidence,
                Is.EqualTo("A finite coordinate produced Infinity."));
            Assert.That(result.Assertions[1].Assertion, Is.EqualTo("Labels remain readable."));
            Assert.That(result.Assertions[1].Passed, Is.True);
            Assert.That(result.Assertions[1].Evidence, Is.EqualTo("All label cases passed."));
        });
    }

    [Test]
    public void Build_DoesNotHeuristicallyAssignUnmatchedEvidence()
    {
        var validation = new PlanValidationNode(
            "PLAN-VAL-001",
            "Contract validation",
            "Validate a contract.",
            ["PLAN-001"],
            ["PLAN-002"],
            ["The exact approved assertion."],
            [],
            "evidence",
            [],
            true,
            PlanValidationStatus.Failed,
            Evidence: ["[FAIL] A similar assertion.: This must not be reassigned."]);

        var result = PlanValidationDetailsPresentationBuilder.Build(MakePlan(validation), validation);

        Assert.Multiple(() =>
        {
            Assert.That(result.Assertions[0].Passed, Is.Null);
            Assert.That(result.Assertions[0].Evidence,
                Is.EqualTo("No assertion-specific evidence was recorded."));
            Assert.That(result.FailedCount, Is.Zero);
        });
    }

    private static Plan MakePlan(PlanValidationNode validation) => new(
        "PLAN",
        "rev",
        PlanSource.TasksJson,
        PlanLifecycleStatus.Blocked,
        "Test plan",
        "feature/test",
        "Test validation details.",
        [
            new PlanTask("PLAN-001", "Produce", "Produce output.", [], "high", PlanTaskStatus.Complete),
            new PlanTask("PLAN-002", "Consume", "Consume output.", ["PLAN-001"], "high", PlanTaskStatus.Pending),
        ],
        [],
        new PlanProgress(1, 2),
        new PlanTimestamps(DateTimeOffset.UtcNow),
        Validations: [validation]);
}

namespace SquadDash.Tests;

[TestFixture]
internal sealed class HumanVerificationReadinessPolicyTests
{
    [Test]
    public void MissingAccessDeclaration_IsAWarningForLegacyPlans()
    {
        var group = MakeGroup([
            MakeTask(
                "REVIEW-20260825-001",
                proofRequirements: [new DecomposedTaskProofRequirement(
                    "visible-state", "human-observation", "Confirm the state is visible.")]),
        ]);

        var finding = HumanVerificationReadinessPolicy.Evaluate(group).Single();

        Assert.Multiple(() =>
        {
            Assert.That(finding.Severity, Is.EqualTo("warning"));
            Assert.That(finding.RequirementId, Is.EqualTo("visible-state"));
        });
    }

    [Test]
    public void ExplicitUpstreamFixtureAndCompleteRecipe_AreStructurallyReady()
    {
        var fixture = MakeTask(
            "REVIEW-20260825-001",
            outputs: [new DecomposedTaskOutput("pending-upgrade", "A pending workspace-upgrade fixture.")]);
        var implementation = MakeTask(
            "REVIEW-20260825-002",
            dependsOn: [fixture.Id]);
        var gate = new DecomposedGate(
            "REVIEW-20260825-GATE-001",
            "Review the pending state.",
            [implementation.Id],
            [],
            [new DecomposedTaskProofRequirement(
                "pending-badge",
                "human-observation",
                "Confirm the pending badge is visible.",
                "Is the pending badge visible?",
                HumanVerificationAccessMode.Fixture,
                new HumanVerificationRecipe(
                    "Create the pending-upgrade fixture.",
                    "Restart SquadDash and open the workspace.",
                    "Confirm the title-bar badge is visible.",
                    "Clear the fixture from the Developer menu."),
                fixture.Id,
                "pending-upgrade")],
            "Is the pending badge visible?");

        var findings = HumanVerificationReadinessPolicy.Evaluate(
            MakeGroup([fixture, implementation], [gate]));

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void ParserReportsEveryInvalidHumanVerificationReferenceInOneRejection()
    {
        var response = """
            TASKS_JSON:
            {
              "groupId": "REVIEW-20260825",
              "groupTitle": "Review readiness",
              "branch": "feature/review-readiness",
              "summary": "Exercise explicit human verification references.",
              "tasks": [
                {
                  "id": "REVIEW-20260825-001", "title": "Create fixture", "description": "Create fixture state.",
                  "dependsOn": [], "priority": "high",
                  "outputs": [{"outputId":"pending-upgrade","description":"Pending state fixture."}]
                },
                {
                  "id": "REVIEW-20260825-002", "title": "Implement UI", "description": "Render fixture state.",
                  "dependsOn": ["REVIEW-20260825-001"], "priority": "high"
                },
                {
                  "id": "REVIEW-20260825-003", "title": "Continue", "description": "Continue after review.",
                  "dependsOn": ["REVIEW-20260825-002"], "priority": "mid"
                }
              ],
              "approvalGates": [{
                "gateId": "REVIEW-20260825-GATE-001",
                "message": "Review the UI.",
                "question": "Are both requested states observable?",
                "afterTaskIds": ["REVIEW-20260825-002"],
                "beforeTaskIds": ["REVIEW-20260825-003"],
                "proofRequirements": [
                  {
                    "requirementId": "missing-task", "proofType": "human-observation",
                    "description": "Observe state one.", "question": "Is state one visible?",
                    "accessMode": "fixture", "fixtureTaskId": "REVIEW-20260825-999", "fixtureOutputId": "state-one"
                  },
                  {
                    "requirementId": "missing-output", "proofType": "human-observation",
                    "description": "Observe state two.", "question": "Is state two visible?",
                    "accessMode": "fixture", "fixtureTaskId": "REVIEW-20260825-001", "fixtureOutputId": "state-two"
                  }
                ]
              }]
            }
            """;

        var parsed = TasksJsonParser.TryParse(response, out _, out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(diagnostic!.Message, Does.Contain("2 independent schema problems"));
            Assert.That(diagnostic.Message, Does.Contain("missing-task"));
            Assert.That(diagnostic.Message, Does.Contain("missing-output"));
        });
    }

    private static DecomposedTaskGroup MakeGroup(
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyList<DecomposedGate>? gates = null) =>
        new(
            "REVIEW-20260825",
            "Review readiness",
            "feature/review-readiness",
            "Validate human review access.",
            tasks,
            ApprovalGates: gates);

    private static DecomposedSubTask MakeTask(
        string id,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<DecomposedTaskOutput>? outputs = null,
        IReadOnlyList<DecomposedTaskProofRequirement>? proofRequirements = null) =>
        new(
            id,
            "Implement the requested behavior.",
            dependsOn ?? [],
            "high",
            "Implement behavior",
            Outputs: outputs,
            ProofRequirements: proofRequirements);
}

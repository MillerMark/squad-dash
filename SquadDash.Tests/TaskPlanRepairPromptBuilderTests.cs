namespace SquadDash.Tests;

[TestFixture]
internal sealed class TaskPlanRepairPromptBuilderTests
{
    private const string RoryStyleCandidate = """
        TASKS_JSON:
        {
          "groupId": "CLIDETECT-20260814",
          "groupTitle": "CLI detection",
          "branch": "feature/cli-detection",
          "summary": "Resolve the selected CLI safely.",
          "tasks": [
            {
              "id": "T01-contracts",
              "title": "Define contracts",
              "description": "Define the resolution contracts.",
              "dependsOn": [],
              "priority": "high",
              "executionMode": "implementation",
              "agentRoutingMode": "generic",
              "genericAgentReason": "Fixture"
            }
          ]
        }
        """;

    [Test]
    public void Build_RevalidatesWithHostParserAndIncludesCompleteRejectedCandidate()
    {
        var prompt = TaskPlanRepairPromptBuilder.Build(
            RoryStyleCandidate,
            null,
            "LIVE SPECIFICATION");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("invalid-task-id:"));
            Assert.That(prompt, Does.Contain("T01-contracts"));
            Assert.That(prompt, Does.Contain("--- BEGIN REJECTED TASKS_JSON CANDIDATE ---"));
            Assert.That(prompt, Does.Contain(RoryStyleCandidate));
            Assert.That(prompt, Does.Contain("one complete corrected TASKS_JSON block"));
            Assert.That(prompt, Does.Contain("re-engage that same specialist"));
            Assert.That(prompt, Does.EndWith("LIVE SPECIFICATION"));
        });
    }

    [Test]
    public void Build_PreservesLaterValidationDiagnosticAlongsideParserDiagnostic()
    {
        var prompt = TaskPlanRepairPromptBuilder.Build(
            RoryStyleCandidate,
            "The assigned roster handle is unavailable.",
            "LIVE SPECIFICATION");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("invalid-task-id:"));
            Assert.That(prompt, Does.Contain("The assigned roster handle is unavailable."));
        });
    }

    [Test]
    public void Build_ReportsIndependentSchemaProblemsInOneRepairTurn()
    {
        const string candidate = """
            TASKS_JSON:
            {
              "groupId": "REPAIR-20260815",
              "groupTitle": "Repair fixture",
              "branch": "feature/repair-fixture",
              "summary": "Exercise complete diagnostics.",
              "tasks": [
                {
                  "id": "bad-task-id",
                  "title": "Implement fixture",
                  "description": "Implement the fixture.",
                  "dependsOn": [],
                  "priority": "urgent",
                  "executionMode": "implementation",
                  "agentRoutingMode": "generic",
                  "genericAgentReason": "No roster specialist applies."
                }
              ],
              "approvalGates": [
                {
                  "gateId": "REPAIR-20260815-GATE-001",
                  "message": "Review the fixture.",
                  "afterTaskIds": ["bad-task-id"],
                  "beforeTaskIds": []
                }
              ]
            }
            """;

        var prompt = TaskPlanRepairPromptBuilder.Build(candidate, null, "LIVE SPECIFICATION");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("3 independent schema problems were found"));
            Assert.That(prompt, Does.Contain("invalid-task-id"));
            Assert.That(prompt, Does.Contain("invalid-task-priority"));
            Assert.That(prompt, Does.Contain("missing-gate-question"));
        });
    }

    [Test]
    public void Build_HandlesUnavailableCandidateWithoutDroppingRepairContract()
    {
        var prompt = TaskPlanRepairPromptBuilder.Build(
            null,
            "A later validator rejected the plan.",
            "LIVE SPECIFICATION");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("(The rejected candidate was unavailable.)"));
            Assert.That(prompt, Does.Contain("A later validator rejected the plan."));
            Assert.That(prompt, Does.Contain("not a patch or substitution list"));
        });
    }
}

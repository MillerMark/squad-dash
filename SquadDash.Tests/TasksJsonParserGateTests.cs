namespace SquadDash.Tests;

/// <summary>
/// Tests for gate validation in <see cref="TasksJsonParser.TryParse"/>.
/// </summary>
[TestFixture]
internal sealed class TasksJsonParserGateTests
{
    // A minimal valid TASKS_JSON with two tasks and a mid-plan gate.
    private const string ValidGateJson = """
        TASKS_JSON:
        {
          "groupId": "PLANS-20260101",
          "groupTitle": "Gate test plan",
          "branch": "feature/gates",
          "summary": "Testing gates",
          "tasks": [
            { "id": "PLANS-20260101-001", "title": "First task", "description": "Do first thing", "dependsOn": [], "priority": "high" },
            { "id": "PLANS-20260101-002", "title": "Second task", "description": "Do second thing", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" },
            { "id": "PLANS-20260101-003", "title": "Third task", "description": "Do third thing", "dependsOn": ["PLANS-20260101-002"], "priority": "mid" }
          ],
          "approvalGates": [
            { "gateId": "PLANS-20260101-GATE-001", "message": "Review before third", "afterTaskIds": ["PLANS-20260101-002"], "beforeTaskIds": ["PLANS-20260101-003"] }
          ]
        }
        """;

    [Test]
    public void ValidGates_ParseSuccessfully()
    {
        var ok = TasksJsonParser.TryParse(ValidGateJson, out var group);
        Assert.Multiple(() =>
        {
            Assert.That(ok,    Is.True);
            Assert.That(group, Is.Not.Null);
            Assert.That(group!.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(group.ApprovalGates![0].GateId, Is.EqualTo("PLANS-20260101-GATE-001"));
        });
    }

    [Test]
    public void PlanSchema_AllowsTrailingCommasAndCommentsWithoutRelaxingRequiredFields()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Tolerant plan",
              "branch": "feature/tolerant-plan",
              "summary": "Accept harmless JSON formatting variations.",
              "tasks": [
                {
                  "id": "PLANS-20260101-001",
                  "title": "Implement feature",
                  "description": "Implement the complete feature.",
                  "dependsOn": [],
                  "priority": "high",
                },
              ], // comments and trailing commas are representation-only
            }
            """;

        Assert.That(TasksJsonParser.TryParse(json, out var group), Is.True);
        Assert.That(group!.Tasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void GateWithEmptyGateId_Fails()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" },
                { "id": "PLANS-20260101-003", "title": "Third", "description": "desc", "dependsOn": ["PLANS-20260101-002"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "", "message": "Oops", "afterTaskIds": ["PLANS-20260101-002"], "beforeTaskIds": ["PLANS-20260101-003"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }

    [Test]
    public void GateWithEmptyMessage_Fails()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" },
                { "id": "PLANS-20260101-003", "title": "Third", "description": "desc", "dependsOn": ["PLANS-20260101-002"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "PLANS-20260101-GATE-001", "message": "", "afterTaskIds": ["PLANS-20260101-002"], "beforeTaskIds": ["PLANS-20260101-003"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }

    [Test]
    public void GateWithDuplicateGateId_Fails()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" },
                { "id": "PLANS-20260101-003", "title": "Third", "description": "desc", "dependsOn": ["PLANS-20260101-002"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "PLANS-20260101-GATE-001", "message": "First gate", "afterTaskIds": ["PLANS-20260101-001"], "beforeTaskIds": ["PLANS-20260101-002"] },
                { "gateId": "PLANS-20260101-GATE-001", "message": "Duplicate", "afterTaskIds": ["PLANS-20260101-002"], "beforeTaskIds": ["PLANS-20260101-003"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }

    [Test]
    public void GateWithInvalidAfterTaskIdRef_Fails()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" },
                { "id": "PLANS-20260101-003", "title": "Third", "description": "desc", "dependsOn": ["PLANS-20260101-002"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "PLANS-20260101-GATE-001", "message": "Gate", "afterTaskIds": ["PLANS-20260101-999"], "beforeTaskIds": ["PLANS-20260101-003"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }

    [Test]
    public void GateWithInvalidBeforeTaskIdRef_Fails()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" },
                { "id": "PLANS-20260101-003", "title": "Third", "description": "desc", "dependsOn": ["PLANS-20260101-002"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "PLANS-20260101-GATE-001", "message": "Gate", "afterTaskIds": ["PLANS-20260101-002"], "beforeTaskIds": ["PLANS-20260101-999"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }

    [Test]
    public void BeforeFirstStepGate_IsRejected()
    {
        // Gate with no afterTaskIds and beforeTaskIds containing only root tasks.
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "PLANS-20260101-GATE-001", "message": "Before first", "beforeTaskIds": ["PLANS-20260101-001"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }

    [Test]
    public void AfterFinalStepGate_IsRejected()
    {
        // Gate with no beforeTaskIds and afterTaskIds containing only leaf tasks.
        var json = """
            TASKS_JSON:
            {
              "groupId": "PLANS-20260101",
              "groupTitle": "Plan",
              "branch": "feature/gates",
              "summary": "Test",
              "tasks": [
                { "id": "PLANS-20260101-001", "title": "First", "description": "desc", "dependsOn": [], "priority": "high" },
                { "id": "PLANS-20260101-002", "title": "Second", "description": "desc", "dependsOn": ["PLANS-20260101-001"], "priority": "mid" }
              ],
              "approvalGates": [
                { "gateId": "PLANS-20260101-GATE-001", "message": "After last", "afterTaskIds": ["PLANS-20260101-002"] }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(json, out _), Is.False);
    }
}

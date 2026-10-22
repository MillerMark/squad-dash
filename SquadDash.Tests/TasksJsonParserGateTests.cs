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
            { "gateId": "PLANS-20260101-GATE-001", "message": "Review before third", "question": "Does the completed work meet the stated contract before task three begins?", "afterTaskIds": ["PLANS-20260101-002"], "beforeTaskIds": ["PLANS-20260101-003"] }
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
    public void GateDependency_ParsesAsExplicitReviewSequence()
    {
        var json = ValidGateJson.Replace(
            "\"message\": \"Review before third\"",
            "\"message\": \"Review before third\", \"dependsOnGateIds\": []",
            StringComparison.Ordinal);

        Assert.That(TasksJsonParser.TryParse(json, out var group, out var diagnostic),
            Is.True, diagnostic?.Message);
        Assert.That(group!.ApprovalGates![0].DependsOnGateIds, Is.Empty);
    }

    [Test]
    public void GateDependency_UnknownGateFailsWithSpecificDiagnostic()
    {
        var json = ValidGateJson.Replace(
            "\"message\": \"Review before third\"",
            "\"message\": \"Review before third\", \"dependsOnGateIds\": [\"PLANS-20260101-GATE-999\"]",
            StringComparison.Ordinal);

        Assert.That(TasksJsonParser.TryParse(json, out _, out var diagnostic), Is.False);
        Assert.That(diagnostic?.Code, Is.EqualTo("invalid-gate-dependency"));
    }

    [Test]
    public void GateDependency_CycleFailsWithSpecificDiagnostic()
    {
        var json = ValidGateJson.Replace(
            "{ \"gateId\": \"PLANS-20260101-GATE-001\", \"message\": \"Review before third\", \"question\": \"Does the completed work meet the stated contract before task three begins?\", \"afterTaskIds\": [\"PLANS-20260101-002\"], \"beforeTaskIds\": [\"PLANS-20260101-003\"] }",
            "{ \"gateId\": \"PLANS-20260101-GATE-001\", \"message\": \"First review\", \"question\": \"Does the first review pass?\", \"afterTaskIds\": [\"PLANS-20260101-002\"], \"beforeTaskIds\": [\"PLANS-20260101-003\"], \"dependsOnGateIds\": [\"PLANS-20260101-GATE-002\"] },\n" +
            "            { \"gateId\": \"PLANS-20260101-GATE-002\", \"message\": \"Second review\", \"question\": \"Does the second review pass?\", \"afterTaskIds\": [\"PLANS-20260101-002\"], \"beforeTaskIds\": [\"PLANS-20260101-003\"], \"dependsOnGateIds\": [\"PLANS-20260101-GATE-001\"] }",
            StringComparison.Ordinal);

        Assert.That(TasksJsonParser.TryParse(json, out _, out var diagnostic), Is.False);
        Assert.That(diagnostic?.Code, Is.EqualTo("gate-dependency-cycle"));
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
    public void TaskContractExamples_ParseAsFirstClassNormativeContract()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "EXAMPLES-20260911",
              "groupTitle": "Contract examples",
              "branch": "feature/contract-examples",
              "summary": "Preserve an exact transformation contract.",
              "tasks": [{
                "id": "EXAMPLES-20260911-001",
                "title": "Transform Razor source",
                "description": "Apply the approved transformation.",
                "contractExamples": [{
                  "exampleId": "replace-code-block",
                  "title": "Replace the first code block",
                  "input": "@code { private int oldCount; }",
                  "expectedBehavior": "@code { private int newCount; }",
                  "language": "razor"
                }],
                "dependsOn": [],
                "priority": "high"
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(json, out var group, out var diagnostic),
            Is.True, diagnostic?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(group!.Tasks[0].ContractExamples, Has.Count.EqualTo(1));
            Assert.That(group.Tasks[0].ContractExamples![0].ExpectedBehavior,
                Is.EqualTo("@code { private int newCount; }"));
        });
    }

    [Test]
    public void TaskContractExamples_ReportAllDefectsInTheAggregateRepairDiagnostic()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "EXAMPLES-20260911",
              "groupTitle": "Contract examples",
              "branch": "feature/contract-examples",
              "summary": "Reject incomplete normative examples.",
              "tasks": [{
                "id": "EXAMPLES-20260911-001",
                "title": "Transform Razor source",
                "description": "Apply the approved transformation.",
                "contractExamples": [{
                  "exampleId": "replace-code-block",
                  "input": "before"
                }],
                "dependsOn": [],
                "priority": "high"
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(json, out _, out var diagnostic), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic?.Code, Is.EqualTo("missing-contract-example-title"));
            Assert.That(diagnostic?.Message, Does.Contain("missing-contract-example-title"));
            Assert.That(diagnostic?.Message, Does.Contain("missing-contract-example-expected-behavior"));
            Assert.That(diagnostic?.Message, Does.Contain("2 independent schema problems were found"));
        });
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
    public void GateWithoutReviewerQuestion_Fails()
    {
        var json = ValidGateJson.Replace(
            "\"question\": \"Does the completed work meet the stated contract before task three begins?\", ",
            string.Empty,
            StringComparison.Ordinal);

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

    [Test]
    public void ProofBearingPlan_WithSuppliedIncorrectFinalAuditAfterTaskIds_IsRejected()
    {
        // A supplied audit whose AfterTaskIds does not match the actual leaf set must be rejected
        // with a validation error — never silently repaired.  Reconciliation is only permitted
        // for authorized editor mutations (DependencyEditRecomputer / StagedPlanEditor.DeleteTask).
        //
        // Plan topology: 001 (root) → 002 (leaf).  Leaf set = {002}.
        // Audit AfterTaskIds = {001}  (wrong — 001 is non-leaf because 002 depends on it).
        var json = """
            TASKS_JSON:
            {
              "groupId": "SANDBOX-20260818",
              "groupTitle": "Party planning regression test",
              "branch": "feature/party-planning",
              "summary": "Verifies that TryParse rejects an incorrect audit boundary.",
              "tasks": [
                {
                  "id": "SANDBOX-20260818-001",
                  "title": "Set the date",
                  "description": "Pick a suitable date for the party.",
                  "dependsOn": [],
                  "priority": "high",
                  "proofRequirements": [
                    { "requirementId": "date-set", "proofType": "automated-test", "description": "Date is persisted in the system." }
                  ]
                },
                {
                  "id": "SANDBOX-20260818-002",
                  "title": "Set the budget",
                  "description": "Define the party budget.",
                  "dependsOn": ["SANDBOX-20260818-001"],
                  "priority": "high"
                }
              ],
              "validations": [
                {
                  "validationId": "SANDBOX-20260818-VAL-001",
                  "title": "Final audit",
                  "description": "Audit all leaf tasks.",
                  "afterTaskIds": ["SANDBOX-20260818-001"],
                  "beforeTaskIds": [],
                  "assertions": ["All tasks have been completed."],
                  "mode": "audit"
                }
              ]
            }
            """;

        var ok = TasksJsonParser.TryParse(json, out _, out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False,
                "A supplied final audit with incorrect AfterTaskIds must be rejected, not silently repaired");
            Assert.That(diagnostic?.Code, Is.EqualTo("invalid-proof-completion-audit"),
                "The rejection diagnostic code must identify the audit boundary violation");
        });
    }
}

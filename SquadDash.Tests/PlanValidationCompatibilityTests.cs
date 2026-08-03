using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Round-trip, backward-compatibility, and lifecycle tests for validation nodes and task
/// outputs/inputs. Ensures plans persist correctly with and without validation nodes.
/// </summary>
[TestFixture]
internal sealed class PlanValidationCompatibilityTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─── Backward Compatibility ──────────────────────────────────────────────────

    [Test]
    public void LegacyPlan_WithoutValidations_DeserializesCorrectly()
    {
        var json = """
            {
              "planId": "LEGACY-20260801",
              "revision": "abc123",
              "source": "tasks_json",
              "lifecycleStatus": "executing",
              "title": "Legacy Plan",
              "branch": "feature/legacy",
              "summary": "A plan without validation nodes.",
              "tasks": [
                { "taskId": "LEGACY-20260801-001", "title": "Task A", "description": "Do A", "dependsOn": [], "priority": "high", "status": "complete" },
                { "taskId": "LEGACY-20260801-002", "title": "Task B", "description": "Do B", "dependsOn": ["LEGACY-20260801-001"], "priority": "high", "status": "pending" }
              ],
              "approvalGates": [],
              "progress": { "completedCount": 1, "totalCount": 2, "executingTaskId": null },
              "timestamps": { "createdAt": "2026-08-01T00:00:00Z" }
            }
            """;

        var plan = JsonSerializer.Deserialize<Plan>(json, ReadOptions);

        Assert.Multiple(() =>
        {
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan!.PlanId, Is.EqualTo("LEGACY-20260801"));
            Assert.That(plan.Validations, Is.Null);
            Assert.That(plan.Tasks, Has.Count.EqualTo(2));
            Assert.That(plan.Tasks[0].Outputs, Is.Null);
            Assert.That(plan.Tasks[1].Inputs, Is.Null);
        });
    }

    [Test]
    public void LegacyPlan_RoundTrips_WithNullValidations()
    {
        var plan = new Plan(
            "LEGACY-20260801", "rev1", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Legacy", "feature/legacy", "Summary",
            [new PlanTask("T1", "Task", "Desc", [], "high", PlanTaskStatus.Complete)],
            [],
            new PlanProgress(1, 1), new PlanTimestamps(DateTimeOffset.UtcNow));

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var deserialized = JsonSerializer.Deserialize<Plan>(json, ReadOptions);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Validations, Is.Null);
            Assert.That(json, Does.Not.Contain("validations"));
        });
    }

    [Test]
    public void PlanWithValidations_RoundTrips_PreservingAllFields()
    {
        var validation = new PlanValidationNode(
            "PLAN-VAL-001", "Verify Contract", "Verify A produces what B consumes.",
            ["T1"], ["T2"],
            ["A produces the declared output."],
            ["contract-a"],
            "evidence", ["dotnet test"], true,
            PlanValidationStatus.Pending);
        var plan = new Plan(
            "PLAN-20260803", "rev1", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary",
            [
                new PlanTask("T1", "Create A", "Create", [], "high", PlanTaskStatus.Complete,
                    Outputs: [new PlanTaskOutput("contract-a", "Public contract")]),
                new PlanTask("T2", "Use A", "Consume", ["T1"], "high", PlanTaskStatus.Pending,
                    Inputs: ["contract-a"]),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var deserialized = JsonSerializer.Deserialize<Plan>(json, ReadOptions);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Validations, Has.Count.EqualTo(1));
            var v = deserialized.Validations![0];
            Assert.That(v.ValidationId, Is.EqualTo("PLAN-VAL-001"));
            Assert.That(v.Title, Is.EqualTo("Verify Contract"));
            Assert.That(v.AfterTaskIds, Is.EqualTo(new[] { "T1" }));
            Assert.That(v.BeforeTaskIds, Is.EqualTo(new[] { "T2" }));
            Assert.That(v.Assertions, Is.EqualTo(new[] { "A produces the declared output." }));
            Assert.That(v.OutputIds, Is.EqualTo(new[] { "contract-a" }));
            Assert.That(v.Mode, Is.EqualTo("evidence"));
            Assert.That(v.Commands, Is.EqualTo(new[] { "dotnet test" }));
            Assert.That(v.RevalidateAtCompletion, Is.True);
            Assert.That(v.Status, Is.EqualTo(PlanValidationStatus.Pending));
            Assert.That(deserialized.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(deserialized.Tasks[0].Outputs![0].OutputId, Is.EqualTo("contract-a"));
            Assert.That(deserialized.Tasks[1].Inputs, Is.EqualTo(new[] { "contract-a" }));
        });
    }

    // ─── Auto Readiness via ApplyStepAccepted ────────────────────────────────────

    [Test]
    public void ApplyStepAccepted_TransitionsPendingValidationToReady_WhenPrereqsComplete()
    {
        var validation = new PlanValidationNode(
            "V1", "Validate", "Validate A", ["A"], ["B"],
            ["A is correct."], [], "evidence", [], true,
            PlanValidationStatus.Pending);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Pending),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(0, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        // Simulate task A completing
        var items = new[]
        {
            MakeItem("A", isChecked: true),
            MakeItem("B"),
        };
        var result = PlanStoreUpdater.ApplyStepAccepted(plan, items, "B");

        Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
    }

    [Test]
    public void ApplyStepAccepted_DoesNotAffectPlan_WithoutValidations()
    {
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Pending)],
            [],
            new PlanProgress(0, 1), new PlanTimestamps(DateTimeOffset.UtcNow));

        var items = new[] { MakeItem("A", isChecked: true) };
        var result = PlanStoreUpdater.ApplyStepAccepted(plan, items, null);

        Assert.That(result.Validations, Is.Null);
    }

    [Test]
    public void ApplyStepAccepted_LeavesValidationPending_WhenPrereqsIncomplete()
    {
        var validation = new PlanValidationNode(
            "V1", "Validate", "Validate A and B", ["A", "B"], ["C"],
            ["Both A and B are correct."], [], "evidence", [], true,
            PlanValidationStatus.Pending);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Pending),
                new PlanTask("B", "B", "B", [], "high", PlanTaskStatus.Pending),
                new PlanTask("C", "C", "C", ["A", "B"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(0, 3), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        // Only A completes — B still pending
        var items = new[]
        {
            MakeItem("A", isChecked: true),
            MakeItem("B"),
            MakeItem("C"),
        };
        var result = PlanStoreUpdater.ApplyStepAccepted(plan, items, "B");

        Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Pending));
    }

    // ─── Full Validation Lifecycle ───────────────────────────────────────────────

    [Test]
    public void FullValidationLifecycle_PendingToReadyToValidatingToPassed()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify integration", ["A"], ["B"],
            ["Integration works."], [], "evidence", [], true,
            PlanValidationStatus.Pending);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        // Auto-ready via ApplyReadyValidations (called by production consumer)
        plan = PlanStoreUpdater.ApplyReadyValidations(plan);
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));

        // Started
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Validating));
        Assert.That(plan.Validations![0].StartedAt, Is.Not.Null);

        // Passed
        plan = PlanStoreUpdater.ApplyValidationResult(plan, "V1", true, "All good", ["evidence1"], "abc1234");
        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
            Assert.That(plan.Validations![0].CompletedAt, Is.Not.Null);
            Assert.That(plan.Validations![0].ValidatedCommit, Is.EqualTo("abc1234"));
            Assert.That(plan.Validations![0].Evidence, Is.EqualTo(new[] { "evidence1" }));
            Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.True);
        });
    }

    [Test]
    public void StaleValidation_TransitionsBackToReady_WhenPrereqsStillComplete()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify", ["A"], ["B"],
            ["Works."], [], "evidence", [], true,
            PlanValidationStatus.Stale,
            Summary: "Stale because of rework.");
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var result = PlanStoreUpdater.ApplyReadyValidations(plan);
        Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
    }

    [Test]
    public void ApplyValidationStale_TransitionsPassedToStale()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify", ["A"], ["B"],
            ["Works."], [], "evidence", [], true,
            PlanValidationStatus.Passed,
            Summary: "Integration passed.",
            Evidence: ["test passed"]);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var result = PlanStoreUpdater.ApplyValidationStale(plan, "V1", "Prerequisite task was reworked.");
        Assert.Multiple(() =>
        {
            Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));
            Assert.That(result.Validations![0].Summary, Is.EqualTo("Prerequisite task was reworked."));
        });
    }

    // ─── Revision Hashing ────────────────────────────────────────────────────────

    [Test]
    public void RevisionHash_IncorporatesValidationNodes()
    {
        var baseGroup = new DecomposedTaskGroup(
            "PLAN-20260803", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260803-001", "Do A", [], "high", "Task A")]);
        var groupWithValidation = baseGroup with
        {
            Validations = [new DecomposedValidationNode(
                "PLAN-20260803-VAL-001", "Validate", "Validate A",
                ["PLAN-20260803-001"], [], ["A works."])]
        };

        var rev1 = PendingDecomposePlanStore.ComputeRevision(baseGroup);
        var rev2 = PendingDecomposePlanStore.ComputeRevision(groupWithValidation);

        Assert.That(rev1, Is.Not.EqualTo(rev2));
    }

    [Test]
    public void RevisionHash_IsDeterministic_ForSameValidationDefinition()
    {
        var group = new DecomposedTaskGroup(
            "PLAN-20260803", "Plan", "feature/plan", "Summary",
            [new DecomposedSubTask("PLAN-20260803-001", "Do A", [], "high", "Task A")],
            Validations: [new DecomposedValidationNode(
                "PLAN-20260803-VAL-001", "Validate", "Validate A",
                ["PLAN-20260803-001"], [], ["A works."])]);

        var rev1 = PendingDecomposePlanStore.ComputeRevision(group);
        var rev2 = PendingDecomposePlanStore.ComputeRevision(group);

        Assert.That(rev1, Is.EqualTo(rev2));
    }

    // ─── Readiness Evaluator Edge Cases ──────────────────────────────────────────

    [Test]
    public void Evaluator_IgnoresAlreadyPassedValidations()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify", ["A"], ["B"],
            ["Works."], [], "evidence", [], true,
            PlanValidationStatus.Passed);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var result = PlanStoreUpdater.ApplyReadyValidations(plan);
        // Already passed — should not change
        Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
    }

    [Test]
    public void Evaluator_HandlesSupersededTasksAsTerminal()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify", ["A"], ["B"],
            ["Works."], [], "evidence", [], true,
            PlanValidationStatus.Pending);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "P", "b", "S",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Superseded),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var result = PlanStoreUpdater.ApplyReadyValidations(plan);
        Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static TaskItem MakeItem(string taskId, bool isChecked = false, bool isSuperseded = false) =>
        new("", null, false, isChecked, "", "", TaskId: taskId, IsSuperseded: isSuperseded);
}

using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanValidationNodeTests
{
    private static Plan MakePlan(string validationStatus = PlanValidationStatus.Pending)
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "B", [], "high", PlanTaskStatus.Complete),
            new PlanTask("C", "C", "C", ["A", "B"], "high", PlanTaskStatus.Pending),
            new PlanTask("D", "D", "D", ["C"], "high", PlanTaskStatus.Pending),
        };
        var validation = new PlanValidationNode(
            "PLAN-VAL-001", "Validate A and B", "Cross-task contract", ["A", "B"], ["C"],
            ["A reaches B through the declared boundary."], [], "evidence", [], true, validationStatus);
        return new Plan(
            "PLAN", "revision", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary", tasks, [],
            new PlanProgress(2, 4), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    [Test]
    public void Evaluate_CompletedPrerequisites_MakesValidationReadyAndBlocksFrontier()
    {
        var plan = MakePlan();

        var state = PlanValidationReadinessEvaluator.Evaluate(plan).Single();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsReady, Is.True);
            Assert.That(state.DownstreamFrontier, Is.EquivalentTo(new[] { "C", "D" }));
            Assert.That(PlanValidationReadinessEvaluator.SelectNextReady(plan)?.ValidationId,
                Is.EqualTo("PLAN-VAL-001"));
            Assert.That(PlanValidationReadinessEvaluator.ComputeAllBlockedTaskIds(plan),
                Is.EquivalentTo(new[] { "C", "D" }));
        });
    }

    [Test]
    public void PassedValidation_ReleasesDownstreamFrontier()
    {
        var plan = MakePlan(PlanValidationStatus.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(PlanValidationReadinessEvaluator.SelectNextReady(plan), Is.Null);
            Assert.That(PlanValidationReadinessEvaluator.ComputeAllBlockedTaskIds(plan), Is.Empty);
            Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.True);
        });
    }

    [Test]
    public void StoreUpdater_PersistsValidationLifecycleAndEvidence()
    {
        var plan = MakePlan();
        plan = PlanStoreUpdater.ApplyValidationReady(plan, "PLAN-VAL-001");
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "PLAN-VAL-001");
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "PLAN-VAL-001", true, "Connected", ["integration test passed"], "abc1234");

        var validation = plan.Validations!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validation.Status, Is.EqualTo(PlanValidationStatus.Passed));
            Assert.That(validation.ValidatedCommit, Is.EqualTo("abc1234"));
            Assert.That(validation.Evidence, Is.EqualTo(new[] { "integration test passed" }));
            Assert.That(validation.CompletedAt, Is.Not.Null);
        });
    }

    [Test]
    public void TasksJsonParser_AcceptsFirstClassValidationNode()
    {
        const string response = """
            TASKS_JSON:
            {
              "groupId":"VALIDATION-20260803",
              "groupTitle":"Validation plan",
              "branch":"feature/validation",
              "summary":"Validate cross-task integration.",
              "tasks":[
                {"id":"VALIDATION-20260803-001","title":"Create A","description":"Create A.","dependsOn":[],"priority":"high","agentRoutingMode":"generic","genericAgentReason":"Fixture.","outputs":[{"outputId":"component-a-contract","description":"The public contract produced by A."}]},
                {"id":"VALIDATION-20260803-002","title":"Integrate A","description":"Integrate A.","dependsOn":["VALIDATION-20260803-001"],"priority":"high","agentRoutingMode":"generic","genericAgentReason":"Fixture.","inputs":["component-a-contract"]}
              ],
              "validations":[{
                "validationId":"VALIDATION-20260803-VAL-001",
                "title":"Verify integration",
                "description":"Verify A is consumed.",
                "afterTaskIds":["VALIDATION-20260803-001"],
                "beforeTaskIds":["VALIDATION-20260803-002"],
                "assertions":["A is consumed by the integration path."],
                "outputIds":["component-a-contract"],
                "mode":"evidence",
                "revalidateAtCompletion":true
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(response, out var group), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(group!.Validations, Has.Count.EqualTo(1));
            Assert.That(group.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(group.Tasks[1].Inputs, Is.EqualTo(new[] { "component-a-contract" }));
        });
    }

    [Test]
    public void PendingAdapter_RoundTripsValidationAndRevision()
    {
        var validation = new DecomposedValidationNode(
            "VALIDATION-20260803-VAL-001", "Verify", "Verify integration",
            ["VALIDATION-20260803-001"], ["VALIDATION-20260803-002"], ["A is consumed."],
            OutputIds: ["component-a-contract"],
            Mode: "evidence");
        var group = new DecomposedTaskGroup(
            "VALIDATION-20260803", "Validation", "feature/validation", "Summary",
            [
                new DecomposedSubTask("VALIDATION-20260803-001", "Create", [], "high", "Create",
                    Outputs: [new DecomposedTaskOutput("component-a-contract", "Contract A")]),
                new DecomposedSubTask("VALIDATION-20260803-002", "Integrate", ["VALIDATION-20260803-001"], "high", "Integrate",
                    Inputs: ["component-a-contract"]),
            ],
            Validations: [validation]);
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var durable = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        var roundTrip = PendingDecomposePlanAdapter.FromPlan(durable);

        Assert.Multiple(() =>
        {
            Assert.That(durable.Validations, Has.Count.EqualTo(1));
            Assert.That(durable.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(durable.Tasks[1].Inputs, Is.EqualTo(new[] { "component-a-contract" }));
            Assert.That(roundTrip.Group.Validations, Has.Count.EqualTo(1));
            Assert.That(PendingDecomposePlanStore.ComputeRevision(roundTrip.Group), Is.EqualTo(revision));
        });
    }

    [Test]
    public void TasksJsonParser_RejectsInputWithoutDependencyOnItsProducer()
    {
        const string response = """
            TASKS_JSON:
            {
              "groupId":"VALIDATION-20260803",
              "groupTitle":"Invalid handoff",
              "branch":"feature/invalid-handoff",
              "summary":"The consumer is not ordered after its producer.",
              "tasks":[
                {"id":"VALIDATION-20260803-001","title":"Produce","description":"Produce.","dependsOn":[],"priority":"high","agentRoutingMode":"generic","genericAgentReason":"Fixture.","outputs":[{"outputId":"shared-contract","description":"A shared contract."}]},
                {"id":"VALIDATION-20260803-002","title":"Consume","description":"Consume.","dependsOn":[],"priority":"high","agentRoutingMode":"generic","genericAgentReason":"Fixture.","inputs":["shared-contract"]}
              ]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(response, out _), Is.False);
    }
}

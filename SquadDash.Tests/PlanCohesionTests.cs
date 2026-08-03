using System.Text.Json;

namespace SquadDash.Tests;

/// <summary>
/// Tests for cohesion-aware plan generation, parsing, and validation.
/// Covers: observable outcomes, production consumers, artifact-only rejection,
/// tailored final proof, round-trip through Inbox, and backward compatibility.
/// </summary>
[TestFixture]
internal sealed class PlanCohesionTests
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

    // ─── Observable Outcome Detection ────────────────────────────────────────────

    [TestCase("Observable outcome: the build succeeds with the new module linked.", ExpectedResult = true)]
    [TestCase("Users can filter by date range in the search panel.", ExpectedResult = true)]
    [TestCase("The test suite passes with all integration scenarios green.", ExpectedResult = true)]
    [TestCase("Add a helper class for search utilities.", ExpectedResult = false)]
    [TestCase("Introduce ISearchIndex.", ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    public bool HasObservableOutcome_DetectsCorrectly(string description) =>
        PlanCohesionValidator.HasObservableOutcome(description);

    // ─── Production Consumer Detection ───────────────────────────────────────────

    [TestCase("SearchPanel calls ISearchIndex.Query and renders results.", ExpectedResult = true)]
    [TestCase("Production consumer: task 003 wires the abstraction into SearchPanel.", ExpectedResult = true)]
    [TestCase("The adapter is integrated into MainWindow.HandleEvent.", ExpectedResult = true)]
    [TestCase("Introduce ISearchIndex and its in-memory implementation.", ExpectedResult = false)]
    [TestCase("Add unit tests for the parser.", ExpectedResult = false)]
    public bool HasProductionConsumer_DetectsCorrectly(string description) =>
        PlanCohesionValidator.HasProductionConsumer(description);

    // ─── Artifact-Only Rejection ─────────────────────────────────────────────────

    [TestCase("Add a helper class.", ExpectedResult = true)]
    [TestCase("Create a utility for date formatting.", ExpectedResult = true)]
    [TestCase("Write unit tests.", ExpectedResult = true)]
    [TestCase("Add a helper class. Observable outcome: the build succeeds.", ExpectedResult = false)]
    [TestCase("Create a utility that SearchPanel calls for rendering.", ExpectedResult = false)]
    [TestCase("SearchPanel calls ISearchIndex and renders results.", ExpectedResult = false)]
    public bool IsArtifactOnly_DetectsCorrectly(string description) =>
        PlanCohesionValidator.IsArtifactOnly(description);

    // ─── Tailored Final Proof ────────────────────────────────────────────────────

    [Test]
    public void TailoredFinalProof_AcceptsCohesiveDescription()
    {
        var task = new DecomposedSubTask(
            "PLAN-20260803-003",
            "End-to-end proof: run `dotnet test --filter SearchIntegration` and confirm the test " +
            "exercises SearchPanel → ISearchIndex → results.",
            [], "mid", "Verify end-to-end search works");
        Assert.That(PlanCohesionValidator.HasTailoredFinalProof(task), Is.True);
    }

    [Test]
    public void TailoredFinalProof_RejectsGenericDocReminder()
    {
        var task = new DecomposedSubTask(
            "PLAN-20260803-003", "Update documentation",
            [], "low", "Update documentation");
        Assert.That(PlanCohesionValidator.HasTailoredFinalProof(task), Is.False);
    }

    [Test]
    public void TailoredFinalProof_RejectsGenericTestReminder()
    {
        var task = new DecomposedSubTask(
            "PLAN-20260803-003", "Run the test suite",
            [], "low", "Run the test suite");
        Assert.That(PlanCohesionValidator.HasTailoredFinalProof(task), Is.False);
    }

    [Test]
    public void TailoredFinalProof_AcceptsObservableOutcomeFinalStep()
    {
        var task = new DecomposedSubTask(
            "PLAN-20260803-003",
            "Observable outcome: typing a query in the search panel returns matching documents. " +
            "Run `dotnet test --filter SearchIntegration` to prove it.",
            [], "mid", "Verify search integration");
        Assert.That(PlanCohesionValidator.HasTailoredFinalProof(task), Is.True);
    }

    // ─── Full Cohesion Validation ────────────────────────────────────────────────

    [Test]
    public void Validate_CohesivePlan_ReturnsNoIssues()
    {
        var group = MakeCohesiveGroup();
        var issues = PlanCohesionValidator.Validate(group);
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_ArtifactOnlyPlan_ReportsIssues()
    {
        var group = new DecomposedTaskGroup(
            "PLAN-20260803", "Plan", "feature/plan", "Summary",
            [
                new DecomposedSubTask("PLAN-20260803-001", "Add a helper class.", [], "high", "Add helper"),
                new DecomposedSubTask("PLAN-20260803-002", "Write unit tests.", ["PLAN-20260803-001"], "mid", "Write tests"),
            ]);
        var issues = PlanCohesionValidator.Validate(group);
        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(issues, Has.Some.Contain("artifact-only"));
            Assert.That(issues, Has.Some.Contain("tailored end-to-end proof"));
        });
    }

    [Test]
    public void Validate_GenericFinalStep_ReportsIssue()
    {
        var group = new DecomposedTaskGroup(
            "PLAN-20260803", "Plan", "feature/plan", "Summary",
            [
                new DecomposedSubTask("PLAN-20260803-001",
                    "Observable outcome: the abstraction is testable. Production consumer: task 002 calls it.",
                    [], "high", "Create abstraction"),
                new DecomposedSubTask("PLAN-20260803-002",
                    "Update documentation.",
                    ["PLAN-20260803-001"], "low", "Update documentation"),
            ]);
        var issues = PlanCohesionValidator.Validate(group);
        Assert.That(issues, Has.Some.Contain("tailored end-to-end proof"));
    }

    [Test]
    public void IsValid_CohesivePlan_ReturnsTrue()
    {
        Assert.That(PlanCohesionValidator.IsValid(MakeCohesiveGroup()), Is.True);
    }

    // ─── Parser Accepts Cohesion Fields ──────────────────────────────────────────

    [Test]
    public void Parser_ParsesCohesiveTasksJson_WithOutputsInputsValidations()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "COHESION-20260803",
              "groupTitle": "Cohesion Test",
              "branch": "feature/cohesion",
              "summary": "Test cohesion-aware fields.",
              "tasks": [
                {
                  "id": "COHESION-20260803-001",
                  "title": "Create search index",
                  "description": "Observable outcome: tests pass. Production consumer: task 002 calls ISearchIndex.",
                  "dependsOn": [],
                  "priority": "high",
                  "outputs": [{ "outputId": "search-index", "description": "Search index abstraction." }],
                  "agentRoutingMode": "generic",
                  "genericAgentReason": "No roster specialist for search."
                },
                {
                  "id": "COHESION-20260803-002",
                  "title": "Wire search into UI and verify end-to-end",
                  "description": "Observable outcome: search panel returns results via ISearchIndex. End-to-end proof: dotnet test confirms the wiring.",
                  "dependsOn": ["COHESION-20260803-001"],
                  "priority": "mid",
                  "inputs": ["search-index"],
                  "agentRoutingMode": "generic",
                  "genericAgentReason": "No roster specialist for UI integration."
                }
              ],
              "validations": [
                {
                  "validationId": "COHESION-20260803-VAL-001",
                  "title": "Verify search wiring",
                  "description": "The search UI reaches document indexing through ISearchIndex.",
                  "afterTaskIds": ["COHESION-20260803-001", "COHESION-20260803-002"],
                  "beforeTaskIds": [],
                  "assertions": ["SearchPanel calls ISearchIndex.Query.", "Old direct path is removed."],
                  "outputIds": ["search-index"],
                  "mode": "evidence",
                  "revalidateAtCompletion": true
                }
              ]
            }
            """;

        var ok = TasksJsonParser.TryParse(json, out var group);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(group, Is.Not.Null);
            Assert.That(group!.Tasks, Has.Count.EqualTo(2));
            Assert.That(group.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(group.Tasks[0].Outputs![0].OutputId, Is.EqualTo("search-index"));
            Assert.That(group.Tasks[1].Inputs, Has.Count.EqualTo(1));
            Assert.That(group.Tasks[1].Inputs![0], Is.EqualTo("search-index"));
            Assert.That(group.Validations, Has.Count.EqualTo(1));
            Assert.That(group.Validations![0].Assertions, Has.Count.EqualTo(2));
        });
    }

    // ─── Backward Compatibility ──────────────────────────────────────────────────

    [Test]
    public void Parser_AcceptsLegacyPlan_WithoutCohesionFields()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId": "LEGACY-20260803",
              "groupTitle": "Legacy Plan",
              "branch": "feature/legacy",
              "summary": "A plan without cohesion fields.",
              "tasks": [
                { "id": "LEGACY-20260803-001", "title": "Task A", "description": "Do A", "dependsOn": [], "priority": "high" },
                { "id": "LEGACY-20260803-002", "title": "Task B", "description": "Do B", "dependsOn": ["LEGACY-20260803-001"], "priority": "mid" }
              ]
            }
            """;

        var ok = TasksJsonParser.TryParse(json, out var group);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(group, Is.Not.Null);
            Assert.That(group!.Tasks, Has.Count.EqualTo(2));
            Assert.That(group.Tasks[0].Outputs, Is.Null);
            Assert.That(group.Tasks[1].Inputs, Is.Null);
            Assert.That(group.Validations, Is.Null);
        });
    }

    // ─── Inbox Round-Trip ────────────────────────────────────────────────────────

    [Test]
    public void CohesionFields_SurviveInboxProposalRoundTrip()
    {
        var group = MakeCohesiveGroup();
        var pending = new PendingDecomposePlan(
            PendingDecomposePlanStore.ComputeRevision(group), group);

        // Build Inbox message
        var message = DecomposePlanInbox.BuildMessage(
            pending, DateTimeOffset.UtcNow, explicitlyRequested: true);

        // Extract from attachment
        var attachment = message.Attachments.First();
        Assert.That(DecomposePlanInbox.TryReadSnapshot(attachment, out var restored), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Group.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(restored.Group.Tasks[0].Outputs![0].OutputId, Is.EqualTo("search-index"));
            Assert.That(restored.Group.Tasks[1].Inputs, Has.Count.EqualTo(1));
            Assert.That(restored.Group.Tasks[1].Inputs![0], Is.EqualTo("search-index"));
            Assert.That(restored.Group.Validations, Has.Count.EqualTo(1));
            Assert.That(restored.Group.Validations![0].ValidationId, Is.EqualTo("COHESION-20260803-VAL-001"));
            Assert.That(restored.Revision, Is.EqualTo(pending.Revision));
        });
    }

    [Test]
    public void CohesionFields_SurvivePendingDecomposePlanAdapterRoundTrip()
    {
        var group = MakeCohesiveGroup();
        var pending = new PendingDecomposePlan(
            PendingDecomposePlanStore.ComputeRevision(group), group);
        var timestamp = DateTimeOffset.UtcNow;

        // Convert to durable Plan
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, timestamp);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(plan.Tasks[0].Outputs![0].OutputId, Is.EqualTo("search-index"));
            Assert.That(plan.Tasks[1].Inputs, Has.Count.EqualTo(1));
            Assert.That(plan.Tasks[1].Inputs![0], Is.EqualTo("search-index"));
            Assert.That(plan.Validations, Has.Count.EqualTo(1));
            Assert.That(plan.Validations![0].ValidationId, Is.EqualTo("COHESION-20260803-VAL-001"));
        });

        // Convert back to PendingDecomposePlan
        var restored = PendingDecomposePlanAdapter.FromPlan(plan);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Group.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(restored.Group.Tasks[1].Inputs, Has.Count.EqualTo(1));
            Assert.That(restored.Group.Validations, Has.Count.EqualTo(1));
        });

        // Verify revision still matches
        Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(plan), Is.True);
    }

    [Test]
    public void CohesionFields_SurviveJsonRoundTrip()
    {
        var group = MakeCohesiveGroup();
        var pending = new PendingDecomposePlan(
            PendingDecomposePlanStore.ComputeRevision(group), group);
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var deserialized = JsonSerializer.Deserialize<Plan>(json, ReadOptions);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(deserialized.Tasks[0].Outputs![0].OutputId, Is.EqualTo("search-index"));
            Assert.That(deserialized.Tasks[0].Outputs![0].Description, Is.EqualTo("Search index abstraction."));
            Assert.That(deserialized.Tasks[1].Inputs, Has.Count.EqualTo(1));
            Assert.That(deserialized.Tasks[1].Inputs![0], Is.EqualTo("search-index"));
            Assert.That(deserialized.Validations, Has.Count.EqualTo(1));
            Assert.That(deserialized.Validations![0].Assertions, Has.Count.EqualTo(2));
        });
    }

    // ─── Prompt Generation ───────────────────────────────────────────────────────

    [Test]
    public void DecomposePlanningInstructions_ContainsCohesionRequirements()
    {
        var spec = DecomposePlanningInstructions.LoadSpecification();
        Assert.Multiple(() =>
        {
            Assert.That(spec, Does.Contain("Observable outcome"));
            Assert.That(spec, Does.Contain("Production consumer"));
            Assert.That(spec, Does.Contain("Artifact-only"));
            Assert.That(spec, Does.Contain("tailored end-to-end proof"));
            Assert.That(spec, Does.Contain("observable outcome"));
            Assert.That(spec, Does.Contain("Production consumer"));
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeCohesiveGroup() => new(
        "COHESION-20260803", "Cohesion Test", "feature/cohesion", "Test plan.",
        [
            new DecomposedSubTask(
                "COHESION-20260803-001",
                "Observable outcome: tests pass proving the in-memory implementation indexes documents. " +
                "Production consumer: task 002 calls ISearchIndex from the existing indexing path.",
                [], "high", "Create search index",
                Outputs: [new DecomposedTaskOutput("search-index", "Search index abstraction.")]),
            new DecomposedSubTask(
                "COHESION-20260803-002",
                "Observable outcome: typing a query in the search panel returns matching documents. " +
                "End-to-end proof: run `dotnet test --filter SearchIntegration` to confirm.",
                ["COHESION-20260803-001"], "mid", "Wire search and verify end-to-end",
                Inputs: ["search-index"]),
        ],
        Validations:
        [
            new DecomposedValidationNode(
                "COHESION-20260803-VAL-001", "Verify search wiring",
                "The search UI reaches document indexing through ISearchIndex.",
                ["COHESION-20260803-001", "COHESION-20260803-002"], [],
                ["SearchPanel calls ISearchIndex.Query.", "Old direct path is removed."],
                OutputIds: ["search-index"]),
        ]);
}

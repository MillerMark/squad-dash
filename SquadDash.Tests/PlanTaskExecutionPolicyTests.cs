namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskExecutionPolicyTests
{
    [Test]
    public void ExplicitVerificationMode_IsNonMutating()
    {
        var task = MakeTask("Run acceptance checks", PlanTaskExecutionMode.Verification);

        Assert.That(PlanTaskExecutionPolicy.IsVerificationOnly(task), Is.True);
    }

    [Test]
    public void ExplicitImplementationMode_WinsOverVerificationLikeTitle()
    {
        var task = MakeTask("End-to-end verification", PlanTaskExecutionMode.Implementation);

        Assert.That(PlanTaskExecutionPolicy.IsVerificationOnly(task), Is.False);
    }

    [Test]
    public void LegacyFinalVerificationTask_IsRecognizedWithoutWeakeningOrdinaryTasks()
    {
        var verification = MakeTask("End-to-end multi-profile verification", executionMode: null);
        var implementation = MakeTask("Implement multi-profile support", executionMode: null);

        Assert.Multiple(() =>
        {
            Assert.That(PlanTaskExecutionPolicy.IsVerificationOnly(verification), Is.True);
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(verification), Is.False);
            Assert.That(PlanTaskExecutionPolicy.IsVerificationOnly(implementation), Is.False);
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(implementation), Is.False);
        });
    }

    [Test]
    public void AdapterRoundTrip_PreservesExecutionMode()
    {
        var group = MakeGroup(MakeTask("Verify feature", PlanTaskExecutionMode.Verification));
        var pending = new PendingDecomposePlan("revision", group);

        var durable = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        var restored = PendingDecomposePlanAdapter.FromPlan(durable);

        Assert.That(restored.Group.Tasks.Single().ExecutionMode,
            Is.EqualTo(PlanTaskExecutionMode.Verification));
    }

    [Test]
    public void VerificationLevel_DefaultsToLightweight_AndRoundTripsExplicitLevels()
    {
        var defaultTask = MakeTask("Implement feature", PlanTaskExecutionMode.Implementation);
        var standardTask = defaultTask with { VerificationLevel = PlanTaskVerificationLevel.Standard };
        var enhancedTask = defaultTask with
        {
            Id = "VERIFY-20260813-002",
            VerificationLevel = PlanTaskVerificationLevel.Enhanced,
        };

        Assert.That(PlanTaskVerificationLevel.Normalize(defaultTask.VerificationLevel),
            Is.EqualTo(PlanTaskVerificationLevel.Lightweight));
        var group = MakeGroup(standardTask) with { Tasks = [standardTask, enhancedTask] };
        var durable = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan("revision", group), DateTimeOffset.UtcNow);
        var restored = PendingDecomposePlanAdapter.FromPlan(durable);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Group.Tasks[0].VerificationLevel,
                Is.EqualTo(PlanTaskVerificationLevel.Standard));
            Assert.That(restored.Group.Tasks[1].VerificationLevel,
                Is.EqualTo(PlanTaskVerificationLevel.Enhanced));
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(defaultTask), Is.False);
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(standardTask), Is.True);
        });
    }

    [Test]
    public void Lightweight_SkipsSeparateReview_UnlessHostDetectsElevatedRisk()
    {
        var lightweight = MakeTask("Implement feature", PlanTaskExecutionMode.Implementation) with
        {
            VerificationLevel = PlanTaskVerificationLevel.Lightweight,
            Priority = "mid",
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(lightweight), Is.False);
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(
                lightweight, ["SquadDash/Formatting.cs"]), Is.False);
            Assert.That(PlanTaskExecutionPolicy.RequiresIndependentVerification(
                lightweight, ["SquadDash/SquadDash.csproj"]), Is.True);
        });
    }

    [Test]
    public void Parser_RejectsUnknownVerificationLevel()
    {
        const string text = """
            TASKS_JSON:
            {
              "groupId":"VERIFY-20260813",
              "groupTitle":"Verify",
              "branch":"feature/verify",
              "summary":"Verify the feature",
              "tasks":[{
                "id":"VERIFY-20260813-001",
                "title":"Implement feature",
                "description":"Implement the approved behavior.",
                "dependsOn":[],
                "priority":"high",
                "executionMode":"implementation",
                "verificationLevel":"exhaustive",
                "agentRoutingMode":"generic",
                "genericAgentReason":"No specialist is required."
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(text, out _, out var diagnostic), Is.False);
        Assert.That(diagnostic?.Code, Is.EqualTo("invalid-task-verification-level"));
    }

    [Test]
    public void TasksProjectionRoundTrip_PreservesExecutionMode()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("tasks.md");
        new DecomposedTasksWriter().WriteGroup(
            path,
            MakeGroup(MakeTask("Verify feature", PlanTaskExecutionMode.Verification)));

        var restored = TasksPanelParser.Parse(File.ReadAllLines(path))
            .DecomposeGroups["VERIFY-20260813"]
            .Tasks.Single();

        Assert.That(restored.ExecutionMode, Is.EqualTo(PlanTaskExecutionMode.Verification));
    }

    [Test]
    public void TasksProjectionRoundTrip_PreservesVerificationLevel()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("tasks.md");
        var task = MakeTask("Implement feature", PlanTaskExecutionMode.Implementation) with
        {
            VerificationLevel = PlanTaskVerificationLevel.Enhanced,
        };
        new DecomposedTasksWriter().WriteGroup(path, MakeGroup(task));

        var restored = TasksPanelParser.Parse(File.ReadAllLines(path))
            .DecomposeGroups["VERIFY-20260813"].Tasks.Single();

        Assert.That(restored.VerificationLevel, Is.EqualTo(PlanTaskVerificationLevel.Enhanced));
    }

    [Test]
    public void Parser_RejectsVerificationModeWithoutProofContract()
    {
        const string text = """
            TASKS_JSON:
            {
              "groupId":"VERIFY-20260813",
              "groupTitle":"Verify",
              "branch":"feature/verify",
              "summary":"Verify the feature",
              "tasks":[{
                "id":"VERIFY-20260813-001",
                "title":"Verify feature",
                "description":"Run the end-to-end checks.",
                "dependsOn":[],
                "priority":"high",
                "executionMode":"verification",
                "agentRoutingMode":"generic",
                "genericAgentReason":"No specialist is required."
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(text, out _, out var diagnostic), Is.False);
        Assert.That(diagnostic?.Code, Is.EqualTo("verification-task-without-proof"));
    }

    private static DecomposedSubTask MakeTask(string title, string? executionMode) =>
        new(
            "VERIFY-20260813-001",
            "Run the approved verification scenario without changing source files.",
            [],
            "high",
            title,
            AgentRoutingMode: "generic",
            GenericAgentReason: "Verification specialist.",
            ProofRequirements:
            [
                new DecomposedTaskProofRequirement(
                    "verification-test", "automated-test", "The end-to-end test passes."),
            ],
            ExecutionMode: executionMode);

    private static DecomposedTaskGroup MakeGroup(DecomposedSubTask task) =>
        new(
            "VERIFY-20260813",
            "Verify Feature",
            "feature/verify",
            "Verify the feature without changing it.",
            [task],
            Validations:
            [
                new DecomposedValidationNode(
                    "VERIFY-20260813-VAL-001",
                    "Completion audit",
                    "Audit the declared verification proof.",
                    [task.Id],
                    [],
                    ["The end-to-end test passed."],
                    Mode: "audit"),
            ]);
}

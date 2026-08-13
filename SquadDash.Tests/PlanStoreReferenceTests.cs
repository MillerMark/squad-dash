using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStoreReferenceTests
{
    private string _tempFolder = null!;
    private PlanStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "SquadDash-PlanReference-" + Guid.NewGuid().ToString("N"));
        _store = new PlanStore(_tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true);
    }

    [Test]
    public void LoadByPlanOrTaskReference_TaskIdReturnsContainingPlan()
    {
        _store.Save(CreatePlan("MODELPROF-20260810", "MODELPROF-20260810-007"));

        var result = _store.LoadByPlanOrTaskReference("MODELPROF-20260810-007");

        Assert.That(result?.PlanId, Is.EqualTo("MODELPROF-20260810"));
    }

    [Test]
    public void LoadByPlanOrTaskReference_ExactPlanIdTakesPrecedence()
    {
        _store.Save(CreatePlan("MODELPROF-20260810", "MODELPROF-20260810-007"));
        _store.Save(CreatePlan("MODELPROF-20260810-007"));

        var result = _store.LoadByPlanOrTaskReference("MODELPROF-20260810-007");

        Assert.That(result?.PlanId, Is.EqualTo("MODELPROF-20260810-007"));
    }

    [Test]
    public void LoadByPlanOrTaskReference_UnknownReferenceReturnsNull()
    {
        _store.Save(CreatePlan("MODELPROF-20260810", "MODELPROF-20260810-007"));

        Assert.That(_store.LoadByPlanOrTaskReference("UNKNOWN-20260810-001"), Is.Null);
    }

    private static Plan CreatePlan(string planId, params string[] taskIds) => new(
        PlanId: planId,
        Revision: "revision-1",
        Source: PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Executing,
        Title: "Reference test plan",
        Branch: "test/reference",
        Summary: "Tests transcript plan and task reference resolution.",
        Tasks: taskIds.Select(taskId => new PlanTask(
            TaskId: taskId,
            Title: "Test task",
            Description: "Test task",
            DependsOn: [],
            Priority: "mid",
            Status: PlanTaskStatus.Pending)).ToArray(),
        ApprovalGates: [],
        Progress: new PlanProgress(0, taskIds.Length, taskIds.FirstOrDefault()),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));
}

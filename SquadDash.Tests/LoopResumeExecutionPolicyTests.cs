namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopResumeExecutionPolicyTests
{
    [Test]
    public void Resolve_NoPersistedExecution_RefusesUnsafeGenericResume()
    {
        var decision = LoopResumeExecutionPolicy.Resolve(null, null, null);

        Assert.That(decision.Kind, Is.EqualTo(LoopResumeExecutionKind.Refuse));
    }

    [Test]
    public void Resolve_GenericExecution_PreservesExactPathAndFilter()
    {
        var persisted = new ActiveLoopExecutionState(
            @"D:\repo\.squad\loop-filtered-tasks.md",
            "God");

        var decision = LoopResumeExecutionPolicy.Resolve(persisted, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(LoopResumeExecutionKind.GenericLoop));
            Assert.That(decision.Execution?.LoopPath, Is.EqualTo(persisted.LoopPath));
            Assert.That(decision.Execution?.FilterText, Is.EqualTo("God"));
        });
    }

    [Test]
    public void Resolve_PersistedPlan_UsesExactGroupAndRevision()
    {
        var persisted = new ActiveLoopExecutionState(
            @"D:\repo\.squad\loop-executing-plan.md",
            "GODCLASS-20260725",
            "GODCLASS-20260725",
            "revision-123");

        var decision = LoopResumeExecutionPolicy.Resolve(persisted, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(LoopResumeExecutionKind.ExecutingPlan));
            Assert.That(decision.GroupId, Is.EqualTo("GODCLASS-20260725"));
            Assert.That(decision.Revision, Is.EqualTo("revision-123"));
        });
    }

    [Test]
    public void Resolve_LegacyPlanGroup_StillUsesExecutingPlanEngine()
    {
        var decision = LoopResumeExecutionPolicy.Resolve(
            null,
            null,
            "GODCLASS-20260725");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(LoopResumeExecutionKind.ExecutingPlan));
            Assert.That(decision.GroupId, Is.EqualTo("GODCLASS-20260725"));
        });
    }
}

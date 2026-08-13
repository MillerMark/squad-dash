using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanApprovalHistoricalPresentationPolicyTests
{
    [Test]
    public void UncrossedBoundary_RemainsEditable()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: false,
            controllingGateStatus: null,
            isPrimaryAnchor: false);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.EditableOctagon));
    }

    [Test]
    public void CrossedBoundaryWithoutGate_IsHidden()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: true,
            controllingGateStatus: null,
            isPrimaryAnchor: false);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.Hidden));
    }

    [Test]
    public void ApprovedPrimaryBoundary_BecomesCheck()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: true,
            controllingGateStatus: PlanGateStatus.Approved,
            isPrimaryAnchor: true);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.ApprovedCheck));
    }

    [Test]
    public void ApprovedEquivalentBoundary_IsHidden()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: true,
            controllingGateStatus: PlanGateStatus.Approved,
            isPrimaryAnchor: false);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.Hidden));
    }

    [TestCase(true, PlanApprovalControlVisualState.ApprovedCheck)]
    [TestCase(false, PlanApprovalControlVisualState.Hidden)]
    public void ApprovedBoundary_UsesOnlyPrimaryAnchor_EvenWhenDownstreamIsUncrossed(
        bool isPrimary, PlanApprovalControlVisualState expected)
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: false,
            controllingGateStatus: PlanGateStatus.Approved,
            isPrimaryAnchor: isPrimary);

        Assert.That(state, Is.EqualTo(expected));
    }

    [Test]
    public void AwaitingApprovalBoundary_IsReadOnly_EvenWhenDownstreamIsUncrossed()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: false,
            controllingGateStatus: PlanGateStatus.AwaitingApproval,
            isPrimaryAnchor: true);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.AwaitingQuestion));
    }

    [TestCase(PlanGateStatus.Pending)]
    public void UnresolvedLockedBoundary_RemainsVisible(string status)
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: true,
            controllingGateStatus: status,
            isPrimaryAnchor: true);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.LockedOctagon));
    }

    [Test]
    public void AwaitingEquivalentBoundary_RemainsLockedOctagon()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: true,
            controllingGateStatus: PlanGateStatus.AwaitingApproval,
            isPrimaryAnchor: false);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.LockedOctagon));
    }

    [Test]
    public void CollectivelyApprovedEquivalentBoundary_IsHiddenBeforeDependentStarts()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: false,
            controllingGateStatus: null,
            isPrimaryAnchor: false,
            hasResolvedEquivalent: true);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.Hidden));
    }

    [Test]
    public void CollectivelyUnresolvedEquivalentBoundary_IsReadOnlyBeforeDependentStarts()
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: false,
            controllingGateStatus: null,
            isPrimaryAnchor: false,
            hasUnresolvedEquivalent: true);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.LockedOctagon));
    }
}

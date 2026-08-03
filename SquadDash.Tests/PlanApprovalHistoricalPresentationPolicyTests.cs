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

    [TestCase(PlanGateStatus.Pending)]
    [TestCase(PlanGateStatus.AwaitingApproval)]
    public void UnresolvedLockedBoundary_RemainsVisible(string status)
    {
        var state = PlanApprovalHistoricalPresentationPolicy.Resolve(
            executionLocked: true,
            controllingGateStatus: status,
            isPrimaryAnchor: true);

        Assert.That(state, Is.EqualTo(PlanApprovalControlVisualState.LockedOctagon));
    }
}

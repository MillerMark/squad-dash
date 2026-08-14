namespace SquadDash.Tests;

[TestFixture]
internal sealed class StagedPlanRevisionHostCollectionTests
{
    [Test]
    public void TryCreateCandidate_AcceptsVerifiedCorrelatedCompletion()
    {
        var evt = MakeEvent();

        var accepted = StagedPlanRevisionHostCollection.TryCreateCandidate(
            evt, out var candidate, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True, error);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.ToolCallId, Is.EqualTo("tool-rory"));
            Assert.That(candidate.AgentName, Is.EqualTo("rory"));
            Assert.That(candidate.Payload.GroupId, Is.EqualTo("CLIDETECT-20260814"));
            Assert.That(candidate.Payload.BaseRevision, Is.EqualTo("base-1"));
            Assert.That(error, Is.Null);
        });
    }

    [TestCase(false, StagedPlanRevisionHostCollection.Protocol)]
    [TestCase(true, "accepted_plan_revision")]
    public void TryCreateCandidate_IgnoresUnverifiedOrUnrelatedCompletion(
        bool verified,
        string protocol)
    {
        var evt = MakeEvent();
        evt.HostProtocolVerified = verified;
        evt.HostProtocol = protocol;

        var accepted = StagedPlanRevisionHostCollection.TryCreateCandidate(
            evt, out var candidate, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(candidate, Is.Null);
            Assert.That(error, Is.Null);
        });
    }

    [Test]
    public void TryCreateCandidate_RejectsMissingCorrelationData()
    {
        var evt = MakeEvent();
        evt.ToolCallId = null;

        var accepted = StagedPlanRevisionHostCollection.TryCreateCandidate(
            evt, out var candidate, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(candidate, Is.Null);
            Assert.That(error, Does.Contain("missing correlation data"));
        });
    }

    [Test]
    public void TryCreateCandidate_RejectsMismatchedResponseIdentity()
    {
        var evt = MakeEvent();
        evt.HostProtocolBaseRevision = "base-2";

        var accepted = StagedPlanRevisionHostCollection.TryCreateCandidate(
            evt, out var candidate, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(candidate, Is.Null);
            Assert.That(error, Does.Contain("did not match its verified request"));
        });
    }

    [Test]
    public void TryCreateCandidate_RejectsMalformedDelta()
    {
        var evt = MakeEvent();
        evt.LatestResponse = "STAGED_PLAN_REVISION_JSON: {}";

        var accepted = StagedPlanRevisionHostCollection.TryCreateCandidate(
            evt, out var candidate, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(candidate, Is.Null);
            Assert.That(error, Does.Contain("required"));
        });
    }

    [Test]
    public void CollectionLedger_ReservesEachToolCallExactlyOnce()
    {
        var ledger = new StagedPlanRevisionCollectionLedger();

        Assert.Multiple(() =>
        {
            Assert.That(ledger.TryReserve(" tool-rory "), Is.True);
            Assert.That(ledger.TryReserve("tool-rory"), Is.False);
            Assert.That(ledger.TryReserve("tool-rory-2"), Is.True);
            Assert.That(ledger.TryReserve(" "), Is.False);
        });
    }

    [Test]
    public void TurnCollectionState_TreatsDirectRevisionAsAddressingOriginalPendingPlanUntilReset()
    {
        var pending = new PendingDecomposePlan(
            "base-1",
            new DecomposedTaskGroup(
                "CLIDETECT-20260814",
                "CLI detection",
                "feature/cli-detection",
                "Detect CLI sources",
                [new DecomposedSubTask("CLIDETECT-20260814-001", "Discover CLI", [], "high")]));
        var other = pending with { Revision = "base-2" };
        var state = new StagedPlanRevisionTurnCollectionState();

        state.MarkHandled("CLIDETECT-20260814", "base-1");

        Assert.Multiple(() =>
        {
            Assert.That(state.Handled, Is.True);
            Assert.That(state.Addresses(pending), Is.True);
            Assert.That(state.Addresses(other), Is.False);
        });

        state.Reset();
        Assert.Multiple(() =>
        {
            Assert.That(state.Handled, Is.False);
            Assert.That(state.Addresses(pending), Is.False);
        });
    }

    private static SquadSdkEvent MakeEvent() => new()
    {
        Type = "subagent_completed",
        ToolCallId = "tool-rory",
        AgentName = "rory",
        HostProtocolVerified = true,
        HostProtocol = StagedPlanRevisionHostCollection.Protocol,
        HostProtocolGroupId = "CLIDETECT-20260814",
        HostProtocolBaseRevision = "base-1",
        LatestResponse = """
            STAGED_PLAN_REVISION_JSON:
            {
              "groupId": "CLIDETECT-20260814",
              "baseRevision": "base-1",
              "summary": "Clarify the update command",
              "operations": [
                {
                  "op": "updateTask",
                  "targetId": "CLIDETECT-20260814-001",
                  "patch": { "title": "Clarify CLI discovery" }
                }
              ]
            }
            """,
    };
}

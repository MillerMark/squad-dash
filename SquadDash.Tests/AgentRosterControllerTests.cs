using System;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentRosterControllerTests
{
    // ── helpers ───────────────────────────────────────────────────────────

    private static AgentRosterController Build(
        Func<IReadOnlyList<AgentStatusCard>>? getAgents              = null,
        Func<string?>?                        getCurrentSessionState = null)
    {
        return new AgentRosterController(
            getAgents:              getAgents              ?? (() => []),
            getCurrentSessionState: getCurrentSessionState ?? (() => null));
    }

    // ── constructor null guards ────────────────────────────────────────────

    [Test]
    public void Constructor_NullGetAgents_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentRosterController(
                getAgents:              null!,
                getCurrentSessionState: () => null));
    }

    [Test]
    public void Constructor_NullGetCurrentSessionState_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentRosterController(
                getAgents:              () => [],
                getCurrentSessionState: null!));
    }

    // ── delegate routing ──────────────────────────────────────────────────

    [Test]
    public void GetAgents_ReturnsDelegateResult()
    {
        var cards = new List<AgentStatusCard>();
        IAgentRosterView view = Build(getAgents: () => cards);

        Assert.That(view.GetAgents(), Is.SameAs(cards));
    }

    [Test]
    public void GetAgents_CalledMultipleTimes_DelegateFreshEachTime()
    {
        int callCount = 0;
        IAgentRosterView view = Build(getAgents: () => { callCount++; return []; });

        view.GetAgents();
        view.GetAgents();

        Assert.That(callCount, Is.EqualTo(2));
    }

    [Test]
    public void CurrentSessionState_ReturnsDelegateResult_NonNull()
    {
        IAgentRosterView view = Build(getCurrentSessionState: () => "active");

        Assert.That(view.CurrentSessionState, Is.EqualTo("active"));
    }

    [Test]
    public void CurrentSessionState_ReturnsDelegateResult_Null()
    {
        IAgentRosterView view = Build(getCurrentSessionState: () => null);

        Assert.That(view.CurrentSessionState, Is.Null);
    }

    [Test]
    public void CurrentSessionState_EvaluatedEachAccess()
    {
        int callCount = 0;
        IAgentRosterView view = Build(getCurrentSessionState: () => { callCount++; return "s"; });

        _ = view.CurrentSessionState;
        _ = view.CurrentSessionState;

        Assert.That(callCount, Is.EqualTo(2));
    }
}

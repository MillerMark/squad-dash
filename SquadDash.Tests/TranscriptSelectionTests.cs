using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptSelectionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AgentStatusCard MakeCard(string name, bool isLead = false) =>
        new(name, name[0].ToString(), "Role", "", "", "", "#AABBCC",
            accentStorageKey: name, isLeadAgent: isLead);

    private static TranscriptThreadState MakeThread(int seqNum = 1)
    {
        var t = new TranscriptThreadState(
            Guid.NewGuid().ToString(),
            TranscriptThreadKind.Agent,
            "Thread",
            DateTimeOffset.Now);
        t.SequenceNumber = seqNum;
        return t;
    }

    private static TranscriptSelectionController MakeController(
        IReadOnlyList<AgentStatusCard> agents,
        bool mainVisible = true) =>
        new(agents, mainVisible);

    // ── HandleCardClick — plain click on non-lead opens thread #1 ─────────────

    [Test]
    public void HandleCardClick_PlainNonLead_OpensThread1()
    {
        var card = MakeCard("Lyra");
        var thread1 = MakeThread(1);
        var thread2 = MakeThread(2);
        card.Threads.Add(thread1);
        card.Threads.Add(thread2);

        var controller = MakeController([card]);
        (AgentStatusCard openedCard, TranscriptThreadState openedThread, bool isAuto) opened = default;
        controller.OpenPanelRequested += (c, t, a) => opened = (c, t, a);

        controller.HandleCardClick(card, shiftHeld: false);

        Assert.That(opened.openedCard, Is.SameAs(card));
        Assert.That(opened.openedThread, Is.SameAs(thread1));
    }

    [Test]
    public void HandleCardClick_PlainNonLead_ClosesAllOtherPanelsFirst()
    {
        var card1 = MakeCard("Lyra");
        var card2 = MakeCard("Orion");
        var t1 = MakeThread(1);
        var t2 = MakeThread(1);
        card1.Threads.Add(t1);
        card2.Threads.Add(t2);

        var controller = MakeController([card1, card2]);
        // Open card2 panel first
        controller.HandleCardClick(card2, shiftHeld: false);

        var closed = new List<(AgentStatusCard, TranscriptThreadState)>();
        controller.ClosePanelRequested += (c, t) => closed.Add((c, t));

        // Now plain click card1 — should close card2's panel
        controller.HandleCardClick(card1, shiftHeld: false);

        Assert.That(closed, Has.Some.Matches<(AgentStatusCard c, TranscriptThreadState t)>(x => x.c == card2));
    }

    [Test]
    public void HandleCardClick_PlainLead_FiresShowMain()
    {
        var lead = MakeCard("Squad", isLead: true);
        var controller = MakeController([lead], mainVisible: false);

        bool showMainFired = false;
        controller.ShowMainRequested += () => showMainFired = true;

        controller.HandleCardClick(lead, shiftHeld: false);

        Assert.That(showMainFired, Is.True);
    }

    // ── HandleChipClick — plain click exclusive within agent ─────────────────

    [Test]
    public void HandleChipClick_Plain_ExclusiveWithinAgent()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        var t2 = MakeThread(2);
        card.Threads.Add(t1);
        card.Threads.Add(t2);

        var controller = MakeController([card]);
        // Open t1 first
        controller.HandleChipClick(card, t1, shiftHeld: false);
        Assert.That(controller.OpenPanels, Has.Some.Matches<(AgentStatusCard c, TranscriptThreadState t)>(x => x.c == card && x.t == t1));

        var closedThreads = new List<TranscriptThreadState>();
        controller.ClosePanelRequested += (_, t) => closedThreads.Add(t);

        // Plain click t2 — should close t1 and open t2
        controller.HandleChipClick(card, t2, shiftHeld: false);

        Assert.That(closedThreads, Contains.Item(t1));
        Assert.That(controller.OpenPanels, Has.Some.Matches<(AgentStatusCard c, TranscriptThreadState t)>(x => x.c == card && x.t == t2));
        Assert.That(controller.OpenPanels, Has.None.Matches<(AgentStatusCard c, TranscriptThreadState t)>(x => x.t == t1));
    }

    [Test]
    public void HandleChipClick_Plain_DoesNotAffectOtherAgentPanels()
    {
        var card1 = MakeCard("Lyra");
        var card2 = MakeCard("Orion");
        var t1 = MakeThread(1);
        var t2 = MakeThread(1);
        card1.Threads.Add(t1);
        card2.Threads.Add(t2);

        var controller = MakeController([card1, card2]);
        // Open card2's panel
        controller.HandleChipClick(card2, t2, shiftHeld: false);
        Assert.That(controller.OpenPanels.Count, Is.EqualTo(1));

        // Plain click on card1's chip — card2's panel should remain
        controller.HandleChipClick(card1, t1, shiftHeld: false);

        Assert.That(controller.OpenPanels, Has.Some.Matches<(AgentStatusCard c, TranscriptThreadState t)>(x => x.c == card2 && x.t == t2));
        Assert.That(controller.OpenPanels, Has.Some.Matches<(AgentStatusCard c, TranscriptThreadState t)>(x => x.c == card1 && x.t == t1));
    }

    // ── HandleChipClick — shift+click toggle ─────────────────────────────────

    [Test]
    public void HandleChipClick_Shift_Toggle_Open()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card]);
        bool opened = false;
        controller.OpenPanelRequested += (_, _, _) => opened = true;

        controller.HandleChipClick(card, t1, shiftHeld: true);

        Assert.That(opened, Is.True);
        Assert.That(controller.OpenPanels.Count, Is.EqualTo(1));
    }

    [Test]
    public void HandleChipClick_Shift_Toggle_Close()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card]);
        controller.HandleChipClick(card, t1, shiftHeld: false); // open it first

        bool closed = false;
        controller.ClosePanelRequested += (_, _) => closed = true;

        controller.HandleChipClick(card, t1, shiftHeld: true); // shift+click to close

        Assert.That(closed, Is.True);
        Assert.That(controller.OpenPanels.Count, Is.EqualTo(0));
    }

    [Test]
    public void HandleChipClick_Shift_CloseLastPanel_FallsBackToMain()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card], mainVisible: false);
        controller.HandleChipClick(card, t1, shiftHeld: false); // open
        controller.SetMainVisible(false); // ensure main is reported as hidden

        bool showMainFired = false;
        controller.ShowMainRequested += () => showMainFired = true;

        controller.HandleChipClick(card, t1, shiftHeld: true); // close last → fallback

        Assert.That(showMainFired, Is.True);
    }

    // ── OnAgentEnteredActivePanel ─────────────────────────────────────────────

    [Test]
    public void OnAgentEnteredActivePanel_OpensThread1Panel()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card]);
        (AgentStatusCard, TranscriptThreadState, bool isAuto) opened = default;
        controller.OpenPanelRequested += (c, t, a) => opened = (c, t, a);

        controller.OnAgentEnteredActivePanel(card);

        Assert.That(opened.Item1, Is.SameAs(card));
        Assert.That(opened.Item2, Is.SameAs(t1));
        Assert.That(opened.isAuto, Is.True);
    }

    [Test]
    public void OnAgentEnteredActivePanel_NoThreads_DoesNothing()
    {
        var card = MakeCard("Lyra");
        var controller = MakeController([card]);

        bool fired = false;
        controller.OpenPanelRequested += (_, _, _) => fired = true;

        controller.OnAgentEnteredActivePanel(card);

        Assert.That(fired, Is.False);
    }

    // ── OnAgentLeftActivePanel ────────────────────────────────────────────────

    [Test]
    public void OnAgentLeftActivePanel_DoesNotClosePanels_MainWindowCountdownOwnsClose()
    {
        // OnAgentLeftActivePanel is intentionally a no-op as of 0822ba2.
        // MainWindow's auto-close countdown owns the actual panel close timing.
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        var t2 = MakeThread(2);
        card.Threads.Add(t1);
        card.Threads.Add(t2);

        var controller = MakeController([card]);
        controller.HandleChipClick(card, t1, shiftHeld: false);
        controller.HandleChipClick(card, t2, shiftHeld: true); // add t2 additively

        Assert.That(controller.OpenPanels.Count, Is.EqualTo(2));

        var closedThreads = new List<TranscriptThreadState>();
        controller.ClosePanelRequested += (_, t) => closedThreads.Add(t);

        controller.OnAgentLeftActivePanel(card);

        // Panels remain open; MainWindow's countdown will close them
        Assert.That(closedThreads, Is.Empty);
        Assert.That(controller.OpenPanels.Count, Is.EqualTo(2));
    }

    [Test]
    public void OnAgentLeftActivePanel_DoesNotFallBackToMain_MainWindowCountdownOwnsClose()
    {
        // OnAgentLeftActivePanel is intentionally a no-op as of 0822ba2.
        // MainWindow's auto-close countdown handles fallback logic.
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card], mainVisible: false);
        controller.HandleChipClick(card, t1, shiftHeld: false);
        controller.SetMainVisible(false);

        bool showMainFired = false;
        controller.ShowMainRequested += () => showMainFired = true;

        controller.OnAgentLeftActivePanel(card);

        // ShowMainRequested does not fire; MainWindow's countdown owns fallback
        Assert.That(showMainFired, Is.False);
    }

    // ── IsSecondaryPanelOpen and card indicator ───────────────────────────────

    [Test]
    public void DoOpenPanel_SetsIsSecondaryPanelOpenOnThread()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card]);
        controller.HandleChipClick(card, t1, shiftHeld: false);

        Assert.That(t1.IsSecondaryPanelOpen, Is.True);
    }

    [Test]
    public void DoClosePanel_ClearsIsSecondaryPanelOpenOnThread()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card]);
        controller.HandleChipClick(card, t1, shiftHeld: false);
        controller.HandleChipClick(card, t1, shiftHeld: true); // close

        Assert.That(t1.IsSecondaryPanelOpen, Is.False);
    }

    [Test]
    public void CardIndicator_TrueWhenAnyThreadOpen()
    {
        var card = MakeCard("Lyra");
        var t1 = MakeThread(1);
        card.Threads.Add(t1);

        var controller = MakeController([card]);
        Assert.That(card.IsTranscriptTargetSelected, Is.False);

        controller.HandleChipClick(card, t1, shiftHeld: false);
        Assert.That(card.IsTranscriptTargetSelected, Is.True);

        controller.HandleChipClick(card, t1, shiftHeld: true); // close
        Assert.That(card.IsTranscriptTargetSelected, Is.False);
    }
}

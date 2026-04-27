using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Manages the open/close state of secondary transcript panels, keyed by
/// (AgentStatusCard, TranscriptThreadState) tuples. Fires events so MainWindow
/// can create and destroy the WPF panel controls.
/// </summary>
internal sealed class TranscriptSelectionController
{
    private readonly IReadOnlyList<AgentStatusCard> _allAgents;
    private readonly HashSet<(AgentStatusCard Agent, TranscriptThreadState Thread)> _openPanels = new();
    private bool _mainVisible;

    public TranscriptSelectionController(IReadOnlyList<AgentStatusCard> allAgents, bool mainVisible = true)
    {
        _allAgents = allAgents;
        _mainVisible = mainVisible;
    }

    // ── Public state ────────────────────────────────────────────────────────

    public IReadOnlyCollection<(AgentStatusCard Agent, TranscriptThreadState Thread)> OpenPanels => _openPanels;

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>bool = isAutoOpened</summary>
    public event Action<AgentStatusCard, TranscriptThreadState, bool>? OpenPanelRequested;

    public event Action<AgentStatusCard, TranscriptThreadState>? ClosePanelRequested;

    /// <summary>Fired when the main transcript panel should become visible.</summary>
    public event Action? ShowMainRequested;

    /// <summary>Fired when the main transcript panel should be hidden.</summary>
    public event Action? HideMainRequested;

    // ── Notification from MainWindow ─────────────────────────────────────────

    /// <summary>
    /// Called by MainWindow when _mainTranscriptVisible changes so the controller
    /// stays in sync with the actual UI state.
    /// </summary>
    public void SetMainVisible(bool visible) => _mainVisible = visible;

    /// <summary>
    /// Replaces the controller's logical open-panel set with the actual live WPF state.
    /// This keeps chip/card toggles aligned even when panels are closed directly from
    /// MainWindow (auto-close, document ownership transfer, close button, etc.).
    /// </summary>
    public void ReconcilePanels(
        IEnumerable<(AgentStatusCard Agent, TranscriptThreadState Thread)> openPanels,
        bool mainVisible)
    {
        _openPanels.Clear();
        foreach (var openPanel in openPanels)
            _openPanels.Add(openPanel);

        _mainVisible = mainVisible;
    }

    // ── Entry points ─────────────────────────────────────────────────────────

    public void HandleCardClick(AgentStatusCard card, bool shiftHeld)
    {
        if (shiftHeld)
            HandleShiftClick(card);
        else
            HandlePlainClick(card);
    }

    public void HandleChipClick(AgentStatusCard card, TranscriptThreadState thread, bool shiftHeld)
    {
        if (shiftHeld)
            HandleChipShiftClick(card, thread);
        else
            HandleChipPlainClick(card, thread);
    }

    public void OnAgentEnteredActivePanel(AgentStatusCard card)
    {
        if (card.IsLeadAgent) return;
        var thread1 = GetThread1(card);
        if (thread1 is null) return;
        if (!_openPanels.Any(p => ReferenceEquals(p.Thread, thread1)))
            DoOpenPanel(card, thread1, isAutoOpened: true);
    }

    public void OnAgentLeftActivePanel(AgentStatusCard card)
    {
        // Do not directly close panels here. MainWindow tracks whether a panel was
        // auto-opened versus user-pinned, and its auto-close countdown owns the
        // actual close timing.
    }

    // ── Card-level click internals ────────────────────────────────────────────

    private void HandlePlainClick(AgentStatusCard card)
    {
        // Close ALL open panels (all agents, all threads) — exclusive global select
        foreach (var p in _openPanels.ToList())
            DoClosePanel(p.Agent, p.Thread);

        if (card.IsLeadAgent)
        {
            DoShowMain();
        }
        else
        {
            DoHideMain();
            var thread1 = GetThread1(card);
            if (thread1 is not null)
                DoOpenPanel(card, thread1);
        }
    }

    private void HandleShiftClick(AgentStatusCard card)
    {
        if (card.IsLeadAgent)
        {
            // Toggle main transcript
            if (_mainVisible)
            {
                // Only hide if there are other panels visible
                if (_openPanels.Count > 0)
                    DoHideMain();
                // else: don't close the last visible thing
            }
            else
            {
                DoShowMain();
            }
            return;
        }

        bool anyOpen = _openPanels.Any(p => p.Agent == card);
        if (anyOpen)
        {
            // Close all this agent's panels
            foreach (var p in _openPanels.Where(p => p.Agent == card).ToList())
                DoClosePanel(p.Agent, p.Thread);
            // Fallback if nothing left
            if (_openPanels.Count == 0 && !_mainVisible)
                DoShowMain();
        }
        else
        {
            var thread1 = GetThread1(card);
            if (thread1 is null)
            {
                // Create an empty placeholder thread for this agent
                thread1 = CreateEmptyThread(card);
                card.Threads.Add(thread1);
            }
            DoOpenPanel(card, thread1);
        }
    }

    // ── Chip-level click internals ────────────────────────────────────────────

    private void HandleChipPlainClick(AgentStatusCard card, TranscriptThreadState thread)
    {
        // Exclusive within agent: close all of this agent's open panels
        foreach (var p in _openPanels.Where(p => p.Agent == card).ToList())
            DoClosePanel(p.Agent, p.Thread);
        // Open only the requested thread
        DoOpenPanel(card, thread);
    }

    private void HandleChipShiftClick(AgentStatusCard card, TranscriptThreadState thread)
    {
        if (_openPanels.Any(p => ReferenceEquals(p.Thread, thread)))
        {
            DoClosePanel(card, thread);
            // Fallback: if nothing open at all
            if (_openPanels.Count == 0 && !_mainVisible)
                DoShowMain();
        }
        else
        {
            DoOpenPanel(card, thread);
        }
    }

    // ── Primitive operations ──────────────────────────────────────────────────

    private void DoOpenPanel(AgentStatusCard card, TranscriptThreadState thread, bool isAutoOpened = false)
    {
        if (_openPanels.Any(p => ReferenceEquals(p.Thread, thread))) return;

        _openPanels.Add((card, thread));
        thread.IsSecondaryPanelOpen = true;
        card.IsTranscriptTargetSelected = true;
        OpenPanelRequested?.Invoke(card, thread, isAutoOpened);
    }

    private void DoClosePanel(AgentStatusCard card, TranscriptThreadState thread)
    {
        if (_openPanels.RemoveWhere(p => ReferenceEquals(p.Thread, thread)) == 0) return;

        thread.IsSecondaryPanelOpen = false;
        card.IsTranscriptTargetSelected = _openPanels.Any(p => p.Agent == card);
        ClosePanelRequested?.Invoke(card, thread);

        // Remove placeholder threads from the card when their panel closes so they don't
        // accumulate in card.Threads across repeated shift-clicks.
        if (thread.IsPlaceholderThread)
            card.Threads.Remove(thread);
    }

    private void DoShowMain()
    {
        _mainVisible = true;
        ShowMainRequested?.Invoke();
    }

    private void DoHideMain()
    {
        _mainVisible = false;
        HideMainRequested?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TranscriptThreadState? GetThread1(AgentStatusCard card) =>
        card.Threads.FirstOrDefault(t => t.SequenceNumber == 1)
        ?? card.Threads.FirstOrDefault(t => t.SequenceNumber > 0)
        ?? card.Threads.FirstOrDefault();

    private static TranscriptThreadState CreateEmptyThread(AgentStatusCard card)
    {
        // Create a minimal placeholder thread for agents with no transcript history.
        // IsPlaceholderThread = true ensures this thread is excluded from sort-key
        // computations (GetAgentCardBucketSortKey filters !IsPlaceholderThread), so
        // merely opening an empty panel does NOT push the agent to the front of the roster.
        var thread = new TranscriptThreadState(
            threadId: $"{card.Name}-empty-{Guid.NewGuid():N}",
            kind: TranscriptThreadKind.Agent,
            title: $"{card.Name} - just now",
            startedAt: DateTimeOffset.UtcNow);
        thread.AgentName = card.Name;
        thread.SequenceNumber = 1;
        thread.LastObservedActivityAt = DateTimeOffset.UtcNow;
        thread.IsPlaceholderThread = true;
        return thread;
    }

    private IEnumerable<TranscriptThreadState> OpenThreadsFor(AgentStatusCard card) =>
        _openPanels.Where(p => p.Agent == card).Select(p => p.Thread);
}

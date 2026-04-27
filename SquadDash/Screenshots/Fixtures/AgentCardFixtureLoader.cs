using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores fixture state for a single <see cref="AgentStatusCard"/>.
/// </summary>
/// <remarks>
/// Constructor dependencies are passed in directly rather than reaching back into
/// <c>MainWindow</c>, keeping the loader independently testable and decoupled.
/// </remarks>
internal sealed class AgentCardFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys =
        ["agentName", "agentStatus", "transcriptChipCount", "isSelected"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ObservableCollection<AgentStatusCard> _agents;
    private readonly Dispatcher _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private AgentStatusCard? _targetCard;
    private string _originalStatusText = string.Empty;
    private bool _originalIsTranscriptTargetSelected;
    private Visibility _originalThreadChipsVisibility;
    private Visibility _originalOverflowChipVisibility;
    private string _originalOverflowChipText = string.Empty;
    private readonly List<TranscriptThreadState> _addedThreads = [];
    private bool _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal AgentCardFixtureLoader(
        ObservableCollection<AgentStatusCard> agents,
        Dispatcher dispatcher)
    {
        _agents     = agents     ?? throw new ArgumentNullException(nameof(agents));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("agentName", out var agentNameEl))
            return Task.CompletedTask; // key absent — nothing to do

        var agentName = agentNameEl.GetString();
        if (string.IsNullOrWhiteSpace(agentName))
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Find the target card (match on Name or DisplayName, case-insensitive)
            var card = _agents.FirstOrDefault(c =>
                string.Equals(c.Name,        agentName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.DisplayName, agentName, StringComparison.OrdinalIgnoreCase));

            if (card is null)
            {
                Debug.WriteLine($"[AgentCardFixtureLoader] Agent '{agentName}' not found — skipping");
                return;
            }

            // ── Snapshot original state ──────────────────────────────────────
            _targetCard                          = card;
            _originalStatusText                  = card.StatusText;
            _originalIsTranscriptTargetSelected  = card.IsTranscriptTargetSelected;
            _originalThreadChipsVisibility       = card.ThreadChipsVisibility;
            _originalOverflowChipVisibility      = card.OverflowChipVisibility;
            _originalOverflowChipText            = card.OverflowChipText;

            // ── agentStatus → StatusText ─────────────────────────────────────
            if (fixture.Data.TryGetValue("agentStatus", out var statusEl))
            {
                card.StatusText = statusEl.GetString() switch
                {
                    "active"  => "Active",
                    "idle"    => "Idle",
                    "dynamic" => "Completed",
                    var other => other ?? string.Empty
                };
            }

            // ── isSelected → IsTranscriptTargetSelected ──────────────────────
            if (fixture.Data.TryGetValue("isSelected", out var isSelectedEl) &&
                isSelectedEl.ValueKind == JsonValueKind.True)
            {
                card.IsTranscriptTargetSelected = true;
            }

            // ── transcriptChipCount → placeholder thread chips ────────────────
            if (fixture.Data.TryGetValue("transcriptChipCount", out var chipCountEl) &&
                chipCountEl.TryGetInt32(out var chipCount) &&
                chipCount > 0)
            {
                const int maxVisibleChips = 3;

                for (var i = 0; i < chipCount; i++)
                {
                    var thread = new TranscriptThreadState(
                        threadId:  $"fixture-placeholder-{i + 1}",
                        kind:      TranscriptThreadKind.Agent,
                        title:     agentName,
                        startedAt: DateTimeOffset.Now.AddMinutes(-(i + 1)));

                    // Non-empty LatestResponse makes HasMeaningfulThreadTranscript() return true
                    thread.LatestResponse = "Fixture placeholder";
                    thread.SequenceNumber = i + 1;
                    thread.ChipLabel      = $"#{i + 1}";
                    thread.ChipVisibility = i < maxVisibleChips
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    _addedThreads.Add(thread);
                    card.Threads.Add(thread);
                }

                var overflowCount = Math.Max(0, chipCount - maxVisibleChips);
                card.ThreadChipsVisibility  = chipCount >= 2 ? Visibility.Visible  : Visibility.Collapsed;
                card.OverflowChipVisibility = overflowCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                card.OverflowChipText       = overflowCount > 0 ? $"+{overflowCount}" : string.Empty;
            }

            _applied = true;
        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            if (_targetCard is null)
                return;

            _targetCard.StatusText                 = _originalStatusText;
            _targetCard.IsTranscriptTargetSelected = _originalIsTranscriptTargetSelected;

            // Remove exactly the placeholder threads we added
            foreach (var thread in _addedThreads)
                _targetCard.Threads.Remove(thread);
            _addedThreads.Clear();

            _targetCard.ThreadChipsVisibility  = _originalThreadChipsVisibility;
            _targetCard.OverflowChipVisibility = _originalOverflowChipVisibility;
            _targetCard.OverflowChipText       = _originalOverflowChipText;

            _targetCard = null;
            _applied    = false;
        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

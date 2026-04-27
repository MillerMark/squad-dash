using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores the display order of <see cref="AgentStatusCard"/> items in the
/// agent panel so that screenshot baselines are reproducible regardless of the order
/// agents were loaded at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Register this loader <em>after</em> <c>ViewModeFixtureLoader</c> (panel visibility must
/// be set before card order matters) and <em>before</em> <c>AgentCardFixtureLoader</c>
/// (card state depends on cards being in their final positions).
/// </para>
/// <para>
/// The fixture key <c>agentOrder</c> accepts an ordered array of agent name strings.
/// Agents present in the collection but absent from the list are appended to the end in
/// their original relative order.  Agents listed but not found in the collection are
/// skipped with a <see cref="Debug.WriteLine"/> warning.
/// </para>
/// </remarks>
internal sealed class AgentOrderFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["agentOrder"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ObservableCollection<AgentStatusCard> _agents;
    private readonly Dispatcher                            _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private List<AgentStatusCard>? _originalOrder;
    private bool                   _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal AgentOrderFixtureLoader(
        ObservableCollection<AgentStatusCard> agents,
        Dispatcher                            dispatcher)
    {
        _agents     = agents     ?? throw new ArgumentNullException(nameof(agents));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("agentOrder", out var agentOrderEl))
            return Task.CompletedTask; // key absent — nothing to do

        if (agentOrderEl.ValueKind != JsonValueKind.Array)
            return Task.CompletedTask;

        // Parse requested order off the dispatcher thread.
        var requestedOrder = new List<string>();
        foreach (var el in agentOrderEl.EnumerateArray())
        {
            var name = el.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                requestedOrder.Add(name);
        }

        if (requestedOrder.Count == 0)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot original order ──────────────────────────────────────
            _originalOrder = [.._agents];

            // ── Build the desired order ──────────────────────────────────────
            // 1. Cards named in requestedOrder (in that order); skip missing with warning.
            var reordered = new List<AgentStatusCard>(requestedOrder.Count);
            foreach (var name in requestedOrder)
            {
                var card = _agents.FirstOrDefault(c =>
                    string.Equals(c.Name,        name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.DisplayName, name, StringComparison.OrdinalIgnoreCase));

                if (card is null)
                    Debug.WriteLine($"[AgentOrderFixtureLoader] Agent '{name}' not found — skipping");
                else
                    reordered.Add(card);
            }

            // 2. Cards not mentioned in requestedOrder, appended in their original order.
            foreach (var card in _agents)
            {
                if (!reordered.Contains(card))
                    reordered.Add(card);
            }

            // ── Reorder in-place via Move ────────────────────────────────────
            for (var i = 0; i < reordered.Count; i++)
            {
                var currentIndex = _agents.IndexOf(reordered[i]);
                if (currentIndex != i)
                    _agents.Move(currentIndex, i);
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
            if (_originalOrder is not null)
            {
                for (var i = 0; i < _originalOrder.Count; i++)
                {
                    var currentIndex = _agents.IndexOf(_originalOrder[i]);
                    if (currentIndex != i)
                        _agents.Move(currentIndex, i);
                }

                _originalOrder = null;
            }

            _applied = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

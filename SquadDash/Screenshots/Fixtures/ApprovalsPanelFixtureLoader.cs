using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores fixture state for the Commit Approvals panel so
/// screenshots can show it populated with representative approval entries
/// without writing to <c>commit-approvals.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>needsApproval</b> — JSON array of <c>{ "description": "…", "sha": "…" }</c>
/// objects.  Each entry is injected as a pending (unchecked) approval item.
/// </para>
/// <para>
/// <b>approved</b> — same schema; each entry is injected as an already-approved
/// (checked) item shown in the "Approved" section.
/// </para>
/// <para>
/// Apply snapshots the current <c>_approvalItems</c> list and prepends the
/// synthetic items to both the in-memory list and the panel display.
/// Restore removes the synthetic items and calls
/// <c>_approvalPanel?.ReplaceAllItems</c> with the original list — no
/// <see cref="CommitApprovalStore"/> write is triggered at any point.
/// </para>
/// </remarks>
internal sealed class ApprovalsPanelFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["needsApproval", "approved"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<List<CommitApprovalItem>>                   _getApprovalItems;
    private readonly Action<List<CommitApprovalItem>>                  _setApprovalItems;
    private readonly Action<IReadOnlyList<CommitApprovalItem>>         _replaceAllInPanel;
    private readonly Dispatcher                                        _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private List<CommitApprovalItem>? _originalItems;
    private bool                      _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="getApprovalItems">Returns the current <c>_approvalItems</c> list.</param>
    /// <param name="setApprovalItems">Replaces <c>_approvalItems</c> with a new list.</param>
    /// <param name="replaceAllInPanel">
    ///   Calls <c>_approvalPanel?.ReplaceAllItems(items)</c> to update the panel
    ///   display.  Must NOT call <see cref="CommitApprovalStore.Save"/>.
    /// </param>
    /// <param name="dispatcher">The WPF UI dispatcher.</param>
    internal ApprovalsPanelFixtureLoader(
        Func<List<CommitApprovalItem>>                   getApprovalItems,
        Action<List<CommitApprovalItem>>                  setApprovalItems,
        Action<IReadOnlyList<CommitApprovalItem>>         replaceAllInPanel,
        Dispatcher                                        dispatcher)
    {
        _getApprovalItems  = getApprovalItems  ?? throw new ArgumentNullException(nameof(getApprovalItems));
        _setApprovalItems  = setApprovalItems  ?? throw new ArgumentNullException(nameof(setApprovalItems));
        _replaceAllInPanel = replaceAllInPanel ?? throw new ArgumentNullException(nameof(replaceAllInPanel));
        _dispatcher        = dispatcher        ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        var hasNeedsApproval = fixture.Data.TryGetValue("needsApproval", out var needsEl) &&
                               needsEl.ValueKind == JsonValueKind.Array;
        var hasApproved      = fixture.Data.TryGetValue("approved", out var approvedEl) &&
                               approvedEl.ValueKind == JsonValueKind.Array;

        if (!hasNeedsApproval && !hasApproved)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot current items ────────────────────────────────────────
            _originalItems = new List<CommitApprovalItem>(_getApprovalItems());

            // ── Build synthetic items from fixture data ────────────────────────
            var synthetic = new List<CommitApprovalItem>();

            if (hasNeedsApproval)
            {
                foreach (var el in needsEl.EnumerateArray())
                {
                    var desc = el.TryGetProperty("description", out var descEl)
                        ? descEl.GetString() ?? "Fixture item"
                        : "Fixture item";
                    var sha = el.TryGetProperty("sha", out var shaEl)
                        ? shaEl.GetString() ?? "0000000"
                        : "0000000";

                    synthetic.Add(new CommitApprovalItem(
                        Id:            System.Guid.NewGuid().ToString("N"),
                        CommitSha:     sha,
                        CommitUrl:     null,
                        Description:   desc,
                        TurnStartedAt: DateTimeOffset.Now,
                        TurnPromptHint: null,
                        IsApproved:    false));
                }
            }

            if (hasApproved)
            {
                foreach (var el in approvedEl.EnumerateArray())
                {
                    var desc = el.TryGetProperty("description", out var descEl)
                        ? descEl.GetString() ?? "Fixture item"
                        : "Fixture item";
                    var sha = el.TryGetProperty("sha", out var shaEl)
                        ? shaEl.GetString() ?? "0000000"
                        : "0000000";

                    synthetic.Add(new CommitApprovalItem(
                        Id:            System.Guid.NewGuid().ToString("N"),
                        CommitSha:     sha,
                        CommitUrl:     null,
                        Description:   desc,
                        TurnStartedAt: DateTimeOffset.Now.AddSeconds(-1),
                        TurnPromptHint: null,
                        IsApproved:    true));
                }
            }

            if (synthetic.Count == 0)
                return;

            // Prepend synthetic items ahead of real ones so they appear at top
            var combined = new List<CommitApprovalItem>(synthetic.Count + _originalItems.Count);
            combined.AddRange(synthetic);
            combined.AddRange(_originalItems);

            _setApprovalItems(combined);
            _replaceAllInPanel(combined);

            _applied = true;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            var original = _originalItems ?? [];
            _setApprovalItems(original);
            _replaceAllInPanel(original);
            _originalItems = null;
            _applied       = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

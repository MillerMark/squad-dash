using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores synthetic background-task state in <see cref="BackgroundTaskPresenter"/>
/// so screenshots can show the background-task sidebar in a specific state.
/// </summary>
/// <remarks>
/// <para>
/// <b>tasks</b> — JSON array of task objects.  Each entry maps to a synthetic
/// <see cref="SquadBackgroundAgentInfo"/> that is prepended to the
/// <see cref="BackgroundTaskPresenter.BackgroundAgents"/> list.
/// </para>
/// <para>
/// Constructor dependencies are passed in via delegates rather than holding a
/// back-reference to <c>MainWindow</c>, keeping the loader independently testable.
/// </para>
/// </remarks>
internal sealed class BackgroundTaskFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["tasks"];

    /// <inheritdoc/>
    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<IReadOnlyList<SquadBackgroundAgentInfo>>   _getBackgroundAgents;
    private readonly Action<IReadOnlyList<SquadBackgroundAgentInfo>> _setBackgroundAgents;
    private readonly Action                                          _refreshDisplay;
    private readonly Dispatcher                                      _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private IReadOnlyList<SquadBackgroundAgentInfo>? _originalAgents;
    private bool _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="BackgroundTaskFixtureLoader"/>.
    /// </summary>
    /// <param name="getBackgroundAgents">Returns the current background-agent list.</param>
    /// <param name="setBackgroundAgents">Replaces the background-agent list.</param>
    /// <param name="refreshDisplay">Triggers <c>RefreshLeadAgentBackgroundStatus</c> to push the new state to the UI.</param>
    /// <param name="dispatcher">The WPF UI dispatcher.</param>
    internal BackgroundTaskFixtureLoader(
        Func<IReadOnlyList<SquadBackgroundAgentInfo>>   getBackgroundAgents,
        Action<IReadOnlyList<SquadBackgroundAgentInfo>> setBackgroundAgents,
        Action                                          refreshDisplay,
        Dispatcher                                      dispatcher)
    {
        _getBackgroundAgents = getBackgroundAgents ?? throw new ArgumentNullException(nameof(getBackgroundAgents));
        _setBackgroundAgents = setBackgroundAgents ?? throw new ArgumentNullException(nameof(setBackgroundAgents));
        _refreshDisplay      = refreshDisplay      ?? throw new ArgumentNullException(nameof(refreshDisplay));
        _dispatcher          = dispatcher          ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("tasks", out var tasksEl) ||
            tasksEl.ValueKind != JsonValueKind.Array)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot original state ──────────────────────────────────────
            _originalAgents = _getBackgroundAgents();

            // ── Build synthetic agents from fixture data ──────────────────────
            var synthetic = new List<SquadBackgroundAgentInfo>();
            var index = 0;

            foreach (var taskEl in tasksEl.EnumerateArray())
            {
                var title = taskEl.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString() ?? $"Fixture Task {index + 1}"
                    : $"Fixture Task {index + 1}";

                var status = taskEl.TryGetProperty("status", out var statusEl)
                    ? statusEl.GetString() ?? "running"
                    : "running";

                var elapsedSeconds = taskEl.TryGetProperty("elapsedSeconds", out var elapsedEl) &&
                                     elapsedEl.TryGetDouble(out var secs)
                    ? secs
                    : 0.0;

                var startedAt = DateTimeOffset.Now.AddSeconds(-elapsedSeconds);

                synthetic.Add(new SquadBackgroundAgentInfo
                {
                    AgentId     = $"fixture-bg-{index + 1}",
                    Description = title,
                    Status      = status,
                    StartedAt   = startedAt.ToString("O"),
                    CompletedAt = status is "completed" or "failed"
                        ? startedAt.AddSeconds(1).ToString("O")
                        : null
                });

                index++;
            }

            if (synthetic.Count == 0)
                return;

            // Prepend synthetic tasks ahead of any pre-existing agents
            _setBackgroundAgents([.. synthetic, .. _originalAgents]);
            _refreshDisplay();

            _applied = true;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            _setBackgroundAgents(_originalAgents ?? Array.Empty<SquadBackgroundAgentInfo>());
            _refreshDisplay();
            _originalAgents = null;
            _applied        = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

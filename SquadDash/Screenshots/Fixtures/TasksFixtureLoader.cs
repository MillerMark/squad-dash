using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores fixture state for the Tasks panel so screenshots can
/// show it populated with representative task items.
/// </summary>
/// <remarks>
/// <para>
/// <b>tasks</b> — JSON array of markdown task-line strings.  Each string may be
/// an open item (<c>"- [ ] …"</c>) or a completed item (<c>"- [x] …"</c>).
/// Owner annotations of the form <c>*(Owner: name)*</c> are stripped from the
/// displayed text.
/// </para>
/// <para>
/// Apply snapshots the current <see cref="StackPanel.Children"/> of the active
/// and completed task panels, then replaces them with synthesised rows built
/// from the fixture data.  Restore re-inserts the snapshotted children without
/// reading or writing <c>tasks.md</c>.
/// </para>
/// </remarks>
internal sealed class TasksFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["tasks"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly StackPanel                _activePanel;
    private readonly StackPanel                _completedPanel;
    private readonly Action<TaskParseResult>   _refreshPanel;
    private readonly Dispatcher                _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private List<UIElement>? _originalActiveChildren;
    private List<UIElement>? _originalCompletedChildren;
    private bool             _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="activePanel">The <see cref="StackPanel"/> that holds open task rows.</param>
    /// <param name="completedPanel">The <see cref="StackPanel"/> that holds completed task rows.</param>
    /// <param name="refreshPanel">
    ///   Delegate that calls <c>_tasksPanelController?.Refresh(result)</c>.
    ///   If the controller has not yet been initialised (panel never shown),
    ///   this is a no-op and the fixture apply becomes a no-op.
    /// </param>
    /// <param name="dispatcher">The WPF UI dispatcher.</param>
    internal TasksFixtureLoader(
        StackPanel              activePanel,
        StackPanel              completedPanel,
        Action<TaskParseResult> refreshPanel,
        Dispatcher              dispatcher)
    {
        _activePanel    = activePanel    ?? throw new ArgumentNullException(nameof(activePanel));
        _completedPanel = completedPanel ?? throw new ArgumentNullException(nameof(completedPanel));
        _refreshPanel   = refreshPanel   ?? throw new ArgumentNullException(nameof(refreshPanel));
        _dispatcher     = dispatcher     ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("tasks", out var tasksEl) ||
            tasksEl.ValueKind != JsonValueKind.Array)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot current children BEFORE refresh clears the panels ────
            _originalActiveChildren    = [.. _activePanel.Children.Cast<UIElement>()];
            _originalCompletedChildren = [.. _completedPanel.Children.Cast<UIElement>()];

            // ── Build a TaskParseResult directly from the fixture lines ────────
            // All open tasks go into a single synthetic priority group; completed
            // items go into the completedItems list.  IsUserOwned is set true so
            // checkboxes render in their enabled (interactive) state.
            const string ownerMarker = " *(Owner:";

            var openGroup      = new TaskPriorityGroup("🟡", "Tasks");
            var completedItems = new List<TaskItem>();

            foreach (var el in tasksEl.EnumerateArray())
            {
                var line    = el.GetString() ?? string.Empty;
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal))
                {
                    var rawText     = trimmed[5..].Trim();
                    var displayText = rawText;

                    var ownerIdx = displayText.IndexOf(ownerMarker, StringComparison.Ordinal);
                    if (ownerIdx > 0)
                        displayText = displayText[..ownerIdx].Trim();

                    openGroup.Items.Add(new TaskItem(
                        Text:        displayText,
                        Owner:       null,
                        IsUserOwned: true,
                        IsChecked:   false,
                        Emoji:       "🟡",
                        RawLine:     line));
                }
                else if (trimmed.StartsWith("- [x]", StringComparison.Ordinal))
                {
                    var text = trimmed[5..].Trim();
                    completedItems.Add(new TaskItem(
                        Text:        text,
                        Owner:       null,
                        IsUserOwned: false,
                        IsChecked:   true,
                        Emoji:       "✅",
                        RawLine:     line));
                }
            }

            IReadOnlyList<TaskPriorityGroup> groups = openGroup.Items.Count > 0
                ? [openGroup]
                : [];

            var parseResult = new TaskParseResult(groups, completedItems);
            _refreshPanel(parseResult);

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
            // Refresh() detached the original children when it called Children.Clear().
            // Re-inserting them here restores the panel to its pre-fixture state.
            _activePanel.Children.Clear();
            if (_originalActiveChildren is not null)
                foreach (var child in _originalActiveChildren)
                    _activePanel.Children.Add(child);

            _completedPanel.Children.Clear();
            if (_originalCompletedChildren is not null)
                foreach (var child in _originalCompletedChildren)
                    _completedPanel.Children.Add(child);

            _originalActiveChildren    = null;
            _originalCompletedChildren = null;
            _applied                   = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

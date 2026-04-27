using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores synthetic quick-reply buttons at the bottom of the coordinator
/// transcript so screenshots can show the quick-reply UX.
/// </summary>
/// <remarks>
/// <para>
/// <b>options</b> — JSON array of strings; each string becomes one button label.
/// </para>
/// <para>
/// <b>reason</b> (optional) — if present, rendered as a small caption above the buttons.
/// Silently ignored when absent or empty.
/// </para>
/// <para>
/// The buttons are injected as a <see cref="BlockUIContainer"/> appended to the
/// coordinator thread's <see cref="System.Windows.Documents.FlowDocument"/>, matching
/// the exact layout produced by <c>MainWindow.BuildQuickReplyBlock</c>.  They are
/// removed in <see cref="RestoreAsync"/>.
/// </para>
/// </remarks>
internal sealed class QuickReplyFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["options", "reason"];

    /// <inheritdoc/>
    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<TranscriptThreadState> _getCoordinatorThread;
    private readonly Dispatcher                  _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private Block? _addedBlock;
    private bool   _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="QuickReplyFixtureLoader"/>.
    /// </summary>
    /// <param name="getCoordinatorThread">Returns the coordinator <see cref="TranscriptThreadState"/> whose document receives the buttons.</param>
    /// <param name="dispatcher">The WPF UI dispatcher.</param>
    internal QuickReplyFixtureLoader(
        Func<TranscriptThreadState> getCoordinatorThread,
        Dispatcher                  dispatcher)
    {
        _getCoordinatorThread = getCoordinatorThread ?? throw new ArgumentNullException(nameof(getCoordinatorThread));
        _dispatcher           = dispatcher           ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("options", out var optionsEl) ||
            optionsEl.ValueKind != JsonValueKind.Array)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            var doc   = _getCoordinatorThread().Document;
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // ── optional reason caption ──────────────────────────────────────
            if (fixture.Data.TryGetValue("reason", out var reasonEl))
            {
                var reasonText = reasonEl.GetString();
                if (!string.IsNullOrWhiteSpace(reasonText))
                {
                    var caption = new TextBlock
                    {
                        Text     = reasonText,
                        Margin   = new Thickness(0, 0, 0, 6),
                        FontSize = 12
                    };
                    caption.SetResourceReference(TextBlock.ForegroundProperty, "AgentRoleText");
                    stack.Children.Add(caption);
                }
            }

            // ── option buttons ────────────────────────────────────────────────
            var panel = new WrapPanel
            {
                Margin      = new Thickness(0, 2, 0, 0),
                Orientation = Orientation.Horizontal
            };

            foreach (var optionEl in optionsEl.EnumerateArray())
            {
                var label = optionEl.GetString();
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                var button = new Button
                {
                    Content         = label,
                    Margin          = new Thickness(0, 0, 8, 8),
                    Padding         = new Thickness(10, 4, 10, 4),
                    BorderThickness = new Thickness(1),
                    Cursor          = Cursors.Hand,
                    MinHeight       = 28
                };

                if (Application.Current.TryFindResource("QuickReplyButtonStyle") is Style style)
                    button.Style = style;

                button.SetResourceReference(Control.BackgroundProperty,  "QuickReplySurface");
                button.SetResourceReference(Control.ForegroundProperty,  "QuickReplyText");
                button.SetResourceReference(Control.BorderBrushProperty, "QuickReplyBorder");

                panel.Children.Add(button);
            }

            if (panel.Children.Count == 0)
                return;

            stack.Children.Add(panel);

            _addedBlock = new BlockUIContainer(stack) { Margin = new Thickness(0, 2, 0, 10) };
            doc.Blocks.Add(_addedBlock);

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
            if (_addedBlock is not null)
            {
                _getCoordinatorThread().Document.Blocks.Remove(_addedBlock);
                _addedBlock = null;
            }

            _applied = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores the prompt-box text before a screenshot is captured, so that
/// screenshots showing a specific in-progress prompt are reproducible.
/// </summary>
/// <remarks>
/// <para>
/// Register this loader after <c>scrollPosition</c> — scroll positions should be
/// established before the prompt text so the complete UI state is set in dependency order.
/// </para>
/// </remarks>
internal sealed class PromptTextFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["promptText"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly TextBox    _promptTextBox;
    private readonly Dispatcher _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private string _originalText = string.Empty;
    private bool   _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal PromptTextFixtureLoader(
        TextBox    promptTextBox,
        Dispatcher dispatcher)
    {
        _promptTextBox = promptTextBox ?? throw new ArgumentNullException(nameof(promptTextBox));
        _dispatcher    = dispatcher    ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("promptText", out var promptTextEl))
            return Task.CompletedTask; // key absent — nothing to do

        var promptText = promptTextEl.GetString();
        if (promptText is null)
        {
            Debug.WriteLine("[PromptTextFixtureLoader] 'promptText' value is null — skipping");
            return Task.CompletedTask;
        }

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot original text ───────────────────────────────────────
            _originalText = _promptTextBox.Text;

            // ── Apply requested prompt text ──────────────────────────────────
            _promptTextBox.Text = promptText;

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
            _promptTextBox.Text = _originalText;
            _applied            = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

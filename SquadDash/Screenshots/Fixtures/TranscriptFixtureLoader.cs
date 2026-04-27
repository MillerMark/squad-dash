using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores fixture state for the coordinator transcript.
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplyAsync</c> snapshots the current <see cref="FlowDocument"/> blocks,
/// clears them, and populates the document with the fixture messages.
/// </para>
/// <para>
/// <c>RestoreAsync</c> reverses the replacement: existing fixture blocks are
/// removed and the original blocks are re-inserted.  Because
/// <see cref="System.Windows.Documents.BlockCollection.Clear"/> detaches blocks
/// from their parent document, re-adding them to the same document is safe.
/// </para>
/// </remarks>
internal sealed class TranscriptFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["messages", "scrollToBottom"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<TranscriptThreadState> _getCoordinatorThread;
    private readonly TranscriptScrollController  _scrollController;
    private readonly Dispatcher                  _dispatcher;
    private readonly string                      _repoRoot;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private List<Block>? _originalBlocks;
    private bool         _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal TranscriptFixtureLoader(
        Func<TranscriptThreadState> getCoordinatorThread,
        TranscriptScrollController  scrollController,
        Dispatcher                  dispatcher,
        string                      repoRoot)
    {
        _getCoordinatorThread = getCoordinatorThread ?? throw new ArgumentNullException(nameof(getCoordinatorThread));
        _scrollController     = scrollController     ?? throw new ArgumentNullException(nameof(scrollController));
        _dispatcher           = dispatcher           ?? throw new ArgumentNullException(nameof(dispatcher));
        _repoRoot             = repoRoot             ?? throw new ArgumentNullException(nameof(repoRoot));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("messages", out var messagesEl))
            return Task.CompletedTask; // key absent — nothing to do

        // ── $ref resolution ───────────────────────────────────────────────────
        // If messages is { "$ref": "path/to/file.json" }, load and substitute.
        // File I/O happens here (off the dispatcher thread) so the UI thread is not blocked.
        JsonDocument? refDoc = null;
        if (messagesEl.ValueKind == JsonValueKind.Object &&
            messagesEl.TryGetProperty("$ref", out var refEl) &&
            refEl.ValueKind == JsonValueKind.String)
        {
            var refPath = refEl.GetString() ?? string.Empty;
            var fullPath = Path.Combine(_repoRoot, refPath);

            try
            {
                var json = File.ReadAllText(fullPath);
                refDoc = JsonDocument.Parse(json);

                if (refDoc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    Debug.WriteLine(
                        $"[TranscriptFixtureLoader] $ref '{refPath}' resolved to {refDoc.RootElement.ValueKind} — expected Array; skipping");
                    refDoc.Dispose();
                    return Task.CompletedTask;
                }

                messagesEl = refDoc.RootElement;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[TranscriptFixtureLoader] Failed to resolve $ref '{refPath}': {ex.Message} — skipping");
                refDoc?.Dispose();
                return Task.CompletedTask;
            }
        }

        if (messagesEl.ValueKind != JsonValueKind.Array)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            var doc = _getCoordinatorThread().Document;

            // Snapshot current blocks; Clear() detaches them so they can be re-added later
            _originalBlocks = doc.Blocks.ToList();
            doc.Blocks.Clear();

            // Populate with fixture messages
            foreach (var msg in messagesEl.EnumerateArray())
            {
                var role = msg.TryGetProperty("role", out var roleEl)
                    ? roleEl.GetString() ?? "user"
                    : "user";
                var text = msg.TryGetProperty("text", out var textEl)
                    ? textEl.GetString() ?? string.Empty
                    : string.Empty;

                var para = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };

                // Role prefix (bold) + body text
                var prefix = role switch
                {
                    "assistant" => "Assistant: ",
                    "system"    => "System: ",
                    _           => "User: "
                };

                var prefixRun = new Run(prefix) { FontWeight = FontWeights.SemiBold };
                var bodyRun   = new Run(text);
                para.Inlines.Add(prefixRun);
                para.Inlines.Add(bodyRun);
                doc.Blocks.Add(para);
            }

            // scrollToBottom defaults to true when absent
            var scrollToBottom =
                !fixture.Data.TryGetValue("scrollToBottom", out var stbEl) ||
                stbEl.ValueKind != JsonValueKind.False;

            if (scrollToBottom)
                _scrollController.RequestScrollToEnd();

            _applied = true;
        }, DispatcherPriority.Normal, ct);

        // Safe to dispose now — dispatcher.Invoke completed synchronously above.
        refDoc?.Dispose();

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            var doc = _getCoordinatorThread().Document;
            doc.Blocks.Clear();

            if (_originalBlocks is not null)
            {
                foreach (var block in _originalBlocks)
                    doc.Blocks.Add(block);

                _originalBlocks = null;
            }

            _applied = false;
        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}

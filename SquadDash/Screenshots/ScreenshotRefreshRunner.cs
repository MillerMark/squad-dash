using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Screenshots;

/// <summary>
/// Drives an automated screenshot refresh pass: for each targeted
/// <see cref="ScreenshotDefinition"/>, applies any requested fixture, replays the
/// named <see cref="IReplayableUiAction"/>, signals <c>MainWindow</c> to capture
/// the PNG via <see cref="CaptureRequested"/>, then restores state.
/// </summary>
/// <remarks>
/// <para>
/// The runner deliberately does NOT call <c>RenderTargetBitmap</c> itself.
/// Instead it raises <see cref="CaptureRequested"/> and awaits a completion signal
/// from the subscriber (typically <c>MainWindow</c>), keeping the runner decoupled
/// from the WPF window hierarchy and re-using the same capture path as interactive
/// use.
/// </para>
/// <para>
/// All awaited operations inside <see cref="RunAsync"/> are marshalled so that
/// callers drive the method from the WPF dispatcher thread; the WPF dispatcher
/// must remain pumping while capture is in progress.
/// </para>
/// </remarks>
public sealed class ScreenshotRefreshRunner
{
    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ScreenshotDefinitionRegistry _definitions;
    private readonly UiActionReplayRegistry        _actions;
    private readonly FixtureLoaderRegistry         _fixtures;
    private readonly string                        _screenshotsDirectory;
    private readonly Func<string, Task>?           _applyThemeAsync;
    private readonly Func<string>?                 _getActiveTheme;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the WPF dispatcher thread when the runner wants <c>MainWindow</c>
    /// to perform a <see cref="System.Windows.Media.Imaging.RenderTargetBitmap"/>
    /// capture and save the PNG to the path specified in the event args.
    /// The subscriber MUST call <see cref="ScreenshotCaptureRequestedEventArgs.SignalSaved"/>
    /// or <see cref="ScreenshotCaptureRequestedEventArgs.SignalFailed"/> before returning.
    /// </summary>
    public event EventHandler<ScreenshotCaptureRequestedEventArgs>? CaptureRequested;

    // ── Construction ───────────────────────────────────────────────────────────

    /// <param name="definitions">Loaded definition registry (see <see cref="ScreenshotDefinitionRegistry.LoadAsync"/>).</param>
    /// <param name="actions">Replay-action registry populated at startup.</param>
    /// <param name="fixtures">Fixture-loader registry (may have zero loaders registered — that is fine).</param>
    /// <param name="screenshotsDirectory">Full path to the <c>docs/screenshots</c> directory.</param>
    /// <param name="applyThemeAsync">
    ///   Optional delegate that applies a named theme (e.g. <c>"Light"</c> or <c>"Dark"</c>)
    ///   to the UI before capture.  When <c>null</c>, theme switching is skipped and the
    ///   currently active theme is used for every definition.
    /// </param>
    /// <param name="getActiveTheme">
    ///   Optional delegate that returns the name of the currently active theme.
    ///   Required when <paramref name="applyThemeAsync"/> is provided — used to snapshot
    ///   the original theme so it can be restored after each definition's capture.
    /// </param>
    public ScreenshotRefreshRunner(
        ScreenshotDefinitionRegistry definitions,
        UiActionReplayRegistry        actions,
        FixtureLoaderRegistry         fixtures,
        string                        screenshotsDirectory,
        Func<string, Task>?           applyThemeAsync = null,
        Func<string>?                 getActiveTheme  = null)
    {
        _definitions          = definitions          ?? throw new ArgumentNullException(nameof(definitions));
        _actions              = actions              ?? throw new ArgumentNullException(nameof(actions));
        _fixtures             = fixtures             ?? throw new ArgumentNullException(nameof(fixtures));
        _screenshotsDirectory = screenshotsDirectory ?? throw new ArgumentNullException(nameof(screenshotsDirectory));
        _applyThemeAsync      = applyThemeAsync;
        _getActiveTheme       = getActiveTheme;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the refresh pass described by <paramref name="options"/>.
    /// When <see cref="ScreenshotRefreshOptions.Mode"/> is
    /// <see cref="ScreenshotRefreshMode.None"/> the method returns immediately.
    /// </summary>
    /// <param name="options">Specifies whether to refresh all or a single named definition.</param>
    /// <param name="ct">Cancellation token.  Cancellation aborts between definitions, not mid-capture.</param>
    public async Task RunAsync(ScreenshotRefreshOptions options, CancellationToken ct = default)
    {
        using var log = new ScreenshotRefreshLog(_screenshotsDirectory);

        IReadOnlyList<ScreenshotDefinition> targets;

        switch (options.Mode)
        {
            case ScreenshotRefreshMode.All:
                targets = _definitions.All;
                if (targets.Count == 0)
                {
                    log.Write("[screenshot] No definitions found — nothing to refresh.");
                    return;
                }
                break;

            case ScreenshotRefreshMode.Named:
                if (string.IsNullOrWhiteSpace(options.TargetName))
                    throw new ArgumentException(
                        "TargetName must be set when Mode is Named.", nameof(options));

                var named = _definitions.TryGet(options.TargetName);
                if (named is null)
                {
                    log.Write($"[screenshot] FAILED: definition '{options.TargetName}' not found.");
                    return;
                }
                targets = [named];
                break;

            default:
                // ScreenshotRefreshMode.None or any future value — nothing to do.
                return;
        }

        foreach (var definition in targets)
        {
            ct.ThrowIfCancellationRequested();
            await RunOneAsync(definition, log, ct).ConfigureAwait(false);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the theme-suffixed file-stem for <paramref name="definition"/>,
    /// matching the naming contract defined in <see cref="ScreenshotManifest"/>:
    /// <c>{name}-{theme}</c> (e.g. <c>"the-coordinator-light"</c>).
    /// Falls back to <c>definition.Name</c> alone when the theme is absent or "Both".
    /// </summary>
    private static string ThemeSuffixedName(ScreenshotDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Theme)
            || string.Equals(definition.Theme, "Both", StringComparison.OrdinalIgnoreCase))
            return definition.Name;

        return $"{definition.Name}-{definition.Theme.ToLowerInvariant()}";
    }

    // ── Per-definition pipeline ────────────────────────────────────────────────

    private async Task RunOneAsync(
        ScreenshotDefinition definition,
        ScreenshotRefreshLog  log,
        CancellationToken     ct)
    {
        log.Write($"[screenshot] Starting: {definition.Name}");

        var themedStem    = ThemeSuffixedName(definition);
        var outputPath    = Path.Combine(_screenshotsDirectory, $"{themedStem}.png");
        IReplayableUiAction? action          = null;
        var                  fixtureApplied  = false;
        string?              originalTheme   = null;

        try
        {
            // ── Step 1.5 — Apply theme ───────────────────────────────────────
            if (_applyThemeAsync is not null
                && !string.IsNullOrWhiteSpace(definition.Theme)
                && !string.Equals(definition.Theme, "Both", StringComparison.OrdinalIgnoreCase))
            {
                originalTheme = _getActiveTheme?.Invoke();
                await _applyThemeAsync(definition.Theme).ConfigureAwait(false);
                await Task.Delay(50, ct).ConfigureAwait(false); // allow theme resources to settle
            }
            // TODO: iterate twice for Both (capture -light and -dark variants separately)

            // ── Step 2 — Apply fixture ────────────────────────────────────────
            var fixture = ScreenshotFixture.Empty;

            if (!string.IsNullOrWhiteSpace(definition.FixturePath))
            {
                // Resolve relative paths from the screenshots directory.
                var fixturePath = Path.IsPathRooted(definition.FixturePath)
                    ? definition.FixturePath
                    : Path.Combine(_screenshotsDirectory, definition.FixturePath);

                if (File.Exists(fixturePath))
                {
                    var fixtureJson = await File.ReadAllTextAsync(fixturePath, ct)
                        .ConfigureAwait(false);
                    fixture = JsonSerializer.Deserialize<ScreenshotFixture>(fixtureJson, s_readOptions)
                              ?? ScreenshotFixture.Empty;
                }
                else
                {
                    log.Write($"[screenshot] Warning: fixture file not found for '{definition.Name}': {fixturePath}");
                }
            }

            await _fixtures.ApplyAllAsync(fixture, ct).ConfigureAwait(false);
            fixtureApplied = true;

            // ── Step 3 — Replay action ────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(definition.ReplayActionId))
            {
                if (!_actions.TryGet(definition.ReplayActionId, out action) || action is null)
                    throw new InvalidOperationException(
                        $"ReplayActionId '{definition.ReplayActionId}' is not registered in UiActionReplayRegistry.");

                if (!action.IsSideEffectFree)
                    throw new InvalidOperationException(
                        $"Action '{definition.ReplayActionId}' has IsSideEffectFree=false " +
                        "and cannot be used in an unattended refresh run.");

                await action.ExecuteAsync(ct).ConfigureAwait(false);

                // Poll IsReadyAsync — max 10 s, 100 ms interval.
                const int    MaxWaitMs       = 10_000;
                const int    PollIntervalMs  = 100;
                var          elapsed         = 0;
                var          ready           = false;

                while (elapsed < MaxWaitMs)
                {
                    ct.ThrowIfCancellationRequested();
                    ready = await action.IsReadyAsync().ConfigureAwait(false);
                    if (ready)
                        break;

                    await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
                    elapsed += PollIntervalMs;
                }

                if (!ready)
                    throw new TimeoutException(
                        $"Action '{definition.ReplayActionId}' did not reach ready state within 10 seconds.");
            }

            // ── Step 4 — Signal MainWindow to capture ─────────────────────────
            Directory.CreateDirectory(_screenshotsDirectory);
            var captureArgs = new ScreenshotCaptureRequestedEventArgs(
                definition.Name,
                outputPath,
                definition.Bounds,
                definition.Top,
                definition.Right,
                definition.Bottom,
                definition.Left);
            CaptureRequested?.Invoke(this, captureArgs);

            var captureError = await captureArgs.WaitAsync().ConfigureAwait(false);
            if (captureError is not null)
                throw new InvalidOperationException($"Capture failed: {captureError}");

            // ── Step 5 — Compare to baseline ─────────────────────────────────
            var baselinePath = Path.Combine(_screenshotsDirectory, "baseline", $"{themedStem}.png");
            var comparison   = ScreenshotComparator.Compare(baselinePath, outputPath);

            if (comparison.Skipped)
                log.Write($"[screenshot] Comparison skipped for '{definition.Name}' (no baseline).");
            else if (comparison.DimensionMismatch)
                log.Write($"[screenshot] Comparison FAILED for '{definition.Name}': dimension mismatch.");
            else
                log.Write($"[screenshot] Comparison: {comparison.MatchPercent:F2}% match " +
                          $"({comparison.DiffPixels} of {comparison.TotalPixels} pixels differ)" +
                          (comparison.DiffImagePath is not null ? $" — diff: {comparison.DiffImagePath}" : ""));

            // ── Step 6 (success) — Log the saved path ─────────────────────────
            log.Write($"[screenshot] Saved: {outputPath}");

            // ── Step 6b — Copy to doc image path if this is a doc screenshot ──
            if (!string.IsNullOrWhiteSpace(definition.DocImagePath))
            {
                var fullDocImagePath = Path.GetFullPath(
                    Path.Combine(_screenshotsDirectory, definition.DocImagePath));
                Directory.CreateDirectory(Path.GetDirectoryName(fullDocImagePath)!);
                File.Copy(outputPath, fullDocImagePath, overwrite: true);
                log.Write($"[screenshot] Copied to doc image: {fullDocImagePath}");
            }
        }
        catch (OperationCanceledException)
        {
            log.Write($"[screenshot] Cancelled: {definition.Name}");
            throw;
        }
        catch (Exception ex)
        {
            // ── Step 7 (failure) ─────────────────────────────────────────────
            log.Write($"[screenshot] FAILED: {definition.Name} — {ex.Message}");
        }
        finally
        {
            // ── Step 8 — Restore state ───────────────────────────────────────
            if (fixtureApplied)
            {
                try
                {
                    await _fixtures.RestoreAllAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Write($"[screenshot] Warning: fixture restore failed for '{definition.Name}' — {ex.Message}");
                }
            }

            if (action is not null)
            {
                try
                {
                    await action.UndoAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Write($"[screenshot] Warning: action undo failed for '{definition.Name}' — {ex.Message}");
                }
            }

            if (originalTheme is not null && _applyThemeAsync is not null)
            {
                try
                {
                    await _applyThemeAsync(originalTheme).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Write($"[screenshot] Warning: theme restore failed for '{definition.Name}' — {ex.Message}");
                }
            }
        }
    }
}

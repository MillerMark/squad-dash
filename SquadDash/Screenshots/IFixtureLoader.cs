using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Screenshots;

/// <summary>
/// Applies and restores application state driven by a <see cref="ScreenshotFixture"/>
/// for one specific domain (e.g. agent-card state, transcript content, voice-feedback
/// overlay).
/// </summary>
/// <remarks>
/// <para>
/// Implementations are registered with a <see cref="FixtureLoaderRegistry"/> at
/// application startup.  The capture pipeline calls
/// <see cref="ApplyAsync"/> before invoking the replay action, and
/// <see cref="RestoreAsync"/> in the <c>finally</c> block — regardless of whether
/// capture succeeded.
/// </para>
/// <para>
/// <strong>Key-visibility contract:</strong> Each loader declares the fixture keys
/// it understands via <see cref="KnownKeys"/>.  Any key present in the
/// <see cref="ScreenshotFixture.Data"/> bag that is <em>not</em> in
/// <see cref="KnownKeys"/> is silently ignored by convention.  This allows fixtures
/// to contain data for multiple domains without each loader needing awareness of
/// the others.  Brittleness detection (warning on unrecognised keys) is deferred to
/// a later phase.
/// </para>
/// <para>
/// Implementations must be idempotent for <see cref="RestoreAsync"/>: if the state
/// has already been restored (e.g. because the user navigated away), the method
/// must succeed silently.
/// </para>
/// </remarks>
public interface IFixtureLoader
{
    /// <summary>
    /// Apply the fixture data to the current application state.
    /// Called before <see cref="IReplayableUiAction.ExecuteAsync"/> in the
    /// screenshot capture pipeline.
    /// </summary>
    /// <param name="fixture">The fixture whose <see cref="ScreenshotFixture.Data"/> bag should be applied.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct);

    /// <summary>
    /// Restore the application state to what it was before <see cref="ApplyAsync"/>.
    /// Called in the <c>finally</c> block of the capture pipeline — guaranteed to
    /// run even when capture or replay throws.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RestoreAsync(CancellationToken ct);

    /// <summary>
    /// The set of <see cref="ScreenshotFixture.Data"/> keys this loader recognises.
    /// Keys present in the fixture but absent from this list are silently ignored.
    /// </summary>
    IReadOnlyList<string> KnownKeys { get; }
}

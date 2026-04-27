using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Screenshots;

/// <summary>
/// Central registry of <see cref="IFixtureLoader"/> implementations, keyed by a
/// short domain name (e.g. <c>"agentCard"</c>, <c>"transcript"</c>,
/// <c>"voiceFeedback"</c>).
/// </summary>
/// <remarks>
/// <para>
/// The registry is instantiated once by <c>MainWindow</c> and held as a
/// <c>private readonly</c> field, following the existing manual-construction
/// pattern in this codebase (no IoC container).  Concrete loaders are registered
/// in Phase 3; at present the registry exists to anchor the wire-up point.
/// </para>
/// <para>
/// <strong>Fan-out semantics:</strong> <see cref="ApplyAllAsync"/> dispatches the
/// fixture to every registered loader sequentially in registration order.  Each
/// loader inspects <see cref="IFixtureLoader.KnownKeys"/> against the fixture bag
/// and handles only the keys it owns; unknown keys are silently skipped.
/// </para>
/// <para>
/// <strong>Restore ordering:</strong> <see cref="RestoreAllAsync"/> calls
/// <see cref="IFixtureLoader.RestoreAsync"/> on every registered loader in
/// <em>reverse</em> registration order, mirroring the
/// constructor/destructor discipline and ensuring that loaders that layered state
/// on top of others tear it down first.
/// </para>
/// </remarks>
public sealed class FixtureLoaderRegistry
{
    // Stored as (domain, loader) pairs to preserve registration order and domain labels.
    private readonly List<(string Domain, IFixtureLoader Loader)> _registrations = [];

    // ── Registration ────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a loader for the given <paramref name="domain"/>.
    /// </summary>
    /// <param name="domain">
    ///   Short camelCase name identifying the application domain managed by this
    ///   loader (e.g. <c>"agentCard"</c>).  Used for diagnostics only — not
    ///   matched against fixture keys.
    /// </param>
    /// <param name="loader">The loader to register.</param>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="domain"/> is null or whitespace, or
    ///   <paramref name="loader"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   A loader for <paramref name="domain"/> is already registered.
    ///   Duplicate domain names indicate a programming error.
    /// </exception>
    public void Register(string domain, IFixtureLoader loader)
    {
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentNullException(nameof(domain));
        if (loader is null)                    throw new ArgumentNullException(nameof(loader));

        if (_registrations.Exists(r => string.Equals(r.Domain, domain, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"A fixture loader for domain '{domain}' is already registered.");

        _registrations.Add((domain, loader));
    }

    // ── Apply / Restore ─────────────────────────────────────────────────────

    /// <summary>
    /// Fans the fixture out to every registered loader in registration order.
    /// Each loader is responsible for filtering the bag to its own
    /// <see cref="IFixtureLoader.KnownKeys"/>; unknown keys are ignored.
    /// </summary>
    /// <param name="fixture">The fixture to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ApplyAllAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (fixture is null) throw new ArgumentNullException(nameof(fixture));

        foreach (var (_, loader) in _registrations)
        {
            ct.ThrowIfCancellationRequested();
            await loader.ApplyAsync(fixture, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Calls <see cref="IFixtureLoader.RestoreAsync"/> on every registered loader
    /// in <em>reverse</em> registration order, ensuring loaders that layered state
    /// on top of others always tear down first.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RestoreAllAsync(CancellationToken ct)
    {
        for (var i = _registrations.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            await _registrations[i].Loader.RestoreAsync(ct).ConfigureAwait(false);
        }
    }

    // ── Diagnostics ─────────────────────────────────────────────────────────

    /// <summary>
    /// Read-only snapshot of registered domain names in registration order.
    /// Intended for diagnostics and tooling output only.
    /// </summary>
    public IReadOnlyList<string> RegisteredDomains
    {
        get
        {
            var domains = new string[_registrations.Count];
            for (var i = 0; i < _registrations.Count; i++)
                domains[i] = _registrations[i].Domain;
            return domains;
        }
    }
}

---
title: "ADR: Replay UI Action Architecture"
nav_exclude: true
---

# ADR: Replay UI Action Architecture

**Status:** Accepted  
**Date:** 2025-07  
**Author:** Orion Vale (Lead Architect)  
**Phase:** Screenshot Automation — Phase 2 (interface layer)  
**Related commit:** Phase 1 — `831f5e1`

---

## Context

The Phase 1 screenshot feature lets users manually trigger captures of the live application window.  Phase 2 requirement (surfaced by Mira Quill): the screenshot tooling must be able to **programmatically recreate transient UI states** — open dialogs, expanded menus, voice-dictation overlay, agent info panels — so that screenshots of those states can be re-captured automatically without a human present.

Transient UI states are, by definition, not visible at application launch.  Without a replay mechanism, every re-capture run requires a human to manually reproduce the state.  At scale (multiple themes × multiple UI states × CI pipeline), that is not viable.

---

## Decision

Introduce an **`IReplayableUiAction`** abstraction and a **`UiActionReplayRegistry`** that together form a named, enumerable catalogue of programmatically invocable UI state transitions.  The interface is defined in `SquadDash/Screenshots/` and registered at application startup.

### Why `IReplayableUiAction` rather than recording click sequences

**Click-sequence recording** (simulate mouse/keyboard input) was considered and rejected for three reasons:

1. **Fragility**: recorded coordinates break on DPI changes, window resize, or any layout refactor — all routine events in an active WPF project.
2. **Opacity**: a click log provides no semantic signal about *what* state it produces; debugging failures requires replaying blindly.
3. **Side-effect risk**: simulated input can accidentally trigger unintended actions (e.g., menu items that send network requests) because the simulation does not distinguish between safe and unsafe operations.

An explicit `IReplayableUiAction` implementation is authored by the developer who owns the UI state.  It is semantically named, version-controlled, and explicitly marks whether it is safe for automated pipelines.

---

## Interface Design

```csharp
public interface IReplayableUiAction
{
    string ActionId { get; }       // stable kebab-case, e.g. "open-agent-info-dialog"
    string Description { get; }    // human-readable, surfaced in Mira's manifest catalogue
    bool   IsSideEffectFree { get; }
    Task   ExecuteAsync(CancellationToken ct);
    Task<bool> IsReadyAsync();
    Task   UndoAsync();
}
```

### `IsReadyAsync` contract

`IsReadyAsync` returns `true` when the UI has reached a **stable, capturable state**.  The caller (screenshot pipeline) polls this before triggering capture.

"Stable" means:

- The target element (dialog, panel, overlay) is **visible and laid out**: `IsVisible == true`, `ActualWidth > 0`, `ActualHeight > 0`.
- **No in-progress layout invalidations**: the WPF layout pass triggered by `ExecuteAsync` has completed.
- **Entry animations have settled**: if the window uses a fade-in or slide-in animation, `IsReadyAsync` must delay until animation duration has elapsed.  The Phase 3 base class will provide a configurable `AnimationSettleDelay` (default: 150 ms) for this purpose.
- **Dispatcher queue is drained** at or above `DispatcherPriority.Render`: no pending layout, render, or loaded callbacks remain.

Phase 2 stub implementations may return `Task.FromResult(true)` after verifying visibility.  Phase 3 concrete implementations must satisfy the full contract above.

### `UndoAsync` contract

`UndoAsync` restores the UI to its **pre-`ExecuteAsync` state**.

Caller responsibilities:

- `UndoAsync` **must be called after every `ExecuteAsync`**, whether the capture succeeded or not (callers use `try/finally`).
- Callers must not assume `UndoAsync` fires on the same thread as `ExecuteAsync`; implementations must dispatch to the WPF thread if required.

Implementation responsibilities:

- **Idempotent**: if the UI is already in the pre-action state (e.g., the dialog was closed by the user before undo ran), `UndoAsync` must succeed silently — never throw.
- **Scope**: only undo what `ExecuteAsync` did.  Do not reset unrelated state.
- **No exceptions for expected conditions**: `ObjectDisposedException` during shutdown, `InvalidOperationException` for a window that has already been GC'd — swallow these silently.

---

## `IsSideEffectFree` and Automated Re-Capture Safety

Some UI actions have side-effects that cannot be undone:

| Example | Problem |
|---------|---------|
| "Send message" button | Network request fired, message persisted |
| "Install Squad" trigger | Background process launched |
| "Save settings" with dirty state | Persists unsaved user edits |

Automated re-capture pipelines (e.g., CI screenshot diffs) **must exclude** any action where `IsSideEffectFree == false`.

**Mitigation — explicit opt-in:**

- Every `IReplayableUiAction` implementation **must** declare `IsSideEffectFree`.
- The screenshot pipeline must filter `UiActionReplayRegistry.All` to `IsSideEffectFree == true` before running unattended.
- Actions that are `IsSideEffectFree == false` can still be used interactively (with a human at the keyboard), but never in automated runs.

---

## `UiActionReplayRegistry` — Pattern and Registration

The registry is a plain class, instantiated once by `MainWindow` as a `private readonly` field, following the existing manual-construction pattern in this codebase (no IoC container).

```csharp
private readonly UiActionReplayRegistry _uiActionReplayRegistry = new();
```

Actions are registered at startup in `RegisterUiReplayActions()`, called at the end of the `MainWindow` constructor after all services and windows are available.

**Why a registry rather than a static list?**

- Actions require live delegates (references to window instances, settings stores, etc.) that only exist after construction.  A static list would require a two-phase initialisation or static side-table.
- The registry is testable in isolation: tests can construct an instance, register mock actions, and verify enumeration without a live WPF application.
- Future: `UiActionReplayRegistry` can be exposed via a named pipe or local HTTP endpoint for integration with external tooling (Phase 4 scope).

**Naming convention**: `ActionId` values are stable kebab-case strings.  They will be persisted in `ScreenshotManifest` files (see below) and must not change once published.  Treat them as a public API.

---

## Integration Point: `ScreenshotManifest.ReplayActionId`

> **Note for Mira Quill (working in parallel):** the `ScreenshotManifest` schema should include an optional field:
>
> ```
> ReplayActionId?: string   // kebab-case ActionId from UiActionReplayRegistry
> ```
>
> When present, the screenshot pipeline must:
>
> 1. Look up the action: `registry.TryGet(manifest.ReplayActionId, out var action)`
> 2. Assert `action.IsSideEffectFree == true` before running in automated mode.
> 3. Call `await action.ExecuteAsync(ct)`.
> 4. Poll `await action.IsReadyAsync()` (with a configurable timeout, suggested 5 s).
> 5. Capture.
> 6. Call `await action.UndoAsync()` in a `finally` block.
>
> When absent, the screenshot pipeline assumes the target UI state is already visible (e.g., the main window at rest).

---

## Alternatives Considered

| Alternative | Rejected because |
|-------------|-----------------|
| Click-sequence recording | Fragile on layout change, no semantic safety signal |
| Accessibility API automation (UI Automation) | Couples screenshot tooling to the accessibility tree; overkill for this scenario; harder to test |
| External scripting (AutoHotKey, Playwright Desktop) | Out-of-process; requires a separate test harness; can't access internal WPF state |
| Static factory methods on each window | Scatters the catalogue across many files; no central enumeration point for Mira's tooling |

---

## Phase Roadmap

| Phase | Scope |
|-------|-------|
| Phase 1 (done — `831f5e1`) | Manual screenshot capture via `ScreenshotOverlayWindow` |
| **Phase 2 (this ADR)** | `IReplayableUiAction`, `UiActionReplayRegistry`, one stub (`OpenPreferencesWindowAction`), `ScreenshotManifest.ReplayActionId` field definition |
| Phase 3 | Concrete implementations for all capturable transient states; `IsReadyAsync` production contract (animation settle, dispatcher drain) |
| Phase 4 | Automated re-capture pipeline; CI integration; manifest catalogue generation |

---

## Amendment: Fixture Infrastructure (Phase 2 — commit TBD)

**Status:** Accepted  
**Author:** Orion Vale (Lead Architect)

### Context

Screenshots of transient UI states often require specific *data* to be present before capture — an agent card must be populated with a particular agent, a transcript must contain certain messages, a voice-feedback overlay must be in a specific state.  Without a mechanism to pre-populate that data deterministically, re-capture runs produce inconsistent results or require manual setup.

### Decision — Generic `Dictionary<string, JsonElement>` Fixture Bag

Fixtures use a **generic `IReadOnlyDictionary<string, JsonElement>` key-value bag** (`ScreenshotFixture.Data`), not typed interfaces.

**Rationale:**

- Typed fixture interfaces were considered and deferred.  A typed contract requires agreement on a shared schema before any concrete loader is authored; premature schema commitment would create friction in Phase 3 as the set of capturable states is still being discovered.
- `JsonElement` values allow any JSON-representable structure to be carried without code-generation or shared assemblies.  Fixture definitions can be authored as plain JSON files and deserialized at load time without custom converters.
- The untyped bag keeps the interface boundary thin: adding a new fixture key for a new domain requires no change to `ScreenshotFixture`, `IFixtureLoader`, or `FixtureLoaderRegistry`.

**Trade-off accepted:** There is no compile-time guarantee that a fixture key is spelled correctly or carries the expected type.  A misspelled key will be silently ignored (see `KnownKeys` contract below).  Brittleness detection (warning or assertion on unrecognised keys) is explicitly deferred to a later phase.

### `KnownKeys` Contract — Unknown Keys Silently Ignored

Each `IFixtureLoader` implementation declares the fixture-bag keys it owns via `IFixtureLoader.KnownKeys`.  The loader is responsible for reading only those keys from `ScreenshotFixture.Data` and ignoring everything else.

**Convention:** Keys present in the fixture bag that are absent from every registered loader's `KnownKeys` are silently ignored.  This is a deliberate design choice:

- Fixtures can contain data for multiple domains without any loader needing awareness of other loaders.
- A fixture definition remains valid even if a loader that previously consumed one of its keys is removed.
- The cost is that misspelled or obsolete keys produce no warning.

**Future:** A later phase will add a `FixtureLoaderRegistry.AuditAsync(ScreenshotFixture)` method that enumerates all keys not claimed by any loader and logs them as warnings.  This closes the brittleness gap without making it a hard error prematurely.

### `RestoreAsync` Ordering Guarantee — Reverse Registration

`FixtureLoaderRegistry.RestoreAllAsync` calls each loader's `RestoreAsync` in **reverse registration order** — the last loader registered is the first to restore.

**Rationale:** Loaders may layer state on top of one another.  For example, a `transcriptLoader` might populate a conversation that an `agentCardLoader` needs to be visible to select.  Restoring in reverse order ensures that dependent state (agent card selection) is torn down before the state it depends on (transcript content) is removed, preventing transient assertion failures or visual glitches during teardown.

This is the same discipline as constructor/destructor ordering in resource-management patterns (RAII, `IDisposable` stacks).

`ApplyAllAsync` preserves forward registration order for symmetry.

### `IReplayableUiAction.DefaultFixture`

A `ScreenshotFixture? DefaultFixture` property with a default interface implementation returning `ScreenshotFixture.Empty` was added to `IReplayableUiAction`.

This allows an action to carry its own default fixture — the minimum data state needed for that action to produce a meaningful screenshot — without requiring every screenshot manifest to repeat it.  The capture pipeline applies the fixture in this precedence:

1. **Manifest-level fixture override** — stored in the `ScreenshotManifest` definition (highest priority).
2. **`DefaultFixture` on the action** — used when no manifest override is present.
3. **`ScreenshotFixture.Empty`** — the default implementation; no state population.

Existing `IReplayableUiAction` implementations are unaffected: the default interface implementation returns `ScreenshotFixture.Empty` automatically without any code change.

### Wire-Up — `MainWindow._fixtureLoaderRegistry`

`MainWindow` holds `private readonly FixtureLoaderRegistry _fixtureLoaderRegistry = new();`, following the same pattern as `_uiActionReplayRegistry`.  No concrete loaders are registered in Phase 2; the field establishes the wire-up point for Phase 3.

### Deferred Work

The following are explicitly out of scope for this phase and will be specced separately:

- **Typed fixture interfaces**: schema-validated, strongly-typed counterparts to the generic bag — deferred until the full set of capturable states is known.
- **Brittleness-detection tests**: assertions or warnings for unrecognised fixture keys, misspelled key names, and type-mismatch scenarios.
- **`FixtureLoaderRegistry.AuditAsync`**: enumeration of unclaimed fixture keys for diagnostic output.

# ADR: Screenshot Metadata Schema (Phase 2)

**Status:** Accepted  
**Date:** 2026-04-24  
**Authors:** Mira Quill (Documentation & Memory Specialist)  
**Related files:**
- `SquadDash/Screenshots/ScreenshotManifest.cs`
- `SquadDash/Screenshots/ScreenshotNamingHelper.cs`
- `docs/screenshots/README.md`

---

## Context

Phase 1 (commit `831f5e1`) shipped the screenshot capture UI — a drag-select
overlay that saves a PNG to `docs/screenshots/baseline/`.  Phase 2 adds a JSON
sidecar written alongside every PNG so that captures are machine-readable and
suitable for automated re-capture and visual regression.

The core question for this ADR is: **what structural metadata is sufficient for
a screenshot to be reliably re-captured and compared?**

---

## Decisions

### 1. C# records, not classes

**Decision:** Use positional `record` types for all schema objects.

**Rationale:**
- Immutable by default (`init`-only properties) — manifests are write-once.
- Structural equality comes for free — useful for snapshot comparison in tests.
- Compact declaration and built-in `ToString()` for debugging.
- `System.Text.Json` on .NET 7+ natively resolves primary constructors for
  deserialization without an explicit `[JsonConstructor]`.

**Trade-off:** Records cannot be subclassed easily, but this schema is intended
to be versioned (via the `version` field) rather than extended via inheritance.

---

### 2. Four edge anchors (Top / Right / Bottom / Left)

**Decision:** Store one anchor per capture-region edge.  Each anchor records the
WPF element whose *matching* edge is inside the region and closest to that edge.

**Rationale:**
- Four anchors fully constrain re-capture: a tool can search the visual tree for
  the named elements, measure their positions, and reconstruct the original
  capture rectangle.
- "Matching edge" rule (top anchor → element's top edge, right anchor → element's
  right edge, etc.) is simpler than "nearest edge to any edge" and more
  semantically stable across layout reflows.
- Pixel distance to edge (`distanceToEdge`) lets a re-capture tool apply a small
  tolerance without hard-coding pixel coordinates.

**Alternative considered:** Store a single "primary control" anchor.  Rejected
because a single anchor cannot constrain all four sides of an arbitrary region.

---

### 3. `NeedsName` flag on anchors

**Decision:** When an anchor's closest element has no `x:Name`, record
`elementName: null` and set `needsName: true` (rather than omitting the anchor).

**Rationale:**
- An unnamed element cannot be reliably found in a re-capture pass without
  fragile positional heuristics.
- Surfacing `needsName: true` in the sidecar makes the gap visible in diffs
  and tooling, encouraging engineers to add names before a baseline is locked.
- Keeping the anchor record (with null name and bounding box) still provides
  debugging context (what element was closest, how big it was).

---

### 4. Schema versioning via `version` field

**Decision:** Include an integer `version` field, starting at `1`.

**Rationale:**
- Any future breaking change (e.g., adding mandatory fields, changing anchor
  semantics) can be gated on `version` at read time.
- A flat integer is simpler to compare than a semver string for schema evolution.

---

### 5. Naming convention — kebab-case, theme-suffixed filenames

**Decision:**
- `name` field: theme-neutral kebab-case identifier (e.g. `"agent-card-selected"`).
- Filename: `{name}-{theme}.png` / `{name}-{theme}.json` — theme is part of the
  filename stem, not the logical name.

**Rationale:**
- Separating name from theme allows a single logical capture to exist in both
  Dark and Light variants without duplicating metadata (except the theme field).
- Kebab-case is universally safe for filenames across Windows/macOS/Linux.
- Appending the theme to the filename (rather than the `name` field) keeps the
  `name` field reusable as a logical identifier in tooling and reports.

---

### 6. `ScreenshotNamingHelper.SuggestName` — anchor-derived naming

**Decision:** Provide a static helper that derives a kebab-case name suggestion
from the anchor element names, with a timestamp fallback for fully unnamed captures.

**Rationale:**
- Reduces friction: most captures will have at least one named anchor, and the
  helper turns `AgentStatusCard` → `agent-status-card-dark` automatically.
- PascalCase splitting via a source-generated regex (`[GeneratedRegex]`) avoids
  allocations on the hot path and is AOT-friendly.
- The timestamp fallback (`capture-{theme}-{yyyyMMddHHmmss}`) ensures every
  capture gets a unique name even when the visual tree is entirely unnamed.

**Naming prompt template** (for AI-assisted interactive naming):
```
Given these anchor element names: {Top}, {Right}, {Bottom}, {Left}
and the theme "{Theme}", suggest a concise kebab-case screenshot name.
Format: [primary-control]-[state?]-[variant?]-[theme]
```

---

### 7. Archive-on-replace baseline strategy

**Decision:** When a new capture replaces an existing baseline, move the old
pair to `docs/screenshots/archive/{name}-{theme}-{timestamp}/`.

**Rationale:**
- Preserves history for visual regression archaeology without bloating the
  `baseline/` folder.
- A timestamped archive folder makes it trivial to see how a capture evolved.
- Moving (not deleting) keeps git history intact via `git mv`.

---

### 8. `.gitattributes` — `docs/screenshots/**/*.png binary`

**Decision:** Add a `binary` attribute for all screenshot PNGs.

**Rationale:**
- Prevents git's line-ending normalisation from corrupting PNG byte sequences.
- Makes `git diff` skip binary-noise diffs for PNG files in PRs.
- Applies to both baseline and archive subtrees via the `**` glob.

---

### 9. `agentOrder` fixture key — reproducible agent panel ordering

**Status:** Accepted — implemented in `SquadDash/Screenshots/Fixtures/AgentOrderFixtureLoader.cs`

**Decision:** Add a fixture key `agentOrder` (ordered array of agent name strings) that
reorders the `ObservableCollection<AgentStatusCard>` in-place before capture and
restores the original order after.

**Rationale:**
- Agent panels are populated at runtime in connection-arrival order, which varies
  between sessions.  Without an explicit ordering fixture, "same screenshot" baselines
  would show different card sequences on different machines or runs.
- In-place reordering via `ObservableCollection<T>.Move` avoids replacing the entire
  collection, keeping data-binding subscriptions intact.
- Agents not listed in `agentOrder` are appended at the end in their original relative
  order — the fixture is a *partial sort*, not a required enumeration.  Missing agent
  names warn via `Debug.WriteLine` and are silently skipped rather than thrown.

**Registration contract:** `AgentOrderFixtureLoader` must be registered
*after* `ViewModeFixtureLoader` (panel visibility must be set before card order
matters) and *before* `AgentCardFixtureLoader` (individual card state depends on
cards being in their final positions).

**Trade-off:** `Move` dispatches `CollectionChanged` events for every swap.  For
panels with many agents this is a non-trivial number of notifications.  Acceptable
because fixture apply/restore is off the main render path (dispatch is synchronous
and guarded by `DispatcherPriority.Normal`).

---

### 10. Transcript `$ref` deduplication — shared message arrays

**Status:** Accepted — implemented in `SquadDash/Screenshots/Fixtures/TranscriptFixtureLoader.cs`

**Decision:** If `fixture.Data["messages"]` is a JSON *object* containing a `"$ref"`
string property, resolve that string as a repo-relative path, load the referenced file,
and use its contents (which must be a JSON array) as the messages array.  Inline
arrays are unchanged and remain fully backward compatible.

**Rationale:**
- Multiple definitions can reference the same long transcript without duplicating it
  inside every fixture JSON file.
- The `$ref` pattern is recognisable to developers familiar with JSON Schema / OpenAPI;
  it carries its own meaning without additional documentation.
- File I/O for `$ref` resolution happens off the dispatcher thread, so the UI thread
  is not blocked during resolution.

**Failure contract:** All failure paths (file not found, parse error, root is not an
array) emit a `Debug.WriteLine` warning and return `Task.CompletedTask` — warning-only,
no throw.  This is consistent with the existing warning-only contract across all fixture
loaders.

**Alternative considered:** Embed a `"$include"` mechanism at the fixture-bag level
(resolved before any loader sees the data).  Rejected to avoid coupling the loading
contract to a pre-processing step; the `$ref` check is self-contained inside
`TranscriptFixtureLoader` and does not affect other loaders.

---

### 11. Scroll position fixtures — `transcriptScrollOffset`, `activeRosterScrollOffset`, `inactiveRosterScrollOffset`

**Status:** Accepted — implemented in `SquadDash/Screenshots/Fixtures/ScrollPositionFixtureLoader.cs`

**Decision:** Add three fixture keys that set the scroll position of scrollable UI
regions before capture and restore them after.

| Key | Type | Target element |
|-----|------|----------------|
| `transcriptScrollOffset` | `double` | Vertical scroll offset of `OutputTextBox` (managed by `TranscriptScrollController`) |
| `activeRosterScrollOffset` | `double` | Horizontal scroll offset of `ActiveAgentsScrollViewer` |
| `inactiveRosterScrollOffset` | `double` | Horizontal (and vertical) scroll offset of `InactiveAgentsScrollViewer` |

**Implementation:** `ScrollPositionFixtureLoader` owns all three keys.

**Rationale:**
- Transcript and roster panels are often taller/wider than the visible viewport.
  Without a scroll-offset fixture, screenshots of partially-scrolled states cannot be
  reproduced deterministically.
- Grouping all scroll-offset keys in a single loader avoids per-scroller loader
  proliferation while keeping the fixture bag key names explicit about which scroller
  each value applies to.
- Restoring scroll position after capture is essential — leaving scrollers at a
  non-default position would corrupt the UI state visible to the user.

**Trade-off:** Scroll offset is in logical pixels (WPF device-independent units), so
captures made at different DPI scales will need regenerating if the layout changes.
This is the same trade-off accepted for `distanceToEdge` in anchor records.

---

### 12. Prompt box text fixture — `promptText`

**Status:** Accepted — implemented in `SquadDash/Screenshots/Fixtures/PromptTextFixtureLoader.cs`

**Decision:** Add a fixture key `promptText` (string) that populates `PromptTextBox`
with a specific string before capture and clears (restores) it after.

**Implementation:** `PromptTextFixtureLoader` owns the `promptText` key.

**Rationale:**
- Screenshots of the input area (empty prompt, typed query, long overflow text) are a
  common screenshot category.  Without a fixture, capturing these states requires manual
  typing before each screenshot session, which is not automatable.
- Restore-after-capture is important because `PromptTextBox` text is user-authored state;
  overwriting it silently without restoring would be surprising and destructive.

**Alternative considered:** Use a replay action (`IReplayableUiAction`) to type the
text.  Rejected because replay actions are for UI interactions that affect navigation or
view state; populating a text field with known content is fixture work, not a UI action.

---

### 13. `IHaveUniqueName` — data-context-derived anchor names for templated elements

**Status:** Accepted — implemented in `SquadDash/VisualTreeEdgeAnalyzer.cs` and `AgentStatusCard`

**Decision:** Introduce an `IHaveUniqueName` interface with a single
`string UniqueName { get; }` property.  `AgentStatusCard` (the data-context type
bound to each agent card `DataTemplate`) implements it by returning `Name`.
`VisualTreeEdgeAnalyzer.FindAnchor` checks
`(element as FrameworkElement)?.DataContext as IHaveUniqueName` as a fallback when
no `x:Name` is present; the returned string is used as the unique name in
`EdgeAnchorRecord.ElementNames`.

**Rationale:**
- All agent card instances in an `ItemsControl` share the same XAML `x:Name`
  (`"AgentCardBorder"`) because the name comes from the `DataTemplate`, not the item.
  WPF name scopes mean the `x:Name` is not unique across items — only the first
  registered instance is found by `FindName`.
- `IHaveUniqueName` lets the analyzer derive a unique, semantically meaningful name
  from the card's own data model (e.g. `"orion-vale"`, `"lyra-morn"`) without
  requiring every `DataTemplate` element to be renamed in XAML.
- This eliminates `needsName: true` anchors on agent card sub-region captures, which
  were previously unavoidable for any region that landed on an unnamed templated
  element.

**Affected file:** `SquadDash/VisualTreeEdgeAnalyzer.cs`

**Trade-off:** Coupling the visual-tree analyzer to a data-model interface is a mild
abstraction leak.  Kept to a single interface check (a cast, not a method call) to
minimise the coupling surface.  Any data context that does not implement
`IHaveUniqueName` falls back to the existing `needsName: true` path — no regression
for non-card elements.

---

## ScreenshotDefinition and the Definitions Registry

> Added: Phase 2 follow-up  
> Related files: `SquadDash/Screenshots/ScreenshotManifest.cs`, `SquadDash/Screenshots/ScreenshotDefinitionRegistry.cs`, `docs/screenshots/definitions.json`

### Definition vs. Manifest — the distinction

Two complementary schema objects describe the same screenshot from different
angles:

| Type | Answers | Lifecycle | Location |
|------|---------|-----------|----------|
| `ScreenshotManifest` | *What was captured?* — the factual record of a single capture event | Written once per capture, immutable | `{name}-{theme}.json` sidecar alongside PNG |
| `ScreenshotDefinition` | *How do I reproduce this?* — the durable recipe | Upserted on every interactive capture; evolves as the UI changes | `docs/screenshots/definitions.json` (shared registry) |

A manifest is a snapshot in time.  A definition is the living source of truth
for re-capture automation.  They share the same `name` and `theme` fields so
that tooling can cross-reference them.

### `ScreenshotDefinition` additions to the schema

`ScreenshotDefinition` introduces two fields that are also present on
`ScreenshotManifest` (where they document what was used at capture time):

- **`replayActionId`** (`string?`): The `IReplayableUiAction.ActionId` of the
  action to replay before capture.  Matches the `ReplayActionId` added to
  `ScreenshotManifest` in this phase.  `null` means the screenshot requires
  no programmatic UI setup.

- **`fixturePath`** (`string?`): Repo-relative path to a fixture JSON file
  that is loaded before capture to put the application in a known data state.
  `null` means no fixture.  This is the "fixture bag" approach — fixtures are
  freestanding JSON files, not embedded in the schema, so they can evolve
  independently.

Both fields are nullable.  A definition with both `null` describes a capture
that is purely geometry-driven (the current window state at the time of capture).

### Upsert behaviour in `ScreenshotDefinitionRegistry`

`ScreenshotDefinitionRegistry.AddOrUpdate(definition)` implements a
**name-keyed upsert** (case-insensitive):

- If no entry exists with the same `name`, the definition is appended.
- If an entry exists, it is **replaced in-place**, preserving list order.
- Empty `description` emits a `Console.Error` warning but does not throw —
  consistent with the manifest's existing warning-only contract.
- Changes are buffered in memory until `SaveAsync()` is called.

This means `definitions.json` is always the most recently saved recipe.
Historical definition snapshots are NOT preserved — use `git log` if you need
to trace how a definition evolved over time.

### Fixture bag approach

Fixtures are referenced by path, not inlined.  Rationale:

- Fixtures can be shared across multiple definitions (same data, different
  capture regions).
- A fixture file can be updated independently of the definition that references
  it without touching `definitions.json`.
- The path is repo-relative, making fixtures portable across machines.

There is no schema enforcement on fixture file contents — the consuming code
(capture pipeline / test fixtures) is responsible for validation.

---

## Consequences

- **Lyra** (or the implementing dev) wires up JSON serialisation using
  `System.Text.Json` with `WriteIndented = true`.  The schema is locked; the
  save/load logic is deliberately out of scope for this ADR.
- Any capture with one or more `needsName: true` anchors is considered an
  *incomplete baseline* — flagging this in PR checks is a future work item.
  With `IHaveUniqueName` now implemented (Decision 13), agent-card captures
  resolve agent names from the data context, eliminating the most common source of
  `needsName: true` in roster captures.
- The `version` field must be checked at read time; a reader encountering
  `version > 1` should log a warning and attempt best-effort parsing rather
  than throwing.
- **Fixture keys implemented to date (Decisions 9–12, plus existing loaders):**
  `viewMode`, `agentOrder`, `agentCard`, `messages` / `scrollToBottom` (with `$ref`
  support), `voiceFeedback`, `quickReply`, `backgroundTask`, `windowGeometry`,
  `transcriptScrollOffset`, `activeRosterScrollOffset`, `inactiveRosterScrollOffset`
  (via `ScrollPositionFixtureLoader`), and `promptText` (via `PromptTextFixtureLoader`).
- The `agentOrder` key is position-sensitive: it must be registered between
  `viewMode` and `agentCard` in `FixtureLoaderRegistry`.  Any future loader that
  depends on card order must register after `agentOrder`.

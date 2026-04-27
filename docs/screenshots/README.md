# Screenshot Storage Convention

> **Schema version:** 1  
> **Introduced:** Phase 2 (metadata)  
> **Last updated:** see git log

---

## Overview

Every PNG capture is accompanied by a JSON sidecar that carries structural
metadata about the capture: geometry, theme, edge anchors, and a human-readable
name.  The two files are always treated as a pair.

---

## File Naming

| File | Pattern | Example |
|------|---------|---------|
| Image | `{name}-{theme}.png` | `agent-card-selected-dark.png` |
| Sidecar | `{name}-{theme}.json` | `agent-card-selected-dark.json` |

### Name rules

- **kebab-case**, all lowercase, no spaces.  
  ✅ `agent-card-selected-dark`  
  ❌ `AgentCard_SelectedDark`, `agent card selected dark`
- The `theme` suffix is part of the filename, not the `name` field — names
  are theme-neutral identifiers.  The same region captured in both themes
  shares the same `name`.
- `description` is **required**.  Saving a manifest with an empty
  `description` will emit a console warning (it is not a hard error, but
  baselines with empty descriptions are considered incomplete).
- The JSON sidecar must be **pretty-printed** (`WriteIndented = true`).

### Name suggestion

Use `ScreenshotNamingHelper.SuggestName(theme, anchors)` to derive a candidate
name from the WPF element names recorded in the edge anchors.  If all four
anchors are unnamed the helper falls back to `capture-{theme}-{yyyyMMddHHmmss}`.

**Interactive naming prompt (for Mira Quill):**

```
Given these anchor element names: {Top}, {Right}, {Bottom}, {Left}
and the theme "{Theme}", suggest a concise kebab-case screenshot name
that describes the captured UI region.

Format: [primary-control]-[state?]-[variant?]-[theme]
Examples: "agent-card-selected-dark", "toolbar-hover-light", "sidebar-collapsed-dark"
```

---

## Folder Structure

```
docs/screenshots/
├── README.md                         ← this file
├── baseline/                         ← current approved baselines
│   ├── agent-card-selected-dark.png
│   ├── agent-card-selected-dark.json
│   └── ...
└── archive/                          ← superseded baselines
    └── {name}-{theme}-{timestamp}/   ← one folder per replaced capture
        ├── {name}-{theme}.png
        └── {name}-{theme}.json
```

### Baseline folder

`docs/screenshots/baseline/` holds the **current approved** PNG + JSON pair
for every named screenshot.  One pair per name/theme combination.

### Archive folder

When a new capture replaces an existing baseline, the old pair is moved to:

```
docs/screenshots/archive/{name}-{theme}-{timestamp}/
```

where `{timestamp}` is `yyyyMMddTHHmmssZ` (UTC, ISO 8601 compact).  This
preserves history without cluttering the baseline folder.

---

## `.gitattributes` rule

Screenshot PNGs are stored as binary to prevent line-ending normalisation:

```gitattributes
docs/screenshots/**/*.png binary
```

This rule is set in the repository root `.gitattributes`.

---

## Definitions Registry (`definitions.json`)

### What is it?

`docs/screenshots/definitions.json` is the **recipe book** for the screenshot
suite.  Each entry is a `ScreenshotDefinition` — a durable description of
*how* to reproduce a screenshot.  It is managed by `ScreenshotDefinitionRegistry`
and is committed alongside PNG baselines.

### How it relates to PNG + sidecar JSON

| File | Purpose | Written by |
|------|---------|-----------|
| `{name}-{theme}.png` | The captured image | Capture pipeline (Lyra) |
| `{name}-{theme}.json` | Manifest — record of *what* was captured | Capture pipeline (Lyra) |
| `definitions.json` | Definitions — recipe for *how* to re-capture | Interactive capture + registry |

A PNG and its sidecar are written **once per capture** and are immutable.
`definitions.json` is **upserted** — each interactive capture adds or replaces
the entry for that name, so the file always reflects the latest recipe.

### `ScreenshotDefinition` fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | `string` | ✅ | Kebab-case theme-neutral identifier, e.g. `"agent-card-selected-dark"`. Matches the PNG filename stem. |
| `description` | `string` | ✅ (warn if empty) | Human-readable description of what the screenshot shows. |
| `theme` | `string` | ✅ | `"Dark"`, `"Light"`, or `"Both"`. When `"Both"`, the runner captures under each theme. |
| `replayActionId` | `string?` | nullable | `IReplayableUiAction.ActionId` to replay before capture. `null` means no action setup. |
| `fixturePath` | `string?` | nullable | Repo-relative path to a fixture JSON loaded before capture. `null` means no fixture. |
| `top` / `right` / `bottom` / `left` | `EdgeAnchorRecord` | ✅ | Four edge anchors (see [Edge Anchors](#edge-anchors) below). Used to reconstruct the capture region during re-capture. |
| `bounds` | `CaptureBounds` | ✅ | Capture region geometry recorded at definition save time. Fallback when element lookup fails. |

### How interactive capture appends to this file

1. The user drag-selects a region and confirms the capture in the overlay UI.
2. The capture pipeline (Lyra) saves `{name}-{theme}.png` and `{name}-{theme}.json`
   to `docs/screenshots/baseline/`.
3. A `ScreenshotDefinition` is constructed from the current capture parameters
   and passed to `ScreenshotDefinitionRegistry.AddOrUpdate(definition)`.
4. The registry upserts the entry by `name` (case-insensitive) — replacing any
   existing entry with the same name.
5. `registry.SaveAsync()` writes the updated `definitions.json`.

> ⚠️ **Lyra's responsibility:** Step 3–5 are wired up in the capture pipeline.
> The schema layer (`ScreenshotDefinitionRegistry`) does not write to the file
> automatically — `SaveAsync()` must be called explicitly.

### How the command-line runner uses it

The `--refresh-screenshots` command-line flag triggers an automated re-capture
pass that works as follows:

1. Load the registry: `ScreenshotDefinitionRegistry.LoadAsync(screenshotsDir)`.
2. Iterate `registry.All`.
3. For each definition:
   - If `ReplayActionId` is set, locate the action in `UiActionReplayRegistry`
     and call `ExecuteAsync()`.  Await `IsReadyAsync()` before proceeding.
   - If `FixturePath` is set, load the fixture JSON and apply it to the UI.
   - Reconstruct the capture bounds from the four edge anchors (walk the WPF
     visual tree to find each named element; apply `distanceToEdge` offsets).
   - Capture the region and write the PNG + sidecar.
   - Call `UndoAsync()` on the replay action (if any) to restore UI state.
4. After all definitions are processed, `SaveAsync()` is NOT called again —
   the runner only reads definitions, it does not modify them.

> **Tip:** Adding `--refresh-screenshots` to a CI step automatically keeps
> baselines fresh when the UI changes, provided all anchors have `needsName: false`.

---

## Fixture Keys Reference

A fixture file is a JSON object whose properties are consumed by registered
`IFixtureLoader` implementations.  Each loader handles only its own keys; unknown
keys are silently skipped.  The file is referenced from a `ScreenshotDefinition`
via `fixturePath` (repo-relative path).

### Example fixture file

```json
{
  "viewMode": "active",
  "agentOrder": ["orion-vale", "lyra-morn", "arjun-sen"],
  "messages": { "$ref": "docs/screenshots/fixtures/long-conversation.json" },
  "scrollToBottom": true
}
```

### Implemented keys

| Key | Type | Loader | Description |
|-----|------|--------|-------------|
| `viewMode` | `string` | `ViewModeFixtureLoader` | Sets the active/inactive view toggle. |
| `agentOrder` | `string[]` | `AgentOrderFixtureLoader` | Ordered list of agent names. Cards are reordered to match; unlisted agents go to the end. Registered after `viewMode`, before `agentCard`. |
| `agentCard` | `object` | `AgentCardFixtureLoader` | Per-card state patches (status, typing indicator, etc.). |
| `messages` | `array` or `{ "$ref": "…" }` | `TranscriptFixtureLoader` | Populates the coordinator transcript. Accepts an inline message array or a `$ref` object pointing to a repo-relative JSON file whose root is an array. Inline and `$ref` are interchangeable; the loader resolves the reference before applying. |
| `scrollToBottom` | `bool` | `TranscriptFixtureLoader` | When `true` (the default), scrolls `OutputTextBox` to the end after applying messages. |
| `voiceFeedback` | `object` | `VoiceFeedbackFixtureLoader` | Voice-activity indicator state. |
| `quickReply` | `object` | `QuickReplyFixtureLoader` | Quick-reply chip visibility and content. |
| `backgroundTask` | `object` | `BackgroundTaskFixtureLoader` | Background-task badge state. |
| `windowGeometry` | `object` | `WindowGeometryFixtureLoader` | Window size and position overrides. |
| `transcriptScrollOffset` | `double` | `ScrollPositionFixtureLoader` | Vertical scroll offset of `OutputTextBox`. |
| `activeRosterScrollOffset` | `double` | `ScrollPositionFixtureLoader` | Horizontal scroll offset of `ActiveAgentsScrollViewer`. |
| `inactiveRosterScrollOffset` | `double` | `ScrollPositionFixtureLoader` | Horizontal/vertical scroll offset of `InactiveAgentsScrollViewer`. |
| `promptText` | `string` | `PromptTextFixtureLoader` | Text to populate `PromptTextBox` before capture; cleared on restore. |

All scroll-offset values are in logical pixels (WPF device-independent units).

### `$ref` in `messages`

If the transcript is long or shared across multiple definitions, use a `$ref` instead
of an inline array:

```json
{ "messages": { "$ref": "docs/screenshots/fixtures/my-thread.json" } }
```

The referenced file must be a JSON array at the root.  If the file is missing or
cannot be parsed, the loader emits a `Debug.WriteLine` warning and skips the key —
it does not throw.

---

## Edge Anchors

Each manifest stores four `EdgeAnchorRecord` entries — one per capture-region
edge.  Each anchor identifies:

| Field | Meaning |
|-------|---------|
| `edge` | Which edge: `"Top"`, `"Right"`, `"Bottom"`, `"Left"` |
| `elementName` | `x:Name` of the closest WPF element, a data-context-derived name (see below), or `null` |
| `needsName` | `true` when `elementName` is `null` (element should be named) |
| `elementLeft/Top/Width/Height` | Element bounding box in logical pixels |
| `distanceToEdge` | Distance from element's matching edge to capture-region edge, in logical pixels |

**Matching-edge rule:**  For the `"Top"` anchor, the element's *top* edge is
compared to the capture region's *top* edge.  For `"Right"`, the element's
*right* edge is compared to the capture region's *right* edge.  And so on.

**NeedsName warning:**  Any anchor with `needsName: true` means the element is
not reliably addressable for automated re-capture.  Add `x:Name` to that
element in XAML and re-capture.

**Data-context names (`IHaveUniqueName`):**  Elements
inside an `ItemsControl` `DataTemplate` share the same `x:Name` across all
instances — WPF name scopes mean only one is reachable by `FindName`.
`VisualTreeEdgeAnalyzer` falls back to `(element as FrameworkElement)?.DataContext
as IHaveUniqueName` when no `x:Name` is present.  Types that implement
`IHaveUniqueName` supply a per-instance unique name (e.g. `AgentStatusCard` returns
`Name`), which is recorded as `elementName`.  This eliminates spurious `needsName: true`
anchors on agent-card sub-region captures.

---

## JSON Schema Reference

See `SquadDash/Screenshots/ScreenshotManifest.cs` for the authoritative C#
record definitions.

### Example sidecar

```json
{
  "version": 1,
  "name": "agent-card-selected-dark",
  "description": "Agent status card in selected state, dark theme",
  "theme": "Dark",
  "region": "full",
  "capturedAt": "2026-04-24T16:00:00Z",
  "bounds": {
    "x": 0,
    "y": 0,
    "width": 1280,
    "height": 800,
    "dpiX": 1.25,
    "dpiY": 1.25
  },
  "top": {
    "edge": "Top",
    "elementName": "AgentStatusCard",
    "needsName": false,
    "elementLeft": 12,
    "elementTop": 4,
    "elementWidth": 320,
    "elementHeight": 80,
    "distanceToEdge": 4
  },
  "right": {
    "edge": "Right",
    "elementName": null,
    "needsName": true,
    "elementLeft": 940,
    "elementTop": 200,
    "elementWidth": 320,
    "elementHeight": 80,
    "distanceToEdge": 12
  },
  "bottom": {
    "edge": "Bottom",
    "elementName": "StatusBar",
    "needsName": false,
    "elementLeft": 0,
    "elementTop": 776,
    "elementWidth": 1280,
    "elementHeight": 24,
    "distanceToEdge": 0
  },
  "left": {
    "edge": "Left",
    "elementName": "SidePanel",
    "needsName": false,
    "elementLeft": 0,
    "elementTop": 48,
    "elementWidth": 240,
    "elementHeight": 752,
    "distanceToEdge": 0
  }
}
```

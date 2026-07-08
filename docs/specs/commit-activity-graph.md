# Commit Activity Graph — Feature Spec

**Status:** Draft — approved architecture from Orion Vale, design decisions from Lyra Morn  
**Authors:** Lyra Morn (UI/rendering), Arjun (service layer)  
**Last updated:** 2026-07-08

---

## 1. Overview

The **Commit Activity Graph** is a floating window that visualises git commit activity per feature group across time. Each feature group occupies a horizontal row. A thin colored line marks the feature's active date range; filled dots mark individual days that had commits, with dot size encoding commit volume.

The window opens lazily — data is fetched only when it is first opened — and loads incrementally as the user expands the visible time range using a slider. An in-memory cache prevents redundant git calls when the user collapses and re-expands the range.

**Scope split:**
- **Arjun** owns `CommitStatService` (pure, stateless, interface-first) and the in-memory cache.
- **Lyra** owns `CommitActivityGraphWindow` — rendering, orchestration, slider, tooltips, and loading-state visuals.

---

## 2. Data Model

### 2.1 `CommitStatResult`

One instance per resolved SHA.

```csharp
public record CommitStatResult(
    string Sha,              // Full or short SHA that was queried
    string FeatureGroupId,   // Maps to FeatureGroup; null maps to "Uncategorized"
    DateOnly TurnDate,       // TurnStartedAt calendar date (NOT git author/commit date)
    int FilesChanged,
    int Insertions,
    int Deletions,
    bool IsFound            // false = SHA not found in repo (missing/failed)
);
```

`IsFound = false` records are persisted in the cache so the same SHA is never re-queried.

### 2.2 `ICommitStatService`

```csharp
public interface ICommitStatService
{
    /// <summary>
    /// Fetches commit stats for the supplied SHAs that are not already cached.
    /// Already-cached SHAs are returned immediately from cache without spawning git.
    /// Progress is reported per resolved batch via the callback.
    /// Workspace path is fixed at construction — not passed per call.
    /// </summary>
    Task<IReadOnlyList<CommitStatResult>> GetStatsAsync(
        IEnumerable<string> shas,
        IProgress<IReadOnlyList<CommitStatResult>>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns any already-cached result for the given SHA, or null if not yet fetched.
    /// </summary>
    CommitStatResult? TryGetCached(string sha);
}
```

`CommitStatService` is registered as a **singleton** in the DI container so the cache survives window close/reopen. The workspace `FolderPath` is passed to the constructor — this product only runs against a single workspace at a time.

---

## 3. Caching Strategy

- The cache is **in-memory only** — no disk persistence, no serialisation.
- Backed by a `ConcurrentDictionary<string, CommitStatResult>` keyed on SHA.
- A cache hit is **immutable** — once a SHA is stored, its record never changes. No invalidation logic needed.
- `IsFound = false` SHAs are also cached. Re-opening the window or expanding the slider will not retry them.
- On first open the cache is empty; the visible range (default 30 days) is fetched in full.
- When the user expands the slider leftward (older dates), newly revealed SHAs are diffed against the cache; only uncached SHAs are sent to git.
- Collapsing and re-expanding the slider to a previously-seen range returns all data instantly from cache — no git calls.

---

## 4. Fetch Strategy

### 4.1 Lazy load on open

`CommitActivityGraphWindow` queries the turn repository for all turn records in the **currently displayed time range** (default: last 30 days). Each turn record carries a `CommitSha`. The window calls `ICommitStatService.GetStatsAsync` with those SHAs.

### 4.2 Incremental fetch on slider expansion

When the slider range changes:

1. Collect turn records for the newly revealed date range.
2. Extract SHAs. Filter out SHAs already in cache via `TryGetCached`.
3. Call `GetStatsAsync` with the uncached subset only.
4. Merge returned results into the existing display.

### 4.3 Batched git calls

Inside `CommitStatService`, uncached SHAs are fetched using:

```
git log --no-walk --format="%H %ad" --date=short <sha1> <sha2> ... <shaN>
```

SHAs are sent in **batches of 50** to bound process-spawn overhead. Batches run with **bounded parallelism of 8 concurrent processes** (`SemaphoreSlim(8)`).

For diffstat (`--numstat`) use a follow-up call on the same batch if needed, or extend the format string — implementation detail left to Arjun.

### 4.4 Workspace path

`CommitStatService` receives the workspace `FolderPath` at **construction** (constructor parameter). This product runs against a single workspace at a time — no per-call path is needed. `CommitActivityGraphWindow` passes `FolderPath` when constructing the service (same pattern as `HandleCheckGitDiffAsync` in `MainWindow.xaml.cs`).

### 4.5 Short SHA ambiguity

Short SHAs are an acceptable risk. If git cannot resolve a SHA the result is recorded as `IsFound = false` and the hollow-dot fallback renders (see §6). No retry logic.

---

## 5. Visual Design

### 5.1 Overall Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [Slider: 30d ◄────────────────────────────────────────────► 365d]        │
│                                                                           │
│  Feature Name A  │▓▓▓▓▓▓▓▓▓●─────────●──●─────────────────│            │
│  Feature Name B  │         ●──────────●─●──────────────────│            │
│  Uncategorized   │─────────────────────────●───────────────│            │
│                                                                           │
│  [X-axis date labels]                                                     │
└──────────────────────────────────────────────────────────────────────────┘
```

- **Left column:** Feature group names, left-aligned, fixed width (~160 px). Sorted alphabetically. **"Uncategorized"** (null `Label`) is **pinned to the top** of the list, above all alphabetical entries.
- **Right area:** Scrollable timeline canvas. One row per feature group.
- Row height: 32 px. Line is vertically centered in the row.

### 5.2 Color Palette

7 palette entries, each with a dark-theme and light-theme variant. Features are assigned by `alphabetical_index mod 7`.

| Index | Name        | Dark theme hex | Light theme hex |
|-------|-------------|----------------|-----------------|
| 0     | Coral       | `#FF6B6B`      | `#C0392B`       |
| 1     | Teal        | `#4ECDC4`      | `#148A82`       |
| 2     | Amber       | `#FFD93D`      | `#B8860B`       |
| 3     | Lavender    | `#A29BFE`      | `#5E35B1`       |
| 4     | Sage        | `#6BCB77`      | `#2E7D32`       |
| 5     | Peach       | `#FFA07A`      | `#BF5722`       |
| 6     | Sky         | `#74B9FF`      | `#1565C0`       |

Dark theme background reference: `AppSurface` = `#1D1B18`  
Light theme background reference: `AppSurface` = `#F5F0EB`

Opacity rules:
- **Line:** drawn at **100% opacity**.
- **Dots (resolved):** drawn at **50% opacity**.
- **Dots (loading/hollow):** drawn at **50% opacity** (outline only — see §6).

### 5.3 Line Rendering

- Thickness: **1 px**.
- Drawn from the X-position of the feature's **first recorded turn date** to the X-position of its **last recorded turn date**, inclusive.
- Does **not** extend to the edges of the full timeline — strictly bounded by the feature's own active range.
- Color: palette entry at 100% opacity.
- Rendered behind dots.

### 5.4 Dot Rendering

One dot per calendar day that has at least one resolved commit for the feature.

**Dot center:**
- Horizontal: X-coordinate of that calendar day on the timeline.
- Vertical: center of the row (i.e., on top of the line).

**Radius growth formula:**

Let `n` = number of commits on that day for that feature.

```
radius(n) = BASE_RADIUS × 1.4^(n−1)
```

| Commits | Multiplier | Example (base = 5 px) |
|---------|------------|----------------------|
| 1       | 1.00×      | 5.0 px               |
| 2       | 1.40×      | 7.0 px               |
| 3       | 1.96×      | 9.8 px               |
| 4       | 2.74×      | 13.7 px              |
| 5       | 3.84×      | 19.2 px              |

`BASE_RADIUS` = **5 px** (adjustable via a constant in the window code).

To avoid runaway sizes, cap at `BASE_RADIUS × 8` (40 px at default base).

**Dot fill:** solid fill, feature color, 50% opacity (`Alpha = 128`).

### 5.5 X-Axis

- Minimum granularity: **one calendar day**.
- Tick marks and labels at sensible intervals depending on the visible range (e.g., every 7 days for a 30-day view, every 30 days for a 365-day view).
- Label format: `MMM d` (e.g., "Jun 3").

---

## 6. Loading and Missing States

These states are mutually exclusive per dot position.

| State       | Visual                                              | Notes                                     |
|-------------|-----------------------------------------------------|-------------------------------------------|
| **Loading** (SHA in-flight) | Hollow circle — stroke only, no fill. Stroke = feature color at 50% opacity. Radius = `BASE_RADIUS`. | Replaced by filled dot once SHA resolves. Progress callback triggers re-render. |
| **Missing** (SHA not found / git error) | No dot rendered. The day is treated as having zero commits for layout purposes. | `IsFound = false` in cache. Avoids visual noise from broken SHAs. |
| **Resolved** | Filled dot, 50% opacity, radius per §5.4 formula. | Standard state. |

**Rationale for "no dot" on missing:** rendering an × glyph at small sizes is illegible and adds noise. Silence is preferable. If diagnosability is needed later, a per-row warning icon can be added as a future enhancement.

**Transition animation:** none required for MVP. A simple immediate swap from hollow to filled is acceptable.

---

## 7. Interaction

### 7.1 Time Range Slider

- Control: horizontal range slider at the top of the window.
- Left handle: start date (can be dragged leftward to expose older history).
- Right handle: end date (defaults to today; typically fixed).
- Default range: **30 days** ending today.
- Maximum range: **365 days** (can be revisited post-MVP).
- On drag: debounce 300 ms before triggering incremental fetch (§4.2) to avoid spamming git during fast drags.
- While new data is loading: already-resolved dots stay visible; newly revealed day positions show hollow dots.

### 7.2 Tooltips

**Hover over a resolved dot:**
```
Feature:   Authentication
Date:      Jun 3, 2026  (squad turn date — not git commit timestamp)
Commits:   3  (files changed: 12, +247 / −58)
```

**Hover over a loading (hollow) dot:**
```
Feature:   Authentication
Date:      Jun 3, 2026
Status:    Loading commit data…
```

**Hover over the line segment (not a dot):**
```
Feature:   Authentication
Active:    May 1, 2026 → Jun 14, 2026
```

Tooltip delay: 400 ms (standard WPF ToolTipService default).

### 7.3 Scrolling

- If the feature list exceeds the window height, the left-column labels and the timeline rows scroll together vertically (synchronized scroll).
- Horizontal scroll is not exposed directly — the slider controls the visible date range. The timeline canvas redraws to fit the window width.

---

## 8. X-Axis Semantics Note

> **Important:** The X-axis position of every dot is determined by `TurnStartedAt` — the date the *squad turn began* — **not** by the git author date or commit timestamp.
>
> A commit may have been made hours or days after the turn started; the graph intentionally places it at the turn date to reflect when the work was *assigned / initiated*, not when git recorded it.
>
> All tooltip copy must reflect this: use "squad turn date" or "turn date", never "committed on" or "commit date".

This distinction is enforced at the data-model level: `CommitStatResult.TurnDate` is populated from `TurnStartedAt` by the caller (the window), not derived from git metadata.

---

## 9. Build Sequence

### Step 1 — Arjun (unblocks Lyra)

Arjun's **first commit** must include:
- `ICommitStatService` interface (§2.2)
- `CommitStatResult` record (§2.1)
- Empty/stub `CommitStatService` (returns empty list, no git calls) so Lyra can compile and begin layout work

Arjun's **subsequent commits**:
- Real `CommitStatService` implementation with batched git calls (§4.3)
- In-memory cache (§3)
- Bounded parallelism (`SemaphoreSlim(8)`)
- Unit tests for cache hit/miss, batch partitioning, `IsFound = false` handling

### Step 2 — Lyra (can begin after Step 1 commit 1)

- `CommitActivityGraphWindow.xaml` / `.xaml.cs`
- Layout: label column + scrollable timeline canvas
- Color palette constants (§5.2)
- Line rendering (§5.3)
- Dot rendering with radius formula (§5.4)
- Loading (hollow) and missing (no-dot) states (§6)
- Time range slider with debounced fetch trigger (§7.1)
- Tooltips (§7.2)
- X-axis date labels (§5.5)
- Integration: wire `CommitStatService` via constructor, passing `workspaceFolderPath` at construction

### Step 3 — Integration review

Both authors review the wired-up window together. Confirm:
- Cache behavior across slider interactions
- Hollow → filled transition on resolution
- Color assignments match across light/dark themes
- Tooltip copy uses "squad turn date" language

---

## 10. Resolved Decisions

All open questions closed 2026-07-08.

| # | Question | Decision |
|---|----------|----------|
| 1 | Should insertions/deletions drive additional visual encoding beyond the tooltip? | **Tooltip only.** No additional visual encoding. Dot radius encodes commit count only. |
| 2 | Max slider range: hard 365-day cap or "load all" option? | **365-day hard cap.** Acceptable for this product. |
| 3 | `CommitStatService` path: per-call or scoped at construction? | **Scoped at construction.** Single workspace path passed to the constructor. `GetStatsAsync` no longer takes `workspaceFolderPath`. |
| 4 | Should canvas redraw immediately on light/dark theme toggle? | **Yes.** `CommitActivityGraphWindow` must listen to the app theme-change event and redraw the canvas immediately with the new palette variant. |
| 5 | "Uncategorized" row position? | **Pinned to top** (overrides earlier "bottom" recommendation). |

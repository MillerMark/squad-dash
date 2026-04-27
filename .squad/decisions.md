# Squad Decisions

## Active Decisions

---

### 2026-04-17 — MainWindow decomposition approach

**Context:** `MainWindow.xaml.cs` had grown to 8,305 lines with 71 fields and 20+ distinct responsibility domains. Orion Vale's architectural audit graded it C. The class was a classic god-object: UI event handlers, agent thread lifecycle, background task tracking, PTT state machine, markdown rendering, prompt execution, conversation persistence, and OS/CLI integration all co-located.

**Decision:** Extract into focused helper classes using constructor injection with `Action<>`/`Func<>` delegates. Preserve the existing code-behind pattern — no MVVM migration.

**Rationale:** No ICommand/ViewModel infrastructure exists in the project. Introducing MVVM would multiply the change surface and require a parallel refactor of every data binding and event handler. Plain C# service objects with delegate injection achieve the same structural clarity (single responsibility, testable units) at a fraction of the risk. The `Action<>`/`Func<>` pattern keeps MainWindow as the single owner of all fields; helper classes call back into it for side effects rather than holding copies of state.

**Outcome:** 9 helper classes extracted over one session. MainWindow.xaml.cs reduced from 8,305 → 5,605 lines (−2,700 lines, −33%). Build: 0 errors throughout. Tests: 379/379 passing at every step.

**Files created:**
- `SquadDash/AgentStatusCard.cs` (322 lines) — `INotifyPropertyChanged` view-model for agent cards; accent colour palette; `SidebarEntry` types
- `SquadDash/ColorUtilities.cs` (54 lines) — Static HSL/RGB math helpers
- `SquadDash/SquadCliAdapter.cs` (124 lines) — OS/process interaction: CLI version resolution, PowerShell window launch, Explorer, external links
- `SquadDash/PushToTalkController.cs` (243 lines) — Double-Ctrl PTT state machine; Azure speech service lifecycle
- `SquadDash/MarkdownDocumentRenderer.cs` (772 lines) — Markdown → WPF `Block`/`Inline` conversion
- `SquadDash/AgentThreadRegistry.cs` (840 lines) — Agent thread lifecycle; aliasing; identity normalisation; 6–12 aliases per thread via `AliasThreadKeys`
- `SquadDash/TranscriptConversationManager.cs` (481 lines) — Conversation persistence: load/save/persist, turn records, history navigation, emergency save
- `SquadDash/BackgroundTaskPresenter.cs` (813 lines) — Background task tracking; completion detection; delayed-promotion pipeline; display label building
- `SquadDash/PromptExecutionController.cs` (923 lines) — Prompt execution: `ExecutePromptAsync`, all slash-command handlers, prompt health monitoring, universe selection, quick-reply disabling

---

### 2026-04-17 — PromptExecutionController partial-class phase skipped

**Context:** The decomposition plan recommended a Phase 1 partial-class split of MainWindow before the full PromptExecutionController extraction, as a de-risking step.

**Decision:** Skip the partial-class phase; implement both phases in a single pass.

**Rationale:** By the time PEC was reached, the delegation pattern was well-established from eight prior extractions. The partial-class step would have produced a PR consisting entirely of renames and file splits — zero behaviour change, but still requiring full review and test verification. The hardest parts (constructor wiring, `_isPromptRunning` ownership, `PromptHealthTimer` transfer, `ActiveToolName` relocation) were not made safer by the partial-class intermediate step.

**Outcome:** `PromptExecutionController.cs` extracted in one pass, 923 lines, 0 errors, 379/379 tests passing.

---

### 2026-04-17 — Workspace sidebar removed in favour of menu

**Context:** The `.squad Workspace` sidebar panel consumed a fixed 280px column and required a two-column grid layout in `MainWindow.xaml`. Its content (links to `.md` files in the `.squad` folder) duplicated navigation already available via the file system, and was always visible regardless of whether users needed it.

**Decision:** Remove the sidebar panel entirely. Move all workspace file links to a `Workspace` top-level menu item. Show individual `.md` file links only when those files exist on disk; always show "Squad Folder" (opens Explorer to `.squad`).

**Outcome:** `MainWindow.xaml` reverts to a single-column layout. `RefreshSidebar()` now populates `WorkspaceMenuItem.Items` dynamically. `SidebarEnabled` and `OpenSquadFolderEnabled` removed from `InteractiveControlState`.

---

### 2026-04-17 — Post-extraction architectural health check (Orion Vale, Audit #2)

**Context:** Following the 9-class MainWindow decomposition and completion of the full original backlog (layer fix, markdown dedup, `IWorkspacePaths`, `JsonFileStorage`, CI pipeline), a fresh architectural audit was conducted on the resulting codebase.

**Findings summary:**

| # | Severity | Concern |
|---|----------|---------|
| 1 | Critical | Zero test coverage for all 9 extracted classes (~4,000 lines of untested logic) |
| 2 | High | `AgentThreadRegistry` exposes backing `Dictionary`/`List` fields as mutable properties — callers bypass aliasing invariants |
| 3 | High | `PromptExecutionController` constructor has 40+ parameters — testing requires 40 mock lambdas; signals continued over-responsibility |
| 4 | High | `_isPromptRunning` ownership is ambiguous: field lives in MainWindow, written by PEC via delegate, read by BackgroundTaskPresenter via delegate |
| 5 | Medium | `TranscriptConversationManager` leaks all internal state as settable properties — acts as a data bag, not a service |
| 6 | Medium | `_toolEntries` dictionary owned by MainWindow, mutated at 8 callsites, with no encapsulating owner |
| 7 | Low | `QuickReplyInstruction` constant duplicated (MainWindow + PEC) — dead copy in MainWindow |
| 8 | Low | `QuickReplyAgentContinuationWindow` constant duplicated (MainWindow + MarkdownDocumentRenderer) |
| 9 | Low | `PromptNoActivityWarning/Stall` thresholds duplicated (MainWindow + PEC) — dead copies in MainWindow |

**Immediate fixes applied (this audit):**
- Removed dead `QuickReplyInstruction` constant from MainWindow (owned and used only by `PromptExecutionController`)
- Removed dead `PromptNoActivityWarningThreshold` and `PromptNoActivityStallThreshold` from MainWindow (owned and used only by `PromptExecutionController`)
- Removed duplicate `QuickReplyAgentContinuationWindow` from MainWindow; line 3043 now references `MarkdownDocumentRenderer.QuickReplyAgentContinuationWindow`
- All 388 tests pass; 0 build errors

**Delegated work items:**

**DEL-1 [Critical] — Test coverage for extracted classes** → Assign to Vesper Knox (testing & quality) — **COMPLETE**
~~Write unit tests for the extractable logic in: `AgentThreadRegistry` (thread key aliasing, `GetOrCreateAgentThread`, `FindAgentThread`), `TranscriptConversationManager` (history navigation, session ID management), `BackgroundTaskPresenter` (completion detection, label building), `ColorUtilities` (HSL/RGB math). Note: `MarkdownDocumentRenderer` and `PromptExecutionController` require WPF dispatcher — integration-test or use a thin seam/adapter pattern.~~
**Outcome (verified 2026-04-18):** `ColorUtilitiesTests.cs` (13 tests), `AgentThreadRegistryTests.cs` (17 tests), `BackgroundTaskPresenterTests.cs` (9 tests), `TranscriptConversationManagerTests.cs` (7 tests). Test project upgraded to `net10.0-windows` + `<UseWPF>true</UseWPF>`. `MainWindowStub.cs` created for static method surface. Total suite: 456 tests passing. Gaps deferred: `MarkdownDocumentRenderer` and `PromptExecutionController` (require live WPF dispatcher/STA environment).

**DEL-2 [High] — Seal `AgentThreadRegistry` collections** → ~~Assign to Arjun Sen~~ **COMPLETE**
~~Replace `internal Dictionary<string, TranscriptThreadState> ThreadsByKey` (and the other three exposed collections) with read-only facade accessors (`IReadOnlyDictionary`, `IReadOnlyList`). All mutation must go through `AgentThreadRegistry` methods. Add `ContainsThread`, `TryGetByToolCallId`, `AllThreads` accessors as needed. Callers are: MainWindow (6 sites), BackgroundTaskPresenter (3 sites), TranscriptConversationManager (4 sites).~~
**Outcome (verified 2026-04-18):** All four collections (`ThreadsByKey`, `ThreadsByToolCallId`, `LaunchesByToolCallId`, `ThreadOrder`) now typed as `IReadOnly*`. `ToolEntries` (formerly `_toolEntries` — see DEL-4) also exposed as `IReadOnlyDictionary`. All mutation internal to `AgentThreadRegistry`. AgentThreadRegistry.cs now 875 lines.

**DEL-3 [High] — Migrate `_isPromptRunning` ownership to `PromptExecutionController`** → Assign to Lyra Morn
Move the `_isPromptRunning` field from MainWindow into PEC as an `internal bool IsPromptRunning { get; private set; }`. Remove the `getIsPromptRunning`/`setIsPromptRunning` delegate pair from the PEC constructor. Update all MainWindow read-sites to use `_pec.IsPromptRunning`. This collapses ~5 constructor parameters into one reference.

**DEL-4 [Medium] — Encapsulate `_toolEntries` in `AgentThreadRegistry`** → ~~Assign to Arjun Sen (bundle with DEL-2)~~ **COMPLETE**
~~Move `_toolEntries: Dictionary<string, ToolTranscriptEntry>` from MainWindow into `AgentThreadRegistry` (it tracks tool calls per agent thread). Expose typed accessors: `GetOrAddToolEntry`, `TryGetToolEntry`, `AllIncompleteToolEntries`, `ClearAll`. Removes 8 raw-dict mutations from MainWindow.~~
**Outcome (verified 2026-04-18):** `ToolEntries` is `IReadOnlyDictionary<string, ToolTranscriptEntry>` on `AgentThreadRegistry`. Bundled with DEL-2 as planned.

**DEL-5 [Medium] — Reduce `TranscriptConversationManager` leaky setters** → Assign to Lyra Morn (bundle with DEL-3)
Replace the 6 raw settable properties (`ConversationState`, `CurrentSessionId`, `HistoryIndex`, etc.) with purpose-built methods (`BeginSession(sessionId)`, `ClearSession()`, `SetConversationState(state)`). Callers in MainWindow should use the method API.

**Deferred (observe-only):**
- `PromptExecutionController` 40-parameter constructor: structurally sound for now; will naturally improve as DEL-3 and DEL-2 reduce delegate count. Revisit after DEL-2/3/4.
- MainWindow still at 4,634 lines / 231 methods: below the alarm threshold post-extraction; monitor but no immediate action.

---

### 2026-04-17 — Persistence layer / display layer violation resolved; IWorkspacePaths contract defined

**By:** Orion Vale

**Layer violation (DONE):** `WorkspaceConversationStore` (persistence layer) was calling `ToolTranscriptFormatter.BuildDetailContent` (display layer). Fix: extracted `ToolTranscriptDescriptor`, `ToolTranscriptDetail`, `ToolEditDiffSummary`, and `ToolTranscriptDetailContent.Build()` into `ToolTranscriptData.cs`. `WorkspaceConversationStore` now calls `ToolTranscriptDetailContent.Build()` directly. No functional change — all 379 tests pass.

**IWorkspacePaths (contract defined, wiring pending):** `WorkspacePaths` was a mutable static service-locator. `IWorkspacePaths` interface and `WorkspacePathsProvider` (immutable, constructor-injected) have been created. Full call-site migration (20+ sites across UI, backend services, launcher) is delegated — see task below. Static `WorkspacePaths.cs` stays until migration is complete.

**Why:** Persistence layer must not depend on display layer — strict architectural rule. `IWorkspacePaths` enables constructor injection and eliminates the mutable-global initialization race. Both changes are zero functional-impact.

---

### 2026-04-17 — Task: Wire IWorkspacePaths across all call sites

**By:** Orion Vale (delegated)
**Owners:** Arjun Sen (backend: `SquadCliAdapter`, `SquadSdkProcess`, `SquadDashRuntimeStamp`, `PromptExecutionController`) + Lyra Morn (UI: `App.xaml.cs`, `MainWindow`, `AgentInfoWindow`, `WorkspaceIssuePresentation`) + Jae Min Kade (launcher: `SquadDashLauncher/Program.cs`) — **COMPLETE**

**What:** Replace all `WorkspacePaths.*` static calls with constructor-injected `IWorkspacePaths`. `App.xaml.cs` creates a `WorkspacePathsProvider` and passes it to `MainWindow`. Each service receives it as a constructor parameter. Delete `WorkspacePaths.cs` only after all call sites are migrated.

**Tests required:** `WorkspacePathsProviderTests.cs` — `Discover()`, constructor normalisation, all 4 properties non-empty, empty-string rejection.

**Outcome (verified 2026-04-18):** `IWorkspacePaths.cs` and `WorkspacePathsProvider.cs` present. `WorkspacePaths.cs` deleted (migration complete). `_workspacePaths` injected throughout `MainWindow.xaml.cs` and `App.xaml.cs`. `WorkspacePathsProviderTests.cs` present. ⚠️ README CI badge URL still has `{owner}/{repo}` placeholder — needs update when repo is pushed.

---

### 2026-04-17 — Task: Implement JsonFileStorage atomic-write helper

**By:** Orion Vale (delegated to Arjun Sen) — **COMPLETE**

**What:** Every store class (`ApplicationSettingsStore`, `PromptHistoryStore`, `RestartCoordinatorStateStore`, `RuntimeSlotStateStore`, `WorkspaceConversationStore`) duplicates a ~10-line atomic temp-file write pattern. Create `SquadDash\JsonFileStorage.cs` with `JsonFileStorage.AtomicWrite<T>(string path, T payload, JsonSerializerOptions? options = null)` and replace all 5 duplicates.

**Tests required:** `JsonFileStorageTests.cs` — new file (Move path), existing file (Copy/Delete path), partial-write safety, round-trip. Add `JsonFileStorage.cs` to test project compile items.

**Outcome (verified 2026-04-18):** `JsonFileStorage.cs` created (34 lines). All 5 stores confirmed delegating to `JsonFileStorage.AtomicWrite`. `JsonFileStorageTests.cs` present in test project.

---

### 2026-04-17 — Task: Remove markdown rendering duplication from MainWindow

**By:** Orion Vale (delegated to Lyra Morn) — **COMPLETE**

**What:** `MainWindow.xaml.cs` still contains duplicates of 11 markdown rendering methods that already live in `MarkdownDocumentRenderer.cs` (including `AppendInlineMarkdown`, `BuildMarkdownTable`, `TryReadMarkdownLink`, etc.). Replace all MainWindow call sites with calls through the renderer instance; promote methods from `private` to `internal` on the renderer as needed; delete the duplicates from MainWindow.

**Out of scope:** `SquadTeamRosterLoader.ParseMarkdownRow`, `MarkdownHtmlBuilder`, `MarkdownFlowDocumentBuilder`.

**Outcome (verified 2026-04-18):** No markdown method definitions remain in `MainWindow.xaml.cs`; all occurrences are call sites through `_markdownRenderer`. MarkdownDocumentRenderer.cs is 793 lines (canonical owner).

---

### 2026-04-17 — Task: CI pipeline (GitHub Actions)

**By:** Orion Vale (delegated to Jae Min Kade) — **COMPLETE**

**What:** Create `.github/workflows/ci.yml` — build and test on push to `main` and PRs to `main`. Use `windows-latest` (WPF requires Windows), .NET 10, Node 20 (for `Squad.SDK` esproj build). Steps: checkout → setup-dotnet → setup-node (with npm cache on `Squad.SDK/package-lock.json`) → `dotnet restore` → `dotnet build --no-incremental --no-restore` → `dotnet test SquadDash.Tests/SquadDash.Tests.csproj --no-build`.

**Outcome (verified 2026-04-18):** `.github/workflows/ci.yml` present and correctly structured. CI badge added to `README.md`. ⚠️ Badge URL uses `{owner}/{repo}` placeholder — Jae Min Kade to update with real GitHub repo coordinates when repo is pushed.

---

### 2026-04-22 — Policy: Agents must report bare commit hash in transcript after every commit

**Scope:** All agents · All commits · All sessions from 2026-04-22 onward

**What:** Every agent that makes a `git commit` must include the resulting **short commit hash (7 chars) as plain text** in their transcript response — placed immediately after describing the commit. Do **not** construct a markdown hyperlink or embed a GitHub URL.

**Format:**
```
Committed: `a1b2c3d`
```

**Example:**
> Committed all changes. `a1b2c3d`

**How to obtain the short hash after committing:**
```bash
git rev-parse --short HEAD   # → a1b2c3d
```

**Why:** SquadDash auto-detects bare commit hashes in transcript text and wraps them in the correct hyperlinks automatically — no manual URL construction needed. Constructing the URL manually caused agents to hallucinate the wrong repo owner/name (e.g. `bradygaster/SquadUI` instead of `MillerMark/SquadUI`), producing broken links. Plain hash = correct link every time.

**Anti-patterns:**
- ❌ Constructing a markdown link: `` [`a1b2c3d`](https://github.com/...) `` — causes hallucination, app auto-links anyway
- ❌ Mentioning a commit without including the hash at all
- ❌ Using the full 40-char hash (use short 7-char hash)
- ❌ Using a branch name or relative ref (`HEAD`) instead of the hash

---

### 2026-04-26 — Documentation Mode

**Date:** 2026-04-26  
**Author:** Lyra Morn  
**Status:** Implemented  

## Decision

Added in-app Documentation Mode to SquadUI. Toggled via View → View Documentation. When active, a resizable panel appears to the right of the transcript containing a topic tree and markdown viewer.

## Rationale

Provides discoverability for new users without leaving the app. Keeps workspace context visible while reading docs. Self-teaching UI reduces friction.

## Implementation

- **XAML layout:** `TranscriptPanelsGrid` now has 3 columns: transcript (auto-width), splitter (8px when visible), docs panel (600px when visible).
- **Docs panel:** Left side = TreeView with hierarchical topics. Right side = WebBrowser rendering markdown via `MarkdownHtmlBuilder.Build()`.
- **Theme-aware:** All surfaces use `DynamicResource` (PanelSurface, PanelBorder, LabelText) for light/dark theme compatibility.
- **Interaction with full-screen mode:** Docs panel hidden when full-screen transcript mode is active. Transcript takes priority.
- **Stub for future:** "Add Document" button present but not wired — placeholder for user-contributed docs.

## Welcome content

Default markdown welcome message introduces SquadUI features and instructs user to select topics from tree. Topics pre-populated with placeholder nodes (Getting Started, Agents, Workspace, Settings).

## Next steps

- Wire topic tree selection to load specific markdown content
- Implement "Add Document" workflow (file picker → copy to `.squad/docs/` → refresh tree)
- Persist docs mode state across sessions via `AppConfig.json`

---

### 2026-04-26 — Transcript UI fixes (3 of 4 complete)

**By:** Lyra Morn  
**Status:** 3 completed, 1 deferred

## Completed

**Fix 1: Title Format** ✅  
Changed secondary transcript title from "Agent — from 2 min ago" to "Agent - 2 min ago" in `BuildSecondaryTranscriptTitle`.

**Fix 3: Countdown Cancellation** ✅  
Transcript auto-close countdown now permanently cancels on user interaction (mouse move, clicks, wheel). Added `CountdownCancelled` flag to `SecondaryTranscriptEntry` and wired handlers on `PanelBorder`.

**Fix 4: Card Hover Glow** ✅  
Agent cards now animate a glow effect when hovering over open transcript panels. Added `MouseEnter`/`MouseLeave` handlers with `DropShadowEffect` and auto-reversing animation.

## Deferred

**Fix 2: "Transcript button must NOT close main transcript"** ⏳  
Requires clarification from user Mark003. Current behavior analysis shows `SelectTranscriptThread` changes the main view but does not hide the main panel. Possible interpretations:
1. Links should open as secondary panels rather than switching main view
2. Secondary panels should not auto-close when opening from within them
3. Existing behavior already implements requirement

**Recommendation:** Clarify actual unwanted behavior with user.

## Outcome

`eb3836b` — Fixes 1, 3, 4 committed. 724/726 tests passing (2 pre-existing unrelated failures).

### 2026-04-26 — Transcript link navigation pattern changed

**By:** Lyra Morn  
**Status:** Implemented

## Decision

Transcript hyperlinks inside the main transcript now open secondary panels instead of replacing the main transcript's content. The coordinator transcript still switches the main view when clicked.

## Context

When users clicked on transcript links (e.g., to view agent transcripts) from within the main transcript window, the `TranscriptHyperlink_Click` handler called `OpenTranscriptThread`, which invoked `SelectTranscriptThread`. This directly replaced the main transcript's document, hiding the current conversation and making it difficult to compare transcripts or return to the original context.

## Implementation

Modified `OpenTranscriptThread` method (MainWindow.xaml.cs, lines 4928-4943):

- For coordinator transcript links: Keep existing behavior (switch main view via `SelectTranscriptThread`)
- For agent transcript links: Use `OpenSecondaryPanel` instead (same code path as agent card clicks)
- Added `FindAgentCardForThread` lookup to map thread → card before opening panel
- Added null check for cases where no card exists for a thread

## Rationale

**Consistency:** Clicking an agent transcript link now behaves identically to clicking the agent's card — both open a secondary panel. Users get a predictable, unified navigation model.

**Non-destructive:** The main transcript remains visible and unchanged. Users can view multiple transcripts simultaneously and easily switch between contexts.

**Discoverability:** By reusing the existing secondary panel infrastructure, we leverage all the existing features (close buttons, title updates, auto-scrolling, accent colors, etc.) without code duplication.

## Impact

- Main transcript is never hidden/replaced when clicking agent transcript links
- Secondary panels can be opened from within transcripts (not just from agent cards)
- Coordinator transcript links still switch the main view (expected behavior for returning to main conversation)
- No breaking changes to existing panel management logic

## Testing

- Build: 0 errors, 0 warnings
- Tests: 724/726 passing (2 pre-existing failures in TranscriptSelectionTests unrelated to this change)
- Manual verification recommended: Click transcript links in main window and verify secondary panels open

## Commit

`6745aef` — "fix: transcript links open in new window instead of replacing main transcript"

---

---

### Decision: Update OnAgentLeftActivePanel Test Semantics

**Date:** 2024  
**Decided by:** Vesper Knox (Testing & Quality Specialist)  
**Status:** Implemented  
**Commit:** 47533dd

## Context

Two tests in `TranscriptSelectionTests.cs` were failing:
- `OnAgentLeftActivePanel_ClosesAllAgentPanels` — expected panels to close, but closedThreads was empty
- `OnAgentLeftActivePanel_LastPanel_FallsBackToMain` — expected ShowMainRequested to fire, but it didn't

Investigation revealed these were **stale tests** testing behavior that was intentionally removed in commit `0822ba2` ("shift-click empty transcript, voice PTT, docs panel preservation").

## The Design Change (0822ba2)

The production method `TranscriptSelectionController.OnAgentLeftActivePanel` was deliberately changed to a **no-op**:

```csharp
public void OnAgentLeftActivePanel(AgentStatusCard card)
{
    // Do not directly close panels here. MainWindow tracks whether a panel was
    // auto-opened versus user-pinned, and its auto-close countdown owns the
    // actual close timing.
}
```

**Rationale:** Panel auto-close timing is now owned by MainWindow's countdown mechanism, which knows whether a panel was auto-opened vs. user-pinned. `OnAgentLeftActivePanel` should not interfere with this logic.

## Decision

Update the two failing tests to **document the new no-op contract** rather than remove them:

### Test 1: `OnAgentLeftActivePanel_DoesNotClosePanels_MainWindowCountdownOwnsClose`
- **Previously expected:** Panels would close when an agent left the active panel
- **Now verifies:** Panels remain open; ClosePanelRequested does NOT fire
- **Why keep it:** Documents that panel closing is no longer this method's responsibility

### Test 2: `OnAgentLeftActivePanel_DoesNotFallBackToMain_MainWindowCountdownOwnsClose`
- **Previously expected:** ShowMainRequested would fire when the last panel is closed
- **Now verifies:** ShowMainRequested does NOT fire
- **Why keep it:** Documents that fallback-to-main logic is MainWindow's responsibility

Both tests now:
- Include comments explaining the intentional no-op design
- Assert the **absence** of old behaviors (empty closedThreads, showMainFired = false)
- Retain the same setup to serve as documentation of the contract

## Outcome

- All 726 tests pass ✓
- Tests now accurately reflect production behavior as of 0822ba2
- Future maintainers will understand that `OnAgentLeftActivePanel` is intentionally a no-op

## Related Files

- `SquadDash.Tests/TranscriptSelectionTests.cs` (lines 233–275)
- `SquadDash/TranscriptSelectionController.cs` (lines 92–97)

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

---

# Decision: Update OnAgentLeftActivePanel Test Semantics

**Date:** 2024  
**Decided by:** Vesper Knox (Testing & Quality Specialist)  
**Status:** Implemented  
**Commit:** 47533dd

## Context

Two tests in `TranscriptSelectionTests.cs` were failing:
- `OnAgentLeftActivePanel_ClosesAllAgentPanels` — expected panels to close, but closedThreads was empty
- `OnAgentLeftActivePanel_LastPanel_FallsBackToMain` — expected ShowMainRequested to fire, but it didn't

Investigation revealed these were **stale tests** testing behavior that was intentionally removed in commit `0822ba2` ("shift-click empty transcript, voice PTT, docs panel preservation").

## The Design Change (0822ba2)

The production method `TranscriptSelectionController.OnAgentLeftActivePanel` was deliberately changed to a **no-op**:

```csharp
public void OnAgentLeftActivePanel(AgentStatusCard card)
{
    // Do not directly close panels here. MainWindow tracks whether a panel was
    // auto-opened versus user-pinned, and its auto-close countdown owns the
    // actual close timing.
}
```

**Rationale:** Panel auto-close timing is now owned by MainWindow's countdown mechanism, which knows whether a panel was auto-opened vs. user-pinned. `OnAgentLeftActivePanel` should not interfere with this logic.

## Decision

Update the two failing tests to **document the new no-op contract** rather than remove them:

### Test 1: `OnAgentLeftActivePanel_DoesNotClosePanels_MainWindowCountdownOwnsClose`
- **Previously expected:** Panels would close when an agent left the active panel
- **Now verifies:** Panels remain open; ClosePanelRequested does NOT fire
- **Why keep it:** Documents that panel closing is no longer this method's responsibility

### Test 2: `OnAgentLeftActivePanel_DoesNotFallBackToMain_MainWindowCountdownOwnsClose`
- **Previously expected:** ShowMainRequested would fire when the last panel is closed
- **Now verifies:** ShowMainRequested does NOT fire
- **Why keep it:** Documents that fallback-to-main logic is MainWindow's responsibility

Both tests now:
- Include comments explaining the intentional no-op design
- Assert the **absence** of old behaviors (empty closedThreads, showMainFired = false)
- Retain the same setup to serve as documentation of the contract

## Outcome

- All 726 tests pass ✓
- Tests now accurately reflect production behavior as of 0822ba2
- Future maintainers will understand that `OnAgentLeftActivePanel` is intentionally a no-op

## Related Files

- `SquadDash.Tests/TranscriptSelectionTests.cs` (lines 233–275)
- `SquadDash/TranscriptSelectionController.cs` (lines 92–97)


---

# UI Fixes: Transcript Layout, Agent Name Abbreviation, Hyperlink Theming

**Status:** Implemented  
**Decided:** 2026-04-27  
**Decider:** Lyra Morn (WPF & UI Specialist)  
**Commit:** `8a0ae77`

## Context

Three focused UI improvements were needed:
1. Transcript panels not filling available space when documentation panel was open
2. "General Purpose Agent" name too verbose in transcript titles
3. Hyperlink hover color in dark theme made text hard to read

## Decisions

### 1. Transcript Panel Layout — Remove Obsolete Docs Column Management

**Decision:** `RebuildTranscriptPanelsGrid()` no longer manages `DocsSplitterColumn` and `DocsPanelColumn`.

**Rationale:**  
Previous fix (commit `0822ba2`) moved `DocsSplitter` and `DocsPanel` from `TranscriptPanelsGrid` (row 3 only) to root grid (rows 1-4, full height span). However, `RebuildTranscriptPanelsGrid()` still contained logic to save/restore docs column widths and re-add them to `TranscriptPanelsGrid`. This created phantom column definitions that prevented transcript panels from expanding to fill available width when docs panel was open with multiple transcripts visible.

**Implementation:**
- Removed `docsSplitterWidth` / `docsPanelWidth` save/restore logic
- Removed `childrenToRemove.Where(c => c != DocsSplitter && c != DocsPanel)` filtering (docs elements aren't in this grid anymore)
- Removed all logic that re-adds docs columns to `TranscriptPanelsGrid.ColumnDefinitions`
- Simplified to: `Children.Clear()`, `ColumnDefinitions.Clear()`, add only transcript panel columns (star-sized) + splitters (8px)

**Outcome:** Transcript panels now properly fill the available space with evenly-sized star columns, regardless of how many panels are open or whether the docs panel is visible.

### 2. Agent Name Abbreviation — "GPA" for "General Purpose Agent"

**Decision:** Display "GPA" instead of "General Purpose Agent" in all transcript UI.

**Rationale:**  
"General Purpose Agent" is verbose and creates visual clutter in transcript titles, especially when combined with relative timestamps ("General Purpose Agent - 2 minutes ago"). Abbreviating to "GPA" maintains clarity while reducing horizontal space consumption.

**Implementation:**
- Added `AbbreviateAgentName(string name)` helper (line 4870) that performs case-insensitive replacement
- Applied to `BuildSecondaryTranscriptTitle()` for secondary panel headers
- Applied to `UpdateTranscriptThreadBadge()` for main transcript title display

**Scope:** All locations where agent names appear in transcript titles. Does not affect agent card labels or other UI contexts.

### 3. Hyperlink Hover Theming — Use Standard `HoverSurface`

**Decision:** Apply implicit `Hyperlink` style with `IsMouseOver` trigger using `{DynamicResource HoverSurface}`.

**Rationale:**  
Transcript hyperlinks (using `thread:` protocol in markdown, rendered as WPF `Hyperlink` elements) had no custom styling and used WPF's default hover behavior. In dark theme, this created poor contrast and made link text hard to read on hover. Standard buttons use `HoverSurface` for mouse-over state (`#252220` in dark, `#E8E0D4` in light), which provides good readability in both themes.

**Implementation:**
- Added implicit `<Style TargetType="Hyperlink">` in `App.xaml` (after TreeViewItem style, line 387)
- Added `IsMouseOver` trigger setting `Background` to `{DynamicResource HoverSurface}`
- Uses `DynamicResource` to respond to theme switches

**Scope:** All `Hyperlink` elements app-wide, including transcript links created by `MarkdownDocumentRenderer.TryReadMarkdownLink()`.

## Alternatives Considered

### Fix 1 — Transcript Layout
- **Alternative:** Manually calculate and set column widths to fill space  
  **Rejected:** WPF's star-sizing already does this; the issue was extra column definitions interfering with layout. Removing the obsolete code is simpler and more correct.

### Fix 2 — Agent Name Abbreviation
- **Alternative:** Abbreviate all long agent names (e.g., "General..." → "G...")  
  **Rejected:** Only "General Purpose Agent" was identified as problematic. Over-abbreviating other names could reduce clarity. Targeted fix is safer.

### Fix 3 — Hyperlink Hover
- **Alternative:** Create a custom brush specifically for hyperlink hover  
  **Rejected:** Reusing `HoverSurface` ensures consistency with button hover behavior and reduces duplication of theme values.

## Impact

- **User Experience:** Transcript panels now use full available width; cleaner titles; consistent hover feedback across UI elements
- **Maintainability:** Removed 40+ lines of obsolete docs column management code; centralized hover theming
- **Performance:** Negligible (layout calculation simplified)
- **Compatibility:** No breaking changes; works with existing transcript data and theme switching

## Testing

- **Build:** 0 errors, 0 warnings
- **Manual verification needed:**
  - Open 2-4 transcript panels with docs panel visible → panels fill width evenly
  - Hover over transcript links in dark theme → background uses `HoverSurface`, text remains readable
  - Open "General Purpose Agent" transcript → title shows "GPA - X ago" in secondary panel header and "GPA's transcript" in main view

## Related Decisions

- Commit `0822ba2`: Moved docs panel to root grid (full height span)
- Commit `eb3836b`: Secondary transcript title format ("Agent - X ago")
- Commit `da3bc95`: Implicit TreeViewItem style pattern (same approach for Hyperlink)

## Notes

The transcript layout fix is a cleanup of technical debt from the docs panel full-height fix. The other two fixes are polish improvements that enhance readability and reduce visual clutter in the transcript UI.

---

### 2026-04-26T12:00:51: docs/ scaffold created

**By:** Mira Quill  
**What:** Created initial `docs/` folder structure with 13 markdown files + `.gitkeep` placeholder  
**Why:** Serves as both real SquadUI documentation and living template for repo authors using the docs panel feature

---

## Files Created

- `docs/README.md` — Home/index with compelling overview
- `docs/SUMMARY.md` — GitBook-style TOC for tree navigation
- `getting-started/` — Installation, first run, images placeholder
- `concepts/` — Agents, squad-team, transcripts, documentation-panel
- `reference/` — Configuration, routing, keyboard-shortcuts
- `contributing/` — Adding-an-agent, writing-docs

Total: 18 files (13 .md, 4 README.md, 1 .gitkeep)

---

## Content Quality

All content is **real and useful** — no lorem ipsum or placeholders. Based on actual codebase exploration:
- README.md (project structure, SquadDash architecture, tool icons)
- .squad/team.md (agent roster, members table format)
- .squad/routing.md (routing table format, issue routing)
- SquadDash/ file structure (helper classes, services)
- decisions.md (MainWindow decomposition, architectural decisions)

Documented key features:
- Agent cards with hover-glow effect
- Shift-click to open transcripts
- Multi-agent transcript panels
- Voice PTT (double-Ctrl)
- Tool call icons (🔎 grep, ✏️ edit, 👀 view, 🤖 task, etc.)
- Routing table format
- team.md members table format
- docs panel tree view + markdown rendering

---

## Dual Purpose

1. **Real documentation** — SquadUI users and contributors can browse these docs inside the app's documentation panel
2. **Living template** — Repos using SquadUI can see a working example of the `docs/` structure and markdown rendering

---

## Next Steps

- Future: Add screenshots to `getting-started/images/`
- Future: Expand keyboard-shortcuts.md as features evolve
- Future: Add architecture diagrams to concepts/


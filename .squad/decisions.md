# Squad Decisions

## Active Decisions

---

### 2026-04-30 — `squad cross-squad` integration architecture

**Context:** The task asked how SquadDash should surface cross-workspace agent interactions. `squad cross-squad` does NOT exist as a CLI command in the 0.9.5-insider release. The functionality lives entirely in the `@bradygaster/squad-sdk` module `runtime/cross-squad.js`.

**What cross-squad actually is (SDK investigation findings):**

Cross-squad is a **manifest-based discovery + GitHub issue delegation** system. No real-time agent-to-agent RPC exists. The mechanism is:

1. **Manifest** — each squad can publish `.squad/manifest.json`:
   ```json
   {
     "name": "my-squad",
     "capabilities": ["feature-A", "feature-B"],
     "accepts": ["issues", "prs"],
     "contact": { "repo": "owner/repo" },
     "description": "optional human description"
   }
   ```

2. **Discovery** — other squads find this squad via:
   - `.squad/upstream.json` — lists upstream repos (type: `"local"` with path, or `"git"`)
   - `.squad/squad-registry.json` — flat list `[{ "name": "...", "path": "..." }]`
   - SDK: `discoverSquads(squadDir)` merges upstreams + registry, deduplicates by name

3. **Delegation** — creates a GitHub issue in the target repo:
   - Title gets `[cross-squad]` prefix
   - Labels: `squad:cross-squad` + any caller-supplied labels
   - SDK: `buildDelegationArgs(options)` returns `gh issue create` args; caller executes `gh`

4. **Status** — queries a delegated issue's state:
   - SDK: `buildStatusCheckArgs(issueUrl)` → `gh issue view --repo owner/repo 123 --json state,title`
   - SDK: `parseIssueStatus(jsonOutput, issueUrl)` → `{ url, state: "open"|"closed"|"unknown", title }`

**Current state of this workspace:** No `.squad/manifest.json`, `.squad/upstream.json`, or `.squad/squad-registry.json` exist. This workspace is not yet participating in any cross-squad network.

**Architecture decision:**

SquadDash should support cross-squad in **two phases**, with Phase 1 being the right scope for now:

**Phase 1 — Discovery + status (read-only, no CLI required):**
- Add a `cross_squad_discover` NDJSON bridge request
- Call `discoverSquads(squadDir)` synchronously inside `runPrompt.ts`, emit a `cross_squad_discovered` event with the discovered squads as JSON
- Surface as a "Cross-Squad" menu item under Workspace (similar to SubSquads)
- If no manifest/upstream config exists: show instructional message with the config schema
- If squads are discovered: display name, capabilities, repo, accepts fields, source
- No `gh` CLI execution needed for Phase 1

**Phase 2 — Delegation (requires `gh` CLI, deferred):**
- Add `cross_squad_delegate` bridge request: title, body, targetRepo, labels
- In `runPrompt.ts`: call `buildDelegationArgs(options)`, spawn `gh` with those args, emit `cross_squad_delegated` or `cross_squad_delegate_error`
- Add UI for composing delegation: prompt box pre-filled from selected issue or freeform
- Add status polling: `cross_squad_status_check` request → runs `gh issue view`, emits result
- Deferred because: (a) requires `gh` CLI present, (b) requires auth, (c) UX needs more design

**Rationale for deferring Phase 2:**
- Phase 1 has zero external dependencies — pure SDK file reads, no auth, no network
- Phase 2 delegation creates real GitHub issues — accidental delegation would be hard to undo
- The CLI doesn't expose `squad cross-squad` yet, suggesting the feature isn't polished enough for casual use
- Phase 1 (discovery) gives SquadDash users insight into their cross-squad topology without risk

**Not building now:** Any manifest editor UI or upstream config wizard — these belong to the squad CLI's `squad upstream` commands (`add|remove|list|sync`), which already handle upstream config management.

**Files to create/modify when Phase 1 is implemented:**
- `Squad.SDK/runPrompt.ts`: `CrossSquadDiscoverRequest` type, `handleCrossSquadDiscover()`, dispatch case
- `SquadDash/SquadSdkEvent.cs`: `CrossSquadDiscoveredJson`, `CrossSquadCount` fields
- `SquadDash/SquadSdkProcess.cs`: `SquadSdkCrossSquadDiscoverRequest` record, `DiscoverCrossSquadsAsync()`
- `SquadDash/MainWindow.xaml.cs`: `cross_squad_discovered` event handler, Workspace → "Cross-Squad" menu item

---

### 2026-05-01 — SubSquads bridge prototype approach

**Context:** The squad CLI 0.9.5-insider replaced `squad streams` with `squad subsquads` (aliases: `workstreams`, `streams`). The API stabilised in `@bradygaster/squad-sdk` which exports `loadSubSquadsConfig(cwd)` and `resolveSubSquad(cwd)`. The CLI only supports `list`, `status`, and `activate <name>` — no create/add/remove subcommands exist.

**Decision:** Wrap `loadSubSquadsConfig` and `resolveSubSquad` in the existing NDJSON bridge via two new request types: `subsquads_list` and `subsquads_activate`. Surface via a "SubSquads" menu item in the Workspace menu that displays config + active state in the transcript.

**Rationale:**
- No new process or shell-out needed — SDK functions are synchronous and imported directly into `runPrompt.ts`.
- Fire-and-forget pattern (same as `rc_start`) keeps the bridge simple.
- `subsquads_activate` writes the `.squad-workstream` file directly, which is the same mechanism used by the CLI.
- Config file is `.squad/workstreams.json` (note: CLI's "not configured" message mistakenly says `streams.json`).

**API notes:**
- Config: `.squad/workstreams.json` — `{ defaultWorkflow, workstreams: [{ name, labelFilter, workflow?, folderScope?, description? }] }`
- Active subsquad resolution order: `SQUAD_TEAM` env var → `.squad-workstream` file → auto-select if exactly one → null
- `resolveSubSquad` returns `{ name, definition, source }` where `source` is `"env"`, `"file"`, or `"config"`

**Implemented:**
- `Squad.SDK/runPrompt.ts`: `handleSubSquadsList`, `handleSubSquadsActivate`, `SubSquadsListRequest`, `SubSquadsActivateRequest` types
- `SquadDash/SquadSdkEvent.cs`: `SubSquadsConfigured`, `SubSquadsCount`, `WorkstreamsJson`, `ActiveSubsquadName`, `ActiveSubsquadSource`, `SubSquadName`
- `SquadDash/SquadSdkProcess.cs`: `SquadSdkSubSquadsListRequest`, `SquadSdkSubSquadsActivateRequest`, `ListSubSquadsAsync`, `ActivateSubSquadAsync`
- `SquadDash/MainWindow.xaml.cs`: event handlers for `subsquads_listed`/`subsquads_activated`/`subsquads_error`; Workspace > SubSquads menu item

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

**Why:** SquadDash auto-detects bare commit hashes in transcript text and wraps them in the correct hyperlinks automatically — no manual URL construction needed. Constructing the URL manually caused agents to hallucinate the wrong repo owner/name (e.g. `bradygaster/SquadDash` instead of `MillerMark/SquadDash`), producing broken links. Plain hash = correct link every time.

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

Added in-app Documentation Mode to SquadDash. Toggled via View → View Documentation. When active, a resizable panel appears to the right of the transcript containing a topic tree and markdown viewer.

## Rationale

Provides discoverability for new users without leaving the app. Keeps workspace context visible while reading docs. Self-teaching UI reduces friction.

## Implementation

- **XAML layout:** `TranscriptPanelsGrid` now has 3 columns: transcript (auto-width), splitter (8px when visible), docs panel (600px when visible).
- **Docs panel:** Left side = TreeView with hierarchical topics. Right side = WebBrowser rendering markdown via `MarkdownHtmlBuilder.Build()`.
- **Theme-aware:** All surfaces use `DynamicResource` (PanelSurface, PanelBorder, LabelText) for light/dark theme compatibility.
- **Interaction with full-screen mode:** Docs panel hidden when full-screen transcript mode is active. Transcript takes priority.
- **Stub for future:** "Add Document" button present but not wired — placeholder for user-contributed docs.

## Welcome content

Default markdown welcome message introduces SquadDash features and instructs user to select topics from tree. Topics pre-populated with placeholder nodes (Getting Started, Agents, Workspace, Settings).

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
**Why:** Serves as both real SquadDash documentation and living template for repo authors using the docs panel feature

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

1. **Real documentation** — SquadDash users and contributors can browse these docs inside the app's documentation panel
2. **Living template** — Repos using SquadDash can see a working example of the `docs/` structure and markdown rendering

---

## Next Steps

- Future: Add screenshots to `getting-started/images/`
- Future: Expand keyboard-shortcuts.md as features evolve
- Future: Add architecture diagrams to concepts/




# ADR-001: Phone Push Notifications for SquadDash

## Status
Proposed

## Context

SquadDash is a desktop WPF application that orchestrates long-running AI agent workflows. Users frequently need to step away from their desk while agents work, requiring awareness of completion states without constant desktop monitoring. The existing WPF UI provides rich visual feedback but requires the user to remain at the machine.

**User requirement:** Receive phone notifications for key workflow milestones (agent turn complete, loop complete, git commits) without requiring the SquadDash window to be in focus.

**Technical environment:**
- WPF desktop application running on Windows
- Event-driven architecture via `SquadSdkEvent` delivered from SDK bridge to `MainWindow.HandleEvent`
- Settings persisted via `ApplicationSettingsStore` (JSON-based atomic writes to `%LocalAppData%\SquadDash\settings.json`)
- Existing service decomposition: `PromptExecutionController`, `BackgroundTaskPresenter`, `TranscriptConversationManager`, `AgentThreadRegistry`

## Decision

**Implement pluggable push notification architecture with ntfy.sh as the primary delivery mechanism, designed to support Pushover, Telegram, and SMS as future additions.**

### Delivery Mechanism

**Phase 1 (Immediate Implementation):** ntfy.sh only  
**Phase 2 (Future):** Add Pushover, Telegram Bot, Twilio SMS as selectable providers

**Rationale for ntfy.sh first:**
1. **Zero friction onboarding:** No API keys, no account creation, no per-message cost
2. **HTTP-trivial integration:** Single `POST https://ntfy.sh/{topic}` with message body
3. **Self-service subscription:** User scans QR code on their phone → auto-subscribes to the topic
4. **Adequate for MVP:** Handles notification delivery reliably; advanced features (priority, sounds, custom icons) deferred to Pushover phase

**Why not Pushover first?**
- $5 purchase barrier before testing
- Requires API key management (user + app keys)
- Better suited as an upgrade path for power users after ntfy.sh proves the pattern

**Why not Telegram/SMS first?**
- Telegram: Higher friction for non-Telegram users (requires Telegram app + bot interaction)
- Twilio SMS: Ongoing per-message cost model; overkill for this use case

**Pluggable design justification:** The HTTP POST pattern is identical across all candidates (URL + headers + JSON body). Abstracting early costs nothing and prevents ntfy.sh lock-in.

---

## Event Taxonomy

Only events representing **workflow completion milestones** should trigger notifications. Streaming progress (thinking_delta, response_delta, tool_progress) is too noisy for phone interrupts.

| Event Name | Default Enabled | Rationale | Owner |
|------------|-----------------|-----------|-------|
| `assistant_turn_complete` | ✅ Yes | Core use case: AI finished answering your prompt. Primary notification trigger. | Talia (SDK event surfacing) |
| `git_commit_pushed` | ❌ No | Optional for users with git-heavy workflows. Can be spammy in rapid-commit scenarios. | Arjun (C# git event detection) |
| `loop_iteration_complete` | ❌ No | Mid-loop checkpoints — useful for very long loops. Default off to avoid notification storms. | Talia (loop events already surfaced) |
| `loop_stopped` | ✅ Yes | Loop workflow finished or manually stopped — clear endpoint signal. | Talia |
| `rc_connection_established` | ❌ No | Nice-to-know status update, not workflow-critical. Default off. | Talia (RC events already surfaced) |
| `rc_connection_dropped` | ✅ Yes | Failure signal — user needs to know remote access broke. | Talia |
| `long_running_task_complete` | 🟡 Deferred | Future: task agents taking >5 minutes. Requires new event type. | Talia (new event) + Arjun (detection) |

**Implementation note:** `assistant_turn_complete` does **not** exist as a discrete `SquadSdkEvent.Type` today. The `"done"` event at line 1403 of `MainWindow.xaml.cs` is the semantic equivalent. **Owner: Talia** to either (a) emit `assistant_turn_complete` from SDK or (b) confirm `"done"` is the canonical signal for notification purposes.

**Per-event toggles:** Settings UI will expose checkboxes for each event type. Stored in `ApplicationSettingsSnapshot.NotificationEventToggles: IReadOnlyDictionary<string, bool>`.

---

## Configuration Architecture

### Storage Location
**Global (machine-wide) settings, not per-workspace.**

**Rationale:**
- Notification endpoints (ntfy topic, Pushover keys, phone number) are user identity, not project-specific
- User wants the same phone to receive notifications regardless of which SquadDash workspace is open
- Consistent with existing global settings: `UserName`, `SpeechRegion`, `LastUsedModel`, `Theme`

### Schema (ApplicationSettingsSnapshot additions)

```csharp
// Added to ApplicationSettingsSnapshot record (ApplicationSettingsStore.cs ~line 515)
public sealed record ApplicationSettingsSnapshot(
    // ... existing parameters ...
    IReadOnlyDictionary<string, string> IgnoredRoutingIssueFingerprintsByWorkspace)
{
    // ... existing properties ...

    /// <summary>
    /// Push notification delivery provider. "ntfy", "pushover", "telegram", "twilio", or null (disabled).
    /// </summary>
    public string? NotificationProvider { get; init; }

    /// <summary>
    /// Endpoint configuration for the selected provider.
    /// ntfy: { "topic": "my-squad-dash-abc123" }
    /// pushover: { "user_key": "...", "api_token": "..." }
    /// telegram: { "bot_token": "...", "chat_id": "..." }
    /// twilio: { "account_sid": "...", "auth_token": "...", "from": "+1...", "to": "+1..." }
    /// </summary>
    public IReadOnlyDictionary<string, string>? NotificationEndpoint { get; init; }

    /// <summary>
    /// Per-event enable/disable toggles.
    /// Key = event name (e.g., "assistant_turn_complete"), Value = enabled (true) or disabled (false).
    /// Missing keys inherit default from Event Taxonomy table above.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? NotificationEventToggles { get; init; }
}
```

### ApplicationSettingsStore Methods (Arjun)

```csharp
// Add to ApplicationSettingsStore class
public ApplicationSettingsSnapshot SaveNotificationProvider(
    string? provider,
    IReadOnlyDictionary<string, string>? endpoint);

public ApplicationSettingsSnapshot SaveNotificationEventToggles(
    IReadOnlyDictionary<string, bool> toggles);
```

---

## Settings UI

### Placement
**Dedicated "Notifications" section in the existing `PreferencesWindow`.**

**Rationale:**
- PreferencesWindow already exists (`PreferencesWindow.cs`) and handles global settings (UserName, SpeechRegion, API Key)
- Avoids adding another top-level window to the application
- Groups related configuration in one location
- Consistent with existing settings UX patterns

### Layout (Wireframe-Level Description)

```
┌─ Notifications ──────────────────────────────────────────┐
│                                                           │
│  Enable Phone Notifications  [✓]                         │
│                                                           │
│  Delivery Method:  [ ntfy.sh ▼ ]  (future: Pushover...)  │
│                                                           │
│  ┌─ ntfy.sh Configuration ──────────────────────────┐    │
│  │  Topic:  [my-squad-dash-abc123_____________]     │    │
│  │                                                   │    │
│  │  [ Generate Random Topic ]                       │    │
│  │                                                   │    │
│  │  [QR Code]  ← Scan with phone ntfy app          │    │
│  │   █████████                                       │    │
│  │   ██ ▄▄▄ ██         Encodes:                     │    │
│  │   ██ ███ ██    https://ntfy.sh/my-squad-dash-... │    │
│  │   ██▄▄▄▄▄██                                       │    │
│  │   █████████                                       │    │
│  └───────────────────────────────────────────────────┘    │
│                                                           │
│  Notify me when:                                          │
│  [✓] AI turn completes                                    │
│  [ ] Git commit pushed                                    │
│  [ ] Loop iteration completes                             │
│  [✓] Loop stopped                                         │
│  [ ] Remote connection established                        │
│  [✓] Remote connection dropped                            │
│                                                           │
│                                    [Test Notification]    │
└───────────────────────────────────────────────────────────┘
```

### Component Ownership: Lyra Morn (WPF/XAML specialist)

**Implementation notes:**
1. **QR Code rendering:** Use `QRCoder` NuGet package (already approved for RC mobile). Generate QR image from `https://ntfy.sh/{topic}`.
2. **"Generate Random Topic":** Button generates a secure topic name like `squad-dash-{username}-{guid-suffix}` to prevent topic collisions.
3. **"Test Notification":** Sends a test message via the configured endpoint to validate setup.
4. **Provider dropdown:** Initially shows only "ntfy.sh". Phase 2 adds "Pushover", "Telegram Bot", "Twilio SMS".
5. **Dynamic config panel:** Config UI (Topic / API Keys / etc.) swaps based on selected provider.

---

## Implementation Plan

### Phase 1: ntfy.sh Foundation (1 week)

#### Arjun Sen: C# Notification Service (2–3 days)
**File:** `SquadDash/PushNotificationService.cs`

```csharp
internal interface IPushNotificationProvider
{
    Task<bool> SendAsync(string title, string message, string? tags = null);
}

internal sealed class NtfyNotificationProvider : IPushNotificationProvider
{
    private readonly string _topic;
    private readonly HttpClient _httpClient;

    public NtfyNotificationProvider(string topic, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("ntfy topic cannot be empty", nameof(topic));
        _topic = topic;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<bool> SendAsync(string title, string message, string? tags = null)
    {
        try
        {
            var content = new StringContent(message, System.Text.Encoding.UTF8, "text/plain");
            content.Headers.Add("Title", title);
            if (!string.IsNullOrWhiteSpace(tags))
                content.Headers.Add("Tags", tags);

            var response = await _httpClient.PostAsync($"https://ntfy.sh/{_topic}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Notifications", $"ntfy send failed: {ex.Message}");
            return false;
        }
    }
}

internal sealed class PushNotificationService
{
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly Func<ApplicationSettingsSnapshot> _getCurrentSettings;
    private IPushNotificationProvider? _currentProvider;

    public PushNotificationService(
        ApplicationSettingsStore settingsStore,
        Func<ApplicationSettingsSnapshot> getCurrentSettings)
    {
        _settingsStore = settingsStore;
        _getCurrentSettings = getCurrentSettings;
        ReloadProvider();
    }

    public void ReloadProvider()
    {
        var settings = _getCurrentSettings();
        _currentProvider = settings.NotificationProvider switch
        {
            "ntfy" when settings.NotificationEndpoint?.TryGetValue("topic", out var topic) == true
                => new NtfyNotificationProvider(topic),
            // Phase 2: "pushover" => new PushoverNotificationProvider(...),
            _ => null
        };
    }

    public async Task NotifyEventAsync(string eventName, string title, string message)
    {
        var settings = _getCurrentSettings();
        var enabled = settings.NotificationEventToggles?.TryGetValue(eventName, out var toggle) == true
            ? toggle
            : GetDefaultEnabledState(eventName);

        if (!enabled || _currentProvider is null)
            return;

        SquadDashTrace.Write("Notifications", $"Sending: event={eventName} title={title}");
        await _currentProvider.SendAsync(title, message, tags: "computer,completed");
    }

    private static bool GetDefaultEnabledState(string eventName)
    {
        return eventName switch
        {
            "assistant_turn_complete" => true,
            "loop_stopped" => true,
            "rc_connection_dropped" => true,
            _ => false
        };
    }
}
```

**Integration point:** MainWindow constructor creates `_pushNotificationService` and wires `ReloadProvider()` after settings changes.

**Event hooks (3–5 call sites in MainWindow.HandleEvent):**

```csharp
// Line ~1403 in HandleEvent switch ("done" case)
case "done":
    _pec.ActiveToolName = null;
    FinalizeCurrentTurnResponse();
    CollapseCurrentTurnThinking();
    _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now);
    _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
    FlushDeferredSystemLines();
    // NEW:
    _ = _pushNotificationService.NotifyEventAsync(
        "assistant_turn_complete",
        "SquadDash",
        "AI response complete");
    break;

// Line ~1360 (loop_stopped case)
case "loop_stopped":
    HandleLoopStopped(evt);
    // NEW:
    _ = _pushNotificationService.NotifyEventAsync(
        "loop_stopped",
        "SquadDash",
        $"Loop stopped after {evt.LoopIteration ?? 0} iterations");
    break;

// Line ~1396 (rc_stopped case) — if user-initiated, skip notification
case "rc_stopped":
    HandleRcStopped(evt);
    // NEW (only if not graceful shutdown):
    if (!_remoteAccessGracefulShutdown) // need to track this flag
    {
        _ = _pushNotificationService.NotifyEventAsync(
            "rc_connection_dropped",
            "SquadDash",
            "Remote connection dropped");
    }
    break;
```

**Test:** `PushNotificationServiceTests.cs` — mock HttpClient, verify POST URL/headers/body.

#### Talia Rune: SDK Event Surfacing (1 day)

**Task:** Confirm whether `"done"` event is the canonical signal for `assistant_turn_complete` or if a new event type should be emitted.

**If new event needed:** Modify SDK bridge to emit `{ "Type": "assistant_turn_complete", ... }` after the final `"done"` event in a turn.

**Deliverable:** Document in `.squad/decisions.md` which event type C# should hook for notifications.

#### Lyra Morn: Settings UI (2–3 days)

**File:** `SquadDash/PreferencesWindow.cs` (extend existing)

**Tasks:**
1. Add "Notifications" section to PreferencesWindow form stack
2. Add provider dropdown (initially single option: "ntfy.sh")
3. Add ntfy topic TextBox + "Generate Random Topic" button
4. Add QRCoder-based QR image display (refresh on topic change)
5. Add per-event checkboxes (load from settings, bind to UI)
6. Wire "Test Notification" button → calls `PushNotificationService.NotifyEventAsync("test", "Test", "SquadDash notifications are working!")`
7. Save button → `_settingsStore.SaveNotificationProvider(...)` + `_settingsStore.SaveNotificationEventToggles(...)`

**Dependencies:**
- QRCoder NuGet package (MIT license) — **already approved** per `.squad/tasks.md`
- `ApplicationSettingsStore.SaveNotificationProvider` + `SaveNotificationEventToggles` methods (Arjun)

**Test:** Manual verification — open Preferences, configure ntfy topic, scan QR code on phone, toggle event checkboxes, save, trigger notification event in app.

#### Integration (1 day)

**File:** `SquadDash/MainWindow.xaml.cs`

1. Add `_pushNotificationService` field
2. Wire in constructor after `_settingsStore` is created
3. Call `_pushNotificationService.ReloadProvider()` in `ApplySettings` after settings changes
4. Add notification hooks in `HandleEvent` switch statement (see Arjun section above)

### Phase 2: Multi-Provider Support (Future)

**Scope:** Add Pushover, Telegram Bot, Twilio SMS providers.

**Changes required:**
1. Arjun: Implement `PushoverNotificationProvider`, `TelegramNotificationProvider`, `TwilioNotificationProvider` classes implementing `IPushNotificationProvider`
2. Lyra: Extend Settings UI provider dropdown + add provider-specific config panels
3. Talia: No changes (events already surfaced)

**Timeline:** Post-MVP, user-requested feature based on feedback.

---

## Interface Contracts

### C# Interfaces

```csharp
// SquadDash/PushNotificationService.cs
internal interface IPushNotificationProvider
{
    /// <summary>
    /// Sends a push notification.
    /// </summary>
    /// <param name="title">Notification title (short, 1 line)</param>
    /// <param name="message">Notification body (1–3 lines)</param>
    /// <param name="tags">Optional comma-separated tags (e.g., "computer,completed")</param>
    /// <returns>True if sent successfully, false if failed (logs error to trace)</returns>
    Task<bool> SendAsync(string title, string message, string? tags = null);
}
```

### ApplicationSettingsStore Methods

```csharp
// SquadDash/ApplicationSettingsStore.cs (add to class)

/// <summary>
/// Saves notification provider and endpoint configuration.
/// </summary>
/// <param name="provider">"ntfy", "pushover", "telegram", "twilio", or null to disable</param>
/// <param name="endpoint">Provider-specific config keys (topic, API keys, phone numbers)</param>
public ApplicationSettingsSnapshot SaveNotificationProvider(
    string? provider,
    IReadOnlyDictionary<string, string>? endpoint)
{
    using var mutex = AcquireMutex();
    var current = LoadCore();
    var updated = current with
    {
        NotificationProvider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim(),
        NotificationEndpoint = endpoint
    };
    SaveCore(updated);
    return updated;
}

/// <summary>
/// Saves per-event notification toggles.
/// </summary>
/// <param name="toggles">Event name → enabled (true/false)</param>
public ApplicationSettingsSnapshot SaveNotificationEventToggles(
    IReadOnlyDictionary<string, bool> toggles)
{
    using var mutex = AcquireMutex();
    var current = LoadCore();
    var updated = current with { NotificationEventToggles = toggles };
    SaveCore(updated);
    return updated;
}
```

### SDK Event (Talia to confirm)

**Option A:** Reuse `"done"` event  
**Option B:** Emit new event type `"assistant_turn_complete"` after `"done"`

**Deliverable:** Decision documented in `.squad/decisions.md` by end of Day 1.

---

## Risks & Mitigations

### Risk 1: ntfy.sh Topic Collisions
**Impact:** Two users pick the same topic name → receive each other's notifications.

**Mitigation:**
- Default topic generation includes username + GUID suffix: `squad-dash-{username}-{guid:N:8}`
- Example: `squad-dash-mark003-a7f3c2e1`
- Extremely low collision probability (8-char hex = 4 billion possibilities)

### Risk 2: Notification Spam
**Impact:** Loop with 100 iterations → 100 phone pings if `loop_iteration_complete` is enabled.

**Mitigation:**
- `loop_iteration_complete` defaults to **disabled**
- Documentation warns users this event is high-frequency
- Future enhancement: rate-limiting (max 1 notification per event type per 10 seconds)

### Risk 3: HTTP POST Failures
**Impact:** Notification silently fails to send; user never knows.

**Mitigation:**
- All HTTP failures logged to SquadDashTrace → visible in Trace window
- "Test Notification" button in Settings UI validates config before user relies on it
- Phase 2: Add in-app notification delivery status indicator (toast on failure)

### Risk 4: QRCoder NuGet Package Dependency
**Impact:** New external dependency increases attack surface, binary size.

**Mitigation:**
- QRCoder already approved for RC mobile (`.squad/tasks.md`)
- MIT license, no native dependencies, ~150 KB
- Well-maintained OSS library (1.4M+ downloads/month on NuGet)
- Acceptable tradeoff for UX benefit (QR scan vastly easier than manual URL entry)

### Risk 5: Sensitive Data in Settings
**Impact:** API keys (Pushover, Twilio) stored in plaintext JSON.

**Mitigation:**
- Phase 1 (ntfy): No API keys, only public topic name (low-sensitivity)
- Phase 2 (Pushover/Twilio): Use Windows DPAPI to encrypt sensitive fields before writing to JSON
- Existing pattern: Azure Speech API Key already uses environment variable (`SQUAD_SPEECH_KEY`) → extend to Notification API Keys
- **Recommendation:** Store Pushover/Twilio secrets in environment variables, not JSON

---

## Consequences

### What Changes
1. **New NuGet dependency:** QRCoder (~150 KB, MIT)
2. **ApplicationSettingsSnapshot schema expansion:** +3 properties (`NotificationProvider`, `NotificationEndpoint`, `NotificationEventToggles`)
3. **PreferencesWindow UI expansion:** +1 section (Notifications)
4. **MainWindow event handling:** +3–5 notification hooks in `HandleEvent` switch
5. **New service class:** `PushNotificationService.cs` (~200 lines)

### What Gets Easier
1. **User awareness:** No need to keep SquadDash window visible to track long-running workflows
2. **Mobile workflows:** Trigger a multi-hour loop, walk away, get notified on phone when complete
3. **Debugging async failures:** RC connection drops → instant phone notification rather than discovering it hours later

### What Gets Harder
1. **Settings complexity:** PreferencesWindow UI now has 6 sections (was 5)
2. **Event taxonomy maintenance:** Every new event type requires a decision: "notify-worthy or not?"
3. **Multi-provider testing:** Phase 2 requires manual testing across 4 providers (ntfy, Pushover, Telegram, Twilio)

### Non-Breaking Guarantees
1. **Notifications are opt-in:** Default state is disabled; user must explicitly configure topic/provider
2. **Zero impact if unconfigured:** No HTTP calls, no QR rendering, no perf cost
3. **Backward-compatible settings:** Existing `settings.json` files load normally; new fields are optional

---

## Open Questions for Mark003

1. **Git commit events:** Should we detect commits made **by SquadDash agents** only, or all commits in the workspace (including user manual commits)? Recommend agent-only to reduce noise.

2. **Rate limiting:** Should we implement "max 1 notification per event type per 10 seconds" in Phase 1, or defer to Phase 2 based on user feedback?

3. **Notification message detail:** For `assistant_turn_complete`, should the notification body include:
   - (a) "AI response complete" (generic)
   - (b) First 50 chars of response text (preview)
   - (c) Lead agent name + "turn complete"

   **Recommendation:** Option (c) — agent name provides context without leaking potentially sensitive response text.

4. **Environment variable precedence:** Should notification endpoints (topic, API keys) be overridable via environment variables for CI/test scenarios, or settings-file-only?

---

## References

- `.squad/tasks.md` — QRCoder approval, notification task description
- `.squad/rc-mobile-architecture.md` — QR code precedent for RC mobile
- `SquadDash/ApplicationSettingsStore.cs` — Existing settings persistence pattern
- `SquadDash/SquadSdkEvent.cs` — Event type definitions
- `SquadDash/MainWindow.xaml.cs` — Event handling entry point (line 1229 `HandleEvent`)
- `SquadDash/PreferencesWindow.cs` — Existing settings UI

---

## Approval Checklist

- [ ] Mark003: Approve event taxonomy (which events, default on/off)
- [ ] Arjun Sen: Review `PushNotificationService` interface contracts
- [ ] Talia Rune: Confirm `assistant_turn_complete` event strategy
- [ ] Lyra Morn: Review Settings UI wireframe
- [ ] All: Approve ntfy.sh-first approach vs. multi-provider Phase 1

---

**Author:** Orion Vale (Lead Architect)  
**Date:** 2026-04-27  
**Reviewers:** Arjun Sen, Talia Rune, Lyra Morn  
**Status:** Awaiting approval

---

## Notification Event Hooks (2026-04-28)

**By:** Talia Rune (TypeScript & SDK Bridge Specialist)

Event hooks for `PushNotificationService` — confirmed by reading `Squad.SDK/runPrompt.ts`, `SquadDash/MainWindow.xaml.cs` (HandleEvent switch at line 1229), and `SquadDash/SquadSdkEvent.cs`.

- **assistant_turn_complete:** uses `"done"` event **[confirmed]**  
  Emitted in `runPrompt.ts` line 582 via `onDone()` callback — fires when the AI agent finishes its full response turn. Semantic match correct.

- **loop_stopped:** uses `"loop_stopped"` event **[confirmed]**  
  Emitted in `runPrompt.ts` lines 686 and 711 — fires when a loop subprocess exits cleanly (code 0 or killed) or when no loop is active and stop is requested. Semantic match correct.

- **rc_connection_dropped:** uses `"rc_stopped"` event **[confirmed]**  
  Emitted in `runPrompt.ts` lines 809 and 823 — fires when remote bridge is stopped (either no active bridge or after successful shutdown). Semantic match correct.

All three events exist in the TypeScript SDK, are emitted at the correct lifecycle moments, and are already handled in the C# `MainWindow.HandleEvent` switch statement. **No code changes needed.**

**Files verified:**
- `Squad.SDK/runPrompt.ts` — lines 582 (`done`), 686/711 (`loop_stopped`), 809/823 (`rc_stopped`)
- `SquadDash/MainWindow.xaml.cs` — lines 1403 (`done`), 1359 (`loop_stopped`), 1395 (`rc_stopped`)
- `SquadDash/SquadSdkEvent.cs` — event model definitions (LoopStatus, LoopMdPath, etc.)



---

## RC Mobile — SDK PR Ownership for Binary Audio Frames (2026-04-30)

**By:** Orion Vale (Lead Architect)
**Status:** Decided

### Decision

**Talia Rune** will author and submit the PR to @bradygaster/squad-sdk adding onAudioChunk, onAudioStart, and onAudioEnd to RemoteBridgeConfig.

**Rationale:** Talia owns the TypeScript/SDK bridge and event protocol layer (routing.md). The audio frame additions are SDK bridge protocol changes — new callback hooks on RemoteBridgeConfig that flow through the NDJSON event stream. This squarely falls within her domain, and she already has the deepest context on how RemoteBridgeConfig is shaped and consumed.

### Timeline constraint

The PR should **not be submitted until the audio format spike (Option C vs B) is resolved.** The callback interface may differ slightly depending on format:
- Option C (WEBM_OPUS): onAudioChunk carries raw WebM container frames — no chunking metadata needed beyond onAudioStart / onAudioEnd bookends.
- Option B (PCM/AudioWorklet): onAudioChunk must also carry sample-rate and channel metadata to support the NAudio resampling step on the C# side.

Submitting before that's known risks a follow-up breaking change to the PR.

### Expected merge timeline

Merge is contingent on Brady Gaster (upstream maintainer). Talia should:
1. Open the spike on Option C first (see RC mobile — spike Option C task).
2. Submit the PR within one session after the format is confirmed.
3. Treat the merge date as an external dependency — unblock local development by running a patched local copy of the SDK in the meantime (consistent with the existing patches/ pattern in this repo).

### Risk

This is the **critical-path scheduling risk** for RC phone voice input. If the upstream PR stalls, the patched-SDK approach is the mitigation — Talia maintains the local patch until merge lands.

**References:** .squad/rc-mobile-architecture.md §Key Decisions #1


---

## RC Mobile — Audio Format Spike: Option C (WEBM_OPUS) vs Option B (PCM) (2026-04-30)

**By:** Talia Rune (TypeScript & SDK Bridge Specialist)
**Status:** Decided — spike complete

### Spike Result

**Option C (WEBM_OPUS) is NOT available in Microsoft.CognitiveServices.Speech 1.49.0.**

Verified by reflecting on the installed NuGet DLL (
etstandard2.0 target):

`
AudioStreamContainerFormat enum values in SDK 1.49.0:
  OGG_OPUS  = 257
  MP3       = 258
  FLAC      = 259
  ALAW      = 260
  MULAW     = 261
  AMRNB     = 262
  AMRWB     = 263
  ANY       = 264
`

WEBM_OPUS is absent. No CreatePushStream overload accepting a compressed container format for WebM exists.

### Why OGG_OPUS is also ruled out

OGG_OPUS (257) is present in the SDK. However, browser MediaRecorder support for OGG container is unreliable:
- Firefox supports OGG_OPUS recording natively.
- Chrome and Safari do **not** — MediaRecorder in those browsers only outputs udio/webm;codecs=opus.

Using OGG_OPUS would produce a Chrome/Safari failure, which is unacceptable for a mobile feature targeting phone browsers.

### Decision: Proceed with Option B (PCM via AudioWorklet + NAudio transcoding)

The WebAudio AudioWorklet pipeline (Option B) is confirmed as the required path:

1. **Phone side:** AudioWorklet extracts raw PCM frames (16-bit, 16 kHz, mono) from the browser microphone and sends them over the WebSocket as binary frames.
2. **C# side:** SquadDash receives binary frames and writes them directly into the existing PushAudioInputStream (configured with AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1)), which is already the format used by desktop PTT.
3. **No NAudio transcoding required** — if the AudioWorklet resamples to 16 kHz/16-bit/mono natively, the PCM frames can be written directly. NAudio is only needed if the phone sends a different sample rate.

### Implementation notes for Talia / Arjun

- The onAudioChunk callback in RemoteBridgeConfig must carry ArrayBuffer (binary PCM frames).
- The SDK PR (see SDK PR Ownership decision, 2026-04-30) should define onAudioChunk(data: ArrayBuffer): void.
- AudioWorklet processor target format: 16 kHz, 16-bit signed little-endian, mono.
- C# side: no new Azure SDK dependency needed — existing PushAudioInputStream + SpeechRecognitionService pattern works unchanged.

**References:** .squad/rc-mobile-architecture.md §Key Decisions #2, §Option B


---

## RC Mobile — PTT-During-LLM-Run Policy (2026-04-30)

**By:** Orion Vale (Lead Architect)
**Status:** Decided

### Context

When a user initiates PTT (push-to-talk) on the phone while an LLM response is already streaming, the three options were:

- **(a) Queue the voice prompt** — record and hold; submit when the current run completes.
- **(b) Abort the current run** — interrupt the in-flight LLM response and start the new voice prompt.
- **(c) Reject with feedback** — block PTT initiation and show the user a status message; auto-unblock when the run ends.

### Decision: Option (c) — Reject with graceful feedback, auto-unblock

**Policy:** When PTT is initiated on the phone while _isPromptRunning is true, the phone UI:

1. Shows **"⏳ AI is responding — wait before speaking"** (or equivalent localized copy).
2. **Disables the PTT button** (visual feedback: greyed out or pulsing).
3. **Auto-unblocks** when the "done" event fires from unPrompt.ts — the same event used for push notification turn-complete hooks.
4. Clears the status message and re-enables the PTT button once unblocked.

**Audio is never captured during the blocked window** — the AudioWorklet is not started until PTT is unblocked. This avoids wasted bandwidth and confusing UX where audio is captured but the prompt is silently dropped.

### Why not the other options

- **Option (a) — Queue:** Queuing voice audio on the phone adds significant complexity (buffer management, timeout handling if recording is long, stale audio problem if the LLM run takes >30s). The desktop does not queue PTT either; the _isPromptRunning guard simply prevents PTT from starting. Consistency with desktop behavior argues against queuing on mobile.
- **Option (b) — Abort:** Aborting an in-flight LLM run is destructive — partial tool calls may be abandoned, the AI may be mid-sentence. This option is reserved for explicit user action (the Abort button), not for accidental PTT on a phone.

### Implementation notes

**TypeScript/RC bridge side (Talia):**
- When handleRcStart wires the RemoteBridgeConfig, it should expose a "busy" status push mechanism to each connected phone.
- When _isPromptRunning transitions true → false (i.e., "done" event received in C#), C# pushes an "rc_status" message over the WebSocket with { status: "idle" }.
- Complementary: when _isPromptRunning transitions false → true, push { status: "busy" }.

**Phone browser side (Talia):**
- PTT button listens for c_status messages.
- On { status: "busy" }: disable button, show "⏳ AI is responding…" overlay.
- On { status: "idle" }: re-enable button, clear overlay.
- On PTT hold-start while already usy: show message and swallow the event (do not open AudioWorklet).

**C# side (Arjun):**
- setIsPromptRunning in MainWindow.xaml.cs already fires when _isPromptRunning changes. Add a call to RcBridge.BroadcastStatus(busy) in that setter alongside the existing UI refresh.
- RcBridge.BroadcastStatus(bool busy) sends { "type": "rc_status", "status": busy ? "busy" : "idle" } to all connected WebSocket clients.

### Alignment with desktop PTT behavior

Desktop: _voiceStartedWithSendEnabled = _pttTargetTextBox == PromptTextBox && !_isPromptRunning — voice capture starts but "send on release" is disabled if a run is in progress. The mobile policy is strictly more conservative (no capture at all during a run), which is appropriate given the higher latency and lack of visual context on a phone.

**References:** .squad/rc-mobile-architecture.md §Key Decisions #4


---

## RC Mobile — Session Isolation Policy for Multi-Phone Connections (2026-04-30)

**By:** Orion Vale (Lead Architect)
**Status:** Decided

### Context

RemoteBridge allows multiple simultaneous WebSocket connections. When two phones are connected and both submit prompts, the question is whether they share one SquadBridge session (shared ddMessage history) or each get an isolated session.

The current handleRcStart implementation already answers this implicitly: onPrompt calls ridge.runPrompt(text, ..., { cwd: request.cwd, sessionId: request.sessionId }) using the **single ridge (SquadBridgeService singleton)** and the **single sessionId from the c_start request**. All phones share one session today.

### Decision: Shared session — intentional, no isolation needed

**All phone connections share a single SquadBridge session and conversation history.** This is the correct policy for SquadDash's use case and requires no code change to handleRcStart.

### Rationale

1. **Mental model: input devices, not independent users.** RC phones are remote controls for the same SquadDash instance. They are additional input surfaces for the same person, not separate users with separate contexts. Two phones connecting simultaneously means the same user switched devices or is testing — not two users having independent conversations.

2. **Consistent with the PTT-during-LLM-run policy.** The reject-with-feedback policy (Key Decision #4) already serialises prompts: only one prompt runs at a time. Shared session history is coherent because the AI always sees a sequential conversation thread, never concurrent interleaved prompts.

3. **ddMessage broadcast goes to all connected phones.** When a prompt comes in from any phone, cBridge.addMessage("user", text) broadcasts to all connected clients. The AI's response via ddMessage("agent", ...) also broadcasts to all. This creates a natural "shared screen" experience — all phones see the same conversation — which is the correct UX for a collaborative or multi-device household scenario.

4. **Isolated sessions would require significant architectural complexity for no benefit.** Per-connection isolation would require: a Map<connId, SquadBridgeSession>, per-connection session lifecycle (create on connect, destroy on disconnect with cleanup), per-connection message history (cannot broadcast ddMessage to all), and coordination with the _isPromptRunning guard across sessions. None of this aligns with the "phone as remote control" mental model.

### Implementation note

No code change is needed. The existing onPrompt wiring in handleRcStart correctly implements this policy:

`	s
onPrompt: async (text) => {
    rcBridge.addMessage("user", text);         // broadcast to ALL phones
    await bridge.runPrompt(text, handlers, {    // shared session
        cwd: request.cwd,
        sessionId: request.sessionId
    });
}
`

The only future consideration: if RemoteBridge gains a connId parameter on onPrompt, the handler should continue to ignore it (treat all connections as shared) unless a future explicit policy change is made.

**References:** .squad/rc-mobile-architecture.md §Key Decisions #5

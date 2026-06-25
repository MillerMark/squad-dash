# SquadDash Task List

> This file is the persistent backlog for SquadDash development.
> Update status inline (`- [ ]` → `- [x]`). AI agents read this file for context.
> Owner is listed per item where known.
> Completed items live in `.squad/completed-tasks.md`.

---

## ⚫ Critical


## 🟡 Mid Priority

- [ ] **[Architecture] Shared vs Local data folder convention — ADR** *(Owner: Orion Vale)*
  SquadDash needs a standard answer for where team-shared vs user-local data lives.
  Shared data lives in version control (`.squad/shared/` or directly in `.squad/`).
  Local data lives in AppData. Both paths should be well-defined for every data type:
  notes, tasks, code-health entries, loop files, guided tour steps.
  Deliverable: ADR in `.squad/decisions.md` defining the folder contract + a `DataScope`
  enum (Shared / Local) usable by all data-aware features.

- [ ] **[Notes] Convert inbox message to note via right-click** *(Owner: Lyra Morn)*
  Right-click on any inbox message → context menu item "Add as Note".
  Populates a new note with the message subject as the note title and body as content.
  Depends on: shared/local data convention ADR.

- [ ] **[Notes] Add New Shared Note from notes panel right-click** *(Owner: Lyra Morn)*
  Right-click in the Notes panel → "Add New Shared Note".
  Shared notes are stored in `.squad/notes/` (version-controlled).
  Local notes remain in AppData.
  Show a 🌐 (or team) icon on shared items to distinguish them from local ones.
  Depends on: shared/local data convention ADR.

- [ ] **[Shared data] Shared-item indicator icon across panels** *(Owner: Lyra Morn)*
  Tasks, notes, code-health entries, and loop files that live in `.squad/` (shared/version-controlled)
  should show a small icon (e.g. 🌐 or a people icon) to indicate they are team-shared.
  Local items (AppData) show no icon or a different indicator.
  Depends on: shared/local data convention ADR.

- [ ] **[Guided Tour] Implement Guided Tour feature** *(Owner: Lyra Morn)*
  Full spec finalized 2026-06-24. Key components:
  - `GuidedTourStep` data model: Title, MarkdownText, TargetControlId, CalloutPlacement, PreAction
  - Steps file: `.squad/guided-tour.json` (workspace override) with embedded-resource fallback
  - `FrmGuidedTourNavigator`: floating no-titlebar draggable window, ← Prev / Step N of N / Next → / ✕ Close
  - Navigator position saved to AppData; validated on restore (must be fully on-screen)
  - Entry: first-launch prompt per machine (AppData flag), Help menu, Options → Discoverability
  - New Help menu: Start Guided Tour | Documentation (GitHub URL) | About
  - Layout: auto-save on tour start, silently restore on tour close
  - Callout closes on Next/Previous; closing callout directly ends tour + points at Help menu
  - Dev mode: Dev menu item opens guided-tour.json in editor; step-by-step preview navigation
  - Options → Discoverability: "Start the Tour" button

- [x] **[Developer Menu] Fix F11 Theme Reveal conflict with Full Screen Transcript**
  Moved Theme Reveal to Ctrl+F11. Key handler updated to pass bare F11 through to Full Screen Transcript.

- [ ] **[Docking] Refactor DockingMapBuilderto loop-based zone layout***(Owner: Lyra Morn)*
  DockingMapBuilder.BuildDockingMap currently uses hardcoded per-zone if/else chains for suppression,
  thin generation, and slot layout (Left3/Left2/Left, Right/Right2/Right3). This makes adding a 4th
  zone tier a near-rewrite. Prerequisite: lock down test cases for all N=0/1/2 configurations (Left and
  Right sides) via the docking test playback window. Then replace the hardcoded chains with a data-driven
  loop: represent each side as an ordered list of ZoneDescriptor records (zone, panels, sourceInZone,
  suppressFlag), derive occupied/thin counts algorithmically, and emit slots in a single forward pass.
  The N+1 thin rule and adjacent-thin check should fall out naturally from the loop without special cases.
  Prerequisite: All 1/2/0-zone docking test cases recorded and passing.

- [ ] **[Architecture] Extract shared MarkdownEditorPanel base class** *(Owner: Lyra Morn)*
  MaintenanceTaskEditorWindow and MarkdownDocumentWindow both implement markdown editing with preview
  but diverge on undo strategy (manual stack vs. WPF built-in), preview renderer (FlowDoc only vs.
  WebBrowser+FlowDoc), and feature set (maintenance editor is missing toolbar, find bar, case cycling,
  auto-list continuation). A shared base class or embedded-control approach would eliminate duplication
  and close the feature gap. See source editor audit (inbox 2026-06-08) for full analysis and options.
  Short-term: attach MarkdownEditorToolbar to MaintenanceTaskEditorWindow.
  Long-term: extract shared MarkdownEditorPanel that both editors subclass.

- [ ] **[Duplication] Investigate DUP-007— re-scan or re-examine original findings** *(Owner: Fred)*
  The original duplication scan (2026-05-21) identified DUP-001–010. Fixes were committed for
  DUP-001–006 and DUP-008–010. DUP-007 has no recorded fix and no description was persisted.
  Re-run the duplication scan (or examine git history from that session) to identify what DUP-007
  was and whether it still needs addressing.

- [ ] **[Routing] Add hard-coded argus-weld file-path guard in BuildStrongMatchRoutingInstruction** *(Owner: Arjun Sen)*
  Orion Vale architectural review (2026-05-24) identified that Argus Weld can be falsely triggered
  by any prompt mentioning a C# source file with "maintenance" in the name (e.g. MaintenanceRunner.cs).
  Fix: in SquadBridgePromptBuilder.BuildStrongMatchRoutingInstruction, add a hard-coded guard —
  if the candidate agent handle is "argus-weld", discard all matched signals unless at least one
  signal is a path starting with ".squad/maintenance" and ending with ".md".
  Must ship in the binary so it applies to ALL workspaces on upgrade.
  File: SquadDash/SquadBridgePromptBuilder.cs

- [ ] **[Routing] ADR — routing.md cannot express negative ownership signals** *(Owner: Mira Quill)*
  Orion Vale (2026-05-24) found a structural gap: ExtractOwnershipTokens harvests ALL backtick
  tokens as positive signals — no way to say "not this file" in the Examples column.
  Record as ADR in .squad/decisions.md. Orion's recommended option: a routing: file-pattern-only
  metadata field in team.md/charter that restricts strong-match to file-path signals only.

- [ ] **[Orion audit] `_isPromptRunning` — move ownership to PromptExecutionController** *(Owner: Arjun Sen)*
  `_isPromptRunning` is declared in MainWindow, mutated by PEC via setter delegate, read by
  `BackgroundTaskPresenter` via getter delegate, and read directly by MainWindow at 8 call sites.
  PEC is the natural owner (it sets the flag at prompt start/end). Consolidate ownership in PEC
  and expose it via a clean property rather than scattered delegates.

- [ ] **[Vesper audit] DocStatusStore — review silent catch blocks** *(Owner: Arjun Sen)*
  Vesper's audit flagged 34 bare catch blocks across the codebase; `DocStatusStore` in particular
  has silent failure suppression that may hide real errors. Review and replace with at minimum a
  `SquadDashTrace.Write` call so failures surface in the trace log.

---

## 🔴 High Priority

- [ ] **[Bug] Prompt history cycling(Ctrl+Up/Down) doesn't copy attachments to queued item***(Owner: Lyra Morn)*
  When a queued prompt item is selected and the user cycles through prompt history with Ctrl+Up/Down,
  only the text content is brought in — attachments from the history entry are not copied to the queued item.
  Attachments appear on the active draft instead. Expected: cycling history into a queued item should
  populate both text and attachments on that queued item, not the draft.

- [ ] **WinGet — smoke-test installer on clean VM***(Owner: you — manual step)*
  Run `.\installer\build-installer.ps1 -Version 1.0.0` (requires Inno Setup 6 installed locally),
  then install on a clean Windows VM with only Node.js pre-installed. Verify: launcher starts,
  SDK bridge connects, workspaces resolve correctly from `%LocalAppData%\SquadDash\app\`.
  **Blocks:** GitHub Release, WinGet submission.

---

## 🔴 High Priority — Maintenance Mode (Phase 1 MVP)

> Feature: SquadDash enters "Maintenance Mode" after configurable idle time and executes tasks from `.squad/maintenance.md`.
> Phase 1 delivers the full backend pipeline end-to-end. Panel UI is Phase 2.

---

## 🟡 Mid Priority

- [ ] Test and evaluate the Decompose feature
  Manually walk through decompose in a range of circumstances to verify reliability.
  Define a test plan covering: typical prompts, edge cases (empty input, very long input,
  ambiguous requests), and confirm output quality and consistency.
  Right-click a task row → "Edit Task" opens a custom WPF editor window (code-only, no XAML).
  Layout: Title textbox (large font, top); Properties section (enabled, frequency, safety);
  UI Options section (YAML editor for `options:` block on left, live rendered preview on right);
  Instructions section (markdown preview on left, syntax-highlighted text editor on right with
  `{{variable}}` hover tooltips and `{{#if}}`/`{{/if}}` highlighting); Cancel + Save buttons.
  Both text editors support double-Ctrl voice dictation via `PttTextBoxAttachment`.
  Save writes back to the source maintenance file via `MaintenanceMdParser.UpdateTask()`.
  Requires adding `SourceFilePath` to `MaintenanceTask` record. Requires round-trip tests.
  **Prerequisite for:** Maintenance — multi-file support.

- [ ] **WinGet — create GitHub Release v1.0.0** *(Owner: you — manual step)*
  After smoke-test passes: create GitHub Release `v1.0.0`, attach the installer `.exe` and its
  SHA256 hash. The public download URL is required for `wingetcreate`.
  **Blocked by:** smoke-test passing.

- [ ] **WinGet — generate and submit manifest** *(Owner: Jae Min — automated once release exists)*
  Run `wingetcreate new <installer-url>`, add `OpenJS.NodeJS` as a `PackageDependencies` entry
  in the installer manifest YAML, open PR to `microsoft/winget-pkgs`.
  **Blocked by:** GitHub Release v1.0.0 existing with a stable download URL.

- [ ] **WinGet — Phase 2: release automation** *(Owner: Jae Min)*
  Create `.github/workflows/release.yml`: on `v*` tag push, run `dotnet publish`, bundle
  installer, upload to GitHub Release, run `wingetcreate update`, open PR to winget-pkgs
  automatically. Requires `WINGET_PKGS_PAT` repo secret.
  **Blocked by:** Phase 1 (manual release) succeeding at least once.

- [ ] **WinGet — write RELEASING.md runbook** *(Owner: Jae Min)*
  Document the full release checklist: bump version, tag, let automation run, verify winget PR.
  Include manual fallback steps. Useful for the first few releases before automation is trusted.

---

## 🟡 Mid Priority — Annotation Editor (Paste Image Window)

- [ ] **[Annot #1] Shift-click multi-drop mode indicator** *(Owner: Lyra Morn)*
  When arrow or rectangle button is shift-clicked to enter multi-drop mode, show a rounded-rect
  underline beneath the active button (same style as document chips / orientation buttons).
  Update tooltip/hover hint to say "Shift+click to drop multiples in a row". Update docs.

- [ ] **[Annot #2] Bug: double undo for each rectangle in shift-click mode** *(Owner: Lyra Morn)*
  Each rectangle dropped in shift-click multi-drop mode adds 2 undo entries instead of 1.
  Dropping 3 rectangles requires 6 Ctrl+Z presses. Not observed for arrows. Find duplicate push
  to the undo stack in the rectangle placement path and remove it.

- [ ] **[Annot #3] Arrow drag: origin point too close to arrowhead** *(Owner: Lyra Morn)*
  When click-dragging to place an arrow, the drag origin starts too close to the tip.
  At minimum double the initial drag distance so the arrow has meaningful length on first drag.

- [ ] **[Annot #4] Enter key crops to crop rectangle + undo + window resize** *(Owner: Lyra Morn)*
  When the crop rectangle is visible and the user presses Enter:
  - Crop the image to the rectangle bounds
  - Resize the annotation window to fit the new (smaller) image
  - Push a full undo entry so Ctrl+Z restores the full image + window size
  - After cropping, user should be able to zoom (Ctrl+wheel) and annotate the smaller region

- [ ] **[Annot #5] Text annotation tool (T button)** *(Owner: Lyra Morn)*
  New toolbar button "T". On click, user draws a rectangle on the canvas. That rectangle becomes
  a text annotation box with: flashing I-beam caret, character entry + paste + backspace/delete,
  word-wrap within box, auto-shrink font to fit (min 12pt Calibri), drag handles to resize/reposition,
  Shift+Enter for newline, Enter to deselect. Font: Calibri ≥12pt (OCR-legible).

- [ ] **[Annot #6] Bug: mouse cursor drop tool — nothing happens on click** *(Owner: Lyra Morn)*
  After using arrow tool, clicking the "drop cursor" tool does nothing. Likely a tool-state
  machine bug — tool mode may not be resetting correctly when switching from arrow to cursor tool.
  Investigate state transitions between annotation tool modes.

- [ ] **[Annot #7] Cropping tool cursor (Photoshop-style)** *(Owner: Lyra Morn)*
  When the crop tool / default crop state is active, show a Photoshop-style crop cursor
  (overlapping rectangles / corner brackets). Add to AnnotationCursors class.

- [ ] **[Annot #8] Drop mouse-cursor tool cursor: arrow + plus** *(Owner: Lyra Morn)*
  When the "drop mouse cursor" tool is active, the canvas cursor should show a mouse-arrow icon
  with a small plus/crosshair next to it (same pattern as arrow/rectangle tool cursors).
  The center of the crosshair is the hotspot. Add to AnnotationCursors class.

- [ ] **[Annot #9] Attach Image + Cancel buttons — move to far right** *(Owner: Lyra Morn)*
  Reposition the Attach Image and Cancel buttons to be right-aligned in the toolbar/footer bar.

---

## 🟡 Mid Priority — MainWindow.xaml.cs Refactoring

> Tracked from Orion + Lyra review (2026-05-19). Full details in session files.
> Full report: `mainwindow-refactor-review.md` (Orion) + `mainwindow-xaml-review.md` (Lyra).
> Current file: ~28,687 lines. Goal: extract cohesive domains into separate classes.

- [x] **[Refactor Phase 1a] Extract `TranscriptSearchController`** *(Owner: Lyra Morn)*
  ~930 lines of transcript search logic (find-in-transcript, Shift+F3 cycling, highlight adorner
  management). `SearchWalker` is already embedded. Minimal `this` dependencies — easy to inject
  via constructor. Fixes the Shift+F3 duplication that currently exists in 2 separate search paths.
  Full line ranges in `mainwindow-refactor-review.md`.
  ✅ Completed 2026-06-23 — commit e612ef0

- [ ] **[Refactor Phase 1b] Extract `PromptKeyboardController`** *(Owner: Lyra Morn)*
  ~700 lines of KeyDown/KeyUp handlers for the prompt input area. Pure input routing with no deep
  WPF visual-tree dependencies. Easy to inject Dispatcher + action callbacks.
  Full line ranges in `mainwindow-refactor-review.md`.

- [ ] **[Refactor Phase 1c] Extract `WatchPanelPresenter`** *(Owner: Lyra Morn)*
  ~85 lines — smallest extraction candidate. Self-contained watch-panel sync logic.
  Good pattern-setter for the larger extractions that follow.

- [ ] **[Refactor Phase 1d] Quick XAML wins — constructor lambdas + SyncWatchPanel + ContextMenuOpening** *(Owner: Lyra Morn)*
  Three zero-risk code-behind cleanups identified in Lyra's XAML review:
  1. 650-line constructor packed with inline lambdas → extract to named event handlers (~200 lines)
  2. `SyncWatchPanel` Clear+loop+Add → `ItemsControl` + `DataTemplate` (~80 lines)
  3. `ContextMenuOpening` builds ContextMenu in C# → move to static XAML `<ContextMenu>` resource (~50 lines)

- [ ] **[Refactor Phase 2a] Extract `QueueTabController`** *(Owner: Lyra Morn)*
  ~1,600 lines — tab drag state machine, queue tab click handling, active-tab logic.
  Needs `_promptQueue` reference + a few UI callbacks. Medium risk.
  Full line ranges + dependency list in `mainwindow-refactor-review.md`.

- [ ] **[Refactor Phase 2b] Extract `AgentCardController`** *(Owner: Lyra Morn)*
  ~1,500 lines — agent card building, coloring, sync logic. References `_agents` collections.
  Coordinate with any concurrent AgentStatusCard changes. Medium risk.

- [ ] **[Refactor Phase 2c] Extract `RemoteAccessController`** *(Owner: Arjun Sen)*
  ~475 lines — RC-session state + bridge calls. Two divergent restart paths that should be unified
  as part of extraction. Arjun owns backend services; RC state is a backend concern.

- [ ] **[Refactor Phase 2d] Extract `DismissOnMovementHelper`** *(Owner: Lyra Morn)*
  Fade-popup dismiss-after-10px gesture duplicated in **5 places** in MainWindow.xaml.cs.
  Extract to a shared helper. Low-risk deduplication, high signal-to-noise.

- [ ] **[Refactor Phase 3a] Extract `DocsTreeController`** *(Owner: Lyra Morn)*
  ~2,500 lines — docs tree expand/rename/filter logic. Largest single LOC win.
  Well-clustered but moderate dependencies on workspace state. Medium risk.

- [ ] **[Refactor Phase 3b] Extract `ToolEntryPresenter`** *(Owner: Lyra Morn)*
  ~2,500 lines — tool-result card rendering, repeating `MakeItem`/`MakeSep` locals duplicated
  in 2+ context menu builders. High LOC win + deduplication. Medium risk.

- [ ] **[Refactor Phase 3c] Extract `DocScreenshotController`** *(Owner: Lyra Morn)*
  ~960 lines — screenshot attach/preview on docs panel. Needs `_pastedImageStore` reference.

- [ ] **[Refactor Phase 4] Extract `TranscriptPanelLayoutController`** *(Owner: Lyra Morn)*
  ~1,900 lines — layout/sizing logic for the transcript panel. Deeply entangled with
  `RichTextBox` visual tree. High risk — do last, after Phase 3 is complete and patterns
  are established. Do NOT start until Phase 3 is done.

---

## 🟡 Mid Priority — Maintenance Mode (Phase 2 Enrichment)

---

## 🟡 Mid Priority — Maintenance Mode (Phase 3 Polish)

- [ ] **[Inbox] Inbox message save lost on shutdown — save INBOX_MESSAGE_JSON earlier** *(Owner: Lyra Morn)*
  INBOX_MESSAGE_JSON is currently saved in the `case "done":` bridge event handler. If the app shuts
  down while a turn is in-flight (streaming), the save never runs and the message is silently lost.
  Fix: save the inbox message as soon as the full response text is finalized (or at streaming end),
  not only on `bridge-done`. Consider a lightweight flush-on-close path that drains any pending
  INBOX_MESSAGE_JSON from the current response before shutdown completes.

- [ ] **[Maintenance] `branch`-safety tasks branch from current HEAD, not always from main** *(Owner: Arjun Sen)*
  When two `safety: branch` tasks run in a session, each task receives a prompt that says "create branch
  `maintenance/YYYYMMDD-<slug>` before making any code changes." The AI agent runs `git checkout -b`
  from whatever branch is currently checked out. Since task 1 commits to its branch and the runner does
  not switch back to main before task 2 starts, task 2 inherits task 1's commits on its new branch.
  Fix: `MaintenanceRunner` should record the base branch at session start (e.g. `git rev-parse --abbrev-ref HEAD`)
  and inject an explicit `git checkout <base-branch>` step into each `branch`-safety task's preamble
  before the `git checkout -b` instruction, so every task always branches from the same known base.

---

## 🟢 Low Priority

- [ ] **[CompactPickerButton] Extract shared init helper to avoid duplicate fix targets** *(Owner: lyra-morn)*
  CompactPickerButton.cs has two near-identical initialization blocks for two button variants.
  The hover-flicker fix (commit 9f0b5fb) had to be applied to both blocks. Extract the shared
  setup into a helper method so future fixes only need to be applied once. Low-priority refactor.

- [ ] **[CodeHealthTaskEditor] Fix branchName alias drift risk in _optionValues** *(Owner: arjun-sen)*
  In CodeHealthTaskEditorWindow.cs, `_optionValues["branchName"]` is set at construction time as a
  one-off alias for `_optionValues["branch"]`. If `branch` is ever updated later in the session,
  `branchName` will not update, causing subtle template variable bugs ({{branchName}} shows stale value).
  Either remove the alias and standardize on one key, or introduce a proper aliasing/computed-value
  mechanism. Found during code health review 2026-06-16 (commit 0778c38).

---

## 🟢 Low Priority — maintenance.md File Quality(Malik's Suggestions)

> These tasks clean up and improve the default `.squad/maintenance.md` file format and content.
> Run one at a time via the loop. S1, S3b, S4 require parser/UI changes; the rest are file-only edits.
> These tasks have a dependency order: do S1 first (new format), then S2/S3b/S4 against the new format,
> then S5/S6/S7/S8 as independent cleanup passes.

---

## 🔵 Low Priority

- [ ] [Diagnostics] Trace FileSystemWatcher failure in ConfigureGitHeadWatcher
  In `MainWindow.xaml.cs`, `ConfigureGitHeadWatcher()` silently swallows `FileSystemWatcher`
  initialization failures with only a comment. Add `SquadDashTrace.Write(...)` in the catch block
  so failures are diagnosable when the branch indicator is blank in unusual environments.

- [ ] [Test Infrastructure] Embed squaddash.md resource in SquadDash.Tests.csproj
  The test `EnsureSquadDashUniverseFiles_WritesSquadDashMdToBothUniversesAndTemplatesUniverses`
  (SquadDash.Tests/SquadInstallerServiceTests.cs:125) uses `Assume.That` to skip file-content
  assertions when the embedded `squaddash.md` resource is not compiled into the test assembly.
  The directory-creation assertion passes but the full test is never exercised in CI.
  Investigate whether embedding the resource in the .csproj would allow full test coverage,
  or whether the Assume pattern is intentional. Update test documentation if the pattern
  is the correct long-term approach.

- [ ] [UX] Flash/highlight PromptAttachmentViewerWindow on re-activation
  When the user clicks the 📎 attachment link a second time, the viewer is brought to front
  (no duplicate spawning) but there's no visual cue that it appeared. Add a brief flash or
  highlight animation (e.g. brief border color pulse or window-level opacity fade-in) so the
  user notices the window even when it was hidden behind the main window.
  Related: `PromptAttachmentViewerWindow.Show()` — the singleton re-activation path.

- [ ] **OpenAI Whisper speech provider — customer request***(Owner: Orion Vale → Lyra Morn)*
  Customer request: support OpenAI speech API as an alternative to Azure Cognitive Speech, for users
  without an Azure subscription. Impact: ~5 modified files + 2 new files.
  Required changes:
  1. Extract `ISpeechRecognitionService` from `SpeechRecognitionService.cs` (events: `PhraseRecognized`, `VolumeChanged`, `RecognitionError`; methods: `StartAsync`, `StopAsync`, `WriteAudioData`)
  2. New `WhisperSpeechRecognitionService.cs` implementing that interface via OpenAI REST API
  3. `ApplicationSettingsSnapshot` + `ApplicationSettingsStore` — add `SpeechProvider` enum ("Azure" | "OpenAI")
  4. `PreferencesWindow.cs` — provider dropdown; show Whisper key field when OpenAI selected; hide Region field (not needed for Whisper)
  5. `MainWindow.xaml.cs` line 7641 — factory-create the right provider from settings
  6. `RemoteSpeechSession.cs` — use the interface (for RC phone PTT)
  Note: Whisper doesn't support phrase-list grammar hints — team name boosting silently becomes no-op for Whisper users.
  Note: Whisper is batch-oriented; streaming requires audio buffering — may have higher latency than Azure.

- [ ] **SubSquads — investigate and expose in UI** *(Owner: Orion Vale → Lyra Morn)*

- [ ] **[Vesper audit] Test coverage — screenshot infrastructure** *(Owner: Vesper Knox)*
  `ScreenshotRefreshRunner`, `ScreenshotNamingHelper`, and related fixture loaders have no unit
  tests. The refresh runner requires a WPF dispatcher — use integration-test seam or thin adapter
  pattern. Naming helper is pure logic and can be covered directly.

- [ ] **[Vesper audit] ScreenshotRefreshRunner — iterate light+dark variants** *(Owner: Vesper Knox)*
  `ScreenshotRefreshRunner.cs:172` has a TODO: "iterate twice for light+dark variants" but only
  one theme pass is currently executed. Implement dual-pass so screenshots are generated for both
  Light and Dark themes in the same refresh run.

- [ ] **Personal Squad — investigate and expose in UI** *(Owner: Orion Vale → Lyra Morn)*
  The `squad personal` feature was bridged (personal_list/personal_init) but the Workspace menu
  item was removed — it printed to transcript only with no visible feedback. Investigate what
  "personal squad" means in the current Squad SDK version (cross-workspace personal agents stored
  in the global Squad data dir), then design and implement useful UI if the feature has real value
  for SquadDash users.

---

## ✅ Recently Completed

> Full details in `.squad/completed-tasks.md`. This section is a compact AI-recall index only.


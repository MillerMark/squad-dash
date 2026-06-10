
## Archived 2026-05-23T20:46:01Z — prune-tasks maintenance pass

- [x] **Doc editor — Phase 1: swap DocSourceTextBox to RichTextBox + plain-text adapter** *(Owner: Arjun Sen)*

- [x] **Doc editor — Phase 2: migrate all DocSourceTextBox API call sites to RichTextBox** *(Owner: Arjun Sen)*

- [x] **Doc editor — Phase 3: animated inline indicator for pending Revise with AI** *(Owner: Arjun Sen)*
  Create a `RevisionPendingIndicator` class: an `InlineUIContainer` wrapping an animated WPF element
  (pulsing border or spinner). On Revise with AI submit (`onSubmitting` callback): insert the indicator
  `Inline` at the selection's `TextPointer`. On `ApplyDocRevision` (success or fallback): remove it.
  Indicator must never appear in `.GetPlainText()` output (it is an object node, not a character).
  The user's normal selection should be unaffected — the indicator is a separate inline sibling.
  **Blocked by:** Phase 2.

- [x] **[Vesper audit] Test coverage — BuiltInPromptInjections, PromptContextDiagnostics** *(Owner: Vesper Knox)*
  Both classes have zero test coverage. `BuiltInPromptInjections` depends on the triggered injection
  evaluator — write tests using a fake/stub evaluator. `PromptContextDiagnostics` is pure formatting
  logic and should be straightforward to cover directly.

- [x] **[Vesper audit] Test coverage — WorkspaceOpenCoordinator, PromptInteractionLogic multi-path workflows** *(Owner: Vesper Knox)*
  Both classes have partial coverage but the branching paths (error paths, edge cases, multi-step
  coordination flows) are not exercised. Audit the existing tests, identify missing paths, and fill
  them in. Focus on correctness contracts rather than line-count.

- [x] **Loop Settings popup — render loop file frontmatter as UI controls** *(Owner: Lyra Morn)*
  When the user right-clicks the gear/settings icon in the Loop panel, parse the YAML frontmatter
  of the active loop `.md` file and render its keys as controls in a popup:
  - Known UI keys get typed controls: `commit_after_task` → 3-way radio/dropdown (`always`/`never`/`ask`);
    bool keys (`build_verify`, `test_after_task`) → toggle/checkbox.
  - Unknown string keys get a text field (injection variables such as `build_command`, `commit_trailer`).
  - On save, write updated values back to the frontmatter block of the loop file.
  - Keys prefixed with `#` (comments) or without a recognized type hint should be ignored or shown as read-only labels.
  Scope: parsing, popup XAML, save-back logic. The `{{variable}}` substitution at prompt send time is a separate task.
  ✅ Implemented — `OpenLoopConfigFlyout` / `LoopConfigFlyoutMode`; frontmatter parsed via `LoopMdParser.Parse` and rendered as typed controls; save-back wired.

- [x] **Loop "Do these" — inject TasksFilterBox text into live loop prompt** *(Owner: Lyra Morn)*
  When the "▶ Do these" button starts the loop, the active Tasks panel filter text is currently only
  substituted in the preview window (`RefreshLoopMergedView`), not in the actual prompt sent to the AI.
  The `[**FILTER**]` placeholder (and its smart context-aware expansion) must also be applied at loop
  start time — either by writing a temporary substituted copy of the loop file, or by injecting the
  filter as a `{{variable}}` that the loop controller resolves before sending the prompt.
  ✅ Fixed (commit d4c5c1b) — `BuildFilterInstruction` shared by preview+live; `LoopController.StartAsync` takes `filterText`; both paths unified via `BuildMergedBody`; system vars `{{routing_instruction}}` etc. removed from shipped loop files.

- [x] **Loop Settings — `{{variable}}` injection at prompt send time** *(Owner: Arjun Sen)*
  Before sending the loop prompt, substitute `{{key}}` tokens in the prompt body with the
  corresponding frontmatter values from the active loop file. Known UI keys (`commit_after_task`,
  `build_verify`, `test_after_task`) and user-defined injection variables (`build_command`,
  `commit_trailer`, etc.) should all be substituted. Missing keys are left as-is.
  Group-type options (UI headers) are skipped. Implemented in `LoopController.ExpandVariables`
  and `LoopMdParser.BuildMergedBody`.

- [x] **Loop template preprocessing — `{{#if}}`/`{{#unless}}` conditional blocks** *(Owner: Arjun Sen)*
  Extend the loop prompt preprocessing pipeline (from the `{{variable}}` injection task) to support
  conditional blocks. Loop file authors write conditions in the body; SquadDash evaluates them against
  the active frontmatter option values before sending the prompt. The AI receives only clean resolved
  text — no template syntax. Implemented in `LoopMdParser.PreprocessConditionals`, called before
  plain `{{key}}` substitution in both `LoopController.ExpandVariables` and `LoopMdParser.BuildMergedBody`.

- [x] **Transcript — ghost selection highlight when content streams in** *(Owner: Arjun Sen)*

- [x] **Merge `maintenance/20260521-eliminate-duplication` → main**
  Branch is clean and ready: DUP-001–006, DUP-008–010 fixed; Fred & Rory agents added;
  weekly/monthly maintenance frequency implemented; 1918 tests passing. Merged as d608dae.

- [x] **Revise with AI — dynamic offset tracking for async revision** *(Owner: Arjun Sen)*
  When Revise with AI (Ctrl+Shift+A) is invoked, `selStart` is saved as an integer. If the user
  edits text *before* that offset while the AI is working, the saved integer is stale. Implement
  a text-change listener on `DocSourceTextBox` that adjusts the saved start offset based on
  `TextChangedEventArgs` delta (characters inserted/deleted and at what position). Represent the
  in-flight revision as a tracked `PendingRevision` record with a mutable `AdjustedStart` property.
  On each `TextChanged` event, for every pending revision: if the edit is before `AdjustedStart`,
  shift it by `(inserted - deleted)` chars. When the AI response lands, use `AdjustedStart` and
  the original length to check if the original text is still intact before applying the replacement.
  Multiple in-flight revisions should each track their own offset independently.

- [x] **[Orion audit] Bridge stall — surface "No bridge activity" warning in UI** *(Owner: Orion Vale)*
  PromptHealth already logs `No bridge activity for Xs since prompt start` to the trace file, but this
  is completely invisible to the user. When the warning fires (currently at 96s), show a visible indicator
  in the UI — e.g. a status bar message, a subtle pulsing warning on the spinner, or a tooltip on the
  activity indicator — so the user knows the bridge is stalled rather than just seeing a silent spinner.
  Root cause traced to a test-flood bridge cascade (2026-05-13): bridge was overwhelmed by unit test
  requests flooding the shared process, entered inactivity-timeout loop, left main coordinator prompt
  stuck for 204 seconds with no UI feedback. The `No bridge activity for 96s` log entry existed but
  user had to manually abort after 3+ minutes. Consider also adding an auto-recovery suggestion after
  the threshold (e.g. "Retry" button).

- [x] **[Maintenance] `IdleDetectionService` — idle timer backbone** *(Owner: Arjun Sen)*
  New service: `IdleDetectionService`. Tracks whether SquadDash is fully idle — no prompt running
  (read from `PromptExecutionController`), no loop running (read from `LoopController.IsRunning`),
  no recent user input. Exposes `IdleThresholdReached` event and `ActivityDetected` event. MainWindow
  subscribes to forward key/mouse events to `RecordActivity()`. Idle timeout is configurable in
  `MaintenanceMdConfig`; service re-reads the config when the threshold is first reached to avoid
  a stale timer. After maintenance completes, caller resets the idle timer via `ResetIdleTimer()`.
  Thread-safe: use `volatile bool` / `Interlocked` for all shared state. Do NOT hold a lock during
  the `IdleThresholdReached` callback.

- [x] **[Maintenance] `MaintenanceMdConfig` + `MaintenanceMdParser` — parse maintenance.md** *(Owner: Arjun Sen)*
  Mirror `LoopMdConfig` / `LoopMdParser`. New config type `MaintenanceMdConfig` holds:
  `IdleTimeoutMinutes` (double, default 15), `GlobalSafety` (string: `report-only`/`branch`/`direct`,
  default `branch`), `MaxTasksPerSession` (int, default 5), and `Tasks` (list of `MaintenanceTaskConfig`).
  Each `MaintenanceTaskConfig` has: `Id` (slug), `Enabled` (bool, from checkbox), `Description` (string),
  `Frequency` (`always`/`daily`/`per-commit`), `Safety` (string, per-task override), `RadioOptions`
  (list of strings), `SelectedRadioOption` (string). Parser reads YAML frontmatter for global options;
  body contains task blocks starting with `- [ ]` or `- [x]`. `frequency:` and `safety:` appear as
  indented sub-lines under each checkbox item. Re-uses the CRLF-normalising approach from `LoopMdParser`.
  Returns null if file does not exist or lacks `configured: true` in frontmatter.

- [x] **[Maintenance] `MaintenanceStateStore` — per-task frequency state** *(Owner: Arjun Sen)*
  Reads/writes `.squad/maintenance-state.json`. Schema: dictionary keyed by task ID, each entry has
  `last_run_date` (ISO 8601 UTC date), `last_seen_commit` (SHA string), `run_count_today` (int),
  `last_outcome` (string: `success`/`skipped`/`error`), `last_run_timestamp` (full ISO 8601).
  `IsEligible(task, currentCommitSha)` returns false if: `daily` task already ran today (UTC),
  or `per-commit` task already ran on `currentCommitSha`. `RecordRun(taskId, commitSha, outcome)`
  updates state and persists atomically (write temp file → rename). If git is unavailable,
  `per-commit` tasks fall back to `daily` behavior. Store lives at `.squad/maintenance-state.json`
  (not committed — add to `.gitignore` template).

- [x] **[Maintenance] `MaintenanceRunner` — orchestrates task execution** *(Owner: Arjun Sen)*
  Mirrors `LoopController` pattern. Constructor takes `executePromptAsync`, `abortPrompt`, callbacks
  for `onTaskStarted`, `onTaskCompleted`, `onStopped`, `onError`. Also accepts an `IdleDetectionService`
  reference to subscribe to `ActivityDetected` for mid-run interrupt. Reads `MaintenanceMdConfig`,
  queries `MaintenanceStateStore` for eligibility, runs tasks in order (up to `MaxTasksPerSession`).
  Each task prompt is injected with: task description, safety level (instructs AI to use
  `maintenance/YYYYMMDD-<slug>` branch if `branch`, generate report only if `report-only`),
  and selected radio option. Between tasks checks `_stopRequested` flag — stops cleanly if user
  activity arrives. Fires `onStopped` when done; caller then invokes `MaintenanceReportWriter`.

- [x] **[Maintenance] `MaintenanceReportWriter` — "While You Were Away" report** *(Owner: Arjun Sen)*
  On maintenance session completion, writes `.squad/maintenance-reports/YYYYMMDD-HHmmss.md`.
  Report format: header with session timestamp + duration, then one section per task: task name,
  outcome, what was found/changed, links to branches or files modified. Accepts a list of
  `MaintenanceTaskResult` records built by `MaintenanceRunner` during execution. Also reads
  existing reports in the folder and auto-prunes: keep the 30 most recent, delete older ones.
  After writing, fires event consumed by MainWindow to show the "While You Were Away" banner on
  next user interaction. Also calls `PushNotificationService` `maintenance_completed` event
  if ntfy is configured.

- [x] **[Maintenance] Default `maintenance.md` — content + first-run installation** *(Owner: Mira Quill)*
  Author the default `.squad/maintenance.md` shipped with SquadDash. Frontmatter:
  `configured: true`, `idle_timeout: 15`, `safety: branch`, `max_tasks_per_session: 5`.
  All 8 standard tasks included as unchecked checkboxes with clear description blocks,
  correct `frequency:` and `safety:` declarations, and radio options where applicable.
  Tasks: (1) run & fix failing tests [daily, branch], (2) design smell scan [daily, branch,
  radio: fix it | create branch | report only], (3) TODO/FIXME → tasks.md [per-commit, direct],
  (4) commit quality review [per-commit, report-only], (5) README currency check [daily, report-only],
  (6) prune stale tasks [daily, direct], (7) unused dependency scan [daily, report-only],
  (8) XML doc coverage report [daily, report-only]. Wire into `WorkspaceOpenCoordinator`
  first-run logic to install `maintenance.md` into `.squad/` if not present (mirrors how loop.md
  is installed). Add `maintenance-state.json` to the `.gitignore` template block.

- [x] **[Maintenance] `BuiltInPromptInjections` — add maintenance injection** *(Owner: Arjun Sen)*
  Add a new `TriggeredPromptInjection` entry to `BuiltInPromptInjections.cs`. Id: `builtin:maintenance-guidance`.
  Pattern: `\b(maintenance|idle|maintenance\s+mode|maintenance\s+task|while\s+(you\s+were|i\s+was)\s+away|maintenance\.md)\b`
  Injection text: tells the AI the maintenance config lives at `{workspaceFolder}\.squad\maintenance.md`,
  describes the frontmatter format (configured/idle_timeout/safety/max_tasks_per_session), task
  checkbox format, and available `frequency:` and `safety:` values. Add to `BuiltInPromptInjections.All`.

- [x] **[Maintenance] Tests — Phase 1 service layer** *(Owner: Vesper Knox)*
  Write NUnit tests for: `MaintenanceMdParser` (valid file, missing file, no `configured: true`,
  each frontmatter key, task checkbox parsing, frequency + safety per-task, radio options parsing).
  `MaintenanceStateStore` (eligibility logic for all three frequency modes, atomic write, corrupt-file
  recovery, UTC midnight rollover). `MaintenanceRunner` (task ordering, max-tasks-per-session cap,
  mid-run stop on `ActivityDetected`, correct prompt injection per safety level). Use test fixtures
  mirroring the `LoopMdParser` test patterns in `SquadDash.Tests/`.

- [x] **[Orion audit] AgentThreadRegistry — lock down mutable backing collections** *(Owner: Arjun Sen)*
  Already implemented: all four collections (`ThreadsByKey`, `ThreadsByToolCallId`, `LaunchesByToolCallId`,
  `ThreadOrder`) expose `IReadOnlyDictionary`/`IReadOnlyList` interfaces. Backing fields are
  `private readonly` — external callers cannot mutate them.

- [x] **[Vesper audit] Test coverage — CommitApprovalStore, DocStatusStore, DocTopicsLoader, LoopOutputStore** *(Owner: Vesper Knox)*
  All four classes have zero test coverage despite critical responsibilities:
  `CommitApprovalStore` (JSON persistence, 200-item cap), `DocStatusStore` (approval tracking,
  case-insensitive key lookup), `DocTopicsLoader` (SUMMARY.md parsing, folder scanning),
  `LoopOutputStore` (sequential log numbering). Write unit tests for each.

- [x] Screenshots health panel — XAML + bindings + status UX *(Owner: lyra-morn)*

- [x] **WinGet — document Node.js prerequisite** *(Owner: Jae Min)*
  `runPrompt.js` calls `node` from PATH — Node.js is required but not bundled.
  Update `README.md` to document this prerequisite clearly. The WinGet manifest will list
  `OpenJS.NodeJS` as a dependency but a README callout helps users who install manually.

- [x] **Physics-based activity spinner on agent cards** *(Owner: Lyra Morn)*
  Add a small spinning circle (fits in ~18×18px) to the left of each agent card's status text
  (e.g. "Running", "Waiting", "Stalled"). The spinner uses physics (momentum + friction) driven
  by a `DispatcherTimer` with a `RotateTransform` + `SolidColorBrush` animated via HSV math.

  **Size & placement:**
  - Max diameter: 18px (writing/red state). Min diameter: ~12px (~2/3, thinking/blue state).
  - Placed immediately left of the status text label, occupying ~1 character width.
  - Fits in an 18×18 bounding square.

  **Physics:**
  - Speed driven by agent activity (tool calls, token stream). Each event adds momentum.
  - Friction decay: 20–30 seconds coast-to-stop during silence (not 10s).
  - Fade out only AFTER the spinner has slowed to a complete stop (~2s fade).

  **Color — thinking vs writing:**
  - Blue = thinking/reading (default). Red = actively writing/streaming output.
  - Transition to red when write activity detected; fade back to blue after 5–10s of no writes.
  - Color transitions are smooth (animated, not instant).

  **Saturation/lightness pulse at max speed** (speed perception ceiling):
  - At max spin speed, hue stays fixed; instead oscillate saturation+lightness for visibility.
  - Dark theme: oscillate toward brighter (higher contrast). Light theme: oscillate toward darker.
  - Creates a pulsing "maxed out" look as a second dimension of activity signal.
  - If theme changes while the spinner is running, update the oscillation direction accordingly.

  **Accessibility (colorblind):**
  - Shape/size difference: red state = larger diameter (18px), blue state = smaller (12px).
  - This gives a non-color cue for writing vs thinking.

  **When to show:** only while an agent turn is active (`isCurrentRunThread` true).
  Hide (or fade out after stop) when idle/waiting with no active turn.

- [x] **[Maintenance] `MaintenancePanelController` — WPF panel** *(Owner: Lyra Morn)*
  New panel controller following `TasksPanelController` pattern. Constructor receives: path getter,
  reload action, `MaintenanceMdConfig` getter, `MaintenanceStateStore` reference. `Refresh()` renders
  the task list as checkbox rows with owner/frequency/safety chips, last-run date + outcome per task,
  and radio button groups for tasks that declare them. Writes checkbox toggles back to `maintenance.md`
  via in-place YAML edit (preserve all other content). Panel header shows idle countdown
  ("Next maintenance in: 12:34") or "Running now — [task name]…" with a pulsing dot when active.
  Follows the same `Border` + `StackPanel` structure as Tasks and Notes panels.

- [x] **[Maintenance] Maintenance tab — wire panel into MainWindow** *(Owner: Lyra Morn)*
  Add a "Maintenance" tab to the existing panel tab strip in MainWindow (alongside Tasks, Notes,
  Loop, Approvals). Icon: wrench or gear-clock glyph. Tab badge shows a red dot when an unread
  "While You Were Away" report is available. Wire `MaintenancePanelController` construction in
  MainWindow with the workspace path getter and required callbacks. Subscribe to
  `IdleDetectionService.IdleThresholdReached` in MainWindow to start `MaintenanceRunner` and
  update the panel header to "Running now…". Subscribe to `MaintenanceRunner.onStopped` to
  reset the countdown display.

- [x] **[Maintenance] "While You Were Away" banner — report surfacing UI** *(Owner: Lyra Morn)*
  On window focus (or any user interaction) after maintenance has run: show a non-blocking
  dismissible banner at the top of the transcript area. Banner text: "Maintenance ran while you
  were away — [N tasks completed]  [View Report]". "View Report" opens the most recent report
  file in the Docs panel or a lightweight read-only overlay. Banner auto-dismisses after 12 seconds
  or on click/dismiss. Also set the Maintenance tab badge (red dot) until the user opens that tab.
  Both signals clear on tab open or banner dismiss.

- [x] **[Maintenance] ntfy push notification — `maintenance_completed` event** *(Owner: Arjun Sen)*
  Add `maintenance_completed` as a new event type in `PushNotificationService`. Called by
  `MaintenanceReportWriter` after writing the report. Notification title: "SquadDash Maintenance
  Complete". Body: "N tasks ran — [summary of outcomes]". Tags: `white_check_mark,robot`. Only
  fires if ntfy is configured in workspace settings (mirrors existing notification guard pattern).
  Obeys the existing rate-limiter in `PushNotificationService` (digest if too frequent).

- [x] **[Maintenance] Tests — Phase 2 panel + report integration** *(Owner: Vesper Knox)*
  Integration tests for: `MaintenanceReportWriter` (correct file naming, auto-prune to 30 files,
  push notification call). `MaintenancePanelController.Refresh()` (checkbox state reflects config,
  last-run data from store, running/idle state header text). Banner show/dismiss lifecycle.
  Cover the case where maintenance-state.json doesn't exist yet (first run).

- [x] **[Maintenance] `MaintenanceRunner` → maintenance transcript thread routing** *(Owner: arjun-sen)*
  Wire `executePromptAsync` inside `MaintenanceRunner` to route output to the Vigil/maintenance
  agent thread ID rather than the coordinator thread. Requires coordination with `AgentThreadRegistry`
  to resolve the maintenance thread by identity key at run-start. The thread must exist (or be lazily
  created) before the first task prompt is dispatched. Sub-agent fan-out reports from the coordinator
  should also route to the same maintenance thread.

- [x] **[Maintenance] Maintenance agent proxy/thread identity system** *(Owner: arjun-sen + lyra-morn)*
  When a maintenance cycle starts, create a named agent thread with the Vigil persona identity
  (read from `.squad/agents/vigil/charter.md`) as the preamble/system prompt. All `MaintenanceRunner`
  prompt output routes to that thread. The maintenance agent appears in the agent roster after its
  first run via lazy registration in `AgentThreadRegistry`. Coordinator fan-out sub-agents also route
  their reports to this thread. Identity key: `vigil` (or the final agreed agent handle). Coordinate
  between Arjun (backend thread registration) and Lyra (roster card display).

- [x] **[Maintenance] Wire `IdleDetectionService` into MainWindow** *(Owner: arjun-sen + lyra-morn)*
  Connect `IdleDetectionService` to `PromptExecutionController._isPromptRunning` (via getter delegate)
  and `LoopController.IsRunning` so the service has accurate idle state. Forward user activity events
  from MainWindow's key/mouse input handlers to `IdleDetectionService.RecordActivity()`. This is
  the integration seam that makes the idle timer tick correctly in production. Pairs with Arjun's
  `_isPromptRunning` ownership consolidation task.

- [x] **[Maintenance] Manual trigger button in Maintenance panel** *(Owner: Lyra Morn)*
  Add a "Run Now" button to the Maintenance panel header bar. Clicking it immediately starts
  the `MaintenanceRunner` (bypassing the idle timer). Disabled while maintenance is already running.
  Shows a "Stop" button during a run that calls `MaintenanceRunner.RequestStop()`. This pairs with
  the abort-mid-run capability.


  Below the task list, add a collapsible "Recent Reports" section in the Maintenance panel.
  Scans `.squad/maintenance-reports/` for existing `.md` files and lists them with date/time and
  task count. Clicking a report opens it in the Docs panel or a lightweight viewer. Shows "No
  reports yet" placeholder on first run.

- [x] **[Maintenance] Safety hard enforcement at runtime in `MaintenanceRunner`** — ✅ Implemented (runtime safety floor check before each task execution; SquadDashTrace warning emitted when effective safety overrides declared safety; SafetyOverrideNote added to MaintenanceTaskResult; report writer surfaces ⚠ note; 3 new tests)

- [x] **[Maintenance] Safety floor warning chip in Maintenance panel** — ✅ Implemented (warning chip "⚠ direct commits" shown inline for direct-safety tasks; plain chip for other non-branch safety; 3 new tests; build passes)
  Display a visible warning chip or indicator in the Maintenance panel task list whenever an enabled
  task has `safety: direct` declared. Chip text: "⚠ direct commits". The indicator is per-task
  (appears inline with that task row). Does not block execution — purely informational.

- [x] **[Maintenance] Auto-add `maintenance-state.json` to `.gitignore` on first run** *(Owner: arjun-sen)*

- [x] **[Maintenance] `per-commit` frequency — git fallback tracing** *(Owner: arjun-sen)*
  In `MaintenanceStateStore`, when `git rev-parse HEAD` fails (e.g. repo not initialised, git not
  on PATH), fall back to `daily` frequency behavior for any `per-commit` tasks. Write a
  `SquadDashTrace.Write` entry recording the fallback so it is visible in the trace log. Do not
  silently swallow the error.

- [x] **[Maintenance] Maintenance panel in-place task editing** *(Owner: lyra-morn)*
  Allow the user to toggle task enabled/disabled state directly in the Maintenance panel UI and
  write the change back to `maintenance.md` (flip `- [ ]` ↔ `- [x]` for the corresponding task).
  Analogous to how Loop Settings popup saves frontmatter values back to `loop.md`. Preserve all
  other file content on write-back. Does not require a save button — apply immediately on toggle,
  then reload the parsed config.

- [x] **[Maintenance] Maintenance agent roster entry** *(Owner: mira-quill)*
  Once the final agent name is decided, add the maintenance agent (Vigil or agreed handle) to
  `.squad/team.md` with status `🌙 Background`. Update `.squad/routing.md` to note that maintenance
  orchestration routes to this agent. Coordinate with the thread identity task to ensure the handle
  matches the registered identity key.

- [x] **[Maintenance] RELEASING.md / runbook — document Maintenance Mode** *(Owner: mira-quill)*
  Document the Maintenance Mode feature in any developer runbooks (`RELEASING.md` or equivalent).
  Cover: what `maintenance.md` is, how to enable/disable tasks, the safety model, where reports
  live, how to reset state (`maintenance-state.json`), and how to test the idle trigger locally.

- [x] **[Maintenance] End-to-end maintenance cycle test** *(Owner: vesper-knox)*
  Single integration test that exercises the full pipeline: force idle threshold → `MaintenanceRunner`
  picks first enabled eligible task → `executePromptAsync` stub called with correct prompt →
  `MaintenanceTaskResult` recorded → `MaintenanceReportWriter` writes report file → banner-triggered
  event fires. Use a fake `IdleDetectionService` and `executePromptAsync` stub. Assert: report file
  exists, banner event raised, state store updated with correct outcome.

- [x] **[maintenance.md] S8 — Move `configured: false` to bottom of frontmatter with annotation** — ✅ `configured: false` moved to last line of global config block (just before `tasks:`), with `# ← change to true to activate` comment. File-only change; no parser update.

- [x] **[maintenance.md] S7 — Shorten the 100-line header comment to a concise reference card** — ✅ Already resolved. S1's rewrite replaced the original ~100-line HTML comment block with 5 concise `#` comment lines at the top of the frontmatter. No further changes needed.

- [x] **[maintenance.md] S6 — Fix backtick fence in `code-smells` task** — ✅ Already resolved. S1's complete rewrite of `maintenance.md` to YAML list format naturally eliminated all stray backtick code fences. No-op; no file changes needed.

- [x] **[maintenance.md] S5 — Add `tooltip:` to option group blocks** — ✅ `MaintenanceOption.Hint` → `Tooltip`; parser accepts both `tooltip:` and `hint:` (backward compat); `MaintenancePanelController` sets `labelBlock.ToolTip` from `opt.Tooltip`; `tooltip:` added to all 7 option groups in `maintenance.md`; 4 new tests; 1872 total pass. Commit `145301d`.

- [x] **[maintenance.md] S4 — Rename `default:` → `value:` and implement write-back** — ✅ `maintenance.md`: `default:` → `value:` in all 7 option blocks; `MaintenanceMdParser.UpdateOptionValue(path, taskId, optionKey, newValue)` navigates task@2/options@4/optionKey@6/value@8 indent structure and writes back; `MaintenancePanelController`: radio pre-selection from `opt.RawValue` + `rb.Checked` write-back handler; 6 new tests; 1868 total pass. Commit `213393f`.

- [x] **[maintenance.md] S3b — Extend parser to support `instructions: |` block scalars** — ✅ Already implemented. `MaintenanceMdParser` lines 64-78 & 128-135 handle YAML block scalar accumulation at indent ≥ 6; `inMultiLineInstructions` flag; finalize-at-EOF at lines 203-205. Three passing tests: `AllLinesJoined`, `FollowedByOptions`, `RunsToClosingFrontmatter`. No code changes needed.

- [x] **[maintenance.md] S2 — Convert choices to YAML list with `value:` and `tooltip:`** — ✅ `MaintenanceOptionChoice` model added; `MaintenanceMdParser` extended to parse YAML list choices at indent 10/12 (backward-compat with bracket format); `MaintenancePanelController` sets `ToolTip` on each radio button; all 7 option blocks in `maintenance.md` updated with meaningful tooltips; 6 new tests; 1862 total pass. Commit `ad1c5f7`.

- [x] **[maintenance.md] S1 — Redesign to per-task YAML blocks** — ✅ Rewrote `.squad/maintenance.md` from `## heading` body sections (which the parser ignored) to a single `tasks:` YAML list inside the frontmatter (the format `MaintenanceMdParser` and `MaintenancePanelController` already expect). All 13 tasks preserved with correct indentation, `choices: [a, b]` bracket format, and `default:` keys. Parser needed no changes. 1856 tests pass.

- [x] **[Maintenance] End-to-end maintenance cycle test** — ✅ Implemented (`MaintenanceCycleIntegrationTests.cs`; single `[Test]` exercises full pipeline: `IdleDetectionService.ForceIdle()` fires `IdleThresholdReached` → `MaintenanceRunner.StartAsync` picks enabled eligible task → `executePromptAsync` stub called with correct prompt → `MaintenanceTaskResult.Completed` recorded → `MaintenanceReportWriter.WriteReport` writes `.md` file → `onCompleted` banner-event fires; asserts report file on disk, banner event raised with correct `RanTaskIds`, state store `GetLastRunAt` non-null for today, prompt contains task instructions; 1 new test passes)

- [x] **[Maintenance] Maintenance panel in-place task editing** — ✅ Implemented (`MaintenancePanelController.ToggleTaskEnabled` reads `.squad/maintenance.md`, flips `enabled: false ↔ true` for the target task ID, writes back preserving all other content, traces via `SquadDashTrace.Write`, then calls the reload callback; checkbox in `BuildTaskRow` wired to `ToggleTaskEnabled`; `OnMaintenanceTaskToggled` in `MainWindow` updated to re-parse and call `Refresh`; 4 new NUnit tests: enabled→disabled, disabled→enabled, preserves other content, invokes reload callback; all 1850 tests pass)

- [x] **[Maintenance] RELEASING.md / runbook — document Maintenance Mode** — ✅ Documented (`docs/features/maintenance-mode.md` created; covers `maintenance.md` structure and frontmatter, enabling/disabling tasks via file and in-panel checkbox, safety model with floor table, frequency values, report format and location, `maintenance-state.json` location/format/reset, `trigger_idle_cycle` local test workflow; `docs/SUMMARY.md` updated)

- [x] **[Maintenance] `per-commit` frequency — git fallback tracing**— ✅ Implemented (`MaintenanceStateStore.IsEligible` now calls `SquadDashTrace.Write(TraceCategory.General, ...)` when `commitSha` is null for a `per-commit` task before falling back to daily logic; 3 new NUnit tests: null-SHA falls back to daily (eligible/not-eligible), trace entry captured via `CapturingTraceTarget`; 14/14 tests pass)

- [x] **[Maintenance] Safety model enforcement — `branch` and `direct` modes**— ✅ Implemented (BuildPrompt now enforces global safety floor via ApplySafetyFloor; branch injects named `maintenance/YYYYMMDD-<slug>` branch; direct injects commit-directly message; report-only overrides more-permissive per-task safety; 5 new tests in MaintenanceRunnerTests.cs; all 17 tests pass)

- [x] **[Maintenance] Phase 2 tests— ReportWriter + PanelController** — ✅ Implemented (commit 0117326; 7 ReportWriter tests: file naming, content sections, duration format, prune-to-30, no-prune-under-30, newest-first sort, absent-dir safe; 8 PanelController WPF tests: idle/running header text, checkbox state from config, empty config, last-run info, null store, first-run no-throw, Run Now button lifecycle)

- [x] **[Maintenance] Phase 1 + Phase 2 + partial Phase 3 — full Maintenance Mode implementation** — ✅ Implemented (commits 8d4582b, 5030b6f, 8173256, 07ac04a, ff53624, f248b80, ed04918; IdleDetectionService, parser, state store, runner, report writer, default maintenance.md, prompt injection, WPF panel, banner, ntfy event, Argus Weld lazy registration, thread routing, Run Now button, agent roster)

- [x] Loop — multi-turn iterations (auto-pause on quick replies, resume on user input) — ✅ Implemented (commit 26ead85; `ExecuteLoopIterationAsync`; `_loopFollowUpTcs`; `CanAutoDispatchPromptQueue` guard; 10 new tests in `LoopMultiTurnTests.cs`; 1637 pass)

- [x] Loop — `LoopController` harden `onBeforeIteration` exceptions — ✅ Fixed (commit 7932ea8; try/catch around `await _onBeforeIteration()`; break on stop/cancel, continue otherwise; test 9 updated)

- [x] Loop — `loop-interactive-repair.md` frontmatter repair + `{{#if}}` conditional commit step — ✅ Fixed (commits 390ebfb, e0a09e8; redundant `stop_loop` JSON block removed; Step 5 uses "Continue to next task" quick reply)

- [x] Transcript — heading inline rendering (commit hash links, backtick spans in headings) — ✅ Fixed (commit 9c22228; headings now use `AppendInlineMarkdown` path; bold preserved via `Bold` span; 5 new tests in `MarkdownDocumentRendererHeadingTests.cs`)

- [x] Loop panel — enum options with ≤5 choices render as radio buttons — ✅ Implemented (commit 751179a; `CreateEnumOptionControl` branches on choice count; GroupName mutual exclusion; 12px indent; ≥6 choices keep ComboBox)

- [x] [Vesper audit] Test coverage — WorkspaceOpenCoordinator, PromptInteractionLogic multi-path workflows — ✅ Implemented (commit d05de11; 11 new NUnit tests; whitespace workspace folder filter; contended-lease Blocked path; GetSlashCommand \n split; /queue-sim+/test-queue immediate-local; single-item history round-trip)

- [x] [Vesper audit] Test coverage — BuiltInPromptInjections, PromptContextDiagnostics — ✅ Implemented (commit 6601175; 63 new NUnit tests; fake regex evaluator for injections; all risk bands + trace summary fields covered)

- [x] Command system — unified HostCommandRegistry/Parser/Executor — ✅ Verified complete (HostCommandRegistry builds catalog injected globally into every prompt; structured JSON multi-command parser; 6 built-in handlers; extensible via `.squad/commands.json`)

- [x] Shutdown race — "cannot change window visibility while shutting down" — ✅ Fixed (commit ace7dbd; `_mainWindowClosingInProgress` flag set at top of `MainWindow_Closing` before `ShowDialog`; guards added to `HandleRestartRequestChanged`, `OnDocRevisionCompleted`, `OnClipboardEditorClosed`, and `TryPostToUi`)

- [x] Loop output log pane — ✅ Implemented (collapsible log pane in Loop panel wired to loop_output_line events)

- [x] RC — LAN access (bind to PC IP, not localhost) — ✅ Implemented (0.0.0.0 binding via patch-package; LAN URL shown in transcript)

- [x] Phone push notifications — ✅ Implemented (NtfyNotificationProvider; cascading rate-limiter; Preferences UI; QR code; per-event toggles)

- [x] Verify task priority icon colors — ✅ Verified 2026-04-29

- [x] RC mobile — decide SDK PR ownership for binary audio frames — ✅ Decided 2026-04-30 (Talia Rune submits PR after Option C spike)

- [x] RC mobile — spike Option C audio format (WEBM_OPUS) — ✅ Spiked 2026-04-30 (WEBM_OPUS absent from SDK 1.49.0; proceed with Option B PCM/AudioWorklet)

- [x] RC mobile — define PTT-during-LLM-run policy — ✅ Decided 2026-04-30 (Option C: reject+feedback; C# broadcasts rc_status busy/idle; auto-unblocks on "done")

- [x] RC mobile — define session isolation policy for multi-phone connections — ✅ Decided 2026-04-30 (shared session; phones are input devices; no code change needed)

- [x] RC — phone voice input via PTT bridge — ✅ Implemented 2026-04-30 (Option B PCM/AudioWorklet; bridge.js patched for binary frames; rc-client PWA; RemoteSpeechSession; rc_status broadcast)

- [x] RC — ngrok/Cloudflare tunnel auto-start — ✅ Implemented 2026-04-30 (commit 69e8900; ngrok+cloudflared support; Preferences UI; 14 new tests; 1002 pass)

- [x] `squad streams` / `subsquads` management — ✅ Prototyped bridge (subsquads_list/activate requests; Workspace > SubSquads menu; 7 new tests; 1009 pass)

- [x] `squad cross-squad` integration — ✅ Architecture decided 2026-04-30 (Phase 1 = discovery-only read bridge; Phase 2 = gh delegation deferred; decision in decisions.md)

- [x] `squad personal` support — ✅ Implemented personal_list/personal_init bridge; Workspace → Personal Squad menu; 7 new tests; 1016 total pass

- [x] `squad aspire` integration — ✅ Phase 1 implemented (OTel auto-activation via initAgentModeTelemetry in runPrompt.ts); Phase 2 (in-app dashboard launch) deferred; architecture in decisions.md

- [x] Loop panel — Stop button + open/edit loop.md

- [x] `squad loop` TypeScript bridge + WPF panel

- [x] Watch capability event parsing + status panel

- [x] `squad rc` remote WebSocket bridge

- [x] Prompt injection of open tasks

- [x] RC mobile — QRCoder NuGet approved

- [x] F11 fullscreen transcript toggle

- [x] Test coverage — new SDK process methods

- [x] Squad update badge in title bar

- [x] Doc source background color

- [x] Squad CLI upgraded to 0.9.5-insider.1

- [x] Contributing docs removed

- [x] Abandoned tool runs / charter menu / version context menu fixes

- [x] **[Maintenance] Report history log in Maintenance panel** — ✅ Implemented (collapsible Recent Reports section below task list; scans .squad/maintenance-reports/, shows date/time and task count, opens file on click, 'No reports yet' placeholder; 5 new tests)

- [x] **[Maintenance] Auto-add `maintenance-state.json` to `.gitignore` on first run** — ✅ Implemented (EnsureMaintenanceStateInGitIgnore in SquadInstallerService; called from WriteSquadDashUniverseFiles and MaintenanceRunner.StartAsync; trace entry on write; 4 new tests)


## Archived 2026-06-10T15:59:07Z — prune-tasks maintenance pass

- [x] **[Docking] Fix: Left3/Right3 empty-zone preview strips at wrong screen position** *(Owner: Lyra Morn)* — commit e227bbd

- [x] **[Docking] Fix: Left3/Right3 over-eagerly shown in docking map when Left2/Right2 are empty** *(Owner: Lyra Morn)* — commit e227bbd

- [x] **[Docking] Feature: "insert at column position" model for left/right zones** *(Owner: Lyra Morn)* — commit 62b1aaa

- [x] **[Docking] Panel docking UI spec** *(Owner: mira-quill)* — commit fc205b2

- [x] **[Bug] Voice dictation focus: auto-route to prompt input when transcript has focus** *(Owner: Lyra Morn)*
  When voice dictation is activated (Ctrl double-tap) while the coordinator transcript panel has focus,
  the dictated text is added to the transcript instead of the active prompt input box. Expected behavior:
  immediately shift focus to the current prompt input box so dictation lands there. Affects usability
  when reviewing transcripts while dictating new prompts.

- [x] **Maintenance — custom task editor** *(Owner: Arjun Sen + Lyra Morn)*

- [x] **Maintenance — multi-file support** *(Owner: Arjun Sen + Lyra Morn)*
  Load all `maintenance*.md` files from the `.squad/` folder (e.g. `maintenance.md`,
  `maintenance-docs.md`, `maintenance-screenshots.md`). The base `maintenance.md` tasks are
  treated as "system" tasks. Additional files contribute supplemental tasks.
  Each task row in the panel must store its source file path so that toggle/frequency changes
  are written back to the correct file. Panel UI groups or labels tasks by source file.
  **Prerequisite for:** Inbox integration, per-repo custom maintenance tasks.

- [x] **[Docking] Wire PanelDockingService into MainWindow layout** *(Owner: orion-vale)* — commit d3acb2d

- [x] **[Docking] Ctrl+click popup menu for panel relocation** *(Owner: orion-vale)* — commit e597d29

- [x] **[Docking] Named layout persistence per workspace** *(Owner: orion-vale)* — commit 2cff1b2

- [x] **[Docking] Panel docking UI spec** *(Owner: mira-quill)* — commit fc205b2


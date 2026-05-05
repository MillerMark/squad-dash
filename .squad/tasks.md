# SquadDash Task List

> This file is the persistent backlog for SquadDash development.
> Update status inline (`- [ ]` → `- [x]`). AI agents read this file for context.
> Owner is listed per item where known.
> Completed items live in `.squad/completed-tasks.md`.

---

## 🟡 Mid Priority

- [x] **Doc editor — Phase 1: swap DocSourceTextBox to RichTextBox + plain-text adapter** *(Owner: Arjun Sen)*
- [x] **Doc editor — Phase 2: migrate all DocSourceTextBox API call sites to RichTextBox** *(Owner: Arjun Sen)*

- [x] **Doc editor — Phase 3: animated inline indicator for pending Revise with AI** *(Owner: Arjun Sen)*
  Create a `RevisionPendingIndicator` class: an `InlineUIContainer` wrapping an animated WPF element
  (pulsing border or spinner). On Revise with AI submit (`onSubmitting` callback): insert the indicator
  `Inline` at the selection's `TextPointer`. On `ApplyDocRevision` (success or fallback): remove it.
  Indicator must never appear in `.GetPlainText()` output (it is an object node, not a character).
  The user's normal selection should be unaffected — the indicator is a separate inline sibling.
  **Blocked by:** Phase 2.

- [ ] **RC browser UI — review and improvement pass** *(Owner: Lyra Morn)*

- [ ] **[Orion audit] `_isPromptRunning` — move ownership to PromptExecutionController** *(Owner: Arjun Sen)*
  `_isPromptRunning` is declared in MainWindow, mutated by PEC via setter delegate, read by
  `BackgroundTaskPresenter` via getter delegate, and read directly by MainWindow at 8 call sites.
  PEC is the natural owner (it sets the flag at prompt start/end). Consolidate ownership in PEC
  and expose it via a clean property rather than scattered delegates.

- [ ] **[Vesper audit] DocStatusStore — review silent catch blocks** *(Owner: Arjun Sen)*
  Vesper's audit flagged 34 bare catch blocks across the codebase; `DocStatusStore` in particular
  has silent failure suppression that may hide real errors. Review and replace with at minimum a
  `SquadDashTrace.Write` call so failures surface in the trace log.

- [ ] **Revise with AI — dynamic offset tracking for async revision** *(Owner: Arjun Sen)*
  When Revise with AI (Ctrl+Shift+A) is invoked, `selStart` is saved as an integer. If the user
  edits text *before* that offset while the AI is working, the saved integer is stale. Implement
  a text-change listener on `DocSourceTextBox` that adjusts the saved start offset based on
  `TextChangedEventArgs` delta (characters inserted/deleted and at what position). Represent the
  in-flight revision as a tracked `PendingRevision` record with a mutable `AdjustedStart` property.
  On each `TextChanged` event, for every pending revision: if the edit is before `AdjustedStart`,
  shift it by `(inserted - deleted)` chars. When the AI response lands, use `AdjustedStart` and
  the original length to check if the original text is still intact before applying the replacement.
  Multiple in-flight revisions should each track their own offset independently.

---

## 🔴 High Priority

- [ ] **[Orion audit] AgentThreadRegistry — lock down mutable backing collections** *(Owner: Arjun Sen)*
  `AgentThreadRegistry` exposes `ThreadsByKey`, `ThreadsByToolCallId`, `LaunchesByToolCallId`,
  and `ThreadOrder` as public mutable dictionaries. Callers can write directly to them, silently
  bypassing the aliasing invariants enforced by `GetOrCreateAgentThread`/`AliasThreadKeys`.
  Make the exposed collections read-only wrappers (e.g. `IReadOnlyDictionary`) so the correctness
  contract cannot be violated externally.

- [ ] **[Vesper audit] Test coverage — CommitApprovalStore, DocStatusStore, DocTopicsLoader, LoopOutputStore** *(Owner: Vesper Knox)*
  All four classes have zero test coverage despite critical responsibilities:
  `CommitApprovalStore` (JSON persistence, 200-item cap), `DocStatusStore` (approval tracking,
  case-insensitive key lookup), `DocTopicsLoader` (SUMMARY.md parsing, folder scanning),
  `LoopOutputStore` (sequential log numbering). Write unit tests for each.

- [ ] **WinGet — smoke-test installer on clean VM** *(Owner: you — manual step)*
  Run `.\installer\build-installer.ps1 -Version 1.0.0` (requires Inno Setup 6 installed locally),
  then install on a clean Windows VM with only Node.js pre-installed. Verify: launcher starts,
  SDK bridge connects, workspaces resolve correctly from `%LocalAppData%\SquadDash\app\`.
  **Blocks:** GitHub Release, WinGet submission.

---

## 🟡 Mid Priority

- [x] Screenshots health panel — XAML + bindings + status UX *(Owner: lyra-morn)*

- [ ] **WinGet — create GitHub Release v1.0.0** *(Owner: you — manual step)*
  After smoke-test passes: create GitHub Release `v1.0.0`, attach the installer `.exe` and its
  SHA256 hash. The public download URL is required for `wingetcreate`.
  **Blocked by:** smoke-test passing.

- [ ] **WinGet — generate and submit manifest** *(Owner: Jae Min — automated once release exists)*
  Run `wingetcreate new <installer-url>`, add `OpenJS.NodeJS` as a `PackageDependencies` entry
  in the installer manifest YAML, open PR to `microsoft/winget-pkgs`.
  **Blocked by:** GitHub Release v1.0.0 existing with a stable download URL.

- [ ] **WinGet — document Node.js prerequisite** *(Owner: Jae Min)*
  `runPrompt.js` calls `node` from PATH — Node.js is required but not bundled.
  Update `README.md` to document this prerequisite clearly. The WinGet manifest will list
  `OpenJS.NodeJS` as a dependency but a README callout helps users who install manually.

- [ ] **WinGet — Phase 2: release automation** *(Owner: Jae Min)*
  Create `.github/workflows/release.yml`: on `v*` tag push, run `dotnet publish`, bundle
  installer, upload to GitHub Release, run `wingetcreate update`, open PR to winget-pkgs
  automatically. Requires `WINGET_PKGS_PAT` repo secret.
  **Blocked by:** Phase 1 (manual release) succeeding at least once.

- [ ] **WinGet — write RELEASING.md runbook** *(Owner: Jae Min)*
  Document the full release checklist: bump version, tag, let automation run, verify winget PR.
  Include manual fallback steps. Useful for the first few releases before automation is trusted.

---

## 🔵 Low Priority

- [ ] **SubSquads — investigate and expose in UI** *(Owner: Orion Vale → Lyra Morn)*

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

- [x] Command system — unified HostCommandRegistry/Parser/Executor — ✅ Verified complete (HostCommandRegistry builds catalog injected globally into every prompt; structured JSON multi-command parser; 6 built-in handlers; extensible via `.squad/commands.json`)

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



# SquadDash Task List

> This file is the persistent backlog for SquadDash development.
> Update status inline (`- [ ]` → `- [x]`). AI agents read this file for context.
> Owner is listed per item where known.

---

## 🔴 High Priority

- [ ] **`squad loop` support — TypeScript bridge** *(Owner: Talia Rune)*
  Add a new `RunLoop` request type to `runPrompt.ts` / `squadService.ts` that invokes `squad loop`
  with the workspace `loop.md` path. Emit start/stop/iteration events over the NDJSON protocol.
  - _Blocked by: C# event types in `SquadSdkEvent.cs` (coordinate with Arjun Sen)_

- [ ] **`squad loop` support — WPF loop control panel** *(Owner: Lyra Morn)*
  New panel or toolbar area showing loop status (running/stopped), current iteration, last run time.
  Start/stop buttons. Wired to the loop events from the SDK bridge.
  - _Blocked by: TypeScript bridge above_

- [ ] **Watch capability event parsing** *(Owner: Talia Rune)*
  Parse new `watch` lifecycle JSON events introduced in Squad 0.9.4:
  fleet/wave dispatch, two-pass hydration, retro, monitor-email/teams.
  Add new event types to `SquadSdkEvent.cs` (coordinate with Arjun Sen).

---

## 🟡 Mid Priority

- [ ] **Watch capabilities status panel** *(Owner: Lyra Morn)*
  New "Watch Capabilities" UI area showing active capability phases (pre-scan, post-triage,
  post-execute, housekeeping). Wired to the new watch events above.
  - _Blocked by: Watch capability event parsing above_

- [ ] **`squad rc` remote WebSocket bridge** *(Owner: Talia Rune)*
  Analyze `squad rc` command and determine whether SquadDash should surface a remote session
  indicator or connection UI. Low-risk to defer until the command stabilises.

- [ ] **Prompt injection of open tasks** *(Owner: Arjun Sen / Coordinator)*
  When `tasks.md` exists in the workspace `.squad/` folder, read unchecked items and inject a
  brief summary into each prompt so the AI is aware of outstanding work.

---

## 🟢 Low Priority

- [ ] **`squad streams` sub-squad management** *(Owner: Talia Rune)*
  Investigate `squad streams` API for sub-squad creation and management. Prototype wrapping
  if the API stabilises in a subsequent Squad release.

- [ ] **`squad cross-squad` integration** *(Owner: Orion Vale — architecture first)*
  Architectural review needed before any implementation. How should SquadDash surface
  cross-workspace agent interactions?

- [ ] **`squad personal` support** *(Owner: Talia Rune)*
  Determine what `squad personal` exposes and whether SquadDash can surface personal agent
  profiles or preferences.

- [ ] **`squad aspire` integration** *(Owner: Orion Vale — architecture first)*
  Explore .NET Aspire integration hooks. Likely a separate milestone.

---

## ✅ Done

- [x] Squad CLI upgraded 0.9.1 → 0.9.4
- [x] SDK 0.9.4 analyzed — new commands documented
- [x] Talia Rune charter updated to own Squad CLI upgrades
- [x] `loop.md` entry in workspace menu (appears when file exists at workspace root)
- [x] Squad update badge in title bar (↑ indicator when npm has newer version)
- [x] Left-click + right-click both open version context menu
- [x] Abandoned tool runs no longer show "Status: Running" after reload
- [x] Open Charter menu hidden for agents with no charter file (e.g. Squad coordinator)

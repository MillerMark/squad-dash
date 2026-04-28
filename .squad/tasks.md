# SquadDash Task List

> This file is the persistent backlog for SquadDash development.
> Update status inline (`- [ ]` → `- [x]`). AI agents read this file for context.
> Owner is listed per item where known.

---

## 🔴 High Priority

- [x] **`squad loop` support — TypeScript bridge** *(Owner: Talia Rune)*
  Add a new `RunLoop` request type to `runPrompt.ts` / `squadService.ts` that invokes `squad loop`
  with the workspace `loop.md` path. Emit start/stop/iteration events over the NDJSON protocol.

- [x] **`squad loop` support — WPF loop control panel** *(Owner: Lyra Morn)*
  New panel or toolbar area showing loop status (running/stopped), current iteration, last run time.
  Start/stop buttons. Wired to the loop events from the SDK bridge.

- [x] **Watch capability event parsing** *(Owner: Talia Rune)*
  Parse new `watch` lifecycle JSON events introduced in Squad 0.9.4:
  fleet/wave dispatch, two-pass hydration, retro, monitor-email/teams.
  Add new event types to `SquadSdkEvent.cs` (coordinate with Arjun Sen).

---

## 🟡 Mid Priority

- [x] **Watch capabilities status panel** *(Owner: Lyra Morn)*
  New "Watch Capabilities" UI area showing active capability phases (pre-scan, post-triage,
  post-execute, housekeeping). Wired to the new watch events above.

- [x] **`squad rc` remote WebSocket bridge** *(Owner: Talia Rune)*
  ~~Analyze `squad rc` command and determine whether SquadDash should surface a remote session
  indicator or connection UI. Low-risk to defer until the command stabilises.~~
  **Implemented** via SDK's `RemoteBridge` class (not `squad rc`/`squad start` CLI — both deprecated
  and spawn conflicting Copilot processes). Added `rc_start`/`rc_stop` NDJSON request types to
  `runPrompt.ts`, wired `RemoteBridge` callbacks to the existing `SquadBridgeService` session (remote
  prompts flow through the same Copilot session, deltas/tool events forwarded to WebSocket clients).
  Added `StartRemoteAsync`/`StopRemoteAsync` to `SquadSdkProcess.cs`. Added "Start/Stop Remote Access"
  menu item in the Workspace menu — toggles on `rc_started`/`rc_stopped` events, shows local URL in
  transcript.

- [x] **Prompt injection of open tasks** *(Owner: Arjun Sen / Coordinator)*
  When `tasks.md` exists in the workspace `.squad/` folder, read unchecked items and inject a
  brief summary into each prompt so the AI is aware of outstanding work.

---

## 🟡 Mid Priority

- [x] **Loop panel — Stop button + open/edit loop.md** *(Owner: Lyra Morn + Arjun Sen)*
  The Loop panel currently only has a Start button. Add a Stop button wired to a `run_loop_stop`
  NDJSON event (Talia adds to bridge; Arjun adds C# method). Add ✏️ button that opens `loop.md`
  in the doc editor if it exists, or prompts to create it from a template if it doesn't.
  Template should include headings and example standing instructions.

- [ ] **Loop output log pane** *(Owner: Lyra Morn)*
  The bridge already emits `loop_output_line` events. Add a collapsible scrollable log pane
  to the Loop panel (or a floating window) that displays the live output from the running loop.

- [ ] **RC — LAN access (bind to PC IP, not localhost)** *(Owner: Talia Rune)*
  Currently the RC server URL is `http://localhost:<port>` — only reachable from the host PC.
  Add a `host` binding option so the server listens on `0.0.0.0` (or the PC's LAN IP).
  SquadDash shows the LAN URL in the transcript alongside the localhost URL when RC starts.
  Prerequisite for phone access on the same WiFi network.

---

## 🟢 Low Priority

- [ ] **RC — mobile web client** *(Owner: Orion Vale — architecture first, then Talia)*
  Design and implement a minimal web client served from the RC HTTP port. The phone browser
  navigates to `http://<pc-ip>:<port>` and gets a text input + response stream. Architecture
  review needed first: authentication flow, streaming protocol, history display.

- [ ] **RC — phone voice input via PTT bridge** *(Owner: Orion Vale — architecture first, then Talia + Arjun)*
  Allow a phone browser to stream microphone audio over WebSocket to SquadDash, which pipes
  it into the existing Azure Cognitive Services `PushAudioInputStream`. The transcribed text
  follows the same PTT completion path as desktop mic input. Key challenges: audio format
  (browser exports WebM/Opus; Azure expects PCM — needs transcoding via NAudio or FFmpeg),
  latency, and auth. Architecture review needed before implementation.

- [ ] **RC — ngrok/Cloudflare tunnel auto-start** *(Owner: Talia Rune + Orion Vale)*
  When RC starts, optionally auto-launch an ngrok or Cloudflare tunnel and surface the public
  URL in the transcript. Enables phone access from outside the home network without router
  port-forwarding. Requires tunnel binary detection and token configuration.

- [ ] **`squad streams` / `subsquads` management** *(Owner: Talia Rune)*
  `squad streams` is deprecated in 0.9.5-insider; the replacement is `squad subsquads`
  (aliases: `workstreams`, `streams`). Investigate the updated API for sub-squad creation
  and management. Prototype wrapping once the API stabilises.

- [ ] **`squad cross-squad` integration** *(Owner: Orion Vale — architecture first)*
  Architectural review needed before any implementation. How should SquadDash surface
  cross-workspace agent interactions? Note: `squad cross-squad` does not appear in the
  0.9.5-insider CLI — may be a planned future feature.

- [ ] **`squad personal` support** *(Owner: Talia Rune)*
  `squad personal` exists in 0.9.5-insider: `init | list | add | remove` subcommands plus
  `consult` mode for ambient personal agents. Determine what SquadDash should surface.

- [ ] **`squad aspire` integration** *(Owner: Orion Vale — architecture first)*
  `squad aspire` exists in 0.9.5-insider — launches .NET Aspire dashboard for observability.
  Explore integration hooks. Likely a separate milestone.

---

## 🟢 Low Priority — Deferred / Debt

- [x] **F11 shortcut — fullscreen transcript toggle** *(Owner: Lyra Morn)*
  Add `Key.F11` handler in `Window_PreviewKeyDown` to call `SetTranscriptFullScreen(!_transcriptFullScreenEnabled)`.
  Add `InputGestureText="F11"` to `FullScreenTranscriptMenuItem` in XAML.

- [x] **Test coverage — new SDK process methods** *(Owner: Vesper Knox)*
  `RunLoopAsync()`, `StartRemoteAsync()`, `StopRemoteAsync()` have no tests.
  Add argument validation tests to `SquadSdkProcessTests.cs` and serialization round-trip
  tests for `SquadSdkRunLoopRequest`, `SquadSdkRcStartRequest`, `SquadSdkRcStopRequest`
  to `SquadSdkProcessSerializationTests.cs`. Also add deserialization tests for the new
  watch/loop/rc fields added to `SquadSdkEvent.cs`.

---

## ✅ Done

- [x] Squad CLI upgraded 0.9.4 → 0.9.5-insider.1
- [x] `squad loop` TypeScript bridge — `RunLoop` NDJSON request, subprocess spawn, loop events
- [x] `squad loop` WPF panel — status bar Loop panel with iteration counter + Start button
- [x] Watch capability event parsing — 5 watch events parsed in TS stubs + C# + WPF panel
- [x] Watch capabilities status panel — WPF panel shows fleet/wave/phase, collapses after retro
- [x] `squad rc` remote bridge — `RemoteBridge` SDK integration (not deprecated CLI commands)
- [x] Prompt injection of open tasks — `TasksContextBuilder.cs` + tests + prompt context wiring
- [x] Doc source background color — `DocSourceTextBox` + `DocSourcePanel` now use `InputSurface` (matches charter/history/tasks windows)
- [x] Contributing docs removed — `docs/contributing/` folder deleted, all links removed
- [x] Squad CLI upgraded 0.9.1 → 0.9.4
- [x] SDK 0.9.4 analyzed — new commands documented
- [x] Talia Rune charter updated to own Squad CLI upgrades
- [x] `loop.md` entry in workspace menu (appears when file exists at workspace root)
- [x] Squad update badge in title bar (↑ indicator when npm has newer version)
- [x] Left-click + right-click both open version context menu
- [x] Abandoned tool runs no longer show "Status: Running" after reload
- [x] Open Charter menu hidden for agents with no charter file (e.g. Squad coordinator)

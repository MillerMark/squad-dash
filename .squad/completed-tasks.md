# SquadDash Completed Tasks

> Archive of finished work. Active backlog is in `.squad/tasks.md`.
> Items are grouped by feature area, most recent first within each group.

---

## Squad CLI & SDK

- [x] **Squad CLI upgraded 0.9.4 → 0.9.5-insider.1** *(Owner: Talia Rune)*
- [x] **Squad CLI upgraded 0.9.1 → 0.9.4** *(Owner: Talia Rune)*
- [x] **SDK 0.9.4 analyzed — new commands documented** *(Owner: Talia Rune)*
- [x] **Talia Rune charter updated to own Squad CLI upgrades**

---

## Loop

- [x] **`squad loop` support — TypeScript bridge** *(Owner: Talia Rune)*
  Add a new `RunLoop` request type to `runPrompt.ts` / `squadService.ts` that invokes `squad loop`
  with the workspace `loop.md` path. Emit start/stop/iteration events over the NDJSON protocol.

- [x] **`squad loop` support — WPF loop control panel** *(Owner: Lyra Morn)*
  New panel or toolbar area showing loop status (running/stopped), current iteration, last run time.
  Start/stop buttons. Wired to the loop events from the SDK bridge.

- [x] **Loop panel — Stop button + open/edit loop.md** *(Owner: Lyra Morn + Arjun Sen)*
  The Loop panel currently only has a Start button. Add a Stop button wired to a `run_loop_stop`
  NDJSON event (Talia adds to bridge; Arjun adds C# method). Add ✏️ button that opens `loop.md`
  in the doc editor if it exists, or prompts to create it from a template if it doesn't.
  Template should include headings and example standing instructions.

- [x] **`loop.md` entry in workspace menu** — appears when file exists at workspace root

---

## Watch

- [x] **Watch capability event parsing** *(Owner: Talia Rune)*
  Parse new `watch` lifecycle JSON events introduced in Squad 0.9.4:
  fleet/wave dispatch, two-pass hydration, retro, monitor-email/teams.
  Add new event types to `SquadSdkEvent.cs` (coordinate with Arjun Sen).

- [x] **Watch capabilities status panel** *(Owner: Lyra Morn)*
  New "Watch Capabilities" UI area showing active capability phases (pre-scan, post-triage,
  post-execute, housekeeping). Wired to the new watch events above.

---

## Remote Control (RC)

- [x] **RC — LAN access (bind to PC IP, not localhost)** *(Owner: Talia Rune)*
  Patched `@bradygaster/squad-sdk` `RemoteBridge` via `patch-package`:
  server now binds to `0.0.0.0` instead of `127.0.0.1`, and origin validation accepts
  LAN private-range IPs (10.x, 172.16–31.x, 192.168.x) so phones on the same WiFi can connect.
  Patch committed in `patches/` with a `postinstall` script in `package.json`.

- [x] **`squad rc` remote WebSocket bridge** *(Owner: Talia Rune)*
  Implemented via SDK's `RemoteBridge` class (not `squad rc`/`squad start` CLI — both deprecated
  and spawn conflicting Copilot processes). Added `rc_start`/`rc_stop` NDJSON request types to
  `runPrompt.ts`, wired `RemoteBridge` callbacks to the existing `SquadBridgeService` session (remote
  prompts flow through the same Copilot session, deltas/tool events forwarded to WebSocket clients).
  Added `StartRemoteAsync`/`StopRemoteAsync` to `SquadSdkProcess.cs`. Added "Start/Stop Remote Access"
  menu item in the Workspace menu — toggles on `rc_started`/`rc_stopped` events, shows local URL in
  transcript.

- [x] **RC mobile — approve QRCoder NuGet package** *(Owner: Arjun Sen)*
  RC mobile web client needs a QR code displayed in SquadDash for the phone to scan (URL + token).
  Two candidates: `QRCoder` (MIT, ~150 KB, no native deps) and `ZXing.Net` (Apache 2.0, larger).
  `QRCoder` is preferred for minimal footprint. **✅ Approved 2026-04-28.**
  See `.squad/rc-mobile-architecture.md` §Key Decisions #3.

## Phone Push Notifications

- [x] **Phone push notifications — architecture & implementation** *(Owner: Arjun Sen + Talia Rune + Lyra Morn)*
  Full implementation: `PushNotificationService.cs` with `IPushNotificationProvider` / `NtfyNotificationProvider`,
  cascading rate-limiter, `SQUADASH_NTFY_TOPIC` env-var override. Hooks in `MainWindow.xaml.cs` for
  `assistant_turn_complete` (via `"done"`), `loop_stopped`, `rc_connection_dropped`, `quick_reply_needed`.
  Notifications section in `PreferencesWindow` with QR code via QRCoder, per-event toggles, Test button.
  Decision documented in `decisions.md` ADR-001. Tests in `PushNotificationServiceTests.cs`.

---

## Agent & Prompt Infrastructure

- [x] **Prompt injection of open tasks** *(Owner: Arjun Sen / Coordinator)*
  When `tasks.md` exists in the workspace `.squad/` folder, read unchecked items and inject a
  brief summary into each prompt so the AI is aware of outstanding work.

---

## UI & Polish

- [x] **Verify task priority icon colors match tasks.md emoji** *(Owner: Lyra Morn)*
  Visual check that the high/mid/low priority circle icons in the Tasks panel match the 🔴🟡🟢
  emoji colors used in tasks.md (both light and dark theme).
  **✅ Verified 2026-04-29** — colors match in both themes after contrast-balancing pass
  (Light: #DE3333 / #A06800 / #1976D2).

- [x] **F11 shortcut — fullscreen transcript toggle** *(Owner: Lyra Morn)*
  Add `Key.F11` handler in `Window_PreviewKeyDown` to call `SetTranscriptFullScreen(!_transcriptFullScreenEnabled)`.
  Add `InputGestureText="F11"` to `FullScreenTranscriptMenuItem` in XAML.

- [x] **Squad update badge in title bar** — ↑ indicator when npm has newer version
- [x] **Left-click + right-click both open version context menu**
- [x] **Doc source background color** — `DocSourceTextBox` + `DocSourcePanel` now use `InputSurface` (matches charter/history/tasks windows)
- [x] **Abandoned tool runs no longer show "Status: Running" after reload**
- [x] **Open Charter menu hidden for agents with no charter file** (e.g. Squad coordinator)

---

## Tests & Coverage

- [x] **Test coverage — new SDK process methods** *(Owner: Vesper Knox)*
  `RunLoopAsync()`, `StartRemoteAsync()`, `StopRemoteAsync()` have no tests.
  Add argument validation tests to `SquadSdkProcessTests.cs` and serialization round-trip
  tests for `SquadSdkRunLoopRequest`, `SquadSdkRcStartRequest`, `SquadSdkRcStopRequest`
  to `SquadSdkProcessSerializationTests.cs`. Also add deserialization tests for the new
  watch/loop/rc fields added to `SquadSdkEvent.cs`.

---

## Docs

- [x] **Contributing docs removed** — `docs/contributing/` folder deleted, all links removed

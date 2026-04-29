# SquadDash Task List

> This file is the persistent backlog for SquadDash development.
> Update status inline (`- [ ]` → `- [x]`). AI agents read this file for context.
> Owner is listed per item where known.
> Completed items live in `.squad/completed-tasks.md`.

---

## 🟡 Mid Priority

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

- [ ] **Phone push notifications — architecture & implementation** *(Owner: Arjun Sen + Talia Rune + Lyra Morn — architecture complete, building)*
  Send push notifications to the user's phone when key SquadDash events occur.

  **Architecture decisions (all confirmed):**
  - **Delivery:** ntfy.sh Phase 1; pluggable `IPushNotificationProvider` for Pushover in Phase 2
  - **Events on by default:** AI turn complete, loop stopped, RC connection dropped
  - **Events off by default:** git commit (agent-only, not manual), loop iteration, RC established
  - **Git commits:** notify for agent-authored commits only (not user's manual commits)
  - **Rate limiting (cascading backpressure):**
    - Normal: max 1 notification per event per 10 seconds
    - If rate exceeds 3/min: consolidate into "N events in the last minute" digest, send once/min
    - If still exceeding: escalate to once/10 min, then once/hour, then once/day
    - Escalation resets when traffic drops back below threshold
  - **Message content:** AI composes its own notification summary — SquadDash injects into each
    prompt: "When done, include `{\"notification\": \"one-sentence summary\"}` in your response."
    C# extracts this JSON block and sends it. Fallback: "[AgentName] turn complete."
  - **Config:** Global/machine-wide in `ApplicationSettingsStore` (not per-workspace)
  - **Settings UI:** New "Notifications" section in PreferencesWindow with QR code display
  - **QRCoder NuGet:** ✅ Approved (MIT, ~150 KB, no native deps)
  - **Env var override:** `SQUADASH_NTFY_TOPIC` for test/CI redirect — Arjun implements

  **Build ownership:**
  - Arjun: `PushNotificationService.cs`, `IPushNotificationProvider`, `NtfyNotificationProvider`,
    settings store methods, rate limiter, commit event hook (agent-authored filter)
  - Talia: confirm `"done"` event is used for turn-complete hook (document in decisions.md);
    confirm `"loop_stopped"` / `"rc_stopped"` hooks
  - Lyra: Notifications section in PreferencesWindow, QRCoder QR display, Test button

- [ ] **RC mobile — decide SDK PR ownership for binary audio frames** *(Owner: Orion Vale)*
  `onAudioChunk` / `onAudioStart` / `onAudioEnd` additions to `RemoteBridgeConfig` must land in
  `@bradygaster/squad-sdk` before the PTT audio path can work end-to-end. Identify who submits
  the PR and confirm expected merge timeline. This is the critical-path scheduling risk for
  RC phone voice input. See `.squad/rc-mobile-architecture.md` §Key Decisions #1.

- [ ] **RC mobile — spike Option C audio format (WEBM_OPUS) before building Option B** *(Owner: Talia Rune)*
  Before building the full WebAudio AudioWorklet PCM pipeline (Option B), spend ½ day verifying
  whether `AudioStreamContainerFormat.WEBM_OPUS` is available in Azure Cognitive Services SDK
  1.49.0 and works with browser-sourced audio. If it does, use Option C (simpler, lower bandwidth).
  Fall back to Option B only if Option C fails. See `.squad/rc-mobile-architecture.md` §Key Decisions #2.

- [ ] **RC mobile — define PTT-during-LLM-run policy** *(Owner: Orion Vale)*
  What happens when the user initiates PTT on the phone while an LLM response is already streaming?
  Options: (a) queue prompt, (b) abort current run, (c) reject with error.
  Recommendation: show "⏳ Wait for response to complete" on phone; auto-unblock when `complete` fires.
  Decision needed before wiring PTT UX. See `.squad/rc-mobile-architecture.md` §Key Decisions #4.

- [ ] **RC mobile — define session isolation policy for multi-phone connections** *(Owner: Orion Vale)*
  `RemoteBridge` allows multiple simultaneous phone connections. If two phones connect and submit
  prompts, do they share one SquadBridge session (shared history) or get isolated sessions?
  Decision affects how `onPrompt` is wired in `handleRcStart`. See `.squad/rc-mobile-architecture.md` §Key Decisions #5.

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

- [ ] **Capture screenshots for documentation** *(Owner: User)*
  Documentation pages in `docs/` contain `📸 Screenshot needed` placeholders.
  Right-click any placeholder in the SquadDash doc viewer to paste a screenshot from clipboard.
  Screenshots needed for: Loop panel, Tasks panel, Voice input, Prompt queue, Fullscreen mode.

---

## ✅ Recently Completed

> Full details in `.squad/completed-tasks.md`. This section is a compact AI-recall index only.

- [x] Verify task priority icon colors — ✅ Verified 2026-04-29
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


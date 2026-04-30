# RC Mobile Architecture Review
**Author:** Orion Vale, Principal Architect  
**Date:** 2025-01-27  
**Status:** Design / Planning — no source changes  
**Scope:** Part A — minimal mobile web client; Part B — phone voice PTT bridge

---

## 0. Findings from Codebase Inspection

Before designing anything, I read the code. Key discoveries that shape every decision below:

| Finding | Location | Impact |
|---|---|---|
| `RemoteBridge.setStaticHandler()` exists | `dist/remote/bridge.d.ts:32` | We do NOT need a separate HTTP server. Same port, same process. |
| History is pushed on connect (`type: 'history'`) | `dist/remote/protocol.d.ts:19` | Phone page gets full history automatically; no REST call needed. |
| Protocol version is `"1.0"` (string constant) | `dist/remote/protocol.d.ts:7` | Client can guard on version mismatch at connect time. |
| `SpeechRecognitionService` uses `PushAudioInputStream` + NAudio `WaveInEvent` at 16 kHz / 16-bit / mono | `SpeechRecognitionService.cs:23,49` | **NAudio is already a project dependency (v2.3.0).** The push-stream model is already proven; we just need to swap the audio source. |
| `rc_started` event currently emits only `url: "http://localhost:<port>"` | `runPrompt.ts:752` | Talia's LAN-URL work is load-bearing for Part A. |
| `RemoteBridgeConfig.onPrompt` wires to the existing `SquadBridgeService` session | `runPrompt.ts:724` | Phone prompts share the exact same session as the desktop. No session isolation needed. |
| Azure Speech SDK version `1.49.0` in project | `SquadDash.csproj:83` | Can check `AudioStreamContainerFormat` enum availability for Option C. |

---

## Part A: Mobile Web Client

### A.1 Static File Serving

`RemoteBridge` exposes:

```ts
setStaticHandler(
  handler: (req: http.IncomingMessage, res: http.ServerResponse) => void
): void;
```

This hook is called for every HTTP GET that is **not** a WebSocket upgrade. It is the correct and intended extension point for serving the PWA — no separate port, no proxy, no Express.

**Implementation approach (in `runPrompt.ts` / `handleRcStart`):**

```ts
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dir = path.dirname(fileURLToPath(import.meta.url));
const clientHtml = fs.readFileSync(
  path.join(__dir, "rc-client", "index.html"), "utf-8"
);

rcBridge.setStaticHandler((req, res) => {
  // Only GET / (or anything that isn't the WebSocket path)
  res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
  res.end(clientHtml);
});
```

The `rc-client/` directory lives alongside `runPrompt.ts` inside `Squad.SDK/`. The entire mobile UI is a **single self-contained `index.html`** — no build step, no bundler. Inline CSS + inline `<script>` is acceptable because the client is intentionally minimal and change-frequency is low.

### A.2 Authentication

**Constraint:** Browser WebSocket upgrades cannot set custom HTTP headers (`Authorization: Bearer …`). The only in-band option before the connection is established is the query string.

**Recommended flow:**

1. SquadDash (C#) receives `rc_started` with `{ port, token, url, lanUrl }` (after Talia's LAN work).
2. SquadDash displays a **QR code** in a small overlay/tooltip encoding:
   ```
   http://<lan-ip>:<port>?token=<token>
   ```
3. User scans QR code on phone; browser navigates to that URL.
4. The static handler in step A.1 serves `index.html`.
5. The JS on the page reads `new URLSearchParams(location.search).get("token")` and includes it on the WebSocket URL:
   ```js
   const ws = new WebSocket(`ws://${location.host}?token=${token}`);
   ```
6. RemoteBridge already has a `tickets` private field and a `sessionToken` — the existing authentication mechanism uses the query-string token. The client does not need to do anything special beyond passing it in the WS URL.

**QR code generation (C# side):** Add `QRCoder` NuGet package (MIT, no external binary deps). Render to `BitmapSource` and show in a WPF `Popup` attached to the RC status bar label. Alternatively use the `ZXing.Net` package which SquadDash may already use for other purposes — check before adding `QRCoder`.

**Why not a manual URL entry?** The token is a UUID-class string. Typing it on a phone is error-prone and defeats the point of RC as a low-friction feature. The QR code is the primary UX.

### A.3 WebSocket Protocol (Server → Phone)

The protocol is fully defined in `dist/remote/protocol.d.ts`. Summary for implementers:

**On connect, the server immediately pushes (in order):**

| Event | Type field | Purpose |
|---|---|---|
| Session metadata | `status` | `version`, `repo`, `branch`, `machine` |
| Full history | `history` | `messages: RCMessage[]` — up to 50 entries |
| Agent roster | `agents` | `agents: RCAgent[]` with live status |

**During a run:**

| Event | Type field | Purpose |
|---|---|---|
| Streaming delta | `delta` | `{ sessionId, agentName, content }` — append to current bubble |
| Run complete | `complete` | `{ message: RCMessage }` — replace the partial bubble |
| Tool activity | `tool_call` | For optional activity indicator |
| Permission gate | `permission` | `{ id, agentName, tool, description }` — requires Approve/Deny UI |
| Error | `error` | Display inline |
| Token usage | `usage` | Optional footer display |
| Keepalive | `pong` | Response to client `ping` |

**Client → Server (text frames, JSON):**

```ts
// Send a prompt
{ "type": "prompt", "text": "What is the status of issue #42?" }

// Target a specific agent
{ "type": "direct", "agentName": "Talia", "text": "…" }

// Slash command
{ "type": "command", "name": "stop" }

// Permission response
{ "type": "permission_response", "id": "<id>", "approved": true }

// Keepalive
{ "type": "ping" }
```

The client MUST check `version` in the `status` event against `RC_PROTOCOL_VERSION = "1.0"`. If the version is unexpected, show a warning banner (do not disconnect — the protocol may be backward-compatible).

### A.4 Conversation History

**No separate REST call is needed.** On WebSocket connect the server pushes `type: 'history'` with the full message array. The phone page renders these as a chat log before accepting user input.

Rendering notes:
- Messages have `role: 'user' | 'agent' | 'system'`. System messages can be rendered as small grey separators.
- `agentName` is set for agent messages — show it as a sub-label.
- History messages are complete; streaming deltas arrive after and should be accumulated in a temporary buffer until `complete` fires.

### A.5 Minimum Viable Mobile UI

The HTML page needs exactly:

```
┌─────────────────────────────────────────┐
│  ● Connected — repo/branch  [PTT btn]   │
├─────────────────────────────────────────┤
│                                         │
│  [history / streaming bubbles]          │
│       (scrollable, newest at bottom)    │
│                                         │
├─────────────────────────────────────────┤
│  [text input          ] [Send]  [🎤]   │
└─────────────────────────────────────────┘
```

Required elements:
- **Connection status bar** — green dot when WS is open, red when closed with reconnect button
- **Agent status pills** — small badges updated from `agents` events: `idle / working / streaming / error`
- **Chat scroll container** — flex column, user bubbles right-aligned, agent bubbles left-aligned
- **Streaming bubble** — accumulate `delta.content` in place, swap with `complete.message.content` on `complete`
- **Permission gate dialog** — modal overlay triggered by `permission` event; Approve/Deny → sends `permission_response`
- **Text input + Send button** — sends `{ type: "prompt", text }` on Enter or button tap
- **PTT button (🎤)** — see Part B

The page should be `<meta name="viewport" content="width=device-width, initial-scale=1">` and use a dark theme matching SquadDash's palette. No external CSS frameworks — a small `<style>` block inline is sufficient.

**Auto-reconnect:** On `close` event, back-off and retry (1s, 2s, 4s … cap at 30s). Re-read the token from `URLSearchParams` on reconnect.

### A.6 Code Sketch — Streaming Renderer

```js
// In the mobile page <script>
let streamBuffer = "";
let streamEl = null;

ws.addEventListener("message", (e) => {
  const msg = JSON.parse(e.data);

  if (msg.type === "history") {
    chatEl.innerHTML = "";
    for (const m of msg.messages) appendBubble(m.role, m.content, m.agentName);
    return;
  }

  if (msg.type === "delta") {
    if (!streamEl) {
      streamEl = appendBubble("agent", "", msg.agentName);
    }
    streamBuffer += msg.content;
    streamEl.textContent = streamBuffer;
    chatEl.scrollTop = chatEl.scrollHeight;
    return;
  }

  if (msg.type === "complete") {
    if (streamEl) {
      streamEl.textContent = msg.message.content;
      streamEl = null;
      streamBuffer = "";
    } else {
      appendBubble(msg.message.role, msg.message.content, msg.message.agentName);
    }
    chatEl.scrollTop = chatEl.scrollHeight;
    return;
  }

  if (msg.type === "permission") {
    showPermissionDialog(msg);
    return;
  }
});
```

---

## Part B: Phone Voice → Azure Speech PTT Bridge

### B.1 Phone Side — Audio Capture

Modern mobile browsers (iOS 14.3+, Android Chrome 74+) support:
- `navigator.mediaDevices.getUserMedia({ audio: true })`
- `AudioWorkletProcessor` for low-latency PCM processing

**Recommended constraints:**

```js
const stream = await navigator.mediaDevices.getUserMedia({
  audio: {
    channelCount: 1,
    sampleRate: 16000,      // request 16 kHz directly
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true,
  },
});
```

> **Note:** Browsers may ignore `sampleRate: 16000` (iOS Safari especially). The `AudioWorkletProcessor` path handles this by resampling from the browser's actual rate (typically 44100 or 48000 Hz) to 16000 Hz before transmission. Always resample in the worklet regardless of what rate was granted.

### B.2 Audio Format Mismatch — Recommendation

Three options were evaluated:

#### Option A: Server-side transcoding via NAudio (❌ Not recommended)

NAudio 2.3.0 does not include a built-in Opus/WebM decoder. A browser's `MediaRecorder` with `mimeType: 'audio/webm;codecs=opus'` produces a Matroska-containerized Opus stream. Decoding this on the server would require either:
- `Concentus` (pure-C# Opus decoder, MIT) — no WebM demuxer bundled
- `MediaFoundation` via NAudio — Windows-only, requires WebM codec installed (not guaranteed)

Both paths add meaningful dependency risk and complexity to the C# side. **Rejected.**

#### Option C: Azure Speech SDK Opus passthrough (⚠️ Possible, needs spike)

Azure Speech SDK 1.49.0 supports `AudioStreamFormat.GetCompressedFormat(AudioStreamContainerFormat.OGG_OPUS)` and potentially `WEBM_OPUS` (this enum value exists in later SDK versions). The appeal is zero transcoding on either side.

However:
- `AudioStreamContainerFormat.WEBM_OPUS` may not be present in 1.49.0 — it was added in SDK ~1.20 but the enum name must be verified against the installed version.
- Even if the enum exists, Azure's Opus recognizer requires the Ogg or WebM framing to be **complete and valid** — partial frames or chunks without a proper container header will cause recognition failure.
- Streaming WebM from a browser involves the `MediaRecorder` writing initialization segment + media segments in real time, which can be tricky to relay correctly over WebSocket without buffering logic.

This is worth a **targeted spike** (see Part C) but should not gate Phase 1.

#### Option B: WebAudio AudioWorklet PCM on the phone (✅ Recommended)

The phone's browser resamples and converts audio to 16 kHz / 16-bit / mono **PCM** before transmitting. The server receives raw PCM and writes it directly into the existing `PushAudioInputStream` — exactly as `SpeechRecognitionService.OnAudioData` already does.

**Why this is right:**
- Zero new server-side dependencies. `PushAudioInputStream` + `SpeechRecognitionService` are already tested.
- Binary WebSocket frames are natively supported — no base64 overhead.
- PCM bandwidth on local WiFi: `16000 samples/s × 2 bytes × 1 channel = 32 KB/s` = ~256 kbps. Acceptable for a local LAN session (typical WiFi: 50–300+ Mbps).
- Works on all modern mobile browsers. iOS Safari supports AudioWorklet since iOS 14.5.

**Tradeoff:** Higher bandwidth than Opus (8× roughly). Acceptable for LAN-only RC scenario.

#### AudioWorkletProcessor sketch

`rc-client/audio-processor.js` (a separate file served via `setStaticHandler`, or inlined as a Blob URL):

```js
// audio-processor.js — loaded as AudioWorklet module
class PcmProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this._targetSampleRate = 16000;
    this._sourceSampleRate = sampleRate; // global from AudioWorkletGlobalScope
    this._buffer = [];
    this._ratio = this._sourceSampleRate / this._targetSampleRate;
  }

  process(inputs) {
    const input = inputs[0]?.[0]; // mono channel
    if (!input) return true;

    // Linear interpolation resample to 16 kHz
    const outLen = Math.floor(input.length / this._ratio);
    const out = new Int16Array(outLen);
    for (let i = 0; i < outLen; i++) {
      const src = i * this._ratio;
      const lo = Math.floor(src);
      const hi = Math.min(lo + 1, input.length - 1);
      const frac = src - lo;
      const sample = input[lo] * (1 - frac) + input[hi] * frac;
      // Clamp and convert float32 → int16
      out[i] = Math.max(-32768, Math.min(32767, Math.round(sample * 32767)));
    }
    this.port.postMessage(out.buffer, [out.buffer]);
    return true;
  }
}

registerProcessor("pcm-processor", PcmProcessor);
```

Main page JS:

```js
const ctx = new AudioContext({ sampleRate: 16000 }); // hint, browser may override
await ctx.audioWorklet.addModule("/audio-processor.js");

const source = ctx.createMediaStreamSource(stream);
const worklet = new AudioWorkletNode(ctx, "pcm-processor");

worklet.port.onmessage = (e) => {
  if (ws.readyState === WebSocket.OPEN) {
    ws.send(e.data); // raw ArrayBuffer of Int16 PCM
  }
};

source.connect(worklet);
worklet.connect(ctx.destination); // necessary to keep worklet alive in some browsers
```

### B.3 Session Multiplexing — Audio vs. Text on the Same Port

The existing `RCClientCommand` union (`protocol.d.ts:104`) does not include audio commands. The `RemoteBridge.handleClientCommand` processes **text WebSocket frames** only; its handler signature takes a string.

**Recommended approach: SDK extension + binary frame routing**

Add to `RemoteBridgeConfig` (in the SDK):

```ts
/** Called when the mobile client sends a binary audio frame (raw PCM) */
onAudioChunk?: (data: Buffer, connectionId: string) => void;
/** Called when the mobile client starts a PTT session */
onAudioStart?: (connectionId: string) => void;
/** Called when the mobile client ends a PTT session */
onAudioEnd?: (connectionId: string) => void;
```

Inside `RemoteBridge.handleConnection`, alongside the existing `on('message', ...)` handler, add:

```ts
ws.on('message', (data, isBinary) => {
  if (isBinary) {
    this.config.onAudioChunk?.(data as Buffer, connectionId);
    return;
  }
  // … existing text handler
});
```

Text-framed PTT lifecycle signals are added to `RCClientCommand`:

```ts
export interface RCAudioStartCommand { type: 'audio_start'; }
export interface RCAudioEndCommand   { type: 'audio_end';   }

export type RCClientCommand = 
  | RCPromptCommand | RCDirectCommand | RCSlashCommand
  | RCPermissionResponse | RCPingCommand
  | RCAudioStartCommand | RCAudioEndCommand;   // NEW
```

**On the Node.js side (`runPrompt.ts` / `handleRcStart`):**

```ts
rcBridge.config.onAudioStart = (connId) => {
  // Forward to C# SquadDash via NDJSON:
  emit({ type: "rc_audio_start", connectionId: connId });
};

rcBridge.config.onAudioChunk = (data, connId) => {
  // Base64-encode PCM chunk → emit over stdout → C# reads and writes to PushAudioInputStream
  emit({ type: "rc_audio_chunk", connectionId: connId, data: data.toString("base64") });
};

rcBridge.config.onAudioEnd = (connId) => {
  emit({ type: "rc_audio_end", connectionId: connId });
};
```

**On the C# side (`SquadSdkProcess.cs` / `MainWindow.xaml.cs`):**

```csharp
// Add to SquadSdkEvent:
// public string? ConnectionId { get; set; }
// public string? AudioData { get; set; }  // base64 PCM

case "rc_audio_start":
    HandleRcAudioStart(evt);
    break;

case "rc_audio_chunk":
    HandleRcAudioChunk(evt);
    break;

case "rc_audio_end":
    HandleRcAudioEnd(evt);
    break;
```

A new `RemoteSpeechSession` class (thin wrapper) tracks the per-connection `SpeechRecognitionService` instance. On `rc_audio_start` it calls `StartAsync` using a `PushAudioInputStream` instead of `WaveInEvent`. On `rc_audio_chunk` it decodes the base64 payload and calls `_pushStream.Write(bytes, bytes.Length)`. On `rc_audio_end` it calls `StopAsync`, which triggers a final `PhraseRecognized` event that flows into `AppendSpeechToPrompt` and then auto-submits via `_onSendPrompt`.

> **Important:** `SpeechRecognitionService` must be extended (or subclassed) to accept an external `PushAudioInputStream` rather than always constructing a `WaveInEvent`. The existing `StartAsync(key, region, phraseHints)` internally creates the `WaveInEvent` and `_pushStream` together — this needs to be split so the stream can be pre-created and filled from RC audio.

#### Why not a separate WebSocket port for audio?

A separate port would require two QR codes (or a combined URL scheme), complicates NAT/firewall rules, and removes the benefit of sharing the single authenticated session. The binary-frame multiplexing approach keeps everything on one port with no extra auth.

#### Why not base64 in JSON for everything (simpler)?

Forwarding audio as base64 JSON blobs over the Node stdout→C# NDJSON pipe is actually fine — the pipe is local and fast. The bandwidth concern is on the WebSocket leg (phone → server), and there we want binary frames. The pipe relay is an internal implementation detail. Base64 on the pipe adds ~33% overhead but the pipe is `localhost` so it doesn't matter.

### B.4 PTT Semantics on Mobile

Desktop uses **double-Ctrl + hold**. This is a keyboard-native gesture with no mobile equivalent.

**Recommendation: Hold-to-talk (press-and-hold 🎤 button)**

- `pointerdown` → send `audio_start`, start capturing
- `pointerup` / `pointercancel` → send `audio_end`, stop capturing and submit
- Visual feedback: button turns red, shows animated waveform during capture

Why hold-to-talk over tap-to-toggle:
1. **Accidental activation** — tap-to-toggle is easy to leave on accidentally while browsing. A hold button is unambiguous.
2. **Matches mobile UX conventions** — voice memos apps (iOS Voice Memos, WhatsApp voice notes) all use hold-to-record.
3. **Aligns with desktop behavior** — desktop PTT also requires holding Ctrl; the mental model is consistent.
4. **Natural end-of-utterance signal** — releasing the button is the "I'm done talking" signal, which the Azure recognizer interprets as an end-of-input flush, producing a clean final transcript.

Maximum hold duration should be soft-capped at **60 seconds** with a visual countdown in the last 10 seconds. Hard cap at 90 seconds with auto-release.

**Escape hatch:** Add a "tap to cancel" gesture (second quick tap while holding) that sends `audio_end` without submitting, analogous to desktop Escape.

### B.5 Latency Budget

End-to-end path for a single voice-to-response round trip:

| Segment | Typical range | Bottleneck? |
|---|---|---|
| Phone mic → AudioWorklet PCM | 10–50 ms | AudioWorklet block size (128 samples @ 16kHz = 8 ms per block) |
| WebSocket send (LAN) | < 5 ms | LAN latency; negligible |
| Node.js onAudioChunk → stdout NDJSON | 1–3 ms | local pipe; negligible |
| C# stdin reader → PushAudioInputStream.Write | 1–2 ms | synchronous write; negligible |
| Azure Speech streaming recognition | **1.5–3 s** | **Primary bottleneck** — cloud round trip + model inference |
| PhraseRecognized → AppendSpeechToPrompt → _onSendPrompt | < 50 ms | WPF dispatch; negligible |
| SquadBridge / LLM response (first token) | **2–15 s** | **Second bottleneck** — LLM latency |
| Streaming deltas → WebSocket → phone | 5–30 ms per delta | LAN; negligible |

**Total perceived latency (voice → first response token):** **3.5–18 seconds.**

This is acceptable for a non-real-time assistant. The user has released the PTT button and knows processing is happening. The phone page should show:

1. `🎤 Transcribing…` while `rc_audio_start` to `rc_audio_end` is active
2. Transcribed text appears in the input box (via `rc_audio_transcribed` event) — gives user confidence
3. Auto-submit triggers, spinner shows, streaming response begins

The **speech recognition latency (~2 s) is the most improvable** part: it can be reduced by switching to a streaming partial-result model where `Recognizing` events (intermediate results) are shown on the phone in real time. The current `SpeechRecognitionService` only surfaces `Recognized` (final) events. Adding `_recognizer.Recognizing` event handling to stream partials to the phone would improve perceived responsiveness significantly.

---

## Part C: Implementation Sequencing

### C.1 Dependency Graph

```
[Talia] LAN IP detection + 0.0.0.0 bind   ← ALREADY IN PROGRESS
          │
          ▼
[Arjun] A: setStaticHandler + index.html   ← can start now (doesn't need LAN URL to build)
          │
          ▼
[Arjun] A: QR code display in C# WPF      ← needs rc_started.lanUrl from Talia
          │
          ▼
[Arjun] B.1: Audio capture + AudioWorklet ← can develop against mock WS server in parallel
          │
[SDK PR] B.3: RemoteBridge binary frame    ← blocks B.2
  support + RCAudioStart/End commands      
          │
          ▼
[Arjun] B.2: Node.js audio relay (NDJSON) ← needs SDK PR + C# receiving end
          │
[Arjun] B.4: C# RemoteSpeechSession       ← parallel with B.2; needs SpeechRecognitionService refactor
          │
          ▼
[Integration] Wire B.2 ↔ B.4, test end-to-end voice round trip
```

### C.2 Parallelization

These tracks can run in parallel once LAN binding is unblocked:

| Track | Owner | Blocks |
|---|---|---|
| Part A: Static HTML page + WebSocket client | Arjun | QR code display |
| Part A: QR code WPF overlay | Arjun | needs `lanUrl` from Talia |
| SDK PR: binary frame + audio commands | SDK contributor (or Brady) | Part B Node relay |
| Part B: AudioWorklet PCM processor | Arjun | can mock WS server locally |
| Part B: `SpeechRecognitionService` refactor | Arjun | Part B C# session |
| Option C spike: Azure WEBM_OPUS | Arjun (½ day) | independent |

### C.3 Riskiest Unknowns — Spike First

**Risk 1 (High): `RemoteBridge` SDK binary frame support**

The SDK's `handleClientCommand` does not expose binary frame handling. This is not a SquadDash-owned file — it lives in `@bradygaster/squad-sdk`. Until this PR is merged and published, the audio relay cannot be tested. **This is the single biggest scheduling risk.**

Mitigation: Spike with a monkey-patched local copy of the compiled `bridge.js` to validate the binary → NDJSON → C# → PushAudioInputStream path before the SDK PR is merged.

**Risk 2 (Medium): iOS Safari AudioWorklet compatibility**

iOS Safari 14.5 supports AudioWorklet but has known quirks:
- `AudioContext.sampleRate` may not honor the 16000 Hz hint
- `AudioWorkletNode` may stop firing when the page is backgrounded (screen lock)

Spike: Load the audio-processor on an iPhone, log the actual sample rate, verify PCM chunks arrive at the server. The backgrounding issue is a product decision — document it as a known limitation of hold-to-talk (user cannot lock phone while recording).

**Risk 3 (Low-Medium): Azure Speech SDK `AudioStreamContainerFormat.WEBM_OPUS`**

If this enum value exists in SDK 1.49.0, Option C becomes a zero-transcoding alternative that eliminates the AudioWorklet complexity and bandwidth overhead. This spike is a ½-day verification:
1. Check if `AudioStreamContainerFormat.WEBM_OPUS` compiles against the installed SDK
2. Create a test that pipes a browser-captured WebM/Opus recording through `PushAudioInputStream` with `GetCompressedFormat(AudioStreamContainerFormat.WEBM_OPUS)`
3. Verify Azure returns accurate transcripts

If it works, abandon Option B and ship Option C instead. If it doesn't, Option B is the fallback.

---

## Key Decisions Needed

The following require explicit owner sign-off before implementation starts:

1. **SDK PR ownership for binary audio frames.** ✅ **Decided 2026-04-30**  
   **Owner: Talia Rune** will author and submit the PR. PR submission is gated on the Option C audio format spike (Key Decision #2) — the callback interface surface differs between WEBM_OPUS and PCM paths. Mitigation for upstream merge delay: maintain a local patch in `patches/` (existing pattern). See `.squad/decisions.md` §RC Mobile — SDK PR Ownership for Binary Audio Frames (2026-04-30).

2. **Option B vs. Option C for audio format.** ✅ **Decided 2026-04-30 — use Option B**  
   Spike confirmed: `AudioStreamContainerFormat.WEBM_OPUS` does **not exist** in SDK 1.49.0. Available compressed formats are OGG_OPUS, MP3, FLAC, ALAW, MULAW, AMRNB, AMRWB, ANY. OGG_OPUS is also ruled out — Chrome/Safari `MediaRecorder` does not support OGG container output.  
   **Decision: Proceed with Option B** — `AudioWorklet` extracts PCM (16 kHz/16-bit/mono) on the phone; binary frames sent over WebSocket; written directly into existing `PushAudioInputStream`. No new Azure SDK dependency required.  
   See `.squad/decisions.md` §RC Mobile — Audio Format Spike (2026-04-30).

3. **QR code NuGet package.**  
   Two candidates: `QRCoder` (MIT, ~150 KB, no native deps) and `ZXing.Net` (Apache 2.0, larger). Neither is in the project today. Owner should approve adding one. `QRCoder` is preferred for its minimal footprint.

4. **Audio-during-LLM-run policy.** ✅ **Decided 2026-04-30 — Option (c): reject with graceful feedback**  
   When PTT is initiated on the phone while `_isPromptRunning` is true, the phone shows **"⏳ AI is responding — wait before speaking"** and disables the PTT button. Auto-unblocks when the `"done"` event fires. No audio is captured during the blocked window.  
   C# broadcasts `{ "type": "rc_status", "status": "busy"|"idle" }` over WebSocket when `_isPromptRunning` changes; phone PTT button listens and enables/disables accordingly.  
   See `.squad/decisions.md` §RC Mobile — PTT-During-LLM-Run Policy (2026-04-30).

5. **Session isolation for multi-phone connections.**  
   `RemoteBridge` allows multiple simultaneous connections. If two phones are connected and both submit prompts, they share the same SquadBridge session and the same `addMessage` history. Is this the desired behaviour, or should each phone connection have an isolated session? This decision affects how `onPrompt` is wired in `handleRcStart`.

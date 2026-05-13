---
title: Sounds
nav_order: 6
parent: Options
---

# Sounds

The Sounds tab lets you attach an audio cue to each SquadDash event. You can use the default Windows alert sound, a custom audio file, or a spoken text-to-speech phrase. For example, enter `"Prompt complete"` in the path box and SquadDash will speak that phrase aloud when the event fires. The tab also contains all TTS provider configuration.

![Screenshot: Sounds tab showing the event rows and TTS configuration section](images/options-sounds.png)
> 📸 *Screenshot needed: The Options window on the Sounds tab — show several event rows with checkboxes and path boxes, plus the Text-to-Speech section below.*

---

## Sound Events

Each event has its own row with three controls:

| Control | Purpose |
|---|---|
| **Checkbox** | Enable or disable the sound for this event |
| **Path box** | What to play (see [path box values](#path-box-values) below) |
| **Browse…** | Opens a file picker filtered to `.mp3` and `.wav` files |

### Events

| Event | When it fires |
|---|---|
| **Prompt complete** | An agent turn finishes successfully |
| **Prompt error / failed** | An agent turn ends in an error |
| **Approval needed** | The agent requests human approval before continuing |
| **Queue empty** | The last queued prompt has dispatched and the queue is now empty |
| **Loop iteration complete** | One iteration of the Loop finishes |
| **Loop stopped** | The Loop finishes or is manually stopped |
| **Commit made** | A git commit is recorded in the workspace |
| **Quick replies shown** | The agent surfaces a set of quick-reply buttons |

### Path box values

Leave the path box **blank** to play the default Windows alert sound when the event fires.

Enter a **file path** (`.mp3` or `.wav`) to play a custom audio file:

```
C:\Sounds\chime.wav
```

Enter a **quoted phrase** to have it spoken aloud using text-to-speech:

```
"Prompt complete"
```

The TTS provider is configured in the [Text-to-Speech](#text-to-speech) section below. The path box tooltip reminds you of this syntax.

### Testing a sound

Right-click any path box and choose **▶  Test sound** to preview what will play for that event without waiting for it to fire naturally:

- **Blank path** — plays the default Windows alert sound.
- **File path** — plays the audio file.
- **Quoted phrase** — speaks the phrase via the configured TTS provider (falls back to the Windows alert sound if TTS is not configured).

---

## Text-to-Speech

When a sound-event path is a quoted phrase, SquadDash speaks it using the TTS provider configured here.

![Screenshot: TTS configuration section showing provider, voice, and Test TTS button](images/options-sounds-tts.png)
> 📸 *Screenshot needed: The lower portion of the Sounds tab showing the Text-to-Speech section — provider combo set to either Azure or OpenAI, the relevant voice controls visible, and the 🔊 Test TTS button.*

### TTS Provider

| Option | Service used |
|---|---|
| **Azure Speech** | Azure Cognitive Services Neural Text-to-Speech. Requires the Azure Speech API key and region configured on the [Speech tab](speech.md). |
| **OpenAI TTS** | OpenAI text-to-speech API. Requires the OpenAI speech API key configured on the [Speech tab](speech.md). |

### Azure Voice

Shown when **Azure Speech** is selected. Type an Azure Neural voice name (e.g. `en-US-JennyNeural`) or select from the dropdown. The dropdown is populated automatically when a valid Azure Speech key and region are configured on the Speech tab.

### OpenAI Voice & Model

Shown when **OpenAI TTS** is selected.

| Setting | Options |
|---|---|
| **Voice** | `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer` |
| **Model** | `tts-1 (fast)` — lower latency; `tts-1-hd (quality)` — higher fidelity |

### Testing TTS

Click **🔊 Test TTS** to speak a short test sentence using the current provider and voice settings. If there is a configuration problem, an error message appears below the button with a **📋 Copy error** link for easy sharing when troubleshooting.

---

## Tips

- Use short phrases for TTS so there is minimal delay between the event and the spoken response.
- The **Browse…** button is disabled when the row's checkbox is unchecked — enable the sound first, then browse.
- Sound settings are saved immediately on every change.

---

## Related

- **[Speech](speech.md)** — Configure the speech API keys that TTS uses
- **[Notifications](notifications.md)** — Push notifications to your phone for the same events
- **[Prompt Queue](../features/prompt-queue.md)** — Queue empty event fires when the last queued prompt runs

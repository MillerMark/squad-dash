---
title: Voice Input
nav_order: 2
parent: Features
---

# Voice Input

SquadDash supports **push-to-talk (PTT)** voice input powered by Azure Cognitive Services Speech. Hold the PTT key while speaking; release when done — your speech is transcribed and sent as a prompt.

---

## Setup

Voice input requires an Azure Cognitive Services Speech key:

1. Open **Preferences** from the top menu.
2. Enter your **Azure Speech Key** and **Region** (e.g., `eastus`).
3. Save.

See **[Configuration](../reference/configuration.md)** for details on obtaining a key.

---

## Using Push-to-Talk

**Hold** the PTT key (or button), speak your prompt, then **release** to send.

| Step | Action |
|---|---|
| 1 | Press and hold the PTT key |
| 2 | Speak your prompt |
| 3 | Release the key — transcription completes and the prompt is submitted |

While recording, a **voice input indicator** appears in the prompt area to confirm the microphone is active.

![Screenshot: Voice input indicator while recording](images/voice-input-recording.png)
> 📸 *Screenshot needed: The prompt area during an active PTT recording session — show the voice input indicator / recording badge. The microphone should be clearly active.*

---

## PTT Key

The default PTT activation is **double-tap Ctrl** (tap Ctrl twice quickly, then hold). See **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** for the full PTT key reference.

---

## Voice Annotation in Transcripts

When a dictated prompt is sent, the transcript marks it automatically:

> *(some or all of this prompt was dictated by voice)*

This annotation appears **after the prompt runs** — it shows in the transcript once the turn fires, not while the prompt is sitting in the queue.

![Screenshot: Voice annotation in the transcript](images/voice-input-annotation.png)
> 📸 *Screenshot needed: A transcript showing a completed prompt turn that was voice-dictated — the "(some or all of this prompt was dictated by voice)" annotation should be visible below the prompt text.*

> **Note:** If a voice-dictated prompt is in the **Prompt Queue** waiting to dispatch, the queue tab shows clean text only. The annotation appears only once the item actually dispatches and runs.

---

## Voice Input in Fullscreen Mode

Push-to-talk works normally in **Fullscreen Transcript** mode. When you activate PTT, the prompt bar slides into view automatically so you can see the recording indicator — without leaving fullscreen.

See **[Fullscreen Transcript](fullscreen-transcript.md)** for details.

---

## Troubleshooting

| Issue | Fix |
|---|---|
| No recording indicator appears | Check that your Azure Speech Key and Region are set in Preferences |
| Transcription is inaccurate | Speak clearly; reduce background noise; verify the correct region is set |
| PTT key does not respond | Ensure SquadDash has focus; try clicking the prompt area first |

---

## Related

- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — PTT key details
- **[Prompt Queue](prompt-queue.md)** — How voice prompts behave when queued
- **[Fullscreen Transcript](fullscreen-transcript.md)** — PTT in fullscreen mode
- **[Configuration](../reference/configuration.md)** — Azure Speech setup

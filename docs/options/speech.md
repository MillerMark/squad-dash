---
title: Speech
nav_order: 2
parent: Options
---

# Speech

The Speech tab configures how SquadDash transcribes your voice into text. Choose a speech-recognition provider, supply the necessary credentials, and optionally define text-replacement rules that clean up recognised phrases before they reach the prompt box.

![Screenshot: Speech tab showing provider selection and Voice Text Replacements grid](images/options-speech.png)
> 📸 *Screenshot needed: The Options window on the Speech tab — show both the provider radio buttons and the Voice Text Replacements data grid with at least one rule.*

---

## Provider

Choose which service transcribes your microphone input:

| Option | Service used |
|---|---|
| **Azure** | Azure Cognitive Services Speech-to-Text |
| **OpenAI** | OpenAI Whisper API |

Select one radio button; the relevant credential fields appear below.

---

## Azure Speech

Shown when **Azure** is selected.

| Field | Description |
|---|---|
| **Azure Speech API Key** | Your Azure Cognitive Services speech resource key. Click **(reveal key)** and hold to temporarily show the value. |
| **Azure Speech Region** | The Azure region your resource is deployed in (e.g. `eastus`, `westus2`, `westeurope`). |

---

## OpenAI Speech

Shown when **OpenAI** is selected.

| Field | Description |
|---|---|
| **OpenAI speech API key** | Your OpenAI API key. Click **(reveal key)** and hold to temporarily show the value. |

---

## Voice Text Replacements

A table of find-and-replace rules applied to every phrase returned by the speech recogniser before it reaches the prompt box. Use this to correct commonly misheard words, expand acronyms, or strip filler phrases.

| Column | Description |
|---|---|
| **Pattern (regex)** | A .NET regular expression to match. The match is case-sensitive by default — use `(?i)` to make it case-insensitive. |
| **Replacement** | The string to substitute. Supports regex group back-references (e.g. `$1`). Leave blank to delete the matched text. |

Rules are applied **in order** from top to bottom.

### Editing rules

- **Double-click** a cell to edit it. Changes are saved automatically when you leave the cell.
- Click **Add Rule** to append a new empty row.
- Select a row and click **Remove Selected** to delete it.

---

## Tips

- Use the pattern `\bum\b|\buh\b` with an empty replacement to strip filler words.
- Regex patterns support .NET syntax — test complex patterns at [regex101.com](https://regex101.com) with the **.NET** flavour selected.

---

## Related

- **[Voice Input](../features/voice-input.md)** — How voice dictation works end-to-end
- **[Sounds → Text-to-Speech](sounds.md#text-to-speech)** — Configure the TTS voice used for spoken sound-event alerts

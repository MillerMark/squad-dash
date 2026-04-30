# Using Remote Control

> ⚠️ **The RC interface is actively in development.** This page reflects current functionality and will be updated as the interface evolves.

---

## Connecting

Open the RC URL on your phone — either by scanning the QR code from the SquadDash transcript or tapping a bookmarked link. The interface connects automatically. A green status dot in the header confirms you're live.

If you lose WiFi briefly, the client reconnects and reloads the transcript automatically. No action needed.

---

## Sending a text prompt

1. Tap the text input field at the bottom of the screen.
2. Type your prompt.
3. Tap **Send**.

The prompt appears in the transcript on both your phone and your PC immediately.

---

## Push-to-talk voice input

> **Requires:** Azure Speech Key and Region configured in **Preferences > Remote Access**.
> **Requires:** An HTTPS connection — PTT does not work over plain `http://`. Use a tunnel (ngrok or Cloudflare) for PTT from outside your network; see [Local LAN Setup](setup-local.md) notes for on-network use.

1. Tap and **hold** the PTT button.
2. Speak your prompt.
3. **Release** the button.

Your speech is transcribed and submitted automatically.

**First use:** Your browser will ask for microphone permission. Tap **Allow**. Without this, PTT won't work.

**PTT is temporarily blocked while the agent is responding.** The PTT button shows a busy state. It unlocks automatically when the agent finishes — you don't need to do anything.

---

## Activity indicators

The header shows two icons that indicate what the agent is currently doing:

| Icon | Meaning |
|---|---|
| 🧠 *(animating + timer)* | Agent is thinking / generating a response |
| 🔨 *(animating + timer)* | Agent is running tools |

Both icons are always visible — dimmed when idle, full brightness with a live elapsed-seconds counter when active. The counter disappears as soon as the activity stops.

---

## Quick reply buttons

When the agent's response includes suggested follow-up options, they appear as tappable buttons below the response. Tap one to send that prompt immediately.

---

## Auto-reconnect

If your phone loses its connection, the RC client reconnects and reloads the full transcript automatically. You do not need to re-scan the QR code within the same RC session.

---

## Related

- **[Local (LAN) Setup](setup-local.md)**
- **[ngrok Setup](setup-ngrok.md)**
- **[Cloudflare Tunnel Setup](setup-cloudflare.md)**
- **[Voice Input](../../features/voice-input.md)** — Azure Speech setup for PTT

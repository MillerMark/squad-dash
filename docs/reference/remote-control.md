# Remote Control

Control SquadDash from your phone browser — read the transcript, send text prompts, and use push-to-talk voice input — while SquadDash runs on your PC. No app install required.

RC is designed for situations where you want to walk away from your desk but stay in the loop.

---

## What you can do

- **Read the transcript** — conversation history loads instantly; new messages stream in live
- **Send text prompts** — type and submit prompts directly from your phone
- **Push-to-talk voice** — hold the PTT button, speak, release — your speech is transcribed and submitted
- **Multiple phones** — everyone sees the same session; anyone can send prompts

---


## Requirements

| Requirement | Details |
|---|---|
| **SquadDash running** | RC is a live connection to an active session — the PC app must be open |
| **Network access** | LAN (same WiFi) or internet tunnel — see connection methods below |
| **Azure Speech key** | Required for push-to-talk only — text prompts work without it |

---

## Starting and stopping

**Start:** Click **Workspace** → **Start Remote Access**.

SquadDash starts a local server and displays a QR code in the transcript. Scan it with your phone to connect.

**Stop:** Click **Workspace** → **Stop Remote Access**. All connected phones are disconnected immediately.

---

## Choosing a connection method

How you reach RC depends on where your phone is relative to your PC:

| Method | Best for | Extra setup |
|---|---|---|
| **[Local (LAN)](remote-control/setup-local.md)** | Phone on the same WiFi as your PC | None |
| **[ngrok](remote-control/setup-ngrok.md)** | Quick, occasional access from another network | Free account + binary |
| **[Cloudflare Tunnel](remote-control/setup-cloudflare.md)** | Stable, regular remote access with a fixed URL | Free account + binary |

**Quick rule of thumb:**

- Same WiFi → **Local**. Nothing to install.
- On mobile data or at a different location occasionally → **ngrok**. Fastest to set up.
- Using RC regularly, or you want a URL that doesn't change session to session → **Cloudflare Tunnel**.

---

## Using the interface

→ **[Using Remote Control](remote-control/using-remote-control.md)** — how to interact with the RC phone interface

---

## Security note

> ⚠️ **Keep your RC URL private.**

The RC server is protected by an authentication token embedded in the URL. Anyone with the URL (or QR code image) can connect to your session and send prompts to your agents.

- Do not share screenshots of the QR code or the RC URL publicly.
- Stop RC when you're done: **Workspace > Stop Remote Access**.
- When using a tunnel, the public URL stops being accessible as soon as you stop RC.

---

## Related

- **[Voice Input](../features/voice-input.md)** — Desktop push-to-talk and Azure Speech setup
- **[Configuration](configuration.md)** — Azure Speech key configuration
- **[Keyboard Shortcuts](keyboard-shortcuts.md)** — Desktop PTT key reference

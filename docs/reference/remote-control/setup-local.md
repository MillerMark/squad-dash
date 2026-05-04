---
title: Local Setup
nav_order: 1
parent: Remote Control
grand_parent: Reference
---

# Local (LAN) Setup

Connect your phone to SquadDash over your local WiFi network. This is the simplest setup — no accounts, no extra software.

**Use this when:** Your phone and PC are on the same WiFi network.

---

## Setup

No special setup is required. LAN access works out of the box when you start RC.

---

## Connecting your phone

1. Open SquadDash on your PC.
2. Click **Workspace** → **Start Remote Access**.

SquadDash starts a local server and displays a QR code and LAN URL in the transcript.

![Screenshot: QR code displayed in the transcript after starting RC](../images/rc-qr-code-transcript.png)
> 📸 *Screenshot needed: The SquadDash transcript showing the RC start confirmation — the "📡 Remote access started" message, the LAN URL, and the QR code graphic.*

3. On your phone, open the **camera app** and point it at the QR code.
4. Tap the link that appears — your phone's browser opens the RC interface automatically.

> **Tip:** If the QR code doesn't scan easily, the LAN URL is also shown in the transcript (e.g., `http://192.168.1.42:PORT`). You can type it directly into your phone's browser.

---

## Troubleshooting

**Phone can't connect — "Unable to reach" or blank page**

- Make sure your phone and PC are on the **same WiFi network**. Mobile data won't work for LAN connections.
- Check that RC is still running — **Workspace** should show **Stop Remote Access**, not Start.
- Try typing the LAN URL directly into your phone's browser instead of scanning the QR code.
- If your PC has a firewall, it may be blocking the RC port. SquadDash adds a Windows Firewall rule automatically, but you may need to allow it if prompted.

**Transcript stops updating**

The RC client reconnects automatically after brief interruptions. If the transcript has been stuck for more than a minute, refresh the browser page — your session history will reload immediately.

---

## Related

- **[Using Remote Control](using-remote-control.md)** — how to interact with the interface once connected
- **[ngrok Setup](setup-ngrok.md)** — access RC from outside your network
- **[Cloudflare Tunnel Setup](setup-cloudflare.md)** — stable remote access with a fixed URL

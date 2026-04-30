# Remote Control

Control SquadDash from your phone browser — read the transcript, send text prompts, and use push-to-talk voice input — while SquadDash runs on your PC. No app install required.

---

## Overview

Remote Control (RC) streams your SquadDash session to any phone browser over your local WiFi network (or the internet, with a tunnel). Once connected, your phone becomes a fully functional remote:

- **Read the transcript** — the full conversation history loads instantly; new messages stream in live.
- **Send text prompts** — type and submit prompts directly from your phone.
- **Push-to-talk voice** — hold the PTT button, speak, release — your speech is transcribed and submitted as a prompt.
- **Multiple phones at once** — everyone sees the same session; anyone can send prompts.

RC is designed for situations where you want to walk away from your desk but stay in the loop — or hand off input to someone else in the room.

![Screenshot: Phone showing the RC interface with transcript and PTT button](images/rc-phone-interface.png)
> 📸 *Screenshot needed: A phone screen showing the RC mobile web interface — the transcript with a few messages, the text input field at the bottom, and the push-to-talk button clearly visible.*

---

## Requirements

| Requirement | Details |
|---|---|
| **Same WiFi network** | Your phone and PC must be on the same network for LAN access (no tunnel needed) |
| **Azure Speech key** | Required for push-to-talk voice input only — text prompts work without it |
| **SquadDash running** | RC is a live connection to an active session — the PC app must be open |

If you only need text prompts, you can skip the Azure Speech setup entirely.

---

## Starting Remote Access

1. Open SquadDash on your PC.
2. Click the **Workspace** menu in the top menu bar.
3. Select **Start Remote Access**.

SquadDash starts a local server and displays a QR code in the transcript.

![Screenshot: QR code displayed in the transcript after starting RC](images/rc-qr-code-transcript.png)
> 📸 *Screenshot needed: The SquadDash transcript showing the RC start confirmation — the "📡 Remote access started" message, the LAN URL, and the QR code graphic.*

4. On your phone, open the **camera app** and point it at the QR code.
5. Tap the link that appears — your phone's browser opens the RC interface automatically.

That's it. Your phone is now connected and showing the live transcript.

> **Tip:** If the QR code doesn't scan easily, the LAN URL is also printed in the transcript (e.g., `http://192.168.1.42:PORT`). You can type that address into your phone's browser manually.

---

## Stopping Remote Access

1. Click **Workspace** in the menu bar.
2. Select **Stop Remote Access**.

The server shuts down and all connected phones are disconnected.

---

## Using the Phone Interface

### Sending a text prompt

1. Tap the text input field at the bottom of the screen.
2. Type your prompt.
3. Tap **Send**.

The prompt appears in the transcript on both your phone and your PC immediately.

### Push-to-talk voice input

> **Requires:** Azure Speech Key and Region configured in **Preferences > Remote Access** (or **Preferences > Speech**).

1. Tap and **hold** the PTT button.
2. Speak your prompt.
3. **Release** the button.

Your speech is transcribed and submitted as a prompt automatically.

![Screenshot: Phone showing the PTT button in active recording state](images/rc-ptt-active.png)
> 📸 *Screenshot needed: The RC phone interface with the PTT button in its "recording" / active state — ideally showing a visual indicator that recording is in progress (e.g., a pulsing ring or color change).*

**First use:** Your browser will ask for microphone permission. Tap **Allow**. Without this permission, PTT will not work.

### Agent status badge

When the agent is processing a response, a status badge appears on screen:

| Badge | Meaning |
|---|---|
| ⏳ **Agent is busy** | The agent is currently generating a response — PTT is temporarily blocked |
| ✅ *(no badge / idle)* | The agent is ready — you can send prompts and use PTT |

PTT unblocks automatically once the agent finishes. You do not need to do anything.

### Auto-reconnect

If your phone loses WiFi briefly, the RC client reconnects automatically and reloads the transcript. You do not need to re-scan the QR code during the same RC session.

---

## Accessing from Outside Your Home Network

By default, RC only works when your phone is on the **same WiFi network** as your PC. This is because the connection goes directly to your PC's local IP address, which isn't reachable from the internet.

### What is a tunnel?

A **tunnel** is a service that creates a secure, public URL that forwards internet traffic to your PC — even though your PC isn't directly accessible from the internet. Think of it as a temporary front door to your local server that anyone with the link can reach.

SquadDash can launch a tunnel automatically when RC starts. Two tunnel providers are supported: **ngrok** and **Cloudflare Tunnel**.

> **When do you need this?** Only if your phone is on a different network than your PC — for example, if you're on mobile data, at a coffee shop, or sharing access with someone in another location.

---

### Option 1 — ngrok

ngrok is quick to set up and works well for occasional use.

#### Step 1: Create a free ngrok account

1. Go to [https://ngrok.com](https://ngrok.com) and click **Sign up**.
2. Complete the registration — a free account is sufficient.

#### Step 2: Download the ngrok binary

1. After signing in, go to the [ngrok download page](https://ngrok.com/download).
2. Download the **Windows** version.
3. Extract the ZIP file — you'll get a single file called `ngrok.exe`.
4. Move `ngrok.exe` to a permanent location, for example: `C:\Tools\ngrok\ngrok.exe`.

#### Step 3: Get your auth token

1. In the ngrok dashboard, click **Your Authtoken** in the left sidebar (or go to `https://dashboard.ngrok.com/get-started/your-authtoken`).
2. Copy the token — it looks like a long string of letters and numbers.

#### Step 4: Configure ngrok in SquadDash

1. Open **Preferences** from the top menu in SquadDash.
2. Go to the **Remote Access** (or **Tunnel**) section.
3. Set **Tunnel Provider** to `ngrok`.
4. Enter the full path to your `ngrok.exe` in the **Binary Path** field (e.g., `C:\Tools\ngrok\ngrok.exe`).
5. Paste your auth token in the **Auth Token** field.
6. Save.

![Screenshot: Preferences showing the RC tunnel configuration section](images/rc-preferences-tunnel.png)
> 📸 *Screenshot needed: The Preferences dialog open to the Remote Access / Tunnel section — show the Tunnel Provider dropdown, Binary Path field, and Auth Token field.*

#### Step 5: Start RC with the tunnel active

Next time you click **Workspace > Start Remote Access**, SquadDash will launch ngrok automatically. A public URL (e.g., `https://abc123.ngrok-free.app`) will appear in the transcript alongside the QR code. Scan it from any network.

---

### Option 2 — Cloudflare Tunnel

Cloudflare Tunnel (`cloudflared`) is free and more stable for long-running or permanent setups.

#### Step 1: Create a free Cloudflare account

1. Go to [https://www.cloudflare.com](https://www.cloudflare.com) and click **Sign up**.
2. Complete the registration — the free plan is all you need.

#### Step 2: Download cloudflared

1. Go to the [cloudflared releases page](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/).
2. Download the **Windows (64-bit)** binary — it's a single `.exe` file called `cloudflared.exe`.
3. Move it to a permanent location, for example: `C:\Tools\cloudflared\cloudflared.exe`.

#### Step 3: Get your tunnel token

Cloudflare Tunnel uses a token to associate the tunnel with your account. You can generate one in the Cloudflare Zero Trust dashboard:

1. Sign in to [https://one.dash.cloudflare.com](https://one.dash.cloudflare.com).
2. In the left sidebar, go to **Networks > Tunnels**.
3. Click **Create a tunnel**.
4. Choose **Cloudflared** as the connector type, then click **Next**.
5. Give your tunnel a name (e.g., `squaddash-rc`), then click **Save tunnel**.
6. On the next screen, Cloudflare shows an install command. Copy the token from that command — it's the long string that appears after `--token`.

#### Step 4: Configure cloudflared in SquadDash

1. Open **Preferences** from the top menu in SquadDash.
2. Go to the **Remote Access** (or **Tunnel**) section.
3. Set **Tunnel Provider** to `cloudflare`.
4. Enter the full path to `cloudflared.exe` in the **Binary Path** field (e.g., `C:\Tools\cloudflared\cloudflared.exe`).
5. Paste your tunnel token in the **Tunnel Token** field.
6. Save.

#### Step 5: Start RC with the tunnel active

Click **Workspace > Start Remote Access**. SquadDash launches cloudflared and a public `*.trycloudflare.com` URL appears in the transcript. Scan the QR code from any network.

---

### Which tunnel should I use?

| | ngrok | Cloudflare Tunnel |
|---|---|---|
| **Cost** | Free tier available | Free |
| **Setup difficulty** | Easier | Slightly more steps |
| **Best for** | Quick, occasional access | Stable, always-on setups |
| **URL stability** | Changes each session (free tier) | Stable (with named tunnel) |
| **Speed** | Fast | Fast |

**Quick recommendation:** If you just want to try RC from outside your network once or twice, start with ngrok — it's the faster path. If you plan to use RC regularly, Cloudflare Tunnel gives you a stable URL that doesn't change.

---

## Tips & Troubleshooting

### Phone can't connect — "Unable to reach" or blank page

- Make sure your phone and PC are on the **same WiFi network**. Mobile data won't work for LAN connections.
- Check that RC is still running on your PC (**Workspace** menu should show **Stop Remote Access**, not **Start Remote Access**).
- Try typing the LAN URL from the transcript directly into your phone's browser instead of using the QR code.
- If your PC has a firewall, it may be blocking the RC port. Check your Windows Firewall settings and allow the connection if prompted.

### PTT button does nothing / no transcription

- Verify your **Azure Speech Key** and **Region** are entered correctly in Preferences.
- Make sure you granted **microphone permission** when your browser asked. If you denied it, go to your browser's site settings and re-enable microphone access for the RC URL.
- PTT is blocked while the agent is generating a response (the ⏳ badge is visible). Wait for the agent to finish, then try again.

### Tunnel doesn't start

- Double-check the **binary path** in Preferences — it must point to the actual `.exe` file, not just the folder.
- Confirm your auth token / tunnel token is correct and hasn't expired.
- Check that you have an internet connection on the PC.
- Look for a `rc_tunnel_error` message in the SquadDash transcript — it may include a more specific error from ngrok or cloudflared.

### Transcript stops updating on phone

The RC client reconnects automatically after brief interruptions. If the transcript has been stuck for more than a minute, refresh the browser page on your phone — your session history will reload immediately.

---

## Security Note

> ⚠️ **Keep your RC URL private.**

The RC server is protected by an **authentication token** embedded in the URL. Anyone who has the URL (including the QR code image) can connect to your session and **send prompts to your agents**.

- Do not share screenshots of the QR code or the RC URL publicly.
- Stop RC when you're done: **Workspace > Stop Remote Access**.
- When using a tunnel, the public URL is only active while RC is running — it stops being accessible as soon as you stop RC.

---

## Related

- **[Voice Input](../features/voice-input.md)** — Desktop push-to-talk and Azure Speech setup
- **[Configuration](configuration.md)** — Azure Speech key configuration
- **[Keyboard Shortcuts](keyboard-shortcuts.md)** — Desktop PTT key reference

# ngrok Setup

Use ngrok to access SquadDash RC from outside your local WiFi — from mobile data, another location, or any internet connection.

**Use this when:** You want quick, occasional remote access from another network and don't need a stable URL between sessions.

---

## Step 1: Create a free ngrok account

1. Go to [https://ngrok.com](https://ngrok.com) and click **Sign up**.
2. Complete the registration — the free tier is sufficient.

---

## Step 2: Download ngrok

1. After signing in, go to the [ngrok download page](https://ngrok.com/download).
2. Download the **Windows** version.
3. Extract the ZIP — you get a single file: `ngrok.exe`.
4. Move it to a permanent location, e.g. `C:\Tools\ngrok\ngrok.exe`.

---

## Step 3: Get your auth token

1. In the ngrok dashboard, click **Your Authtoken** in the sidebar (or go to `https://dashboard.ngrok.com/get-started/your-authtoken`).
2. Copy the token — it looks like a long string of letters and numbers.

---

## Step 4: Configure in SquadDash

1. Open **Preferences** from the top menu.
2. Go to the **Remote Access** section.
3. Set **Tunnel Provider** to `ngrok`.
4. Enter the full path to `ngrok.exe` in the **Binary Path** field (e.g., `C:\Tools\ngrok\ngrok.exe`).
5. Paste your auth token in the **Auth Token** field.
6. Save.

![Screenshot: Preferences showing RC tunnel configuration with ngrok selected](../images/rc-preferences-tunnel.png)
> 📸 *Screenshot needed: The Preferences dialog open to the Remote Access / Tunnel section — showing the Tunnel Provider dropdown set to ngrok, the Binary Path field, and the Auth Token field.*

---

## Step 5: Start RC

Click **Workspace** → **Start Remote Access**. SquadDash launches ngrok automatically. A public URL (e.g., `https://abc123.ngrok-free.app`) appears in the transcript alongside the QR code. Scan it from any network.

> **Note:** On the free ngrok tier, the public URL changes every session. Re-scan the QR code after each restart. For a stable URL, use [Cloudflare Tunnel](setup-cloudflare.md) instead.

---

## Troubleshooting

**Tunnel doesn't start**

- Check the **binary path** — it must point to `ngrok.exe` directly, not just the folder.
- Verify your auth token is correct in Preferences.
- Confirm your PC has an internet connection.
- Check the SquadDash transcript for a `rc_tunnel_error` message with more details.

---

## Related

- **[Using Remote Control](using-remote-control.md)** — how to interact with the interface once connected
- **[Local (LAN) Setup](setup-local.md)** — no-tunnel option for same-network use
- **[Cloudflare Tunnel Setup](setup-cloudflare.md)** — stable alternative with a fixed URL

# Cloudflare Tunnel Setup

Use Cloudflare Tunnel (`cloudflared`) to access SquadDash RC from outside your local network. It's free, reliable, and — unlike ngrok's free tier — gives you a stable URL that doesn't change between sessions.

**Use this when:** You want regular or always-on remote access, or you need a URL your phone can bookmark.

---

## Step 1: Create a free Cloudflare account

1. Go to [https://www.cloudflare.com](https://www.cloudflare.com) and click **Sign up**.
2. Complete the registration — the free plan is all you need.

---

## Step 2: Download cloudflared

1. Go to the [cloudflared releases page](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/).
2. Download the **Windows (64-bit)** binary — a single file: `cloudflared.exe`.
3. Move it to a permanent location, e.g. `C:\Tools\cloudflared\cloudflared.exe`.

---

## Step 3: Get your tunnel token

1. Sign in to [https://one.dash.cloudflare.com](https://one.dash.cloudflare.com).
2. In the left sidebar, go to **Networks > Tunnels**.
3. Click **Create a tunnel**.
4. Choose **Cloudflared** as the connector type, then click **Next**.
5. Give your tunnel a name (e.g., `squaddash-rc`), then click **Save tunnel**.
6. On the next screen, Cloudflare shows an install command. Copy the token — it's the long string that appears after `--token` in that command.

---

## Step 4: Configure in SquadDash

1. Open **Preferences** from the top menu.
2. Go to the **Remote Access** section.
3. Set **Tunnel Provider** to `cloudflare`.
4. Enter the full path to `cloudflared.exe` in the **Binary Path** field (e.g., `C:\Tools\cloudflared\cloudflared.exe`).
5. Paste your tunnel token in the **Tunnel Token** field.
6. Save.

---

## Step 5: Start RC

Click **Workspace** → **Start Remote Access**. SquadDash launches cloudflared and a public `*.trycloudflare.com` URL appears in the transcript. Scan the QR code from any network.

> **Note:** With a named tunnel, the URL is stable — your phone can bookmark it and reconnect without re-scanning the QR code after each restart.

---

## Troubleshooting

**Tunnel doesn't start**

- Check the **binary path** — it must point to `cloudflared.exe` directly, not just the folder.
- Verify your tunnel token is correct in Preferences.
- Confirm your PC has an internet connection.
- Check the SquadDash transcript for a `rc_tunnel_error` message with more details.

---

## Related

- **[Using Remote Control](using-remote-control.md)** — how to interact with the interface once connected
- **[Local (LAN) Setup](setup-local.md)** — no-tunnel option for same-network use
- **[ngrok Setup](setup-ngrok.md)** — quicker setup for occasional remote access

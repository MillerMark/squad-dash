---
title: Remote Access
nav_order: 3
parent: Options
---

# Remote Access (alpha)

The Remote Access tab lets you configure an outbound tunnel so SquadDash can be reached from outside your local network — for example from your phone or a remote machine.

![Screenshot: Remote Access tab showing tunnel provider and auth token fields](images/options-remote-access.png)
> 📸 *Screenshot needed: The Options window on the Remote Access tab — show the Tunnel Provider combo and the auth token field (masked).*

---

## Remote Access Tunnel

When Remote Access starts, SquadDash can optionally auto-launch a tunnel binary that creates a publicly accessible URL pointing back to your local instance.

| Setting | Description |
|---|---|
| **Tunnel Provider** | The tunnelling service to use. Select from the dropdown (e.g. ngrok, Cloudflare Tunnel). |
| **Tunnel Auth Token** | The authentication token for the chosen provider. Leave blank if the tunnel binary is already pre-configured with credentials on your machine. Click **(reveal token)** and hold to temporarily show the value. |

> **Note:** The tunnel binary must already be installed on your machine. SquadDash launches it automatically when Remote Access starts, but does not download or install the binary itself.

---

## Related

- **[Remote Control overview](../reference/remote-control.md)** — Full guide to setting up and using Remote Access
- **[ngrok setup](../reference/remote-control/setup-ngrok.md)** — Step-by-step ngrok tunnel configuration
- **[Cloudflare Tunnel setup](../reference/remote-control/setup-cloudflare.md)** — Step-by-step Cloudflare tunnel configuration

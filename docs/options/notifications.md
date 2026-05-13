---
title: Notifications
nav_order: 5
parent: Options
---

# Notifications

The Notifications tab configures push notifications so you can receive alerts on your phone or another device when SquadDash events occur — useful when you step away from your desk while agents are running.

Notifications are delivered via **[ntfy](https://ntfy.sh)**, a free open-source push-notification service.

![Screenshot: Notifications tab showing the ntfy topic, QR code, and event checkboxes](images/options-notifications.png)
> 📸 *Screenshot needed: The Options window on the Notifications tab — show the enabled checkbox, ntfy topic field, QR code, and the "Notify me when" checkboxes.*

---

## Enabling Notifications

Check **Notifications enabled** at the top of the tab to turn the feature on. When unchecked, no notifications are sent regardless of the event checkboxes below.

---

## Delivery Method

Currently the only supported delivery method is **ntfy.sh**. Future providers may be added.

### Setting up ntfy

1. Install the **ntfy** app on your phone ([iOS](https://apps.apple.com/app/ntfy/id1625396347) / [Android](https://play.google.com/store/apps/details?id=io.heckel.ntfy)).
2. Enter a **Topic** name in the field provided — this is the channel your notifications will be sent to. Keep it private (it acts as a shared secret).
3. Or click **Generate Random Topic** to create a hard-to-guess random topic name automatically.
4. Scan the **QR code** with the ntfy app to subscribe to that topic in one tap.
5. The subscribe URL is also shown as text below the QR code if you prefer to copy it manually.

> **Privacy note:** Anyone who knows your topic name can subscribe to your notifications. Use the Generate Random Topic button to get a hard-to-guess name.

---

## Notify Me When

Choose which events trigger a notification:

| Event | When it fires |
|---|---|
| **AI turn complete** | An agent finishes a response turn |
| **Git commit made** | A commit is recorded in the workspace repository |
| **Loop iteration complete** | One iteration of the Loop completes |
| **Loop stopped** | The Loop finishes or is stopped |
| **Remote connection established** | A Remote Access session connects |
| **Remote connection dropped** | A Remote Access session disconnects |

Check any combination. Only checked events send notifications.

---

## Testing

Click **Test Notification** to send a test message to your configured topic immediately. Use this to confirm your phone is subscribed before relying on notifications.

---

## Related

- **[Loop Panel](../panels/Loop.md)** — Running agents in a continuous loop
- **[Remote Control](../reference/remote-control.md)** — Setting up remote access

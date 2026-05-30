---
title: Inbox Panel
nav_order: 6
parent: Panels
---

# Inbox Panel

The Inbox Panel shows messages sent to you by agents during maintenance runs or other automated workflows. Messages are delivered by embedding an `INBOX_MESSAGE_JSON` block in an agent response; SquadDash parses the block and stores the message in `.squad/inbox/`. The panel lets you read, filter, act on, archive, and delete those messages without leaving the main window.

---

## Opening the Panel

**View** menu → **Inbox** (toggles visibility). Visibility is persisted per workspace.

Close the panel with its **×** button.

---

## Panel Layout

The panel is split into two panes side-by-side:

```
┌─ Inbox ─────────────────────────────────────────────────────────[×]─┐
│ [Filter…]  [☐ Unread only]                                           │
│ ┌──────────────────────────┐  ┌──────────────────────────────────┐   │
│ │ ● Lint failures found    │  │  Lint failures found             │   │
│ │ ● Unused imports report  │  │  argus-weld · 2h ago             │   │
│ │   Dead code summary      │  │  [Fix this] [Add to backlog]     │   │
│ └──────────────────────────┘  │  ──────────────────────────────  │   │
│                               │  Found 14 unused import          │   │
│                               │  statements across 6 files…      │   │
│                               └──────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

> 📸 *Screenshot needed: Inbox panel with two or three messages in the list, one selected with the viewer pane open showing subject, sender/timestamp, action buttons, and body.*

---

## Message List (left pane)

Messages are sorted newest-first. Each row shows:

| Element | Description |
|---------|-------------|
| **Unread dot** | A small filled circle in the accent colour. Hidden once the message is read. |
| **Subject** | Bold and full-opacity when unread; normal weight and reduced opacity when read. |

Hovering over a row that has a body opens a **markdown popup** showing the sender, relative timestamp, and a preview of the body text. The popup appears to the left of the panel and closes when the cursor leaves.

Clicking a row marks the message as read and opens it in the viewer pane.

---

## Header Controls

| Control | What it does |
|---------|-------------|
| **Filter box** | Hides rows whose subject does not match the entered text. Supports `@handle` prefix syntax (see [Filtering](#filtering) below). |
| **Unread only** checkbox | When checked, hides all read messages regardless of the filter. Shows "No unread messages" if none remain. |

---

## Filtering

| What you type | Effect |
|---------------|--------|
| `lint` | Shows messages whose subject contains "lint" (case-insensitive). |
| `@argus-weld` | Shows messages from argus-weld or that mention `@argus-weld` in the body. |
| `@argus-weld lint` | Shows messages from argus-weld **and** whose subject contains "lint". Both conditions must match. |

---

## Message Viewer (right pane)

Clicking a row opens the viewer pane. The viewer shows:

1. **Subject** — large heading at the top.
2. **Sender and timestamp** — `from · relative time` (e.g. "argus-weld · 2h ago"). The `from` field is set by the agent that composed the message.
3. **Action buttons** — one button per entry in the message's `actions` array, if any. See [Action Buttons](#action-buttons) below.
4. **Attachments** — chips displayed in a wrap row below the actions. See [Attachments](#attachments) below.
5. **Body** — full markdown body rendered as a flow document.

---

## Action Buttons

Action buttons are deferred prompts baked into the message at composition time. Clicking one injects a fully self-contained prompt into the queue and routes it based on the button's `routeMode`:

| Route mode | What happens when clicked |
|------------|--------------------------|
| `start_named_agent` | Routes the prompt directly to the named specialist agent. |
| `start_coordinator` | Routes the prompt to the coordinator. |
| `draft` | Pre-fills the user's input box with the prompt text without sending; the user can edit before sending. |
| `done` | Dismisses the action with no prompt injection. |

A button is permanently disabled once clicked — the used state is persisted to the message file so it survives a panel reload.

> **Why deferred?** Maintenance tasks run while you are away. Action buttons let you review their findings and decide what to do when you return, without losing the context the agent captured.

---

## Attachments

Attachments are displayed as clickable chips below the action buttons. Each chip shows an icon and a label:

| Type | Icon | Click behaviour |
|------|------|----------------|
| `url` | 🔗 | Opens the URL in the default browser. |
| `file` | 📄 | Opens the file — markdown files open in the markdown viewer; other files use the system default. |
| `image` | 🖼 | Opens the image in a dedicated viewer window. |
| `task-ref` | ✅ | Shows the task's current status, priority, owner, and title in a popup. |
| `text` | 📝 | Opens the inline text or markdown content in a markdown viewer window. |
| *(other)* | 📎 | Generic chip. |

---

## Context Menu

Right-click any message row to open its context menu:

| Item | What it does |
|------|-------------|
| **Add to Chat** | Inserts the message body as an attachment in the prompt box. |
| **Mark as read** / **Mark as unread** | Toggles the read state (and the unread dot). Only the applicable item is shown. |
| **Archive** | Moves the message file to `.squad/inbox/archive/`. The row is removed from the list immediately. Archived messages are not shown in the panel. |
| **Delete** | Permanently deletes the message file. The row is removed immediately. |

---

## Opening a Message in a Floating Window

Messages can be popped out into a standalone **InboxMessageWindow** for side-by-side reading while you work in the main window. Use **View** → the message, or right-click a row and select **Open in window**.

> 📸 *Screenshot needed: InboxMessageWindow floating alongside the main SquadDash window, showing a message with action buttons.*

---

## Where Messages Are Stored

Messages are stored as individual JSON files in the workspace's `.squad` folder:

```
.squad/inbox/<message-id>.json
```

Archived messages are moved to:

```
.squad/inbox/archive/<message-id>.json
```

The panel does **not** use a file-system watcher. It refreshes programmatically when an agent turn that produces an `INBOX_MESSAGE_JSON` block completes, so new messages appear automatically at the end of the relevant agent run.

---

## Sending Messages from an Agent

Any agent (typically a maintenance task) can send an inbox message by including an `INBOX_MESSAGE_JSON` block at the end of its response. Full format reference and action-button guidelines are documented in [Maintenance Mode — Inbox messages](../features/maintenance-mode.md#inbox-messages-and-deferred-actions).

---

## Tips

- Use the **Unread only** toggle to focus on what needs attention after returning from a maintenance window.
- Action button prompts must be fully self-contained — the agent that receives them has no conversation history from when the message was written.
- Archiving a message moves it off the active list but preserves the file. Delete only when you are sure you no longer need the record.
- The `from:` field is free-form text — by convention agents use their handle (e.g. `argus-weld`), making `@handle` filtering reliable.

---

## Related

- **[Maintenance Panel](Maintenance.md)** — View and manage the maintenance tasks that send inbox messages
- **[Maintenance Mode](../features/maintenance-mode.md)** — Full `INBOX_MESSAGE_JSON` format, `actions` array, and deferred-action guidance
- **[Tasks Panel](Tasks.md)** — Track the open task backlog

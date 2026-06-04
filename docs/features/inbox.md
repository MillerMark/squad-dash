---
title: Inbox Panel
nav_order: 11
parent: Features
---

# Inbox Panel

The Inbox panel is an AI-to-user messaging system. Agents send you structured messages — with rich body text, attachments, and optional action buttons — that accumulate in the panel while you work. You can read them at any time, even hours after they were sent.

---

## How Agents Send Messages

Any agent (coordinator or `argus-weld`) can send an inbox message by appending an `INBOX_MESSAGE_JSON` block to the end of its response:

```
INBOX_MESSAGE_JSON:
{
  "subject": "Your subject line here",
  "from": "coordinator",
  "body": "Full **Markdown** body text.",
  "attachments": [],
  "actions": []
}
```

SquadDash detects the block, persists it to `.squad/inbox/`, and immediately updates the Inbox panel.

> **Agent guidance:** The `INBOX_MESSAGE_JSON` block must appear at the very end of the response. Everything after the opening `{` until the closing `}` is parsed as JSON.

---

## The Inbox Panel

The panel appears in the left sidebar.

- **Unread badge** — a count bubble on the panel header shows how many unread messages are waiting.
- **Message list** — messages are listed in arrival order, each showing the sender handle and subject line.
- **Click to open** — clicking any message opens the **message popup** showing the full body, attachments, and action buttons.
- **Read/unread state** — a message is marked read the first time you open it; unread messages are visually distinguished in the list.

---

## Message Fields

| Field | Required | Description |
|---|---|---|
| `subject` | yes | Plain-text subject line shown in the panel list and popup header |
| `from` | yes | Sender handle — `"coordinator"` or `"argus-weld"` |
| `body` | yes | Markdown content rendered in the popup |
| `attachments` | no | Array of typed attachment objects (see below) |
| `actions` | no | Array of deferred quick-reply buttons (see below) |

---

## Attachments

Each attachment has a `type` field that controls how it is displayed and what happens when you click it.

### `url` — Open a web link

```json
{
  "type": "url",
  "label": "View pull request",
  "href": "https://github.com/org/repo/pull/42"
}
```

Clicking opens the URL in your default browser.

---

### `file` — Open a local file

```json
{
  "type": "file",
  "label": "See migration guide",
  "path": "docs/migration.md"
}
```

Clicking opens the file. `.md` files open in the built-in Markdown viewer; all other files open with the system's default application.

---

### `image` — View an image

```json
{
  "type": "image",
  "label": "Screenshot",
  "path": "docs/images/before-after.png"
}
```

Clicking opens a lightweight image viewer window. Both local paths and remote URLs (via `href` instead of `path`) are supported.

---

### `task-ref` — Link to a task

```json
{
  "type": "task-ref",
  "label": "Flaky CI test",
  "taskId": "fix-ci-flake"
}
```

Clicking shows a popup with the task's current status, priority, owner, and description, read live from `.squad/tasks.md`.

---

### `text` — Inline Markdown content

```json
{
  "type": "text",
  "label": "Full diff summary",
  "content": "## Summary\n\nChanged 3 files: `src/A.cs`, `src/B.cs`, `tests/C.cs`."
}
```

Clicking shows the Markdown content in a dialog — useful for longer supplementary text that would clutter the main body.

---

## Action Buttons

The `actions` array renders as clickable buttons in the message popup. Actions are *deferred* — they do nothing until you click them, making them ideal for situations where the agent cannot wait for a real-time reply (for example, during [Maintenance Mode](maintenance-mode.md)).

Each action has:

| Field | Required | Description |
|---|---|---|
| `label` | yes | Button text |
| `routeMode` | yes | `"start_named_agent"`, `"start_coordinator"`, `"draft"`, or `"done"` |
| `targetAgent` | for `start_named_agent` | Handle of the agent to route the prompt to |
| `prompt` | for `start_*` and `draft` modes | Fully self-contained prompt injected when clicked; for `draft`, pre-fills the input box without sending |

### Route modes

| `routeMode` | What happens on click |
|---|---|
| `start_named_agent` | Sends `prompt` to the agent specified by `targetAgent` |
| `start_coordinator` | Sends `prompt` to the coordinator |
| `draft` | Pre-fills the user's input box with `prompt` without sending; the user can edit before sending |
| `done` | Dismisses the message popup; no prompt is sent |

> **Self-contained prompts:** Each `prompt` must include all the context needed to act — file paths, symptoms, and any relevant background. There is no conversation history available when the button is clicked; the prompt is the entire briefing.

---

## Complete Example

The following shows a message using every field and attachment type:

```
INBOX_MESSAGE_JSON:
{
  "subject": "Code smell scan — 3 findings",
  "from": "argus-weld",
  "body": "## Scan Results\n\nI found **3 issues** during the maintenance window. See attachments for details.",
  "attachments": [
    {
      "type": "text",
      "label": "Full findings report",
      "content": "### Finding 1\n`src/Parser.cs:88` — deeply nested conditional (depth 6).\n\n### Finding 2\n`src/Utils.cs:204` — unused private method `BuildCache()`.\n\n### Finding 3\n`tests/ParserTests.cs:31` — test name does not describe expected outcome."
    },
    {
      "type": "file",
      "label": "Open Parser.cs",
      "path": "src/Parser.cs"
    },
    {
      "type": "task-ref",
      "label": "Reduce nesting in Parser",
      "taskId": "refactor-parser-nesting"
    },
    {
      "type": "image",
      "label": "Complexity heatmap",
      "path": "docs/images/complexity-heatmap.png"
    },
    {
      "type": "url",
      "label": "Cyclomatic complexity reference",
      "href": "https://en.wikipedia.org/wiki/Cyclomatic_complexity"
    }
  ],
  "actions": [
    {
      "label": "Fix Parser.cs now",
      "routeMode": "start_named_agent",
      "targetAgent": "arjun-sen",
      "prompt": "Arjun: during a maintenance scan on 2026-05-22 I found deeply nested conditionals (depth 6) in src/Parser.cs starting at line 88. Please refactor to reduce nesting to at most depth 3. Preserve existing behaviour and keep all tests passing."
    },
    {
      "label": "Add to backlog",
      "routeMode": "start_coordinator",
      "prompt": "Add a mid-priority task to address 3 code-smell findings from the 2026-05-22 maintenance scan: (1) deeply nested conditional in src/Parser.cs:88, (2) unused method BuildCache() in src/Utils.cs:204, (3) non-descriptive test name in tests/ParserTests.cs:31."
    },
    {
      "label": "Dismiss",
      "routeMode": "done"
    }
  ]
}
```

---

## Storage

Messages are persisted as JSON files in `.squad/inbox/` inside your workspace directory:

```
.squad/inbox/{timestamp}-{id}.json
```

Files are written when the message is received and read back each time SquadDash opens, so your inbox survives restarts.

> **⚠️ Never commit inbox files:** `.squad/inbox/` is **user-local and ephemeral** — it must never be tracked by git. Each user's inbox is specific to their session and machine. The inbox is automatically excluded from version control during Squad setup. If you manually manage `.gitignore`, ensure `.squad/inbox/` is listed.

---

## Related

- **[Maintenance Mode](maintenance-mode.md)** — The primary scenario where agents send inbox messages while you are away
- **[Tasks Panel](../panels/Tasks.md)** — The `task-ref` attachment type reads live data from `.squad/tasks.md`

---
title: Host Commands
nav_order: 5
parent: Reference
---

# Host Commands

Host commands allow the AI to directly control SquadDash — starting loops, inspecting queues, opening panels, and more — without any manual user interaction.

---

## How It Works

Before every prompt, SquadDash appends a `HOST_COMMANDS:` catalog to the system context describing what commands are available and how to invoke them. When the AI wants to invoke one or more commands, it appends a `HOST_COMMAND_JSON:` block at the **very end** of its response:

```
HOST_COMMAND_JSON:
[
  { "command": "start_loop" },
  { "command": "open_panel", "parameters": { "name": "Approvals" } }
]
```

Commands are executed **sequentially** after the turn completes. Each invocation is recorded in the transcript with a distinct entry showing the command name, parameters, and result.

---

## Result Behaviors

| Behavior | Description |
|---|---|
| `silent` | Executes and produces no follow-up turn |
| `inject_result_as_context` | Result is fed back to the AI as the next user turn |
| `notify_user` | Result is shown to the user as a notification |

---

## Built-In Commands

### `start_loop`

Starts the SquadDash native loop.

- **Parameters:** none
- **Result behavior:** silent

```json
{ "command": "start_loop" }
```

---

### `stop_loop`

Stops the SquadDash native loop after the current iteration completes.

- **Parameters:** none
- **Result behavior:** silent

```json
{ "command": "stop_loop" }
```

---

### `get_queue_status`

Returns the current prompt queue items as JSON. The result is injected back as context for the AI's next turn.

- **Parameters:** none
- **Result behavior:** inject_result_as_context

```json
{ "command": "get_queue_status" }
```

---

### `open_panel`

Opens a named panel in the SquadDash UI.

- **Parameters:**

  | Name | Type | Required | Description |
  |---|---|---|---|
  | `name` | string | ✓ | Panel to open. Valid values: `Approvals`, `Tasks`, `Trace`, `Health` |

- **Result behavior:** silent

```json
{ "command": "open_panel", "parameters": { "name": "Approvals" } }
```

---

### `inject_text`

Feeds arbitrary text back to the AI as the next user turn. Useful for chaining multi-step AI actions.

- **Parameters:**

  | Name | Type | Required | Description |
  |---|---|---|---|
  | `text` | string | ✓ | Text to inject as the next turn |

- **Result behavior:** inject_result_as_context

```json
{ "command": "inject_text", "parameters": { "text": "Summarize what you just did." } }
```

---

### `clear_approved`

Clears approved entries from the Approvals panel.

- **Parameters:** none
- **Result behavior:** silent

```json
{ "command": "clear_approved" }
```

---

## Extending with Custom Commands

You can add workspace-specific commands by creating `.squad/commands.json` in the workspace root. Custom commands are merged with built-in commands — you cannot override a built-in command name.

### Format

```json
[
  {
    "name": "my_command",
    "description": "Does something useful",
    "parameters": [
      {
        "name": "target",
        "type": "string",
        "required": true,
        "description": "The target to act on"
      }
    ],
    "resultBehavior": "silent",
    "requiresConfirmation": false
  }
]
```

### Field Reference

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | ✓ | snake_case command identifier |
| `description` | string | ✓ | Shown to the AI in the catalog |
| `parameters` | array | — | List of parameter descriptors |
| `resultBehavior` | string | — | `silent` (default), `inject_result_as_context`, or `notify_user` |
| `requiresConfirmation` | boolean | — | If `true`, SquadDash prompts the user before executing |

> **Note:** Custom commands defined in `.squad/commands.json` are listed in the catalog but have no built-in handler — you must implement handling via the extension mechanism or a companion tool.

---

## Transcript Appearance

Each host command invocation appears as a distinct entry in the transcript with the command name, any parameters, and the outcome (success / error / skipped). Commands that produce output show a collapsible result block.

---

## Related

- **[Keyboard Shortcuts](keyboard-shortcuts.md)** — Hotkeys for navigation and the prompt box
- **[Slash Commands](slash-commands.md)** — `/` commands for the prompt box
- **[Routing](routing.md)** — How the AI coordinator chooses which agent handles each prompt

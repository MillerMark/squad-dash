---
title: Loop File Templates
nav_order: 7
parent: Reference
---

# Loop File Templates

Loop files (`.squad/*.md`) support **template preprocessing** — a two-pass substitution system that runs before the prompt body is sent to the AI. Loop authors write a single file; SquadDash renders the right version based on the current UI settings. The agent only ever sees clean prose — no template tokens.

---

## Processing Order

```
frontmatter options
      │
      ▼
  Pass 1: Conditional blocks  ──── evaluated against raw option values
      │
      ▼
  Pass 2: Variable substitution ── replaces {{key}} tokens in the result
      │
      ▼
  Final prompt sent to agent
```

Conditionals are always evaluated first. Variable substitution runs on the output of Pass 1.

---

## Pass 1 — Conditional Blocks

Wrap content in `{{#if}}` or `{{#unless}}` blocks to include or exclude it based on a frontmatter option value. The entire block — including its delimiter lines — is removed when the condition is false.

### Syntax

```
{{#if key == "value"}}
Content included when key equals "value".
{{/if}}

{{#unless key == "value"}}
Content included when key does NOT equal "value".
{{/unless}}
```

### Examples

**Route commit behavior based on a user setting:**

```
{{#if commit_after_task == "always"}}
Commit the changes automatically without prompting.
{{/if}}
{{#if commit_after_task == "ask"}}
Ask the user whether to commit before proceeding.
{{/if}}
{{#if commit_after_task == "never"}}
Do not commit. Leave changes unstaged.
{{/if}}
```

**Conditionally include test-writing instructions:**

```
{{#if test_after_task == "true"}}
After completing the task, write or update unit tests to cover the changes.
{{/if}}
```

**Skip a step when a flag is off:**

```
{{#unless build_verify == "true"}}
Skip build verification for this iteration.
{{/unless}}
```

**Control routing instructions:**

```
{{#if route_work == "true"}}
Check `.squad/routing.md` and assign the task to the appropriate agent.
{{/if}}
{{#unless route_work == "true"}}
Do the work yourself without consulting the routing table.
{{/unless}}
```

### Rules

- Conditions compare **string values**. Boolean options are compared as `"true"` or `"false"` (see [Tips](#tips--gotchas)).
- Nesting is **not supported**. `{{#if}}` blocks may not contain other `{{#if}}` or `{{#unless}}` blocks.
- Only `==` equality comparisons are supported — no `!=`, `>`, `<`, or logical operators.
- Unknown keys in a condition evaluate as false (the block is removed).

---

## Pass 2 — Variable Substitution

After conditionals are resolved, `{{key}}` tokens in the remaining text are replaced with their current option values.

### Syntax

```
{{key}}
```

### Examples

**Reference a user-configured setting inline:**

```
Commit setting is: {{commit_after_task}}
Max iterations allowed: {{max_iterations}}
```

**Use the built-in `{{iteration}}` token:**

```
This is iteration {{iteration}} of the loop.
```

`{{iteration}}` is a **built-in special case** — it is always substituted with the current loop iteration number, regardless of what is in the frontmatter.

### Rules

| Token | Behavior |
|---|---|
| `{{key}}` where `key` is a known option | Replaced with the option's current value |
| `{{iteration}}` | Always replaced with the current iteration number |
| `{{key}}` where `key` is a `group`-type option | Left as-is (group options have no value) |
| `{{key}}` where `key` is unknown | Left as-is |

---

## Frontmatter Options Schema

Options are declared under the `options:` key in YAML frontmatter. Each option can have:

| Field | Required | Description |
|---|---|---|
| `value` | For non-group types | Current value; used by both conditional and substitution passes |
| `type` | No | `bool`, `enum`, `group`, or omitted for plain string |
| `label` | No | Display label shown in the Loop Settings UI |
| `hint` | No | Tooltip text in the UI |
| `choices` | For `enum` | List of valid values |

**Option types:**

| Type | UI Control | Notes |
|---|---|---|
| `bool` | Checkbox | Value is `true` or `false` (stored as bool, compared as `"true"`/`"false"`) |
| `enum` | Dropdown | Must list `choices`; value must be one of them |
| `group` | Section header | No `value`; organizes options visually; skipped by substitution |
| *(omitted)* | Text input | Plain string value |

### Example frontmatter

```yaml
---
configured: true
interval: 0.1
timeout: 60
max_iterations: 10
options:
  after_task_header:
    type: group
    label: "After Task Completes:"
  commit_after_task:
    value: always
    type: enum
    choices: [always, never, ask]
    label: "Commit:"
    hint: "When to automatically commit completed work"
  build_verify:
    value: true
    type: bool
    label: "Verify build"
  test_after_task:
    value: true
    type: bool
    label: "Write tests"
description: "My loop description"
commands: [stop_loop]
---
```

![Screenshot: Loop Settings panel showing options rendered as UI controls](images/loop-settings-options-ui.png)
> 📸 *Screenshot needed: The Loop Settings panel with `commit_after_task` shown as a dropdown, `build_verify` and `test_after_task` as checkboxes, and `after_task_header` as a section heading label.*

---

## Complete Example

A realistic loop file using both template features together:

```markdown
---
configured: true
interval: 5
timeout: 15
max_iterations: 20
options:
  after_task_header:
    type: group
    label: "After Task Completes:"
  commit_after_task:
    value: always
    type: enum
    choices: [always, never, ask]
    label: "Commit:"
    hint: "When to automatically commit completed work"
  build_verify:
    value: true
    type: bool
    label: "Verify build"
  test_after_task:
    value: true
    type: bool
    label: "Write tests"
description: "Task loop"
commands: [stop_loop]
---

This is iteration {{iteration}}.

Check `.squad/tasks.md` for the next open task and complete it.

{{#if build_verify == "true"}}
After making changes, run the build and confirm it passes before proceeding.
{{/if}}

{{#if test_after_task == "true"}}
Write or update unit tests to cover your changes.
{{/if}}

{{#if commit_after_task == "always"}}
Commit the completed work with a clear commit message. Do not prompt the user.
{{/if}}
{{#if commit_after_task == "ask"}}
Ask the user whether to commit before proceeding.
{{/if}}
{{#if commit_after_task == "never"}}
Leave changes uncommitted.
{{/if}}

If no open tasks remain, stop the loop.
```

With the defaults above (`build_verify: true`, `test_after_task: true`, `commit_after_task: always`), the agent receives on iteration 3:

```
This is iteration 3.

Check `.squad/tasks.md` for the next open task and complete it.

After making changes, run the build and confirm it passes before proceeding.

Write or update unit tests to cover your changes.

Commit the completed work with a clear commit message. Do not prompt the user.

If no open tasks remain, stop the loop.
```

The `ask` and `never` commit blocks are gone. The agent sees only what applies.

---

## Tips / Gotchas

**Boolean values are strings in conditions.**  
Write `{{#if build_verify == "true"}}`, not `{{#if build_verify == true}}`. The condition parser compares string representations, so the unquoted form will never match.

**Nesting is not supported.**  
You cannot put a `{{#if}}` inside another `{{#if}}`. Flatten your logic into sibling blocks with separate conditions.

**Group-type options are skipped by substitution.**  
A `{{after_task_header}}` token would be left as-is in the output — group options are UI section headers with no value. Don't use them as substitution tokens.

**Unknown keys pass through unchanged.**  
A typo in a `{{key}}` token won't cause an error — the token just stays in the prompt. Check the agent output if instructions appear garbled.

**Unknown keys in conditions are falsy.**  
If you reference a key that doesn't exist in `options:`, the condition evaluates as false and the block is removed. This is silent — no warning is emitted.

**`{{iteration}}` is always available.**  
You don't need to declare it in `options:`. It is injected automatically by the loop engine.

**Order of passes matters.**  
Variable substitution runs on the output of the conditional pass. A `{{key}}` token inside a removed `{{#if}}` block is never substituted — the whole block is gone before Pass 2 runs.

---

## Related

- **[Loop Panel](../panels/Loop.md)** — Controls, timing fields, and loop.md structure
- **[Host Commands](host-commands.md)** — `stop_loop` and other agent-invocable commands
- **[Routing](routing.md)** — How `.squad/routing.md` assigns work to agents

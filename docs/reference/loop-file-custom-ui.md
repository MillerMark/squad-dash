---
title: Loop File - Custom UI
nav_order: 8
parent: Reference
---

# Loop File — Custom UI

Loop files can define a **custom settings panel** by declaring an `options:` block in their YAML frontmatter. SquadDash reads this block and renders a Loop Settings popup specific to that loop file — checkboxes, dropdowns, section headers, and text inputs — so users can adjust loop behavior without editing the file directly.

> **Status note:** The Loop Settings popup that renders these controls is not yet built. The frontmatter schema is fully implemented and is read by the template preprocessor. The UI controls will appear once the popup is complete.

For how these options flow into the prompt body (conditionals and variable substitution), see **[Loop File Templates](loop-file-templates.md)**.

---

## The Options Block

Declare options under the `options:` key inside the frontmatter `---` block, before the closing `---`. Each key you define becomes one UI control in the Loop Settings panel.

```yaml
---
configured: true
interval: 5
timeout: 30
options:
  my_option:
    value: hello
    label: "My Option"
description: "My loop"
---
```

**Position rules:**
- `options:` must appear inside the frontmatter delimiters (`---`).
- Option keys use 2-space indentation; their sub-fields use 4-space indentation.
- `options:` ends when a non-indented line (other than `---`) is encountered, so `description:` and other top-level keys must come after the options block.

---

## Option Types

### `bool` — Checkbox

A boolean option renders as a checkbox in the Loop Settings panel. The value is stored as `true` or `false`.

```yaml
options:
  build_verify:
    value: true
    type: bool
    label: "Verify build"
    hint: "Run the build after each iteration and confirm it passes"
```

![Screenshot: bool option rendered as a checkbox in the Loop Settings panel](images/loop-custom-ui-bool-checkbox.png)
> 📸 *Screenshot needed: A checkbox labeled "Verify build" in the Loop Settings panel, shown in its checked state, with the hint text visible on hover.*

**What the template engine sees:**

The value is the string `"true"` or `"false"`. Use it in conditionals with quoted comparison:

```
{{#if build_verify == "true"}}
Run the build and confirm it passes.
{{/if}}
```

Or substitute inline:

```
Build verification is currently: {{build_verify}}
```

---

### `enum` — Dropdown / Picker

An enum option renders as a dropdown (picker) showing the allowed choices. `choices:` is required; `value:` must be one of those choices.

```yaml
options:
  commit_after_task:
    value: always
    type: enum
    choices: [always, never, ask]
    label: "Commit:"
    hint: "When to automatically commit completed work"
```

![Screenshot: enum option rendered as a dropdown in the Loop Settings panel](images/loop-custom-ui-enum-dropdown.png)
> 📸 *Screenshot needed: A labeled dropdown reading "Commit:" with the currently selected value "always" visible, and the choices list [always, never, ask] expanded.*

**What the template engine sees:**

The value is whichever choice the user selected — a plain string. Branch on it with `{{#if}}`:

```
{{#if commit_after_task == "always"}}
Commit the completed work automatically.
{{/if}}
{{#if commit_after_task == "ask"}}
Ask the user whether to commit before proceeding.
{{/if}}
{{#if commit_after_task == "never"}}
Leave changes uncommitted.
{{/if}}
```

---

### `group` — Section Header

A group option renders as a visual section heading in the Loop Settings panel. It has no `value` and is not substituted or evaluated in template processing — it exists purely to organise controls.

```yaml
options:
  after_task_header:
    type: group
    label: "After Task Completes:"
  commit_after_task:
    value: always
    type: enum
    choices: [always, never, ask]
    label: "Commit:"
```

![Screenshot: group option rendered as a section header above related controls](images/loop-custom-ui-group-header.png)
> 📸 *Screenshot needed: The Loop Settings panel showing "After Task Completes:" as a bold or styled section heading, with the "Commit:" dropdown immediately below it.*

**What the template engine sees:**

Nothing — group options are skipped by both the conditional and substitution passes. A `{{after_task_header}}` token in the prompt body would be left unchanged.

---

### Plain string — Text Input

An option with no `type:` (or an unrecognized type) renders as a free-form text input. The user can type any value.

```yaml
options:
  max_iterations:
    value: "10"
    label: "Max iterations"
    hint: "Stop the loop after this many iterations"
```

![Screenshot: plain string option rendered as a text input in the Loop Settings panel](images/loop-custom-ui-text-input.png)
> 📸 *Screenshot needed: A text field labeled "Max iterations" with the current value "10" shown inside the input box.*

**What the template engine sees:**

The raw string value typed by the user. Substitute it with `{{max_iterations}}` or use it in a condition:

```
Max iterations allowed: {{max_iterations}}
```

---

## Option Fields Reference

| Field | Required | Description |
|---|---|---|
| `value` | Required for non-`group` types | The current (default) value of the option. For `bool`, use `true` or `false`. For `enum`, must match one of the `choices`. For plain string, any text. |
| `type` | No | `bool`, `enum`, `group`, or omit for plain string. |
| `label` | No | Human-readable label shown next to the control in the Loop Settings panel. If omitted, the key name is shown. |
| `hint` | No | Tooltip or helper text shown on hover in the Loop Settings panel. |
| `choices` | Required for `enum` | Inline YAML list of allowed values, e.g. `[always, never, ask]`. Ignored for other types. |

---

## Full Example

A complete frontmatter block mixing all four option types:

```yaml
---
configured: true
interval: 5
timeout: 30
max_iterations: 20
options:
  behavior_header:
    type: group
    label: "Behavior:"
  max_iterations:
    value: "20"
    label: "Max iterations"
    hint: "Stop the loop after this many iterations"
  route_work:
    value: false
    type: bool
    label: "Route work to agents"
    hint: "Check routing.md and delegate to the appropriate agent"
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
    hint: "Run the build after each iteration"
  test_after_task:
    value: true
    type: bool
    label: "Write tests"
    hint: "Write or update unit tests to cover changes"
description: "Task loop"
commands: [stop_loop]
---
```

The Loop Settings panel for this file would display two section headers ("Behavior:" and "After Task Completes:") grouping the controls beneath them.

![Screenshot: Full Loop Settings panel showing all four option types](images/loop-custom-ui-full-example.png)
> 📸 *Screenshot needed: The Loop Settings popup for this loop file, showing the "Behavior:" section header with the "Max iterations" text field and "Route work to agents" checkbox below it, then the "After Task Completes:" section header with the "Commit:" dropdown and "Verify build" / "Write tests" checkboxes below it.*

---

## How Options Flow into Templates

Options defined in this block are the values that `{{key}}` tokens and `{{#if}}` / `{{#unless}}` conditionals resolve against when SquadDash builds the prompt to send. The preprocessor reads the current option values (as set by the user in the Loop Settings panel, defaulting to the `value:` fields you declare here) and applies them in two passes.

See **[Loop File Templates](loop-file-templates.md)** for the full syntax reference — conditional blocks, variable substitution, built-ins like `{{iteration}}`, and processing-order rules.

---

## Tips

**Use `label` to show friendly names — the key is for the template engine.**  
A key like `commit_after_task` is what you write in `{{#if commit_after_task == "always"}}`. The `label` field ("Commit:") is only for the UI. Choose keys that read clearly in template syntax; choose labels that read clearly to the user.

**`group` has no `value` — don't use it as a substitution token.**  
A `{{behavior_header}}` token in the prompt body is left unchanged by the substitution pass. Group entries are purely visual organisers.

**`enum` requires `choices:`.**  
If you declare `type: enum` without a `choices:` list, the parser has no valid values to offer. Always pair them.

**`bool` values are strings in conditions.**  
Write `{{#if build_verify == "true"}}`, not `{{#if build_verify == true}}`. The condition parser compares string representations, so the unquoted form never matches.

**Declaration order is display order.**  
Options appear in the Loop Settings panel in the order they are declared in the frontmatter. Put `group` headers immediately before the options they label.

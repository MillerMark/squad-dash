---
title: Tasks Panel
nav_order: 1
parent: Panels
---

# Tasks Panel

The Tasks Panel is a read-only sidebar that surfaces the open task backlog from `.squad/tasks.md`. It groups tasks by priority so you can see at a glance what needs doing — without leaving SquadDash or opening a separate editor.

---

## Opening the Panel

**View** menu → **Tasks** (toggles visibility). Visibility is persisted per workspace.

Close the panel with its **×** button.

---

## What the Panel Shows

Tasks are read from `.squad/tasks.md` in the workspace's `.squad/` folder and grouped into three priority buckets, always in High → Mid → Low order:

![Screenshot: Tasks panel showing prioritised task groups](images/tasks-panel.png)
| Emoji | Priority label | Dot color |
|---|---|---|
| 🔴 | High Priority | Red |
| 🟡 | Mid Priority | Amber |
| 🟢 | Low Priority | Blue (TaskPriorityLow theme colour) |

The panel is **read-only** — checking off tasks requires editing `.squad/tasks.md` directly.

---

## Task Format in `.squad/tasks.md`

Use standard checkbox list format under `##` headings that include a priority emoji:

```markdown
## 🔴 High Priority

- [ ] **Fix login timeout** *(Owner: Arjun Sen)*
  Session tokens expire before the 30-minute idle window.

- [ ] **Resolve flaky CI test**

## 🟡 Mid Priority

- [ ] **Add dark mode toggle** *(Owner: Lyra Morn)*

## 🟢 Low Priority

- [ ] **Update README screenshots**

## ✅ Done

- [x] **Initial scaffolding**
```

### Parsing rules

- Only **open** tasks (`- [ ]`) are shown. Completed tasks (`- [x]`) are ignored.
- The `**bold**` wrapper around the task title is stripped automatically — only the plain title is displayed.
- The `*(Owner: …)*` suffix is stripped from the displayed title. Owner information drives the row indicator and checkbox behavior (see [Assigning Tasks](#assigning-tasks) below), but is not shown as text in the panel.
- Parsing stops when a `##` heading containing `✅` is encountered — everything from that heading onward is ignored. Put your Done section last.
- If the same priority emoji appears in multiple `##` sections (e.g., two `## 🔴` blocks), their items are merged into a single group in the panel.
- Non-priority `##` headings (no emoji match) reset the active group — items below them are not collected until the next priority heading.

---

## Assigning Tasks

Right-click any open task to open its context menu, then choose **Assign to** to set or change the owner.

The submenu is populated dynamically when it opens:

| Entry | What it does |
|---|---|
| **Me** | Writes `*(Owner: you)*` onto the task line. The task row gains a checkbox so you can tick it complete directly from the panel. |
| *(agent names)* | One entry per active, non-utility squad member from `.squad/team.md` (retired members are excluded). Selecting a name writes `*(Owner: Agent Name)*` onto the line. |
| **Remove owner** | Strips the `*(Owner: …)*` suffix entirely. Only shown when the task already has an owner. |

The current owner is indicated by a checkmark (✓) next to the matching entry when the submenu opens.

### How ownership is stored

Assignment writes back to `.squad/tasks.md` immediately. The owner suffix is appended to (or replaces) the task title line in the format:

```
- [ ] **Task title** *(Owner: Agent Name)*
```

`*(Owner: you)*` is the special value that makes a task "user-owned" — those tasks show a checkbox in the panel instead of a dot, and are the only tasks you can tick complete directly in the UI.

### Visual difference between owned and unowned tasks

| Task type | Col 0 indicator | Can tick complete in panel? |
|---|---|---|
| User-owned (`*(Owner: you)*`) | ☐ Checkbox | Yes |
| Agent-owned or unowned | ◼ Filled rounded dot | No (right-click → Mark as Complete) |

---

## Filtering Tasks

The Tasks panel header contains a compact filter box. Typing in it immediately hides non-matching tasks and collapses any priority group that becomes empty.

![Screenshot: Tasks panel filter box in the panel header with some text typed](images/tasks-panel-filter.png)
> 📸 *Screenshot needed: The Tasks panel header row — show the filter text box with something typed (e.g. `login`) and the task list narrowed to matching items. Ideally capture a cleared state alongside for comparison.*

### Filter modes

| What you type | Effect |
|---|---|
| `login` | Shows tasks whose title contains "login" (case-insensitive). |
| `@lyra-morn` | Shows only tasks owned by Lyra Morn. |
| `@lyra-morn login` | Shows tasks owned by Lyra Morn **and** whose title contains "login". Both conditions must match. |
| `@me` | Shows only tasks assigned to **you** (`*(Owner: you)*`). |

### @ IntelliSense in the filter box

Typing `@` in the filter box opens the same IntelliSense dropdown used in the prompt box, but scoped to agents who actually own tasks in the current backlog, with **me** always listed first.

![Screenshot: @ IntelliSense dropdown open inside the Tasks filter box](images/tasks-filter-at-intellisense.png)
> 📸 *Screenshot needed: Tasks filter box with `@` typed, IntelliSense dropdown open showing "me" at top, then agent handles below. One entry highlighted.*

Use **↑** / **↓** to move through suggestions, **Tab** or **Enter** to accept. Accepting inserts `@handle ` (with a trailing space) so you can immediately type an additional text filter after the handle. Press **Escape** to dismiss without committing.

> **Tip:** While the dropdown is open and only one suggestion remains, the panel live-previews that agent's tasks so you can see the result before accepting.

### Clearing the filter

Click the **×** button that appears to the right of the filter box when text is present. The button is hidden when the filter is empty.

---

## Context Menu

Right-click any task row to open its context menu.

### Open task context menu

| Item | What it does |
|---|---|
| **Mark as Complete** | Moves the task to the Done section by writing `- [x]` to `.squad/tasks.md`. |
| **Assign to ▶** | Opens the assignment submenu — see [Assigning Tasks](#assigning-tasks) below. |
| **Follow up…** | Attaches a follow-up prompt to the task (visible when follow-up actions are configured). |
| **Show / Hide Completed Tasks** | Toggles the Done section at the bottom of the panel. |
| **Edit Tasks** | Opens `.squad/tasks.md` in your editor. |

### Completed task context menu

| Item | What it does |
|---|---|
| **Mark as Incomplete** | Returns the task to the open list by writing `- [ ]` to `.squad/tasks.md`. |
| **Follow up…** | Attaches a follow-up prompt to the completed task. |
| **Show / Hide Completed Tasks** | Toggles the Done section visibility. |
| **Edit Tasks** | Opens `.squad/tasks.md` in your editor. |

---

## Refreshing the Panel

The panel reloads automatically whenever `.squad/tasks.md` changes on disk. You can also trigger a refresh with the `/tasks` slash command in the prompt box.

---

## Tips

- Keep the highest-priority tasks near the top of each section so the most important ones are immediately visible.
- Use `/dropTasks` to clear the cached task context that the squad CLI uses; this does not affect the panel display.
- The panel renders task titles only — put actionable detail in sub-bullets below the task line for agents reading the raw file.

---

## Related

- **[Loop Panel](Loop.md)** — Run agents in a loop to work through the task backlog automatically
- **[Slash Commands](../reference/slash-commands.md)** — `/tasks` and `/dropTasks` commands

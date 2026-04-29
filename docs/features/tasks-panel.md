# Tasks Panel

The **Tasks Panel** surfaces your workspace's open task backlog at a glance. It reads directly from `.squad/tasks.md` and groups tasks by priority so you can see what needs doing without leaving SquadDash.

---

## Opening the Tasks Panel

Toggle the Tasks Panel from the **View** menu or the **toolbar**.

![Screenshot: Tasks panel open in the main window](images/tasks-panel-open.png)
> 📸 *Screenshot needed: The SquadDash main window with the Tasks Panel open — show tasks grouped under High, Mid, and Low priority sections with colored dots. Ideally show a mix of priorities.*

---

## What the Panel Shows

Tasks are read from `.squad/tasks.md` and grouped into three priority buckets:

| Priority | Emoji | Dot Color |
|---|---|---|
| 🔴 High Priority | 🔴 | Red |
| 🟡 Mid Priority | 🟡 | Amber |
| 🟢 Low Priority | 🟢 | Blue |

The panel displays **up to 7 tasks** across all priorities. If there are more, the footer shows:

> *showing 7 of 12 — see tasks.md for full details*

![Screenshot: Tasks panel footer with overflow message](images/tasks-panel-footer.png)
> 📸 *Screenshot needed: The bottom of the Tasks Panel showing the "showing X of Y — see tasks.md for full details" footer. Capture when there are more tasks than visible.*

---

## Task Format in `.squad/tasks.md`

The panel reads the standard checkbox list format:

```markdown
## 🔴 High Priority

- [ ] **Fix login timeout** *(Owner: Arjun Sen)*
  Session tokens expire before the 30-minute idle window.

## 🟡 Mid Priority

- [ ] **Add dark mode toggle** *(Owner: Lyra Morn)*

## 🟢 Low Priority

- [ ] **Update README screenshots**
```

Only **open** tasks (`- [ ]`) are shown. Completed tasks (`- [x]`) are filtered out automatically.

---

## Refreshing the Panel

The Tasks Panel reloads whenever `.squad/tasks.md` is saved. You can also use the `/tasks` slash command to force a refresh.

---

## Related

- **[Loop Panel](loop-panel.md)** — Run agents in a loop to work through your task backlog automatically
- **[Slash Commands](../reference/slash-commands.md)** — `/tasks` and `/dropTasks` commands

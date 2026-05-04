---
title: Notes Panel
nav_order: 4
parent: Panels
---

# Notes Panel

The Notes Panel is a lightweight scratchpad built into SquadDash. It lets you save snippets of text — transcript excerpts, ideas, instructions — as named notes that persist across sessions. Each note opens in a full markdown editor window when clicked.

---

## Opening the Panel

**View** menu → **Notes** (toggles visibility). The menu item has a checkmark when the panel is open. Visibility is persisted per workspace.

Close the panel with its **×** button.

---

## Panel Layout

```
┌─ Notes ──────────────────────────────────────[×]─┐
│  Fix login timeout investigation notes            │
│  Prompt ideas for dark-mode pass                  │
│  Agent briefing — Arjun                           │
└───────────────────────────────────────────────────┘
```

Each row shows the note's title. Notes are listed newest-first.

---

## Creating a Note

Notes are created from selected text elsewhere in the UI — there is no "New Note" button in the panel itself.

### From the transcript

1. Select any text in the main transcript.
2. Right-click → **Add to Notes**.

SquadDash derives a title from the first line of the selected text and saves the full selection as the note body. The Notes panel opens automatically if it was closed.

### From the documentation editor

1. Select text in the doc-source editor.
2. Right-click → **Add to Notes**.

### From agents

Agents can call the `add_to_notes` host command to push content directly into the Notes panel.

---

## Opening a Note

Single-click any row to open the note in a dedicated **Markdown Editor** window. The editor:

- Renders markdown in a live preview pane.
- Auto-saves changes as you type — no Save button needed.
- Supports the same screenshot capture workflow as the documentation editor (right-click an image placeholder → paste from clipboard).

---

## Renaming a Note

Right-click any row → **Rename**. The row switches to an inline text field. Press **Enter** to commit or **Escape** to cancel.

---

## Deleting a Note

Right-click any row → **Delete**. A confirmation dialog appears before the note is permanently removed.

---

## Where Notes Are Stored

Notes are stored in the workspace state directory:

```
%LocalAppData%\SquadDash\workspaces\<workspace-id>\notes\
```

- `notes.json` — ordered list of note titles and IDs.
- `<note-id>.md` — one markdown file per note.

Notes are not committed to the git repository.

---

## Tips

- Use notes to capture context you want to pass to an agent in a future prompt — open the note, copy the relevant section, and paste it into the prompt box.
- Notes persist across SquadDash restarts; they are a good place to save running instructions or research that doesn't belong in the codebase.
- The auto-derived title comes from the first ~50 characters of the selected text. Rename the note immediately after creation if you want a more descriptive title.

---

## Related

- **[Tasks Panel](Tasks.md)** — Track open work items from `.squad/tasks.md`
- **[Approvals Panel](Approvals.md)** — Review agent commits

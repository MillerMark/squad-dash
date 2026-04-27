# Documentation Panel

How SquadDash reads and renders documentation from the `docs/` folder in your workspace.

---

## What is the Documentation Panel?

The **documentation panel** is a built-in docs browser inside SquadDash. It shows:
- A **tree view** on the left (folder and file structure)
- A **markdown viewer** on the right (rendered content)

This panel displays the very documentation you're reading right now.

---

## How It Works

SquadDash reads from:

```
docs/
  README.md
  SUMMARY.md
  getting-started/
    installation.md
  concepts/
    agents.md
  reference/
    routing.md
```

### Tree View

The left panel shows:
- Folders as expandable nodes
- Markdown files as clickable items

Click any `.md` file to render it in the right panel.

---

## SUMMARY.md — GitBook Format

If `docs/SUMMARY.md` exists, SquadDash uses it to build the tree view:

```markdown
# Summary

* [Home](README.md)

## Getting Started

* [Installation](getting-started/installation.md)
* [First Run](getting-started/first-run.md)
```

This creates a structured navigation tree.

---

## Markdown Rendering

SquadDash renders markdown with:
- **Headings** (H1-H6)
- **Paragraphs**
- **Bold**, *italic*, `code`
- Code blocks with syntax highlighting
- Tables
- Lists (bulleted and numbered)
- Links (internal and external)

Rendering is powered by `MarkdownDocumentRenderer.cs`.

---

## Images

Place images in subfolders:

```
docs/
  getting-started/
    images/
      screenshot.png
```

Reference in markdown:

```markdown
![Screenshot](images/screenshot.png)
```

SquadDash resolves paths relative to the markdown file's directory.

---

## Cross-References

Use relative links to navigate between docs:

```markdown
See [Agents](../concepts/agents.md) for more.
```

SquadDash resolves these and makes them clickable.

---

## Why This Matters

The documentation panel serves two purposes:

1. **Real documentation** — SquadDash's own docs live in `docs/` and render inside the app
2. **Living template** — Other repos using SquadDash can add a `docs/` folder and get instant browsable documentation

---

## Adding Documentation to Your Repo

1. Create a `docs/` folder in your workspace
2. Add markdown files
3. (Optional) Create `SUMMARY.md` for structured navigation
4. Open SquadDash — your docs appear in the panel

---

## Next

- **[Configuration](../reference/configuration.md)** — Workspace and app settings

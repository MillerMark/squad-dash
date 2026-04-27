# Writing Documentation

How to use SquadDash's documentation panel to write and organize docs.

---

## Overview

SquadDash includes a built-in documentation browser that reads from the `docs/` folder in your workspace. This guide covers how to structure and write effective documentation.

---

## Folder Structure

Use a hierarchical structure:

```
docs/
  README.md               ← Home page
  SUMMARY.md              ← Table of contents (GitBook format)
  getting-started/
    README.md             ← Section landing page
    installation.md
    first-run.md
    images/               ← Images for this section
      screenshot.png
  concepts/
    README.md
    agents.md
  reference/
    README.md
    routing.md
  contributing/
    README.md
    adding-an-agent.md
```

---

## SUMMARY.md — Navigation

The `SUMMARY.md` file defines the tree structure in the docs panel:

```markdown
# Summary

* [Home](README.md)

## Getting Started

* [Installation](getting-started/installation.md)
* [First Run](getting-started/first-run.md)

## Concepts

* [Agents](concepts/agents.md)
```

SquadDash uses this to build the left-side tree view.

---

## Markdown Best Practices

### Headings

Use headings to structure content:

```markdown
# Top-Level Heading (H1)

## Section Heading (H2)

### Subsection Heading (H3)
```

### Code Blocks

Use fenced code blocks with language hints:

````markdown
```csharp
public class Example {
    public string Name { get; set; }
}
```
````

### Tables

Use markdown tables for structured data:

```markdown
| Column 1 | Column 2 |
|---|---|
| Value 1 | Value 2 |
```

### Lists

Bulleted lists:

```markdown
- Item 1
- Item 2
- Item 3
```

Numbered lists:

```markdown
1. Step 1
2. Step 2
3. Step 3
```

### Links

Internal links (relative paths):

```markdown
See [Agents](../concepts/agents.md) for more.
```

External links:

```markdown
[GitHub](https://github.com)
```

---

## Images

Place images in an `images/` subfolder within the section:

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

## Section Landing Pages

Each section should have a `README.md` that:
- Explains what the section covers
- Links to child pages

### Example

```markdown
# Getting Started

This section covers installation and first run.

## What You'll Learn

- How to install prerequisites
- How to build SquadDash
- What happens on first launch

---

## Next

- **[Installation](installation.md)** — Clone and build
- **[First Run](first-run.md)** — Launch and connect
```

---

## Writing Style

- **Concise** — Get to the point quickly
- **Structured** — Use headings, lists, tables
- **Examples** — Show code and commands
- **Cross-references** — Link to related pages

Avoid:
- Marketing fluff
- Long paragraphs without structure
- Vague descriptions

---

## Meta: This Very Documentation

The docs you're reading right now live in `docs/` and follow this exact structure. Use SquadDash's own docs as a reference template.

---

## Next Steps

1. Create a `docs/` folder in your workspace
2. Add markdown files
3. Write a `SUMMARY.md` for navigation
4. Open SquadDash — your docs appear in the panel

---

## Next

- **[Documentation Panel](../concepts/documentation-panel.md)** — How the docs panel works
- **[Adding an Agent](adding-an-agent.md)** — Create a new agent

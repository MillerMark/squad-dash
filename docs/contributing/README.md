---
title: Contributing
nav_order: 6
has_children: true
---

# Contributing

How to contribute to SquadDash — adding agents, writing docs, and extending functionality.

---

## What You'll Learn

- **[Adding an Agent](adding-an-agent.md)** — How to create a new agent for your team
- **[Writing Documentation](writing-docs.md)** — How to use SquadDash's docs panel to write docs

---

## Ways to Contribute

### 1. Add Agents

Extend your Squad team by creating new specialized agents. Each agent can handle a specific domain (e.g., security, performance, DevOps).

### 2. Write Documentation

Improve SquadDash's docs or document your own workspace. The docs panel makes documentation a first-class citizen.

### 3. File Issues

Report bugs or request features on the GitHub repository.

### 4. Submit Pull Requests

Fix bugs, add features, or improve code quality. Follow the project's coding standards and include tests.

---

## Development Setup

See **[Installation](../getting-started/installation.md)** for how to build SquadDash from source.

---

## Code Standards

- **C#** — Follow Microsoft C# conventions
- **XAML** — Use clear naming and structure
- **Tests** — NUnit 4.4+, one test class per production class
- **Commit messages** — Descriptive and concise

---

## Running Tests

```bash
dotnet test squad-dash.slnx
```

All tests should pass before submitting a PR.

---

## Next

- **[Adding an Agent](adding-an-agent.md)** — Step-by-step guide
- **[Writing Documentation](writing-docs.md)** — Docs best practices

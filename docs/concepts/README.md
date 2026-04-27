---
title: Concepts
nav_order: 4
has_children: true
---

# Concepts

This section explains the core concepts behind SquadDash and the Squad AI team model.

---

## What You'll Learn

- **[Agents](agents.md)** — What agents are, how they work, and how SquadDash visualizes them
- **[Squad Team](squad-team.md)** — The `.squad/team.md` and `.squad/routing.md` model
- **[Transcripts](transcripts.md)** — Multi-transcript panel system and live streaming
- **[Documentation Panel](documentation-panel.md)** — How SquadDash renders docs from `docs/` folder

---

## The Squad Model

Squad is an AI team system where:
- Multiple specialized agents work on a codebase simultaneously
- Each agent has a defined role and charter
- Work is routed based on type (backend, frontend, tests, docs, etc.)
- Agents collaborate by handoff and delegation

SquadDash makes this model **visual** and **interactive**.

---

## Core Entities

| Entity | Description |
|---|---|
| **Agent** | A specialized AI with a charter (role + responsibilities) |
| **Transcript** | Conversation history between you and an agent |
| **Team** | Set of agents defined in `.squad/team.md` |
| **Routing** | Rules in `.squad/routing.md` for work assignment |
| **Workspace** | A folder (usually a Git repo) with a Squad team installed |

---

## Next

Start with **[Agents](agents.md)** to understand the fundamental building block.

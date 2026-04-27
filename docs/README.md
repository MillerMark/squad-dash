---
title: Home
nav_order: 1
permalink: /
---

# SquadDash

**A native Windows dashboard for managing AI coding agent teams powered by the Squad CLI.**

SquadDash transforms the Squad AI team system into a visual, interactive experience. Work with multiple specialized agents in parallel, see their activity in real-time, and manage your entire AI development team from one native WPF application.

Note: This documentation is largely AI generated at this point and has not been reviewed.

---

## What is SquadDash?

SquadDash is a desktop application for Windows that provides:

- **Visual agent management** — See all your agents at a glance with live status cards
- **Multi-agent transcripts** — Open multiple agent conversation panels simultaneously
- **Real-time activity tracking** — Watch tool calls, thinking blocks, and task progress as they happen
- **Team configuration** — Define your AI team in `.squad/team.md` with routing rules in `.squad/routing.md`
- **Integrated documentation** — Browse repo docs in a built-in panel with tree navigation and markdown rendering
- **Voice input** — Push-to-talk (double-Ctrl) for hands-free prompt entry using Azure Speech
- **Workspace-aware** — Automatically installs Squad CLI per-workspace, tracks installation state

---

## Why SquadDash?

The Squad CLI is powerful but command-line driven. SquadDash brings:

- **Discoverability** — Hover on agent cards to see glowing accent highlights; shift-click to open transcripts
- **Parallel workflows** — Launch multiple agents as background tasks and monitor them all at once
- **Context preservation** — Every agent conversation is persisted and resumable
- **Better debugging** — See every tool call with labeled icons (🔎 grep, ✏️ edit, 👀 view, 🤖 task)
- **Less context switching** — Docs, transcripts, and workspace all in one window

---

## Key Features

### Agent Cards with Live Status
Each agent appears as a card showing:
- Agent name and role
- Current status (idle, running, thinking)
- Accent color coding
- Hover-glow effect for visual feedback

### Multi-Transcript Panels
- Shift-click any agent card to open its transcript
- Multiple transcripts open side-by-side
- Live streaming as agents work
- Tool call history with labeled icons

### Documentation Panel
- Reads from `docs/` folder in your workspace
- Tree view on the left, rendered markdown on the right
- This very documentation displays inside SquadDash

### Voice Input (PTT)
- Double-tap **Ctrl** (and hold down) to activate push-to-talk. Release when finished speaking.
- Powered by Azure Cognitive Services Speech
- Hands-free prompt entry

### Routing & Team Management
- Define agents in `.squad/team.md`
- Configure routing rules in `.squad/routing.md`
- GitHub issue label routing (`squad:{member}`)

---

## Quick Links

📚 **[Getting Started](getting-started/README.md)** — Installation and first run  
✨ **[Features](features/README.md)** — View modes, fullscreen, font size  
🧠 **[Concepts](concepts/README.md)** — How agents, transcripts, and routing work  
📖 **[Reference](reference/README.md)** — Configuration, routing tables, keyboard shortcuts  
🛠️ **[Contributing](contributing/README.md)** — Add agents, write docs, extend SquadDash

---

## Stack

- **WPF** — .NET 10 Windows desktop application
- **C#** — All application logic and services
- **TypeScript** — SDK bridge in `Squad.SDK/`
- **Node.js** — Squad CLI runtime (installed per workspace)

---

## License

This project is under active development. See the repository for license details.

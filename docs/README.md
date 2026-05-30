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

- **Discoverability** — Hover over agent cards to see color-coded highlights that visually link each agent to its transcripts; open as many transcripts as you need, side by side, to compare activity across agents or runs
- **Parallel workflows** — Launch multiple agents as background tasks and monitor their progress simultaneously.
- **Context preservation** — Every agent conversation is persisted, searchable, and resumable across sessions
- **Better debugging** — See every tool call with labeled icons (🔎 grep, ✏️ edit, 👀 view, 🤖 task)
- **Less context switching** — Docs, transcripts, and workspace all in one window
- **Panels that keep you in control** — The **Tasks** panel surfaces your full project backlog; the **Loop** panel lets you iterate through a filtered set of tasks for a complex feature or project; **Approvals** collects every AI-generated commit so you can review each one at your own pace. **Notes** gives you a free-form markdown scratchpad. **Maintenance** provides powerful tools for keeping your codebase healthy — finding and removing duplication, spotting code smells, running architectural reviews, and eliminating security vulnerabilities. And **Inbox** gives your AI team a dedicated channel to deliver rich, markdown-formatted reports back to you, keeping those summaries safe and searchable even after transcripts have been cleared.
---

## Key Features

### Agent Cards with Live Status
Each agent appears as a card showing:
- Agent name and role
- Current status (idle, running, thinking)
- Accent color coding
- Hover-glow effect for visual feedback

### Multi-Transcript Viewer
- Click or Shift-click any agent card to open its transcript
- Multiple transcripts open side-by-side
- Live streaming thoughts and tool calls as agents work
- Tool call history with labeled icons and built-in diff viewer

### Documentation Panel
- Reads from `docs/` folder in your workspace
- Tree view on the left, rendered markdown on the right
- Write your repo's documentation as markdown files in a `docs/` folder, and this panel renders the markdown here as you work — the same content you're editing can be published directly to [GitHub Pages](https://pages.github.com/) (`https://username.github.io/repo`) so your repo's documentation lives both inside SquadDash and on the web, available to anyone

### Voice Input (PTT)
- Double-tap **Ctrl** (and hold down) to activate push-to-talk. Release when finished speaking.
- Powered by Azure Cognitive Services Speech or OpenAI Whisper
- Hands-free prompt entry

### Team Management
- Add new agents to the team with the integrated **Hire Agent** dialog.

### Command Line Access

Even inside SquadDash, you never lose access to the raw power of the Squad CLI. The workspace menu gives you instant access to all the management files that keep your team running — team definitions, routing rules, and agent charters — so you can inspect or update them without leaving the app. Right-click any agent card to pull up that agent's charter or conversation history directly. The workspace menu also lets you jump straight to the `.squad` folder, launch PowerShell, or open the Squad CLI itself with a single click. All the functionality you'd normally reach for on the command line is right there inside the environment, just a click away.

---

## Quick Links

📚 **[Getting Started](getting-started/README.md)** — Installation and first run

✨ **[Features](features/README.md)** — View modes, fullscreen, font size

🧠 **[Concepts](concepts/README.md)** — How agents, transcripts, and routing work

📖 **[Reference](reference/README.md)** — Configuration, routing tables, keyboard shortcuts

---

## Stack

- **WPF** — .NET 10 Windows desktop application
- **C#** — All application logic and services
- **TypeScript** — SDK bridge in `Squad.SDK/`
- **Node.js** — Squad CLI runtime (installed per workspace)

---

## License

This project is under active development. See the repository for license details.

# SquadDash

[![CI](https://github.com/MillerMark/squad-dash/actions/workflows/ci.yml/badge.svg)](https://github.com/MillerMark/squad-dash/actions/workflows/ci.yml)

A Windows WPF dashboard for managing AI coding agents powered by the [Squad CLI](https://www.npmjs.com/package/@bradygaster/squad-cli). SquadDash lets you open any workspace, install Squad into it, run agents, and interact with your AI team — all from a native desktop application.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **Windows** | WPF app — Windows only |
| **.NET 10 SDK** | `net10.0-windows` target |
| **Node.js LTS** | `node`, `npm`, and `npx` must be on `PATH` — required at runtime. Download from [nodejs.org](https://nodejs.org/). |

> **Why Node.js?** SquadDash shells out to `node`/`npm`/`npx` at runtime to install and run the Squad CLI inside each workspace. The Squad SDK bridge (`Squad.SDK/runPrompt.js`) also invokes `node` directly from `PATH` to execute prompts — so Node.js must be installed system-wide, not just bundled with a single tool. The app checks for all three on startup and shows an error if any are missing. **Node.js LTS (v20 or later) is recommended.**

---

## Getting Started

```bash
# 1. Clone the repo
git clone <repo-url>
cd SquadDash

# 2. Install root-level npm dependencies (Squad CLI dev tooling)
npm install

# 3. Build
dotnet build squad-dash.slnx

# 4. Run
dotnet run --project SquadDash
```

Or open `squad-dash.slnx` in **Visual Studio 2022+** and press **F5**.

---

## Project Structure

| Folder | Description |
|---|---|
| `SquadDash/` | Main WPF application (`net10.0-windows`). Contains all UI, services, and Squad CLI integration logic. Entry point: `App.xaml`. |
| `SquadDashLauncher/` | Thin launcher executable that manages hot-swappable runtime slots. Handles deploy-and-restart during Debug builds so the app can update itself without a full restart. |
| `Squad.SDK/` | TypeScript SDK project (`@bradygaster/squad-sdk`). Provides the Node.js-side bridge for interacting with Squad programmatically. |
| `SquadDash.Tests/` | NUnit unit tests for `SquadDash`. Run with `dotnet test`. |
| `VoiceHeuristics/` | Standalone .NET library implementing voice-to-text insertion heuristics used by the push-to-talk feature. |
| `.squad/` | Squad AI team configuration — agent charters, decisions log, routing rules, and universe definitions. See [.squad/team.md](.squad/team.md). |
| `R&D/` | Research and design assets (not shipped). |

---

## SquadDash Architecture

`MainWindow.xaml.cs` is the application coordinator. It owns all WPF state and delegates distinct responsibilities to focused helper classes. MainWindow is **31,382 lines**; over 200 helper and service classes account for the application's logic.

The code-behind pattern is deliberately preserved — no MVVM migration. Helper classes receive `Action<>`/`Func<>` delegates from MainWindow's constructor rather than holding copies of MainWindow's fields, which keeps state ownership unambiguous. See [`decisions.md`](.squad/decisions.md) for the rationale.

### Helper classes in `SquadDash/`

| File | Lines | Responsibility |
|---|---|---|
| `AgentStatusCard.cs` | 410 | `INotifyPropertyChanged` view-model for agent cards; accent colour palette; `SidebarEntry` types |
| `ColorUtilities.cs` | 86 | Static HSL/RGB math: `RgbToHsl`, `HslToRgb`, `HueToRgb`, `CreateAccentBrush`, `CreateDarkAccentBrush` |
| `SquadCliAdapter.cs` | 179 | OS/process interaction: CLI version resolution, PowerShell window launch, Explorer open, external links |
| `PushToTalkController.cs` | 289 | Double-Ctrl PTT state machine; Azure Speech Service lifecycle; voice hint visibility |
| `MarkdownDocumentRenderer.cs` | 1,183 | Markdown → WPF `Block`/`Inline` conversion: paragraphs, code fences, tables, quick-reply blocks |
| `AgentThreadRegistry.cs` | 1,076 | Agent thread lifecycle: creation, aliasing, identity normalisation, key lookup, background thread sync |
| `TranscriptConversationManager.cs` | 1,780 | Conversation persistence: load/save/persist, turn records, history navigation, emergency save |
| `BackgroundTaskPresenter.cs` | 1,291 | Background task tracking; completion detection; delayed-promotion pipeline; display label building |
| `PromptExecutionController.cs` | 2,468 | Prompt execution: `ExecutePromptAsync`, all slash-command handlers, prompt health monitoring, universe selection, quick-reply disabling |

### Thinking Block — Tool Call Icons

The transcript view shows a **Thinking** block for every tool call an agent makes. Each entry displays an icon and a short label derived from the tool's arguments. `BackgroundTaskPresenter` builds these labels.

| Tool | Icon | Label source | Example |
|---|---|---|---|
| `grep` | 🔎 | `path` arg (relative); falls back to `pattern` | 🔎`SquadDash\MainWindow.xaml` |
| `glob` | 🔎 | `pattern` arg | 🔎`**/*.cs` |
| `view` | 👀 | `path` arg (relative) | 👀`SquadDash\Foo.cs` |
| `edit` | ✏️ | `path` arg (relative) | ✏️`SquadDash\Foo.cs` |
| `create` | 📄 | `path` arg (relative) | 📄`SquadDash\Foo.cs` |
| `web_fetch` | 🌍 | URL with scheme stripped | 🌍`example.com/page` |
| `task` | 🤖 | `description` arg | 🤖`Fix agent card image scaling quality` |
| `skill` | ⚡ | `skill` arg | ⚡`Squad` |
| `store_memory` | 💾 | `subject` arg; falls back to `fact` | 💾`naming conventions` |
| `report_intent` | 🎯 | `intent` arg | 🎯`Fixing tool display text` |
| `sql` | 🗄️ | `description` arg | 🗄️`Insert auth todos` |
| `powershell` | 💻 | `description` arg; falls back to `command` | 💻`Build SquadDash to verify zero errors` |

Paths are made relative to the open workspace root before display so labels stay short regardless of where the workspace lives on disk.

---

## Running Tests

```bash
dotnet test squad-dash.slnx
```

Tests use NUnit 4.4+. Test files live in `SquadDash.Tests/`.

---

## How Squad Installation Works

When you point SquadDash at a workspace folder, it:

1. Verifies `node`, `npm`, and `npx` are on `PATH`
2. Ensures a `package.json` exists in the workspace (creates one if not)
3. Runs `npm install` to install the Squad CLI locally
4. Applies Windows compatibility fixes to the installed CLI
5. Runs `squad init` if the workspace hasn't been initialized yet

Installation state is tracked by the presence of `.squad/team.md`, `package.json`, and `node_modules/.bin/squad.cmd` in the workspace.

---

## AI Team

This repo is developed with a Squad AI team. Each agent has a defined role and charter:

| Agent | Role |
|---|---|
| Orion Vale | Lead Architect |
| Lyra Morn | WPF & UI Specialist |
| Arjun Sen | C# Backend Services Specialist |
| Talia Rune | TypeScript & SDK Bridge Specialist |
| Jae Min Kade | Deployment & Infrastructure Specialist |
| Vesper Knox | Testing & Quality Specialist |
| Mira Quill | Documentation & Memory Specialist |
| Sorin Pyre | Performance Engineer |
| Atlas Wren | Mac & Cross-Platform Specialist |
| Scribe | Session Logger |
| Ralph | Work Monitor |
| Argus Weld | Continuous Improvement |
| Malik Graves | Markdown Specialist |

Full team details: [.squad/team.md](.squad/team.md)

---

## Development Notes

- **Debug builds** automatically use run-slot deployment via `SquadDashLauncher` — the app hot-swaps itself on rebuild without losing open workspaces.
- **Speech features** require a valid Azure Cognitive Services Speech key (configured in app preferences).
- **Windows compatibility patching** (`SquadRuntimeCompatibility`) is applied automatically after Squad CLI install — no manual steps needed.
- Architectural decisions are tracked in [.squad/decisions.md](.squad/decisions.md).

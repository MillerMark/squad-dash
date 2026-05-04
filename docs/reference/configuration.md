---
title: Configuration
nav_order: 1
parent: Reference
---

# Configuration

How to configure Squad workspaces and SquadDash application settings.

---

## Squad Workspace Configuration

Each workspace has a `.squad/config.json` file:

```json
{
  "version": 1
}
```

Currently, this file is minimal. Future versions may add:
- Model preferences
- API endpoint overrides
- Logging levels

---

## SquadDash Application Settings

SquadDash stores its own settings in:

```
%APPDATA%\SquadDash\ApplicationSettings.json
```

### Example

```json
{
  "AzureSpeechKey": "your-key-here",
  "AzureSpeechRegion": "eastus",
  "Theme": "dark",
  "LastWorkspacePath": "D:\\Projects\\MyRepo"
}
```

### Fields

| Field | Description |
|---|---|
| `AzureSpeechKey` | Azure Cognitive Services Speech API key (for PTT) |
| `AzureSpeechRegion` | Azure region (e.g., `eastus`, `westus`) |
| `Theme` | UI theme (`dark` or `light`) |
| `LastWorkspacePath` | Most recently opened workspace |

---

## Environment Variables

SquadDash does not require environment variables, but respects:

| Variable | Purpose |
|---|---|
| `PATH` | Must include `node`, `npm`, and `npx` |

---

## Workspace Paths

SquadDash uses an `IWorkspacePaths` interface to locate key folders:

| Property | Path |
|---|---|
| `WorkspaceRoot` | Root folder of the workspace |
| `SquadFolder` | `.squad/` directory |
| `AgentsFolder` | `.squad/agents/` directory |
| `SessionsFolder` | `.squad/sessions/` directory |

These are auto-discovered when you open a workspace.

---

## Node.js and Squad CLI Installation

When you open a workspace, SquadDash:
1. Checks if `package.json` exists (creates one if not)
2. Runs `npm install` to install Squad CLI locally
3. Applies Windows compatibility fixes
4. Verifies `.squad/team.md` exists (runs `squad init` if not)

Installation state is tracked by:
- Presence of `node_modules/.bin/squad.cmd`
- Presence of `.squad/team.md`

---

## Azure Speech Configuration

To enable push-to-talk (PTT):
1. Open **Preferences** from the top menu
2. Enter your Azure Cognitive Services Speech key and region
3. Save

![Screenshot: Preferences dialog showing speech configuration](images/preferences-dialog-speech-config.png)
> 📸 *Screenshot needed: The Preferences dialog — show the Azure Speech Key and Region input fields.*

SquadDash validates the key on save. Invalid keys show an error.

---

## Debug vs. Release Builds

### Debug

- Uses `SquadDashLauncher` for hot-swappable runtime slots
- Automatically deploys to `Run\SquadDash-A\` or `Run\SquadDash-B\`
- Allows in-place updates without losing open workspaces

### Release

- Runs directly without launcher
- No slot deployment

---

## Next

- **[Routing](routing.md)** — How routing tables work
- **[Keyboard Shortcuts](keyboard-shortcuts.md)** — Hotkeys and shortcuts

# First Run

What happens when you launch SquadUI for the first time, and how to connect to a workspace.

---

## Launching SquadUI

Run:

```bash
dotnet run --project SquadDash
```

Or from Visual Studio: **F5**.

---

## First Launch Flow

### 1. Node.js Check

SquadUI verifies that `node`, `npm`, and `npx` are accessible on your `PATH`. If any are missing, you'll see an error dialog.

**Fix:** Install Node.js LTS and restart SquadUI.

---

### 2. Workspace Selection

You'll be prompted to select a workspace folder. This is any directory where you want Squad to run — typically a Git repository.

**What SquadUI Does:**
1. Checks if `package.json` exists (creates one if not)
2. Installs the Squad CLI locally via `npm install`
3. Applies Windows compatibility fixes to the installed CLI
4. Runs `squad init` if `.squad/team.md` doesn't exist

---

### 3. Squad Initialization

If the workspace is not yet Squad-enabled, SquadUI runs:

```bash
squad init
```

This creates:
- `.squad/team.md` — Your AI team roster
- `.squad/routing.md` — Routing rules for work assignment
- `.squad/config.json` — Squad configuration

---

### 4. Main Window Appears

You'll see:
- **Agent cards** — One card per agent defined in `.squad/team.md`
- **Prompt input** — Type or use voice (double-Ctrl)
- **Top menu** — Workspace, Preferences, Trace

![Screenshot: SquadUI main window showing agent cards](images/main-window-agent-cards.png)
> 📸 *Screenshot needed: The full SquadUI main window — show all agent cards, the prompt input bar at the bottom, and the top menu.*

---

## Setting Up Your First Team

Edit `.squad/team.md` to define your agents. Example:

```markdown
# Squad Team

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Alice | Backend Specialist | agents/alice/charter.md | active |
| Bob | Frontend Specialist | agents/bob/charter.md | active |
```

Create charter files in `.squad/agents/{name}/charter.md`:

```markdown
# Alice — Backend Specialist

You are Alice, the backend specialist. You handle API design, database schema, and server logic.
```

---

## Routing Configuration

Edit `.squad/routing.md` to define who handles what:

```markdown
# Work Routing

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| API endpoints | Alice | REST routes, GraphQL resolvers |
| React components | Bob | UI components, styling |
```

See **[Routing](../reference/routing.md)** for full syntax.

---

## Opening Agent Transcripts

- **Shift-click** any agent card to open its transcript panel
- Multiple transcripts can be open simultaneously
- Live streaming shows tool calls and responses in real-time

![Screenshot: Transcript panel open alongside agent cards](images/transcript-panel-open.png)
> 📸 *Screenshot needed: Main window with one (or more) transcript panels open — show the transcript panel layout, tool call icons, and streaming response.*

---

## Voice Input (Optional)

To enable push-to-talk:

1. Open **Preferences** from the top menu
2. Enter your **Azure Cognitive Services Speech** key and region
3. Press **double-Ctrl** to activate voice input

![Screenshot: Preferences dialog with Azure Speech fields](images/preferences-dialog-speech.png)
> 📸 *Screenshot needed: The Preferences dialog — show the Azure Speech Key and Region fields.*

---

## Next Steps

- **[Agents](../concepts/agents.md)** — How agents work in Squad
- **[Squad Team](../concepts/squad-team.md)** — Team and routing model
- **[Documentation Panel](../concepts/documentation-panel.md)** — Browse docs inside SquadUI

# Squad Team

How the `.squad/team.md` and `.squad/routing.md` files define your AI team and work assignment.

---

## team.md — Team Roster

The **team.md** file defines all agents on your Squad team.

### Example

```markdown
# Squad Team

> ProjectName

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Lyra Morn | WPF & UI Specialist | agents/lyra-morn/charter.md | active |
| Arjun Sen | Backend Services Specialist | agents/arjun-sen/charter.md | active |
| Vesper Knox | Testing & Quality Specialist | agents/vesper-knox/charter.md | active |
| Mira Quill | Documentation Specialist | agents/mira-quill/charter.md | active |
```

### Format

- **Name** — Agent's name (unique identifier)
- **Role** — Short description (shown on agent card)
- **Charter** — Path to charter.md file (relative to `.squad/`)
- **Status** — `active`, `inactive`, or special markers like `📋 Silent`, `🔄 Monitor`

---

## routing.md — Work Routing

The **routing.md** file defines who handles what type of work.

### Example

```markdown
# Work Routing

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| WPF/XAML UI | Lyra Morn | MainWindow, dialogs, data binding |
| Backend services | Arjun Sen | Stores, SquadSdkProcess, persistence |
| Testing | Vesper Knox | NUnit tests, coverage, quality |
| Documentation | Mira Quill | README, decisions.md, session logs |
```

### How SquadDash Uses Routing

When you send a prompt:
1. SquadDash reads `.squad/routing.md`
2. Determines which agent(s) should handle the request
3. Routes the prompt to the appropriate agent(s)

You can also manually open any agent's transcript and send a direct prompt.

---

## Issue Label Routing

SquadDash supports GitHub issue routing:

```markdown
## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage and assign `squad:{member}` | Lead Architect |
| `squad:{name}` | Pick up and complete | Named member |
```

When a GitHub issue gets a `squad:{member}` label, that agent can pick it up automatically.

---

## How SquadDash Reads team.md

SquadDash parses the **Members** table:
1. Extracts each row
2. Loads the charter file from `.squad/agents/{name}/charter.md`
3. Creates an agent card with the name and role
4. Uses the status field to determine if the agent is active

---

## Special Agent Types

Some agents have special behavior:

| Status Marker | Behavior |
|---|---|
| `active` | Standard agent — shows in roster |
| `📋 Silent` | Scribe agents — run in background, don't show transcript |
| `🔄 Monitor` | Observer agents — watch for events |

---

## Example: SquadDash's Own Team

SquadDash is built by a Squad team. See `.squad/team.md` in the SquadDash repo for a real-world example:

- **Lyra Morn** — WPF & UI
- **Arjun Sen** — Backend services
- **Talia Rune** — TypeScript SDK
- **Jae Min Kade** — Deployment & launcher
- **Vesper Knox** — Testing
- **Mira Quill** — Documentation
- **Orion Vale** — Lead architect
- **Sorin Pyre** — Performance

Each agent handles a distinct domain, reducing conflicts and enabling parallel work.

---

## Next

- **[Routing](../reference/routing.md)** — Full routing syntax reference

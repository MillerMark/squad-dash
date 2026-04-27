# Routing

How `.squad/routing.md` defines work assignment rules and GitHub issue routing.

---

## Routing Table Format

The **Routing Table** is a markdown table in `.squad/routing.md`:

```markdown
# Work Routing

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| WPF/XAML UI | Lyra Morn | MainWindow, dialogs, data binding |
| Backend services | Arjun Sen | Stores, persistence, SDK process |
| Testing | Vesper Knox | NUnit tests, coverage |
| Documentation | Mira Quill | README, decisions.md |
```

### Columns

| Column | Description |
|---|---|
| **Work Type** | Category of work (e.g., "UI", "Backend", "Testing") |
| **Route To** | Agent name who handles this work |
| **Examples** | Clarifying examples of what falls into this category |

---

## How Routing Works

When you send a prompt:
1. SquadDash (or the Squad Coordinator agent) reads `.squad/routing.md`
2. Analyzes the prompt to determine work type
3. Routes to the agent listed in the **Route To** column

You can also:
- Manually open an agent's transcript and send a direct prompt
- Use quick-reply buttons to route to a specific agent

---

## Issue Label Routing

SquadDash supports GitHub issue routing:

```markdown
## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze and assign `squad:{member}` | Lead Architect |
| `squad:{name}` | Pick up and complete | Named member |
```

### How It Works

1. A GitHub issue gets the `squad` label
2. The **Lead** (e.g., Orion Vale) triages it:
   - Analyzes the issue
   - Assigns a `squad:{member}` label (e.g., `squad:lyra-morn`)
   - Comments with triage notes
3. The named member picks up the issue in their next session

### Example Labels

- `squad` — Inbox (untriaged)
- `squad:lyra-morn` — Assigned to Lyra Morn
- `squad:vesper-knox` — Assigned to Vesper Knox

---

## Routing Rules

The **Rules** section in `routing.md` can define policies:

```markdown
## Rules

1. **Eager by default** — spawn all agents who could start work
2. **Scribe always runs** after substantial work (background mode)
3. **Quick facts → coordinator answers** — don't spawn for trivial questions
4. **Two agents could handle it?** Pick the one whose domain is primary
5. **"Team, ..." → fan-out** — spawn all relevant agents in parallel
6. **Anticipate downstream** — if building a feature, spawn tester simultaneously
7. **Issue-labeled work** — route to the member with the `squad:{member}` label
```

These are guidelines for the Squad Coordinator.

---

## Example: SquadDash Routing

See `.squad/routing.md` in the SquadDash repo for a full example:

- **WPF/XAML UI** → Lyra Morn
- **C# backend services** → Arjun Sen
- **TypeScript/SDK** → Talia Rune
- **Deployment** → Jae Min Kade
- **Testing** → Vesper Knox
- **Documentation** → Mira Quill
- **Performance** → Sorin Pyre
- **Architecture** → Orion Vale

---

## Editing routing.md

1. Open `.squad/routing.md` in your workspace
2. Edit the table to add/remove/update routes
3. Save — SquadDash reloads routing rules automatically

---

## Next

- **[Squad Team](../concepts/squad-team.md)** — How team.md and routing.md work together

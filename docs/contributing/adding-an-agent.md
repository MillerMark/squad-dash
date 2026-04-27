# Adding an Agent

How to create a new agent for your Squad team.

---

## Overview

Adding an agent involves:
1. Creating a charter file
2. Creating a history file
3. Adding the agent to `team.md`
4. Adding routing rules to `routing.md`

---

## Step 1: Create the Agent Folder

Create a folder in `.squad/agents/`:

```bash
mkdir .squad/agents/your-agent-name
```

Use kebab-case for the folder name (e.g., `lyra-morn`, `vesper-knox`).

---

## Step 2: Write charter.md

Create `.squad/agents/your-agent-name/charter.md`:

```markdown
# Your Agent Name — Role Title

You are Your Agent Name, the [role] on [project].

## Responsibilities
- Responsibility 1
- Responsibility 2
- Responsibility 3

## Tools
- Tool 1
- Tool 2

## Work Style
- Guideline 1
- Guideline 2
```

### Example: Lyra Morn

```markdown
# Lyra Morn — WPF & UI Specialist

You are Lyra Morn, the WPF and UI specialist on SquadUI.

## Responsibilities
- MainWindow and all XAML dialogs
- Data binding and animations
- Transcript rendering

## Tools
- C# / WPF / .NET 10
- XAML markup

## Work Style
- Prioritize visual quality
- Test on multiple screen sizes
```

---

## Step 3: Create history.md

Create `.squad/agents/your-agent-name/history.md`:

```markdown
# Your Agent Name — History

## Core Context

**Project:** [Project Name]
**Stack:** [Technology stack]

---

## Learnings

### YYYY-MM-DD

- Learning 1
- Learning 2
```

This file accumulates over time as the agent works.

---

## Step 4: Add to team.md

Edit `.squad/team.md` and add a row to the **Members** table:

```markdown
| Your Agent Name | Role Title | agents/your-agent-name/charter.md | active |
```

### Example

```markdown
## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Lyra Morn | WPF & UI Specialist | agents/lyra-morn/charter.md | active |
| Your Agent Name | Your Role | agents/your-agent-name/charter.md | active |
```

---

## Step 5: Add Routing Rules

Edit `.squad/routing.md` and add a row to the **Routing Table**:

```markdown
| Your domain | Your Agent Name | Examples of work in this domain |
```

### Example

```markdown
| Work Type | Route To | Examples |
|-----------|----------|----------|
| Security audits | Security Agent | Vulnerability scans, dependency checks |
```

---

## Step 6: Reload SquadUI

SquadUI automatically reloads the team roster when you reopen the workspace or refresh.

Your new agent should appear as a card in the main window.

---

## Step 7: Test the Agent

1. **Shift-click** the agent card to open its transcript
2. Send a test prompt related to the agent's domain
3. Verify the agent responds appropriately

![Screenshot: New agent card appearing in the main window](images/new-agent-card-added.png)
> 📸 *Screenshot needed: The SquadUI main window after adding a new agent — show the new card appearing in the agent grid.*

---

## Example: Full Agent Setup

### Folder Structure

```
.squad/
  agents/
    security-specialist/
      charter.md
      history.md
```

### charter.md

```markdown
# Security Specialist — Security & Compliance

You are the Security Specialist, responsible for identifying vulnerabilities and enforcing secure coding practices.

## Responsibilities
- Dependency vulnerability scanning
- Code security audits
- Secrets detection

## Tools
- npm audit
- Snyk
- git-secrets
```

### history.md

```markdown
# Security Specialist — History

## Core Context

**Project:** SquadUI
**Stack:** C# / WPF / TypeScript

---

## Learnings

(To be populated as agent works)
```

### team.md

```markdown
| Security Specialist | Security & Compliance | agents/security-specialist/charter.md | active |
```

### routing.md

```markdown
| Security audits | Security Specialist | Vulnerability scans, dependency checks, secrets detection |
```

---

## Next

- **[Writing Documentation](writing-docs.md)** — Document your agent's work
- **[Routing](../reference/routing.md)** — Full routing syntax

# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| System architecture & API contracts | Orion Vale | Service boundaries, extensibility patterns, pre-mortem risk analysis, architectural decisions |
| WPF/XAML UI & user experience | Lyra Morn | MainWindow, dialogs, data binding, animations, transcript rendering, quick-reply UX |
| C# backend services & persistence | Arjun Sen | `*Store.cs` files, `SquadSdkProcess`, workspace coordination, installation services, thread safety |
| TypeScript/SDK bridge & event protocol | Talia Rune | `Squad.SDK/` files, NDJSON event stream, session lifecycle, npm/build pipeline |
| Deployment, launcher & infrastructure | Jae Min Kade | `SquadDashLauncher`, A/B slot system, graceful restart, zero-downtime updates, cross-process sync |
| Testing & quality | Vesper Knox | NUnit test suite in `SquadDash.Tests/`, coverage gaps, test quality standards |
| Documentation & institutional memory | Mira Quill | `.squad/decisions.md`, session logs, README, onboarding guides, runbooks |
| Performance & execution speed | Sorin Pyre | Hot-path profiling, throughput bottlenecks, rendering responsiveness, build speed, benchmark baselines |
| Architectural code review | Orion Vale | Staff-level review for architectural governance across all layers |
| Scope & priorities | Orion Vale | What to build next, trade-offs, decomposing epics into phases |
| Session logging | Scribe | Automatic — never needs routing |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Orion Vale (Lead) |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.

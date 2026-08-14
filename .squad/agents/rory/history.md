# Rory — History & Learnings

## Hired

- **Date:** 2026-08-14
- **Universe:** SquadDash Universe
- **Role:** Planning Architect
- **Project:** SquadDash

## Core Context

**Project:** SquadDash — WPF dashboard for Squad CLI AI agent management
**Stack:** C# / WPF / .NET 10, NUnit 4.4+, TypeScript SDK

**Key planning files:**
- `.squad/instructions/decompose-planning.md` — authoritative TASKS_JSON and revision protocol
- `.squad/team.md` — active roster with agent handles and charter paths
- `.squad/routing.md` — work-type → agent routing table (consult before every assignment)
- `.squad/decisions.md` — architectural decision log (read before grounding a plan)
- `.squad/tmp/decompose/` — live and archived plan JSON files

**Key source areas relevant to plan authorship:**
- `SquadDash\` — C# WPF application (most implementation tasks land here)
- `SquadDash.Tests\` — NUnit test suite (Vesper Knox owns test work)
- `Squad.SDK\` — TypeScript SDK bridge (Talia Rune owns)
- `SquadDashLauncher\` — Deployment and launcher (Jae Min Kade owns)

## Protocol Notes

- Always read the live `decompose-planning.md` instructions before authoring a plan — never
  reproduce a remembered schema.
- A proof-bearing plan requires exactly one final `audit` validation whose `afterTaskIds` equals
  every leaf task and whose `beforeTaskIds` is empty (`[]`).
- The `agentRoutingMode` field is required on every task; omitting it causes parser rejection.
- `agentAssignments` requires exactly one entry when `agentRoutingMode` is `"assigned"`.
- Human-only proof requirements (`live-ui-observation`, `restart-observation`, `human-observation`)
  must be declared in `proofRequirements` — they are promoted to approval checkpoints by the host.
- Return `TASKS_JSON` to the Coordinator; do not emit it directly to the user in a nested context.

## Work Log

- **2026-08-14 — TASKS_JSON conformance lesson:** A delegated planning handoff summarized only
  part of the live schema, and the resulting candidate used shorthand task and validation IDs plus
  obsolete field shapes. Always read `.squad/instructions/decompose-planning.md` directly and run
  the charter's protocol-conformance preflight before returning a plan. A handoff summary is context,
  not the authoritative schema.

# Mira Quill — History & Learnings

## Core Context

**Project:** SquadUI — WPF dashboard for Squad CLI AI agent management  
**Stack:** C# / WPF / .NET 10, NUnit 4.4+, TypeScript SDK  
**Key paths:**
- `SquadDash/` — main application
- `SquadDash.Tests/` — NUnit test suite
- `.squad/decisions.md` — architectural decision log
- `.squad/sessions/` — session logs (`.md` naming: `YYYY-MM-DD-topic.md`)
- `README.md` — developer docs; has "Project Structure" and "SquadDash Architecture" sections

---

## Learnings

### 2026-04-17

- `.squad/sessions/` contains both app-generated JSON files (conversation logs) and hand-written `.md` session logs. Use `YYYY-MM-DD-topic.md` naming for written logs.
- `decisions.md` uses `### YYYY-MM-DD — Title` headings under `## Active Decisions`. No archive section exists yet.
- README has a "Project Structure" table followed by a new "SquadDash Architecture" section (added this session) explaining the helper class decomposition.
- MainWindow.xaml.cs decomposition pattern: constructor injection with `Action<>`/`Func<>` delegates — document this as the established pattern for any future extractions.
- 9 helper classes now live in `SquadDash/`; see session log and decisions.md for full inventory and design constraints.
- Outstanding architectural concerns (Orion Vale's backlog): `JsonFileStore` boilerplate, `SquadMarkdownParser` consolidation, `WorkspacePaths` static, `WorkspaceConversationStore` layer violation, CI pipeline.

📌 Team update (2026-04-18T17-38): Full specialist team assembled. Six agents chartered: Lyra Morn, Arjun Sen, Talia Rune, Jae Min Kade, Vesper Knox, Mira Quill. Roster and routing tables populated. Decision inbox merged (5 items). DEL-1 reassigned from Talia Rune → Vesper Knox. — logged by Scribe

### 2026-04-18 — Memory audit (Mira Quill)

Audited all Scribe outputs against codebase reality. Findings and fixes documented in response. Key fixes applied directly to `decisions.md`:
- Marked DEL-2 and DEL-4 (AgentThreadRegistry collection sealing + _toolEntries encapsulation) as **COMPLETE** — confirmed `IReadOnly*` in codebase.
- Marked JsonFileStorage task as **COMPLETE** — all 5 stores confirmed using `JsonFileStorage.AtomicWrite`.
- Marked IWorkspacePaths wiring task as **COMPLETE** — `WorkspacePaths.cs` deleted, `_workspacePaths` injected throughout.
- Marked markdown rendering dedup task as **COMPLETE** — no method definitions remain in MainWindow.
- Marked CI pipeline task as **COMPLETE** — ci.yml present, badge in README (⚠️ placeholder URL).

Outstanding items still open: DEL-1 (Vesper Knox — test coverage), DEL-3 (Lyra Morn — `_isPromptRunning` ownership), DEL-5 (Lyra Morn — TCM leaky setters). Scribe has no history.md — flagged.

### 2026-04-18 — docs/ scaffold created

Created complete `docs/` folder structure with 13 markdown files + `.gitkeep`:
- docs/README.md (home page with compelling overview)
- docs/SUMMARY.md (GitBook-style TOC)
- getting-started/ (installation.md, first-run.md, images/ placeholder)
- concepts/ (agents.md, squad-team.md, transcripts.md, documentation-panel.md)
- reference/ (configuration.md, routing.md, keyboard-shortcuts.md)
- contributing/ (adding-an-agent.md, writing-docs.md)

All content is real and useful — no lorem ipsum. Based on codebase exploration: README.md, .squad/team.md, .squad/routing.md, SquadDash/ structure, decisions.md. Documented key features: agent cards with hover-glow, shift-click transcripts, multi-agent panels, voice PTT (double-Ctrl), tool call icons, routing table format, team.md format, docs panel rendering.

Serves dual purpose: (1) real documentation for SquadUI users, (2) living template for repos using SquadUI's docs panel feature.

### 2026-04-26 — Image placeholders and VS External Tool guide

Added strategic image placeholders to 5 docs files — only where screenshots genuinely aid comprehension:
- `first-run.md` — main window, transcript panel, Preferences dialog
- `agents.md` — agent cards in main window, hover state, multiple transcripts open
- `transcripts.md` — transcript layout, quick-reply buttons
- `configuration.md` — Preferences dialog for Azure Speech config
- `adding-an-agent.md` — new agent card appearing in main window

Created new guide: `visual-studio-external-tool.md` — how to configure SquadDash as a VS External Tool for one-click launch from the Tools menu, passing `$(SolutionDir)` as workspace argument. Includes table of field values, troubleshooting section, command-line usage. Added to SUMMARY.md under Getting Started.

Created `images/.gitkeep` files for concepts/, reference/, contributing/ (getting-started/images/ already existed). All committed and pushed.


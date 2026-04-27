# Jae Min Kade — History & Learnings

## Core Context

**Project:** SquadUI — WPF dashboard for Squad CLI AI agent management  
**Stack:** C# / WPF / .NET 10, NUnit 4.4+, TypeScript SDK  
**Key paths:**
- `SquadDashLauncher/` — launcher entry point
- `.github/workflows/` — CI configuration (to be created)
- `.squad/decisions.md` — architectural decision log

---

## Learnings

📌 Team update (2026-04-18T17-38): Jae Min Kade owns two delegated tasks from Orion Vale's audit — decided by Orion Vale

**Task 1 — CI pipeline (GitHub Actions):**  
Create `.github/workflows/ci.yml` — build and test on push to `main` and PRs to `main`. Use `windows-latest` (WPF requires Windows), .NET 10, Node 20 (for `Squad.SDK` esproj build). Steps: checkout → setup-dotnet → setup-node (with npm cache on `Squad.SDK/package-lock.json`) → `dotnet restore` → `dotnet build --no-incremental --no-restore` → `dotnet test SquadDash.Tests/SquadDash.Tests.csproj --no-build`. Acceptance: valid YAML, passes on test PR, badge in README, no secrets required.

**Task 2 — Wire IWorkspacePaths (launcher):**  
Replace all `WorkspacePaths.*` static calls in `SquadDashLauncher/Program.cs` with constructor-injected `IWorkspacePaths`. Coordinate with Lyra Morn (UI files) and Arjun Sen (backend files). Delete `WorkspacePaths.cs` only after all call sites across all three layers are migrated.

---

## 2026-04-18 — Fix CI badge placeholder in README.md

**Task:** Replace `{owner}/{repo}` placeholder in the CI badge URL with real GitHub coordinates (flagged by Mira Quill's memory audit).

**Remote URL found:** `https://github.com/MillerMark/SquadUI.git`  
→ owner: `MillerMark`, repo: `SquadUI`  
(Determined by reading `.git/config` directly — `git` binary was not on PATH in the PowerShell environment.)

**Change made in `README.md` line 3:**
- Before: `[![CI](https://github.com/{owner}/{repo}/actions/workflows/ci.yml/badge.svg)](https://github.com/{owner}/{repo}/actions/workflows/ci.yml)`
- After: `[![CI](https://github.com/MillerMark/SquadUI/actions/workflows/ci.yml/badge.svg)](https://github.com/MillerMark/SquadUI/actions/workflows/ci.yml)`

No commit made — left for Scribe.

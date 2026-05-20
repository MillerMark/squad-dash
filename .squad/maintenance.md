---
configured: true
idle_timeout: 15
max_tasks_per_session: 5
safety: branch
---

<!--
================================================================================
  SQUADASH MAINTENANCE TASK FILE
================================================================================

This file defines autonomous maintenance tasks that run during idle windows.
Enable individual tasks by setting `enabled: true` under each task heading.
All tasks ship disabled — opt in to exactly what you want.

────────────────────────────────────────────────────────────────────────────────
FRONTMATTER KEYS
────────────────────────────────────────────────────────────────────────────────

  configured: true          Required. Must be present for SquadDash to load
                            this file.

  idle_timeout: 15          Minutes of inactivity before a maintenance window
                            triggers. Default: 15.

  max_tasks_per_session: 5  Maximum number of tasks to run per maintenance
                            window. Tasks are run in order; the session stops
                            after this many complete. Default: 5.

  safety: branch            Global safety floor. Per-task safety cannot be
                            less safe than this value. See the Safety Model
                            section below.

────────────────────────────────────────────────────────────────────────────────
SAFETY MODEL
────────────────────────────────────────────────────────────────────────────────

  report-only   No file changes. The AI generates a report or analysis only.
                All findings are written to the maintenance transcript; no
                source files are created, modified, or deleted.

  branch        Before making any edits, the AI creates a new branch named
                  maintenance/YYYYMMDD-<task-slug>
                (e.g. maintenance/20260520-fix-failing-tests).
                All commits go to that branch. The current branch is never
                touched. This is the recommended default.

  direct        The AI may commit directly to the current branch. This is an
                explicit opt-in for tasks that are safe by definition (e.g.
                writing only to tasks.md). Use with caution.

  Floor rule:   The global `safety:` value is a floor. A per-task setting
                cannot be less safe than the global value. If global is
                `branch`, a task set to `direct` will be silently promoted to
                `branch`. To allow `direct` on a task, you must also set the
                global safety to `direct`.

────────────────────────────────────────────────────────────────────────────────
FREQUENCY VALUES
────────────────────────────────────────────────────────────────────────────────

  daily         The task runs at most once per calendar day. Subsequent
                maintenance windows on the same day skip it.

  per-commit    The task runs once per unique HEAD commit SHA. It re-runs
                after new commits land; it is skipped if HEAD hasn't changed
                since the last run.

  always        The task runs every maintenance window, regardless of when
                it last ran.

────────────────────────────────────────────────────────────────────────────────
TASK BLOCK FORMAT
────────────────────────────────────────────────────────────────────────────────

Each task is a level-2 heading whose slug matches the heading text:

  ## task-slug

  enabled: false            Set to true to enable this task.
  frequency: daily          How often to run. See Frequency Values above.
  safety: branch            Per-task safety. Cannot be less safe than global.
  title: Human Readable Title
  instructions: |
    Multi-line instructions injected into the AI prompt when this task runs.
    Reference option values with {{option-key}} template syntax.

  options:
    option_key:
      type: radio
      label: Label shown to user (and readable by AI)
      default: choice-value
      choices: choice-a | choice-b | choice-c
        choice-a: Description of this choice
        choice-b: Description of this choice
        choice-c: Description of this choice

  The options block follows the same format as loop.md options. Radio options
  present a set of mutually-exclusive choices; the selected value is injected
  into the instructions via {{option_key}}.

================================================================================
-->

# Maintenance Tasks

---

## run-tests

enabled: false
frequency: daily
safety: branch
title: Run Tests
instructions: |
  Run all tests in the repository. Use the appropriate test runner for this
  project (e.g. `dotnet test`, `npm test`, `go test ./...`).

  If failing tests are found, take action according to {{if_failing}}:
  - If "fix": Diagnose each failing test. Fix the root cause in source (not by
    deleting tests or weakening assertions). Commit all fixes to the branch.
  - If "report": Do not change any code. Write a summary of every failing test,
    the error message, and your diagnosis of the likely cause.

options:
  if_failing:
    type: radio
    label: If failing tests are found
    default: report
    choices: fix | report
      fix: Fix failing tests and commit fixes to a new branch
      report: Report failures only — do not change any code

---

## eliminate-duplication

enabled: false
frequency: daily
safety: branch
title: Eliminate Code Duplication
instructions: |
  Scan the codebase for duplicated logic — identical or near-identical code
  blocks, copy-pasted utility functions, repeated patterns that should be
  extracted. Focus on meaningful duplication (not trivial one-liners).

  Take action according to {{if_found}}:
  - If "fix": Refactor inline on the current branch. Extract shared logic,
    update all call sites, ensure tests still pass.
  - If "branch": Create a maintenance branch and refactor there.
  - If "report": Do not change any code. List each duplication instance with
    file paths, line ranges, and a brief description of the shared logic.

options:
  if_found:
    type: radio
    label: If duplication is found
    default: report
    choices: fix | branch | report
      fix: Refactor inline on the current branch
      branch: Create a new branch and refactor there
      report: Report duplications only — do not change any code

---

## architectural-practices

enabled: false
frequency: daily
safety: report-only
title: Architectural Practice Review
instructions: |
  Review the codebase for larger architectural problems: poor separation of
  concerns, leaky abstractions, god objects, circular dependencies, missing
  service boundaries, wrong layer responsibilities, etc.

  Take action according to {{if_found}}:
  - If "branch": Create a maintenance branch. Implement the improvements you
    identify. Document your reasoning in commit messages.
  - If "report": Do not change any code. Write a structured report of each
    finding: problem description, affected files/layers, and a recommended fix.

options:
  if_found:
    type: radio
    label: If architectural issues are found
    default: report
    choices: branch | report
      branch: Create a new branch and implement architectural improvements
      report: Report findings only — do not change any code

---

## code-smells

enabled: false
frequency: daily
safety: branch
title: Code Smell Cleanup
instructions: |
  Scan the codebase for code smells: poor readability, long methods, unclear
  naming, overly complex conditionals, dead code, unnecessary abstraction,
  inefficient patterns, missing null checks, etc.

  Take action according to {{if_found}}:
  - If "fix": Address smells inline on the current branch.
  - If "branch": Create a maintenance branch and address smells there.
  - If "report": Do not change any code. List each smell with file path, line
    number, category, and a brief description of the issue and suggested fix.

options:
  if_found:
    type: radio
    label: If code smells are found
    default: report
    choices: fix | branch | report
      fix: Fix smells inline on the current branch
      branch: Create a new branch and fix smells there
      report: Report smells only — do not change any code

---

## speed-improvements

enabled: false
frequency: daily
safety: branch
title: Performance Improvements
instructions: |
  Review the codebase for performance opportunities: inefficient algorithms,
  unnecessary allocations, repeated expensive operations, missing caching,
  synchronous I/O where async would improve throughput, LINQ queries that
  could be rewritten, N+1 query patterns, etc.

  Take action according to {{if_found}}:
  - If "fix": Implement optimisations inline on the current branch. Add a brief
    comment explaining the change where the improvement is non-obvious.
  - If "branch": Create a maintenance branch and implement improvements there.
  - If "report": Do not change any code. Describe each opportunity, its likely
    impact, and the recommended approach.

options:
  if_found:
    type: radio
    label: If performance opportunities are found
    default: report
    choices: fix | branch | report
      fix: Implement improvements inline on the current branch
      branch: Create a new branch and implement improvements there
      report: Report opportunities only — do not change any code

---

## todo-fixme-scan

enabled: false
frequency: per-commit
safety: direct
title: TODO / FIXME / HACK Scanner
instructions: |
  Scan all source files for TODO, FIXME, HACK, XXX, and NOTE comments.
  For each comment found, create a task entry in `.squad/tasks.md` if one
  does not already exist for that comment. Include the file path, line number,
  and the full comment text in the task description.

  Do not modify any source files. Only append to `.squad/tasks.md`.

---

## prune-tasks

enabled: false
frequency: daily
safety: direct
title: Prune Completed Tasks
instructions: |
  Open `.squad/tasks.md`. Remove all items that are marked as completed
  (`[x]`) and have no open sub-tasks. Archive removed items to
  `.squad/tasks-archive.md` (append, do not overwrite) with a timestamp.

  Do not modify any source files.

---

## commit-review

enabled: false
frequency: per-commit
safety: report-only
title: Commit Quality Review
instructions: |
  Retrieve the list of commits since the last time this task ran (use the
  stored last-run SHA if available; otherwise review the last 10 commits).
  For each commit, review the diff and note:
  - Code quality issues introduced (smells, complexity, missing tests)
  - Missing or inadequate commit message detail
  - Potential bugs or regressions
  - Positive patterns worth reinforcing

  Write a structured review report. Do not change any code.

---

## xml-doc-coverage

enabled: false
frequency: daily
safety: report-only
title: XML Doc Comment Coverage
instructions: |
  Scan all public types, methods, properties, and interfaces in the C# source
  for missing XML doc comments (`<summary>`, `<param>`, `<returns>`).
  Produce a coverage report grouped by file, listing each undocumented member.
  Do not change any code.

  If this is not a C# project, adapt to the equivalent docstring convention
  (JSDoc for TypeScript/JavaScript, docstrings for Python, godoc for Go).

---

## magic-numbers

enabled: false
frequency: daily
safety: branch
title: Extract Magic Numbers and Hardcoded Strings
instructions: |
  Scan the codebase for magic numbers (numeric literals used in logic without
  explanation) and hardcoded strings that belong in named constants or
  configuration (connection strings, URLs, thresholds, timeouts, limits, etc.).

  Take action according to {{if_found}}:
  - If "extract": Extract each magic value into a named constant or config
    entry. Update all references. Commit to a maintenance branch.
  - If "report": Do not change any code. List each instance with file path,
    line number, the literal value, and a suggested constant name.

options:
  if_found:
    type: radio
    label: If magic numbers or hardcoded strings are found
    default: report
    choices: extract | report
      extract: Extract as named constants on a new maintenance branch
      report: Report findings only — do not change any code

---

## readme-currency

enabled: false
frequency: daily
safety: report-only
title: README Currency Check
instructions: |
  Compare README.md (and any other top-level docs) against the current state
  of the codebase. Check for:
  - Setup or build instructions that no longer match the actual commands
  - Outdated dependency versions or requirements
  - References to files, directories, or features that no longer exist
  - Missing documentation for significant new features or changed APIs

  Write a gap report. Do not change any files.

---

## unused-dependencies

enabled: false
frequency: daily
safety: report-only
title: Unused Dependency Scan
instructions: |
  Check for NuGet packages (*.csproj), npm packages (package.json), or other
  dependency manifests in the repository. Identify packages that appear to be
  unused (not referenced in source or only referenced transitively).

  Write a report listing each potentially unused dependency, the manifest file
  it appears in, and a note on how to verify and remove it. Do not change any
  files.

---

## naming-conventions

enabled: false
frequency: daily
safety: report-only
title: Naming Convention Audit
instructions: |
  Audit the codebase for naming inconsistencies:
  - Variables, fields, properties, methods deviating from the project's
    established convention (PascalCase, camelCase, snake_case, etc.)
  - Inconsistent pluralisation (e.g. `items` vs `itemList` vs `itemCollection`)
  - Abbreviations used in some places but not others
  - Test method naming inconsistencies

  Take action according to {{if_found}}:
  - If "fix": Rename inconsistencies directly on the current branch. Update all
    references. Ensure the project still builds.
  - If "report": Do not change any code. List each inconsistency with file
    path, line number, current name, and suggested name.

options:
  if_found:
    type: radio
    label: If naming inconsistencies are found
    default: report
    choices: fix | report
      fix: Fix naming inconsistencies inline on the current branch
      report: Report inconsistencies only — do not change any code

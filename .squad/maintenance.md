---
# Full documentation: docs/features/maintenance-mode.md
# Task frequency is controlled by `frequency: daily`, `frequency: weekly`, `frequency: monthly`, `frequency: per-commit`, or `frequency: always`
# Set configured: true to enable maintenance mode.
# Set enabled: true on individual tasks to activate them.
# Global safety floor: per-task safety cannot be less safe than this value.
#   report-only < branch < direct
idle_timeout: 15
max_tasks_per_session: 5
safety: branch
enabled_on_idle: false
configured: false  # ← change to true to activate
tasks:
  - id: run-tests
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
        tooltip: "Fix failures or only report them"
        value: fix
        choices:
          - value: fix
            tooltip: Fix each failing test; commit fixes to the branch
          - value: report
            tooltip: Report failures only — do not change any code

  - id: eliminate-duplication
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
        tooltip: "Refactor now, create a branch, or just report"
        value: report
        choices:
          - value: fix
            tooltip: Refactor inline on the current branch
          - value: branch
            tooltip: Create a maintenance branch and refactor there
          - value: report
            tooltip: List each instance — do not change any code

  - id: architectural-practices
    enabled: true
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
        tooltip: "Implement improvements or produce a report"
        value: report
        choices:
          - value: branch
            tooltip: Implement improvements on a maintenance branch
          - value: report
            tooltip: Write a report — do not change any code

  - id: code-smells
    enabled: true
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
        tooltip: "Fix inline, use a branch, or report only"
        value: report
        choices:
          - value: fix
            tooltip: Address smells inline on the current branch
          - value: branch
            tooltip: Create a maintenance branch and address smells there
          - value: report
            tooltip: List each smell — do not change any code

  - id: speed-improvements
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
        tooltip: "Implement optimisations or report opportunities"
        value: report
        choices:
          - value: fix
            tooltip: Implement optimisations inline on the current branch
          - value: branch
            tooltip: Create a maintenance branch for the optimisations
          - value: report
            tooltip: Describe each opportunity — do not change any code

  - id: todo-fixme-scan
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

  - id: prune-tasks
    enabled: false
    frequency: daily
    safety: direct
    title: Prune Completed Tasks
    instructions: |
      Open `.squad/tasks.md`. Remove all items that are marked as completed
      (`[x]`) and have no open sub-tasks. Archive removed items to
      `.squad/tasks-archive.md` (append, do not overwrite) with a timestamp.

      Do not modify any source files.

  - id: commit-review
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

  - id: xml-doc-coverage
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

  - id: magic-numbers
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
        tooltip: "Extract to constants or report them"
        value: report
        choices:
          - value: extract
            tooltip: Extract to named constants; commit to a maintenance branch
          - value: report
            tooltip: List each instance — do not change any code

  - id: readme-currency
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

  - id: unused-dependencies
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

  - id: naming-conventions
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
        tooltip: "Rename inconsistencies or produce a report"
        value: report
        choices:
          - value: fix
            tooltip: Rename directly on the current branch; update all references
          - value: report
            tooltip: List each inconsistency — do not change any code
---

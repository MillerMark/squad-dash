# Code Health Safety UI Consolidation — Implementation Complete

## Overview
Successfully consolidated task safety UI from duplicated per-task options into a unified, reusable pattern. Each task can now opt-in to expose a safety level selector via a gear icon → radio button UI.

---

## Changes Made

### 1. **Schema Updates** (CodeHealthMdConfig.cs, CodeHealthMdParser.cs)
Added `has_safety_options: boolean` field to CodeHealthTask record:
- `HasSafetyOptions`: When `true`, displays unified safety radio UI in the gear popup
- Persists to task definition in code-health.md

### 2. **State Persistence** (CodeHealthStateStore.cs)
Extended TaskState to track per-task safety override:
- `SafetyOverride`: Stores user's selected safety level (`"report-only"`, `"branch"`, or `"direct"`)
- Persisted to `.squad/code-health-state.json` (atomic write)
- Methods:
  - `GetSafetyOverride(taskId)`: Retrieve current override
  - `SetSafetyOverride(taskId, level)`: Store override & persist

### 3. **UI Implementation** (CodeHealthPanelController.cs)
Implemented unified safety radio UI in task gear popup:
- Appears when `hasSafetyOptions: true`
- Three radio buttons (in order of safety: safest → highest risk):
  1. **Report Only** — no code changes
  2. **Branch** — create branch before changes
  3. **Direct** — changes on current branch
- Includes tooltips for UX clarity
- Immediately persists selection to state store
- Separator between safety options and task-specific options

---

## Usage

### Enable Safety UI for a Task
In `.squad/code-health.md`, add field to task definition:

```yaml
tasks:
  - id: example-task
    enabled: true
    frequency: daily
    safety: branch
    title: Example Task
    has_safety_options: true  # ← Enable unified safety UI
    instructions: |
      Your instructions here...
```

### UI Flow
1. User clicks gear icon on task row
2. Popup opens showing:
   - Task title
   - **Safety Level** (radio buttons) — if `has_safety_options: true`
   - Task-specific options (if any) — separated by horizontal line
3. User selects a safety level → immediately saved to state store

### Reading Safety Override (Backend)
During task execution, the backend will:
1. Call `CodeHealthStateStore.GetSafetyOverride(taskId)` to read the override
2. If set, use that level; otherwise use task's defined `safety:` value
3. Enforce global safety floor per existing logic

---

## Removed Redundancy

This pattern **replaces** per-task safety option blocks like:
```yaml
options:
  if_found:
    type: radio
    label: "If issues found"
    value: report
    choices:
      - value: branch
        tooltip: "..."
      - value: report
        tooltip: "..."
```

Tasks should remove these and instead set `has_safety_options: true`.

---

## Coordination Points

### Arjun Sen (C# Backend)
- **Branch naming**: Generate as `codehealth/{taskId}/{timestamp}`
- **Handlebars rendering**: 
  - Inject `{{branchName}}` and `{{safety}}` variables before passing instructions to AI
  - Use resolved safety level (override or fallback to task default)
- **Decomposition**: When emitting TASKS_JSON, include `{{branchName}}` and `{{safety}}` in step descriptions

### Malik Graves (Configuration)
- **Schema migration**: Add `has_safety_options: true` to eligible tasks
- **Remove redundant options**: Delete per-task `options: { if_found: ... }` blocks
- **Inject Handlebars**: Replace task-specific safety conditionals with template variables:
  - Old: `{{#if if_found == "branch"}} ... {{/if}}`
  - New: `{{#if safety == "branch"}} ... {{/if}}` (or direct {{branchName}} reference)

---

## Files Modified

1. **SquadDash/CodeHealthMdConfig.cs** — Added HasSafetyOptions field
2. **SquadDash/CodeHealthMdParser.cs** — Parse & serialize HasSafetyOptions
3. **SquadDash/CodeHealthStateStore.cs** — Persist SafetyOverride to state store
4. **SquadDash/CodeHealthPanelController.cs** — Unified safety radio UI

---

## Testing Checklist

- [ ] Task with `has_safety_options: true` shows gear icon
- [ ] Clicking gear opens popup with safety radio buttons
- [ ] Selecting a safety level persists to `.squad/code-health-state.json`
- [ ] Re-opening panel → selection remembered
- [ ] Task without `has_safety_options` or with `false` → no safety radio (only task options if any)
- [ ] Safety override properly reported to backend during execution
- [ ] Default safety level (from task definition) used if no override set

---

## Next Steps

1. **Malik**: Update `.squad/code-health.md` to add `has_safety_options` to qualifying tasks
2. **Arjun**: Implement backend-side reading of `CodeHealthStateStore.GetSafetyOverride()`
3. **Vesper**: Add test coverage for safety UI state persistence & selection
4. **Mark**: Review end-to-end flow once backend integration complete

---

## Questions?

See HANDOFF_NOTES_FOR_LYRA.md for original task context and Mark's design criteria.

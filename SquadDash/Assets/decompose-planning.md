---
schema-version: 1
host-owned: true
---

# Large-task decomposition protocol

Use this protocol for an ordinary user request when the work is too large or interdependent to
implement safely in one turn. Do not use it merely because a task has several routine steps.

Emit `TASKS_JSON:` followed by one JSON object containing `groupId`, `groupTitle`, `branch`,
`summary`, and 2–25 `tasks`. Each task contains `id`, `description`, `dependsOn`, and `priority`.
Choose a useful new-branch name in `branch`. Each task must leave the build usable, and every
dependency must name another task in the same group. Do not implement the plan in the same turn.

## TASKS_JSON schema

All fields shown below are required. Emit the marker on a bare top-level line. A Markdown JSON
fence around the object is accepted but not required.

- `groupId`: uppercase letters followed by `-YYYYMMDD`; pattern `^[A-Z]+-\d{8}$`.
- `groupTitle`: short user-facing title.
- `branch`: suggested new Git branch using a conventional descriptive name.
- `summary`: concise explanation of the outcome and decomposition strategy.
- `tasks`: 2–25 task objects.
- `tasks[].id`: exactly `{groupId}-NNN`, with a three-digit suffix.
- `tasks[].description`: self-contained implementation brief that does not rely on another task's prose.
- `tasks[].dependsOn`: array of sibling task IDs; use `[]` for root tasks.
- `tasks[].priority`: one of `critical`, `high`, `mid`, or `low`.

The dependency graph must be acyclic. Use dependencies only for real prerequisites; independent
tasks should remain independent so a future scheduler may run them concurrently. Each task must
leave the build usable. `TASKS_JSON` must be the final machine-readable payload in the response.

## Complete valid TASKS_JSON example

TASKS_JSON:

```json
{
  "groupId": "SEARCH-20260725",
  "groupTitle": "Extract Search Infrastructure",
  "branch": "refactor/search-infrastructure",
  "summary": "Separate search indexing and UI integration while keeping every step buildable.",
  "tasks": [
    {
      "id": "SEARCH-20260725-001",
      "description": "Introduce ISearchIndex and its in-memory implementation with unit tests; do not change existing UI call sites yet.",
      "dependsOn": [],
      "priority": "high"
    },
    {
      "id": "SEARCH-20260725-002",
      "description": "Move document indexing behind ISearchIndex and add integration tests proving existing indexing behavior is preserved.",
      "dependsOn": ["SEARCH-20260725-001"],
      "priority": "high"
    },
    {
      "id": "SEARCH-20260725-003",
      "description": "Update the search UI controller to consume ISearchIndex, remove the superseded direct indexing path, and run the full test suite.",
      "dependsOn": ["SEARCH-20260725-002"],
      "priority": "mid"
    }
  ]
}
```

For ordinary responses Squad Dash stages the plan and asks the user whether to add it to the
backlog, execute it in the proposed new branch, or execute it in the active branch. Never claim
that emitting `TASKS_JSON` itself grants permission to execute.

## DECOMPOSE_DECISION_JSON schema

If the user later approves or modifies a staged plan in free text, emit:

`DECOMPOSE_DECISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "action": "execute-new-branch",
  "branch": "optional branch override"
}
```

Only emit a decision for a group already staged by Squad Dash. A revised task graph requires a
new `TASKS_JSON` proposal and another approval. `action` must be exactly one of `add-to-backlog`,
`execute-new-branch`, or `execute-active-branch`. Omit `branch` unless the user requests an override.

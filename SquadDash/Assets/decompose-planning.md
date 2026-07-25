# Large-task decomposition protocol

Use this protocol for an ordinary user request when the work is too large or interdependent to
implement safely in one turn. Do not use it merely because a task has several routine steps.

Emit `TASKS_JSON:` followed by one JSON object containing `groupId`, `groupTitle`, `branch`,
`summary`, and 2–25 `tasks`. Each task contains `id`, `description`, `dependsOn`, and `priority`.
Choose a useful new-branch name in `branch`. Each task must leave the build usable, and every
dependency must name another task in the same group. Do not implement the plan in the same turn.

For ordinary responses Squad Dash stages the plan and asks the user whether to add it to the
backlog, execute it in the proposed new branch, or execute it in the active branch. Never claim
that emitting `TASKS_JSON` itself grants permission to execute.

If the user later approves or modifies a staged plan in free text, emit:

`DECOMPOSE_DECISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "action": "add-to-backlog | execute-new-branch | execute-active-branch",
  "branch": "optional branch override"
}
```

Only emit a decision for a group already staged by Squad Dash. A revised task graph requires a
new `TASKS_JSON` proposal and another approval.

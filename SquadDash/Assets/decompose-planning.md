---
schema-version: 4
host-owned: true
---

# Large-task decomposition protocol

Use this protocol for an ordinary user request when the work is too large or interdependent to
implement safely in one turn. Do not use it merely because a task has several routine steps.

## Explicit plan-creation intent

When the user explicitly requests plan creation, emit `TASKS_JSON` even if the work might fit in
one turn. Preserve the human approval boundary — do **not** implement in the same response.

**Creation verbs and forms that trigger TASKS_JSON:**
- Verb + "plan": *create a plan*, *draft a plan*, *devise a plan*, *design a plan*, *write a plan*,
  *make a plan*, *prepare a plan*, *propose a plan*, *outline a plan*, *generate a plan*,
  *formulate a plan*, *produce a plan*
- "plan out X": *plan out the migration*, *plan out our sprint*
- The `/plan` slash command (always treats the body as an explicit plan-creation request)

**When the user says "create a plan and implement it"** (plan-and-implement intent), still emit
`TASKS_JSON`. SquadDash will hold the user approval boundary before any execution begins.

**Patterns that do NOT trigger TASKS_JSON** — retain prose discussion instead:
- First-person statements: "I plan to do X", "my plan is to…"
- Information questions: "what's the plan?", "do you have a plan?"
- Attributive references: "the plan is to…", "plan A vs plan B"

Emit `TASKS_JSON:` followed by one JSON object containing `groupId`, `groupTitle`, `branch`,
`summary`, and 2–25 `tasks`. It may also contain optional `validations` and `delivery` fields. Each task contains `id`, `title`, `description`, `dependsOn`, `priority`, and an explicit `agentRoutingMode`.
Choose a useful new-branch name in `branch`. Each task must leave the build usable, and every
dependency must name another task in the same group. Do not implement the plan in the same turn.

Plan for cohesion, not merely local completion. When separate tasks produce and consume components,
declare the outputs and integration responsibility in their descriptions and add first-class validation
nodes for important cross-task contracts. A supporting-artifact task may complete before production
integration, but its later consumers and the validation that proves their relationship must be explicit.

## TASKS_JSON schema

All fields shown below except `delivery` are required. Emit the marker on a bare top-level line. A Markdown JSON
fence around the object is accepted but not required.

- `groupId`: uppercase letters followed by `-YYYYMMDD`; pattern `^[A-Z]+-\d{8}$`.
- `groupTitle`: short user-facing title.
- `branch`: suggested new Git branch using a conventional descriptive name.
- `summary`: concise explanation of the outcome and decomposition strategy.
- `delivery`: optional; use `"inbox"` only when the user explicitly asks for the plan to be sent
  to their Inbox. Otherwise omit it or use `"transcript"`.
- `tasks`: 2–25 task objects.
- `tasks[].id`: exactly `{groupId}-NNN`, with a three-digit suffix.
- `tasks[].title`: concise, action-oriented, human-readable task name. Do not use only a phase
  number or repeat the task ID; for example, use `Extract WorkspaceFileWatcherCoordinator`, not
  `Phase 1A`.
- `tasks[].description`: self-contained implementation brief that does not rely on another task's prose.
- `tasks[].dependsOn`: array of sibling task IDs; use `[]` for root tasks.
- `tasks[].priority`: one of `critical`, `high`, `mid`, or `low`.
- `tasks[].agentRoutingMode`: required; either `"assigned"` or `"generic"`. Never omit this to obtain a fallback.
- `tasks[].agentAssignments`: required with exactly one `{ "agentHandle", "role", "allowGenericChildren" }` object when `agentRoutingMode` is `"assigned"`; omit it for `"generic"`. Use an exact active handle from `.squad/team.md`. Set `allowGenericChildren` to `true` unless the task has a specific reason to prefer direct execution. Generic children remain subordinate helpers: they never satisfy or replace the named primary assignment, and the named agent must synthesize their work. Multiple primary assignments remain unavailable until SquadDash can isolate writers in separate worktrees.
- `tasks[].genericAgentReason`: required only with `agentRoutingMode: "generic"`; explain why no roster specialist is appropriate.
- `tasks[].parallelEligible`: optional boolean. Set true only when the task may run concurrently with other dependency-ready tasks; dependencies still control readiness and SquadDash may serialize work when scopes conflict.
- `tasks[].outputs`: optional list of stable `{ "outputId", "description" }` contracts produced by
  this task for later tasks. Use lowercase kebab-case IDs that describe capabilities rather than
  provisional class names.
- `tasks[].inputs`: optional list of output IDs from prerequisite tasks that this task must consume.
  A consumer may name several outputs, and one output may have several consumers.
- `tasks[].parentTaskId`: optional. Use only in a revised plan to split a blocked task into smaller
  replacements. Keep the original parent task in the full proposal and point every replacement at it.
  SquadDash marks the parent superseded only after the revised plan is approved.
- `validations`: optional array of first-class, non-mutating cross-task validation nodes. Include these
  when correctness depends on outputs from multiple tasks communicating, being consumed, replacing a
  placeholder, or jointly satisfying an observable scenario. Do not add ceremonial validations to a
  simple plan whose task-level acceptance already proves the result.
- `validations[].validationId`: exactly `{groupId}-VAL-NNN`.
- `validations[].title`: concise user-facing contractual outcome.
- `validations[].description`: explain the relationship being validated and why it matters to the plan.
- `validations[].afterTaskIds`: non-empty list of tasks whose completion makes the validation eligible.
- `validations[].beforeTaskIds`: tasks and their downstream frontier that must wait for validation. Use
  `[]` for a final validation that gates only plan completion.
- `validations[].assertions`: non-empty list of observable, falsifiable contractual claims. Do not use
  vague assertions such as "components work together."
- `validations[].outputIds`: optional list of stable task output IDs whose relationship is covered.
- `validations[].mode`: `command`, `evidence`, or `hybrid`. `command` executes deterministic commands;
  `evidence` requests evidence-backed assessment; `hybrid` requires both.
- `validations[].commands`: required for `command` and `hybrid`; omit for `evidence`. Commands must be
  non-mutating and scoped to the workspace.
- `validations[].revalidateAtCompletion`: normally `true` when later work could invalidate the result.

Validation nodes are DAG work, not human approvals. They become ready only after every `afterTaskIds`
task completes, may run while unrelated branches continue, and block only their declared downstream
frontier. A failed validation must preserve completed work and identify the violated assertion; it must
not silently rewrite production code.

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
  "delivery": "transcript",
  "tasks": [
    {
      "id": "SEARCH-20260725-001",
      "title": "Introduce the search index abstraction",
      "description": "Introduce ISearchIndex and its in-memory implementation with unit tests; do not change existing UI call sites yet.",
      "dependsOn": [],
      "priority": "high",
      "outputs": [{ "outputId": "search-index-contract", "description": "The reusable search index abstraction and in-memory implementation." }],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers the new search abstraction."
    },
    {
      "id": "SEARCH-20260725-002",
      "title": "Move document indexing behind ISearchIndex",
      "description": "Move document indexing behind ISearchIndex and add integration tests proving existing indexing behavior is preserved.",
      "dependsOn": ["SEARCH-20260725-001"],
      "priority": "high",
      "inputs": ["search-index-contract"],
      "outputs": [{ "outputId": "indexed-document-path", "description": "Document indexing routed through ISearchIndex." }],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers search indexing."
    },
    {
      "id": "SEARCH-20260725-003",
      "title": "Migrate the search UI to ISearchIndex",
      "description": "Update the search UI controller to consume ISearchIndex, remove the superseded direct indexing path, and run the full test suite.",
      "dependsOn": ["SEARCH-20260725-002"],
      "priority": "mid",
      "inputs": ["search-index-contract", "indexed-document-path"],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers this UI integration."
    }
  ],
  "validations": [
    {
      "validationId": "SEARCH-20260725-VAL-001",
      "title": "Verify the UI uses the migrated search path",
      "description": "Confirm the search UI reaches document indexing through ISearchIndex and the superseded direct path is no longer active.",
      "afterTaskIds": ["SEARCH-20260725-002", "SEARCH-20260725-003"],
      "beforeTaskIds": [],
      "assertions": [
        "The search UI calls the ISearchIndex-backed path.",
        "The former direct indexing path is not reachable from the search UI.",
        "The user-visible search scenario still returns indexed documents."
      ],
      "outputIds": ["search-index-contract", "indexed-document-path"],
      "mode": "evidence",
      "revalidateAtCompletion": true
    }
  ]
}
```

For ordinary responses SquadDash stages the plan and asks the user whether to add it to the
backlog, execute it in the proposed new branch, or execute it in the active branch. Never claim
that emitting `TASKS_JSON` itself grants permission to execute. When `delivery` is `"inbox"`,
SquadDash sends the staged plan to the Inbox with the same three host-owned actions instead of
showing approval controls beneath the transcript response.

## DECOMPOSE_DECISION_JSON schema

If the user later approves or modifies a staged plan in free text, emit:

`DECOMPOSE_DECISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "revision": "revision supplied by SquadDash in the pending-plan context",
  "action": "execute-new-branch",
  "branch": "optional branch override"
}
```

Only emit a decision for a group already staged by SquadDash. A revised task graph requires a
new `TASKS_JSON` proposal and another approval. `action` must be exactly one of `add-to-backlog`,
`execute-new-branch`, or `execute-active-branch`. Omit `branch` unless the user requests an override.
The `revision` must exactly match the revision supplied by SquadDash for that pending group.

## DECOMPOSE_RECOVERY_JSON schema

If the user explicitly asks to retry or replan a blocked approved plan, emit:

`DECOMPOSE_RECOVERY_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "revision": "revision supplied by SquadDash in blocked-plan context",
  "action": "replan-failed-task"
}
```

`action` must be `retry-as-written` or `replan-failed-task`. Never infer recovery merely because a
plan is blocked; emit this payload only for explicit user intent. Replanning asks SquadDash to obtain
a complete revised `TASKS_JSON`, including the existing tasks and smaller replacement tasks whose
`parentTaskId` identifies the blocked parent. The revised graph requires another user approval.

## DECOMPOSE_STEP_RESULT_JSON schema

During an approved Executing Plan loop, SquadDash supplies the exact group, task, and revision. The
executor must not edit `tasks.md`; it reports one result and SquadDash owns the status transition:

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "taskId": "GROUP-YYYYMMDD-NNN",
  "revision": "revision from the persisted group header",
  "executionAttemptId": "host-supplied attempt ID when this task has an assigned roster agent",
  "status": "complete",
  "commit": "Git commit SHA",
  "summary": "concise outcome",
  "remainingWork": [],
  "verification": {
    "status": "passed",
    "command": "exact command that ran",
    "summary": "what passed"
  },
  "agentExecutions": [
    {
      "requestedAgent": "host-assigned roster handle",
      "actualPrimaryAgent": "same roster handle"
    }
  ]
}
```

`status` must be `complete`, `partial`, or `failed`. Complete requires a new commit and passed
verification. Partial requires concrete `remainingWork` and never unlocks dependent tasks. SquadDash
validates the assignment, revision, Git commit, clean-worktree boundary, and verification evidence
before changing any plan status. `executionAttemptId` and `agentExecutions` are required only when
SquadDash supplies a verified roster assignment context for the current attempt. Report the supplied
attempt ID and the observable requested/actual roster handles. Do not report tool-call IDs or child
lineage: SquadDash owns and validates that internal evidence directly.

## PLAN_GATE_APPROVAL_JSON schema

When SquadDash pauses an executing plan at a human approval gate, the AI must
NOT resume or emit TASKS_JSON. If the user approves the gate in free text, emit:

`PLAN_GATE_APPROVAL_JSON:`

```json
{
  "planId": "GROUP-YYYYMMDD",
  "gateId": "GROUP-YYYYMMDD-GATE-001",
  "revision": "revision supplied by SquadDash for the active gate",
  "requestVersion": 1,
  "note": "optional approval note from the user"
}
```

Only emit this when SquadDash has explicitly paused an executing plan at the
named gate and provided the exact planId, gateId, and revision. Never infer
gate approval from conversation context alone. Omit `note` unless the user
provided specific approval commentary.

## PLAN_GATE_RESPONSE_JSON schema

When an approval request is active and the user responds in free form, distinguish revisions to
the reviewed work from unrelated work. Never modify reviewed plan work during the classification
turn. For an explicit rework request, emit:

`PLAN_GATE_RESPONSE_JSON:`

```json
{
  "planId": "GROUP-YYYYMMDD",
  "gateId": "GROUP-YYYYMMDD-GATE-001",
  "revision": "revision supplied by SquadDash",
  "requestVersion": 1,
  "disposition": "request-rework",
  "taskIds": ["GROUP-YYYYMMDD-001"],
  "instructions": "The user's concrete requested changes"
}
```

Use `unrelated` when the request is separate work and `clarification` when the intended approval
task or requested change is ambiguous; those dispositions omit `taskIds` and `instructions` and
leave the approval unchanged. Always use the exact current request version. A rework response may
name only completed tasks listed in the gate's reviewed boundary.

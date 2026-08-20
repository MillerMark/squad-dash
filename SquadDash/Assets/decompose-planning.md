---
schema-version: 8
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
`summary`, and 1–25 `tasks`. It may also contain optional `approvalGates`, `validations`, and
`delivery` fields. Each task contains `id`, `title`, `description`, `dependsOn`, `priority`, an explicit
`agentRoutingMode`, an explicit `executionMode` (`implementation` or `verification`), and an optional
`verificationLevel` (`lightweight`, `standard`, or `enhanced`).
Choose a useful new-branch name in `branch`. Each task must leave the build usable, and every
dependency must name another task in the same group. Do not implement the plan in the same turn.

Plan for cohesion, not merely local completion. Every implementation step must satisfy **two
cohesion requirements**:

1. **Observable outcome.** The step description must name a user-visible or host-observable result —
   something a human tester or SquadDash itself can verify without reading code. "Users can filter
   by date range in the search panel" or "The build succeeds with the new module linked" are
   observable; "add a helper class" or "write unit tests" alone are not, because they describe
   artifacts rather than outcomes.
2. **Production consumer.** The step description must state *how* its output reaches a production
   consumer or integration surface — not merely that an artifact will exist in the repository.
   Name exact call sites, events, configuration entries, tests, files, or symbols only when they
   have already been verified and the precision prevents a materially different implementation.
   Do not invent exact implementation details during planning. Execution handoffs will record the
   concrete files and symbols actually changed so downstream steps can continue the accepted work.

Artifact-only wording such as "add a helper", "create a utility", or "add tests" is **insufficient**
unless the same step or a named successor integrates the artifact into a production path.

When separate tasks produce and consume components, declare the outputs and integration
responsibility in their descriptions. Use a first-class validation node when an independent result
adds meaningful confidence at that boundary, protects downstream work from a plausible integration
failure, or verifies a contract not already established by task evidence. Omit it when it merely
repeats a step's own build, tests, or verification. Neither including nor omitting validations is
preferred; make the judgment from the value the validation adds.

### Proportional completion proof

Every plan must make completion objectively demonstrable, but it does **not** need a separate final
proof task or final audit when the implementation tasks, their tests, and their accepted proof
evidence already establish the feature's acceptance criteria. Prefer accumulating evidence along the
execution path: require the task that changes behavior to add or update the focused tests that prove
that behavior, and let later checks reuse those recorded results.

Add a final verification task only when a material end-to-end or cross-task risk remains unproven.
Keep that task cheap, non-mutating, and scoped to the missing evidence. It must:
- Identify the specific acceptance criterion that earlier task evidence does not establish.
- Reuse accepted proof evidence and focused tests rather than repeating full implementation reviews.
- Run the smallest deterministic build, test, or demonstration command that closes the remaining gap.
- Never add tests, modify production code, or redo independent verification; test creation belongs in
  the implementation task whose behavior the test protects.
- Declare only the proof requirements needed for the remaining gap.

Use `executionMode: "verification"` only when a task runs checks or records observations and is not
expected to change source files. Verification-only steps report the current evaluated HEAD but do not
create a commit and do not receive a redundant independent Verity Cross review. Use this mode anywhere
the approved task is genuinely non-mutating. If the
step may repair failures or otherwise change the repository, use `executionMode: "implementation"`.

Bad final steps: "Update documentation", "Run the test suite", "Clean up".
Good final step: "Verify that clicking 'Export' in the Reports panel produces a downloadable CSV
containing all filtered rows, by running `dotnet test --filter ReportExportIntegration` and
confirming the test exercises the full UI→service→file path."

## Plan detail budget

Calibrate detail to what the user must approve and what downstream agents must coordinate. Describe
observable outcomes, constraints, verified integration points, cross-task contracts, meaningful risks,
and falsifiable completion evidence. Leave local implementation choices to the assigned agent unless
they are verified compatibility, safety, migration, or dependency boundaries.

Do not prescribe speculative class names, helper decomposition, exact file-by-file edits, an exact
number of tests, or an exhaustive test-case inventory. Instead state the behaviors, failure modes, and
validation scenarios that must be covered. The implementing and verifying agents choose the smallest
sufficient implementation and test set from the repository state they encounter.

Include detail only when it prevents materially different interpretations, coordinates tasks, preserves
an explicit user decision, or makes completion objectively falsifiable. Each task is a bounded execution
contract, not a step-by-step pseudo-implementation.

Validation nodes and independent task verification are executed by Verity Cross, the built-in
`fact-checker` agent. Their assertions must therefore be provable by AI-accessible repository,
command, artifact, or previously approved evidence. Never put a requirement for a person to look at
the UI, hear audio, judge visual quality, or observe a restart directly into a validation assertion.
Declare it as a `proofRequirements` entry with `live-ui-observation`, `restart-observation`, or
`human-observation`; SquadDash will promote it to an explicit human approval checkpoint.

If the complete object is too large to return inline, write only that JSON object to
`.squad/tmp/agent-artifacts/<file>.json` and return this top-level hidden transport instead:

```text
TASKS_JSON_FILE:
{"path":".squad/tmp/agent-artifacts/<file>.json","sha256":"optional sha256"}
```

Never use `SQUADDASH_ARTIFACT_JSON` for a plan. That protocol displays its contents to the user;
`TASKS_JSON_FILE` is consumed privately and SquadDash renders the compact plan receipt.

## TASKS_JSON schema

Every field explicitly labeled required below is required. Fields labeled optional may be omitted.
Emit the marker on a bare top-level line. A Markdown JSON fence around the object is accepted but
not required. Use the exact property names below; do not substitute familiar shorthand such as
`assignedTo`, `owner`, or an approval-gate `title` for the canonical properties.

- `groupId`: uppercase letters followed by `-YYYYMMDD`; pattern `^[A-Z]+-\d{8}$`.
- `groupTitle`: short user-facing title.
- `branch`: suggested new Git branch using a conventional descriptive name.
- `summary`: one short paragraph, normally one to three sentences, giving the guiding intent for the
  whole plan: the user-visible outcome, architectural direction, and any non-negotiable cohesion
  constraint. Keep implementation detail in the relevant tasks. SquadDash injects this north star
  into every task, validation, verification, rework, and human-review turn, so write it to remain
  useful outside the proposal conversation.
- `delivery`: optional; use `"inbox"` only when the user explicitly asks for the plan to be sent
  to their Inbox. Otherwise omit it or use `"transcript"`.
- `tasks`: required array containing 1–25 task objects.
- `tasks[].id`: exactly `{groupId}-NNN`, with a three-digit suffix.
- `tasks[].title`: concise, action-oriented, human-readable task name. Do not use only a phase
  number or repeat the task ID; for example, use `Extract WorkspaceFileWatcherCoordinator`, not
  `Phase 1A`.
- `tasks[].description`: self-contained implementation brief that does not rely on another task's prose.
- `tasks[].dependsOn`: array of sibling task IDs; use `[]` for root tasks.
- `tasks[].priority`: one of `critical`, `high`, `mid`, or `low`.
- `tasks[].executionMode`: required for new plans; either `"implementation"` for source-producing
  work or `"verification"` for non-mutating proof work. Verification tasks must declare at least one
  `proofRequirements` entry and must not create a commit merely to satisfy plan bookkeeping.
- `tasks[].verificationLevel`: optional single string for implementation work. Omit it for the default
  `"standard"` independent review. Use `"lightweight"` only for low-risk, locally provable work where
  host checks of commands, evidence, commit scope, and changed files are sufficient; the host may
  upgrade it when the actual diff is riskier. Use `"enhanced"` when cross-cutting, integration, safety,
  or test-sensitivity risk warrants deeper counterexample and negative-case scrutiny. Do not add a
  nested verification object. This setting has no effect on `executionMode: "verification"` steps.
- `tasks[].agentRoutingMode`: required; either `"assigned"` or `"generic"`. Never omit this to obtain a fallback.
- `tasks[].agentAssignments`: required with exactly one `{ "agentHandle", "role", "allowGenericChildren" }` object when `agentRoutingMode` is `"assigned"`; omit it for `"generic"`. `agentHandle` and `role` are required non-empty strings; `allowGenericChildren` is a required boolean. Use an exact active handle from `.squad/team.md`. Set `allowGenericChildren` to `true` unless the task has a specific reason to prefer direct execution. Generic children remain subordinate helpers: they never satisfy or replace the named primary assignment, and the named agent must synthesize their work. Multiple primary assignments remain unavailable until SquadDash can isolate writers in separate worktrees. Exact example: `"agentAssignments":[{"agentHandle":"arjun-sen","role":"Implement the persistence service","allowGenericChildren":true}]`.
- `tasks[].agentAssignments[].agentHandle`: required exact active roster handle.
- `tasks[].agentAssignments[].role`: required concise description of the assigned responsibility.
- `tasks[].agentAssignments[].allowGenericChildren`: required boolean controlling subordinate generic helpers.
- `tasks[].genericAgentReason`: required only with `agentRoutingMode: "generic"`; explain why no roster specialist is appropriate.
- `tasks[].parallelEligible`: optional boolean. Set true only when the task may run concurrently with other dependency-ready tasks; dependencies still control readiness and SquadDash may serialize work when scopes conflict.
- `tasks[].outputs`: optional list of stable `{ "outputId", "description" }` contracts produced by
  this task for later tasks. Use lowercase kebab-case IDs that describe capabilities rather than
  provisional class names.
- `tasks[].outputs[].outputId`: required globally unique lowercase kebab-case capability ID.
- `tasks[].outputs[].description`: required non-empty description of the produced contract.
- `tasks[].inputs`: optional list of output IDs from prerequisite tasks that this task must consume.
  A consumer may name several outputs, and one output may have several consumers.
- `tasks[].proofRequirements`: optional list of stable `{ "requirementId", "proofType", "description", "question" }`
  contracts. Supported worker/host types are `ai-assessed`, `automated-test`, `build`, and
  `host-recorded`. Human-only types are `live-ui-observation`, `restart-observation`, and
  `human-observation`. Do not describe a live observation as an automated test. SquadDash moves
  human-only requirements to a generated approval checkpoint after the task; the task worker is
  never asked to fabricate proof it cannot observe. A complete step result must return matching
  `proofEvidence` only for the worker/host requirements that remain on the task. `question` is
  optional for stored-plan compatibility, but supply it for every human-only requirement. Every
  human-only requirement must be atomic: its `question` must contain exactly one independently
  verifiable true/false claim, where true means the check passed and false means it failed. If a
  reviewer must check three outcomes, emit three proof requirements with three stable IDs; never
  bundle them into one question. Example: `Does clicking an item show a selection highlight?`.
  If a human observation was already accepted at an earlier gate and no later work can invalidate it,
  do not ask for it again. Choose the smallest sufficient set of acceptance-level observations, with
  at most seven human checks in one checkpoint. Do not convert every implementation detail into a
  checkbox. The initial proposal must make the number and placement of all generated human
  checkpoints obvious to the reviewer.
- `tasks[].proofRequirements[].requirementId`: required unique lowercase kebab-case identifier.
- `tasks[].proofRequirements[].proofType`: required supported proof type listed above.
- `tasks[].proofRequirements[].description`: required non-empty evidence contract.
- `tasks[].proofRequirements[].question`: optional except that every human-only requirement should
  supply one atomic true/false question.
- `approvalGates`: optional array of explicit human checkpoints. Do not add a gate before every
  root task or after the final task merely to duplicate plan start/completion controls.
- `approvalGates[].gateId`: required non-empty stable identifier unique within the plan; use
  `{groupId}-GATE-NNN` for newly-authored plans.
- `approvalGates[].message`: required concise user-facing reason for the checkpoint, such as
  `Review the dry-run summary before applying workspace changes`. This is not a title property.
- `approvalGates[].afterTaskIds`: required array of task IDs that must all complete before the gate
  becomes ready; use `[]` only when the boundary is expressed entirely by `beforeTaskIds` and is
  not before every root task.
- `approvalGates[].beforeTaskIds`: required array of task IDs whose work must wait for approval;
  use `[]` only for a final human proof checkpoint with explicit `proofRequirements`.
- `approvalGates[].question`: required in every newly proposed explicit human approval gate (the
  host only treats it as optional while reading older stored plans). Ask the concrete approval question in plain language;
  do not merely restate the gate message or say “Confirm this works.” SquadDash features this
  question in the approval card and gives the reviewer a shortcut to open the plan.
- `approvalGates[].proofRequirements`: optional human-only proof contracts using the same object
  shape. Approval records the reviewer's identity, note, time, and a durable internal attestation.
  Use this explicitly when the desired checkpoint boundary differs from the producing task's exit.
  Each entry must likewise be one atomic true/false verification item.
- `approvalGates[].proofRequirements[].requirementId`, `.proofType`, `.description`, and `.question`:
  use the same exact fields and rules as task proof requirements; every gate proof must be human-only.
- `approvalGates[].presentationAnchor`: optional host-managed display hint; the Plan Visualizer sets
  this to indicate which task node is the primary visual anchor for the gate control. Do not emit it
  in new plans; the host writes and preserves it across edits.
- `tasks[].parentTaskId`: optional. Use only in a revised plan to split a blocked task into smaller
  replacements. Keep the original parent task in the full proposal and point every replacement at it.
  SquadDash marks the parent superseded only after the revised plan is approved.
- `tasks[].amendmentGateId`: host-owned compatibility metadata for a dynamically inserted amendment;
  never emit it in an ordinary new plan.
- `validations`: optional array of at most 16 first-class, non-mutating cross-task validation nodes.
  Include one when its independent result adds meaningful confidence at a cross-task boundary,
  protects downstream work from a plausible integration failure, or establishes a contract not
  already proven by task evidence. Omit one when it would merely repeat a task's build, tests, or
  verification and would not affect the decision to continue. Exercise judgment; neither presence
  nor absence is preferred.
- `validations[].validationId`: exactly `{groupId}-VAL-NNN`.
- `validations[].title`: concise user-facing contractual outcome, normally 2–5 words. It is always
  rendered below the validation shield, including in narrow stage columns, so do not repeat the
  description or task names in the title.
- `validations[].description`: explain the relationship being validated and why it matters to the plan.
- `validations[].afterTaskIds`: non-empty list of tasks whose completion makes the validation eligible.
- `validations[].beforeTaskIds`: tasks and their downstream frontier that must wait for validation. Use
  `[]` for a final validation that gates only plan completion.
- `validations[].assertions`: non-empty list of observable, falsifiable contractual claims. Do not use
  vague assertions such as "components work together."
- `validations[].outputIds`: optional list of stable task output IDs whose relationship is covered.
- `validations[].mode`: `command`, `evidence`, `hybrid`, or `audit`. `command` executes deterministic commands;
  `evidence` requests evidence-backed assessment; `hybrid` requires both. An `audit` is optional and
  should be added only when a material cross-task completion claim cannot be established by task-level
  proof and deterministic commands. Do not add an audit merely because a plan contains proof
  requirements. If a final audit is warranted, it must cover every leaf task, have an empty
  `beforeTaskIds`, compare the already recorded requirements, commits, and proof evidence, and avoid
  rerunning implementation reviews or duplicating a final verification task. It must reject test-only
  work offered in place of a required live observation.
- `validations[].commands`: required for `command` and `hybrid`; omit for `evidence`. Commands must be
  non-mutating and scoped to the workspace.
- `validations[].revalidateAtCompletion`: normally `true` when later work could invalidate the result.

Validation nodes are DAG work, not human approvals. They become ready only after every `afterTaskIds`
task completes, may run while unrelated branches continue, and block only their declared downstream
frontier. A failed validation must preserve completed work and identify the violated assertion; it must
not silently rewrite production code.

The dependency graph must be acyclic. Use dependencies only for real prerequisites; independent
tasks should remain independent so a future scheduler may run them concurrently. Each task must
leave the build usable. `TASKS_JSON` or `TASKS_JSON_FILE` must be the final machine-readable payload
in the response.

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
      "description": "Introduce ISearchIndex and InMemorySearchIndex so that document indexing has a testable seam. Observable outcome: unit tests pass proving the in-memory implementation indexes and queries documents. Production consumer: task 002 will call ISearchIndex from the existing indexing path.",
      "dependsOn": [],
      "priority": "high",
      "executionMode": "implementation",
      "outputs": [{ "outputId": "search-index-contract", "description": "The reusable search index abstraction and in-memory implementation." }],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers the new search abstraction."
    },
    {
      "id": "SEARCH-20260725-002",
      "title": "Move document indexing behind ISearchIndex",
      "description": "Replace the direct indexing call in DocumentIndexer.IndexAll with ISearchIndex.Add, and add integration tests proving existing indexing behavior is preserved. Observable outcome: the integration test suite passes and DocumentIndexer no longer references the old IndexWriter directly. Production consumer: SearchPanel.Query (wired in task 003) will read from ISearchIndex.",
      "dependsOn": ["SEARCH-20260725-001"],
      "priority": "high",
      "executionMode": "implementation",
      "inputs": ["search-index-contract"],
      "outputs": [{ "outputId": "indexed-document-path", "description": "Document indexing routed through ISearchIndex." }],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers search indexing."
    },
    {
      "id": "SEARCH-20260725-003",
      "title": "Migrate the search UI to ISearchIndex",
      "description": "Update SearchPanel to call ISearchIndex.Query, remove the superseded direct indexing path, and add or update the focused SearchIntegration coverage in the same task. Observable outcome: typing a query in the search panel returns matching documents from the ISearchIndex-backed path, proven by `dotnet test --filter SearchIntegration` exercising SearchPanel → ISearchIndex → InMemorySearchIndex → document results.",
      "dependsOn": ["SEARCH-20260725-002"],
      "priority": "mid",
      "executionMode": "implementation",
      "inputs": ["search-index-contract", "indexed-document-path"],
      "proofRequirements": [
        {
          "requirementId": "search-integration-test",
          "proofType": "automated-test",
          "description": "Run the SearchIntegration scenario through SearchPanel, ISearchIndex, and rendered results."
        }
      ],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers this UI integration."
    }
  ],
  "approvalGates": [
    {
      "gateId": "SEARCH-20260725-GATE-001",
      "message": "Review the migrated indexing path before connecting the search UI",
      "afterTaskIds": ["SEARCH-20260725-002"],
      "beforeTaskIds": ["SEARCH-20260725-003"],
      "question": "Does the migrated document-indexing path preserve existing indexing behavior?"
    }
  ]
}
```

For ordinary responses, the AI submits a plan proposal and SquadDash independently validates,
persists, and stages it. The AI must describe its own action as **submitting** or **proposing** the
plan. Never claim that a plan was staged, added to the Plans panel, delivered to Inbox, or made
available for approval: those are host-owned outcomes that are true only after SquadDash displays
an explicit staging receipt. A suitable final sentence is: `Submitting the plan proposal now.`
Never claim that emitting `TASKS_JSON` itself grants permission to execute. After confirmed
staging, SquadDash asks the user whether to add the plan to the backlog, execute it in the proposed
new branch, or execute it in the active branch. When `delivery` is `"inbox"`, SquadDash sends the
confirmed staged plan to the Inbox with the same three host-owned actions instead of showing
approval controls beneath the transcript response.

## STAGED_PLAN_REVISION_JSON schema

Use this protocol only to correct an unapproved proposal that SquadDash has already staged. Read
the revision-locked compact outline first, retrieve only affected entities from the exact pending
definition using its source selectors, and use the exact `groupId` and `baseRevision` supplied by SquadDash.
For a targeted correction, emit one compact delta instead of repeating the complete task graph:

`STAGED_PLAN_REVISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "baseRevision": "revision supplied by SquadDash for the pending proposal",
  "summary": "concise description of the requested correction",
  "operations": [
    {
      "op": "updateTask",
      "targetRef": "T2",
      "patch": {
        "priority": "high"
      }
    },
    {
      "op": "editTaskText",
      "targetRef": "T2",
      "path": "/description",
      "oldText": "one exact clause from the current description",
      "newText": "the corrected clause"
    },
    {
      "op": "editValidationText",
      "targetRef": "V1",
      "path": "/assertions/2",
      "oldText": "one exact clause from the current assertion",
      "newText": "the corrected clause"
    },
    {
      "op": "addValidation",
      "validation": {
        "validationId": "GROUP-YYYYMMDD-V02",
        "description": "Complete validation object required for an add operation.",
        "afterTaskIds": ["GROUP-YYYYMMDD-002"],
        "beforeTaskIds": [],
        "assertions": ["The requested contract is proven."],
        "outputIds": [],
        "mode": "audit",
        "revalidateAtCompletion": true
      }
    }
  ]
}
```

Allowed operations are `updatePlan`, `editPlanText`, `updateTask`, `editTaskText`, `addTask`,
`removeTask`, `updateApprovalGate`, `editApprovalGateText`, `addApprovalGate`,
`removeApprovalGate`, `updateValidation`, `editValidationText`, `addValidation`, and
`removeValidation`. Update operations require `targetId` except
`updatePlan`, plus a `patch` containing only changed fields. Array-valued fields in a patch replace
the complete array; they are not appended. Add operations require one complete `task`,
`approvalGate`, or `validation` object. Remove operations require `targetId`. Do not use
`reopenTask`: an unapproved proposal has no execution history to reopen.

For an existing outlined entity, use its short revision-local `targetRef` (`T1`, `T2`, `G1`,
`V1`, and so on) instead of reproducing its canonical `targetId`. Never supply both. SquadDash maps
the short reference to the exact canonical ID before applying the delta. Add operations still require
complete schema-compliant objects and IDs because the new entity does not yet exist in the outline.

Text-edit operations make small, stale-safe changes inside an existing string. They require a
JSON Pointer `path`, a non-empty `oldText` that occurs exactly once at that path, and `newText`
(which may be empty to delete the match). Use `editPlanText` for plan metadata,
`editTaskText` for a task, `editApprovalGateText` for a gate, and `editValidationText` for a
validation. Paths can address a direct string such as `/description`, an array entry such as
`/assertions/2`, or a nested string such as `/proofRequirements/0/description`. Prefer these
operations when changing a clause in a long description, assertion, proof requirement, assignment
role, or similar text. Use a whole-property patch only when the complete property is intentionally
being replaced. SquadDash rejects a missing or repeated `oldText` rather than guessing.

SquadDash rejects a stale `baseRevision`, applies the operations locally, validates the complete
materialized graph, computes a new revision, and presents the revised full plan for approval. A
delta never approves or executes the plan. Follow the outline's `revisionStrategy`: a small plan may
use a complete replacement `TASKS_JSON` when a broad rewrite affects most of the graph; medium and
large plans strongly prefer deltas, and a large plan is replaced only for a genuine near-total rewrite.
Never emit
`PLAN_REVISION_JSON` for a staged proposal; that marker is reserved for an accepted durable plan
when SquadDash separately injects its live revision protocol.

## DECOMPOSE_DECISION_JSON schema

If the user later approves a staged plan or chooses one of its host-owned execution actions in
free text, emit:

`DECOMPOSE_DECISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "revision": "revision supplied by SquadDash in the pending-plan context",
  "action": "execute-new-branch",
  "branch": "optional branch override"
}
```

Only emit a decision for a group already staged by SquadDash. If the user changes an unapproved
staged proposal's tasks, dependencies, validations, assignments, or other graph content, do not
emit this decision; follow `STAGED_PLAN_REVISION_JSON` above. `action` must be exactly one of `collect`,
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
  "commit": "exact abbreviated Git commit SHA from git rev-parse --short HEAD",
  "summary": "concise outcome",
  "remainingWork": [],
  "deferredWork": [
    {
      "requirement": "exact current-task requirement deliberately owned downstream",
      "reason": "why it belongs to the named downstream contract",
      "ownerTaskIds": ["GROUP-YYYYMMDD-LATER-TASK"]
    }
  ],
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
  ],
  "proofEvidence": [
    {
      "requirementId": "stable requirement ID from the approved task",
      "proofType": "exact proof type from the approved task",
      "summary": "what was actually observed",
      "artifacts": ["optional command, report, screenshot, or trace reference"]
    }
  ]
}
```

For implementation results, run `git rev-parse --short HEAD` after committing and copy that
command's output into `commit` exactly. Do not retype the SHA from memory or from the `git commit`
summary. SquadDash resolves the abbreviation and validates the full commit itself.

`status` must be `complete`, `partial`, or `failed`. Complete requires a new commit and passed
verification. Partial requires concrete `remainingWork` and never unlocks dependent tasks. SquadDash
validates the assignment, revision, Git commit, clean-worktree boundary, and verification evidence
before changing any plan status. `executionAttemptId` and `agentExecutions` are required only when
SquadDash supplies a verified roster assignment context for the current attempt. Report the supplied
attempt ID and the observable requested/actual roster handles. Do not report tool-call IDs or child
lineage: SquadDash owns and validates that internal evidence directly.
Every explicit current-task requirement is owned by the current task unless the approved graph
clearly assigns it to a named downstream task. Report every deliberate deferral in `deferredWork`;
use an empty array when there are none. A complete result may not silently omit a requirement or
invent downstream ownership that is absent from the approved task contracts.

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

When an approval request is active and the user responds in free form, distinguish replacement of
a completed task result, bounded additional work on the accumulated result, and unrelated work.
Never modify reviewed plan work during the classification turn. When the user wants to withdraw
acceptance of a completed task's own result so it can be replaced or corrected, emit:

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

Do not use `request-rework` merely because the user says "change" or names code created by a
completed task. Use it only when that task's accepted result itself must be reopened.

When the completed reviewed tasks should remain complete and the user wants bounded additional
work before approving their accumulated result, emit:

`PLAN_GATE_RESPONSE_JSON:`

```json
{
  "planId": "GROUP-YYYYMMDD",
  "gateId": "GROUP-YYYYMMDD-GATE-001",
  "revision": "revision supplied by SquadDash",
  "requestVersion": 1,
  "disposition": "add-amendment",
  "taskIds": ["GROUP-YYYYMMDD-001", "GROUP-YYYYMMDD-002"],
  "title": "Add the requested integration correction",
  "instructions": "The user's bounded additional work"
}
```

`add-amendment` is appropriate for new integration, cleanup, hardening, compatibility, or UX
work discovered during review; a correction spanning multiple reviewed tasks; or work performed
during the approval pause that must be incorporated and verified. It creates a new task after the
named reviewed tasks and returns to the same approval gate. It does not erase or reopen their
accepted results. Omit `taskIds` only when the amendment applies to the entire reviewed boundary.

Use `unrelated` when the request is separate work and `clarification` when the intended approval
task or requested change is ambiguous; those dispositions omit `taskIds` and `instructions` and
leave the approval unchanged. Always use the exact current request version. A rework response may
name only completed tasks listed in the gate's reviewed boundary.

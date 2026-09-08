---
configured: true
interval: 0.1
timeout: 120
description: "Executing Plan — runs dependency-eligible tasks from one approved plan"
commands: [stop_loop]
---

<maintenance_inbox_reminder>
You are running in maintenance mode — the user is not present. Follow these rules:

1. Do NOT emit QUICK_REPLIES_JSON. Live quick replies require the user to be present and will block the queue.

2. Instead, embed any decision points as deferred actions in your INBOX_MESSAGE_JSON block.
   Use the `actions` array so the user can make choices later when they review the message.

3. Each action MUST have a self-contained `prompt` (except routeMode `"done"` which is a dismiss).
   Write the prompt as a complete briefing — include file paths, class names, method names, symptoms, and all
   context you discovered. Prefer stable identifiers (class/method names) over line numbers, which go stale.
   Assume the reader has NO memory of this session.
   Each action may also include an optional `"hint"` field — a short tooltip string shown when the user hovers
   over the button. For routeMode `"done"` actions, including a hint is encouraged.

4. For report-only tasks: send findings as an inbox message with `"from": "argus-weld"`.
   Subject = short descriptive title (no 'Maintenance Report:' prefix, no date). Body = full Markdown report. Actions = any follow-up choices.
   Put INBOX_MESSAGE_JSON on a bare top-level line; do not wrap it in markdown code fences.

Example actions array:
  "actions": [
    { "label": "Fix this", "routeMode": "start_named_agent", "targetAgent": "arjun-sen",
      "prompt": "Arjun: during maintenance on [date] I found X in [file:line]. Please fix it. [full context]" },
    { "label": "Add to backlog", "routeMode": "start_coordinator",
      "prompt": "Add a task: [description discovered during maintenance on [date]]" },
    { "label": "Dismiss", "routeMode": "done", "hint": "Acknowledge — no action will be taken" }
  ]
</maintenance_inbox_reminder>

---

## Approved Plan Execution — Iteration {{iteration}}

You are Argus Weld executing the single approved plan supplied in the host context below.

SquadDash has already selected the exact dependency-eligible task and supplies its approved contract,
plan intent, revision, and upstream handoffs in this turn's host context. Do not scan or select work
from `tasks.md`; it is a compatibility projection, not the execution source. Work only on the exact
host-assigned task. SquadDash owns every task status marker: do not edit, stage, or commit `tasks.md`.

For the assigned task:

1. Complete the step fully and correctly.
2. For implementation steps, commit only source changes to the branch specified in the group header.
   For a host-marked verification-only step, do not create a commit; report the current evaluated HEAD.
   Never stage `tasks.md`.
3. Build must be green when done.
4. Do NOT emit QUICK_REPLIES_JSON.
5. Do NOT emit HOST_COMMAND_JSON or change the task marker.
6. End with exactly one host-owned result payload using the assigned group, task, and revision:

```

This object is specifically the `complete` + `changed` variant. Do not obtain a partial or failed
result by changing only its `status`: `completionKind` is required for `complete` and forbidden for
`partial` and `failed`; those non-complete variants must also omit `evaluatedCommit` and
`noOpEvidence`.
DECOMPOSE_STEP_RESULT_JSON:
{
  "groupId": "GROUP-YYYYMMDD",
  "taskId": "GROUP-YYYYMMDD-NNN",
  "revision": "revision from the group header",
  "executionAttemptId": "host-supplied attempt ID for assigned tasks",
  "status": "complete",
  "completionKind": "changed",
  "commit": "exact abbreviated Git commit SHA from git rev-parse --short HEAD",
  "summary": "one-sentence outcome retained for compatibility and compact surfaces",
  "completedWork": [
    "one concrete completed outcome; an item may contain more than one sentence",
    "another concrete completed outcome"
  ],
  "remainingWork": [],
  "deferredWork": [],
  "verification": {
    "status": "passed",
    "command": "exact build or test command",
    "summary": "what passed"
  },
  "agentExecutions": [
    {
      "requestedAgent": "exact plan-assigned roster handle",
      "actualPrimaryAgent": "same plan-assigned roster handle"
    }
  ]
}
```

If inspection proves that every task requirement was already satisfied at the host-recorded starting
snapshot and no source change is needed, do not create an empty commit. Return the same complete
envelope with `completionKind: "verified-no-op"`, `commit: null`, `evaluatedCommit` set to the exact
inspected HEAD, and a non-empty `noOpEvidence` array. Each evidence item must contain one exact
`requirement`, a concrete `observation`, and one or more repository `locations`. A no-op is a factual
completion claim: SquadDash accepts it only when HEAD still equals the task baseline, the worktree has
no task changes, and an independent Fact Checker verifies the whole task against the existing repository
state. If any task-owned work is missing, do not claim no-op; report it truthfully so the normal one-rework
policy can correct it.

After committing, run `git rev-parse --short HEAD` and copy that command's output into `commit`
exactly. Do not retype the SHA from memory or from the `git commit` summary. SquadDash resolves the
abbreviation to the full commit and validates it against the host-recorded task baseline.

When an assigned worker reports a commit, treat the SHA as untrusted handoff data. Resolve it
immediately with `git rev-parse --verify "<reported-sha>^{commit}"`. If Git rejects it, ask that same
worker to run `git rev-parse --short HEAD` and return the exact output, then verify the correction
before emitting `DECOMPOSE_STEP_RESULT_JSON`.

Whenever the host supplies an `executionAttemptId`, preserve it exactly, including for an explicitly
generic task. When the task has roster agent assignments, `agentExecutions` is also required and each
requested/actual handle must match one exact plan assignment. Omit `agentExecutions` for a generic
task. Do not report tool-call IDs or child lineage; SquadDash validates those host-owned values
directly.

Every explicit current-task requirement is owned by this task unless the approved plan clearly
assigns it to a named downstream task. If work is deliberately deferred, include one `deferredWork`
entry per requirement with `requirement`, `reason`, and `ownerTaskIds`. The named owners must be
downstream task IDs whose approved contracts actually own that work. Otherwise use `deferredWork: []`.
Never silently omit a requirement or assume that an unspecified later task will absorb it.

`completedWork` is the authoritative completion summary shown to users. Return it as a JSON array
with one independently readable item per completed outcome. An item may contain multiple sentences.
Do not combine the items into Markdown, and do not place bullet characters inside the strings.

Use `status: "partial"` when useful work was committed but the whole assigned task was not completed.
List the concrete unfinished work in `remainingWork`; never claim complete. Use `status: "failed"`
when the step made no safe progress. A partial result may include a commit, but if it does its
verification status must be `passed`.

**On failure:** write a concise narrative and emit a failed-result variant with `status: "failed"`,
no `completionKind`, `evaluatedCommit`, or `noOpEvidence`, no commit unless one was safely created,
concrete `remainingWork`, and truthful verification evidence. Preserve a host-supplied
`executionAttemptId`.
SquadDash will block the plan, persist the result, stop the loop, and offer recovery actions.

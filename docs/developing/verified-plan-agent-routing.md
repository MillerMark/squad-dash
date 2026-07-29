# Verified Plan Agent Routing

This document covers the full assignment transport pipeline used when a coordinator launches a verified roster agent from a plan step. It is intended for developers working on the SquadDash host, the Squad SDK, or the agent infrastructure layer.

---

## Overview

When a plan step assigns work to a named roster agent, SquadDash transports the assignment through the **prompt layer** — the agent's full instructions are embedded in the `prompt` argument of the background task launch call. The host validates that the agent actually received the right charter and produced observable evidence of the required context reads before accepting the work as complete.

---

## Routing Modes

Plan routing is explicit and fail-closed:

- **Assigned roster routing** uses `agentRoutingMode: "assigned"` with one or more
  `agentAssignments`. SquadDash resolves each handle against the active roster, creates a
  host-owned execution attempt, injects the complete charter and required context paths, and
  displays the roster identity only after the assignment envelope and charter verify.
- **Explicit generic routing** uses `agentRoutingMode: "generic"` with a recorded reason.
  SquadDash creates a generic execution attempt, permits exactly one coordinator-owned primary
  worker, prohibits children, and expects no `agentExecutions` roster claims. The worker retains
  **Temporary Agent** identity.
- **Ordinary interactive delegation** may prefer a roster member according to settings, but it is
  advisory unless a host-owned executable-plan attempt supplies the assignment authorization.

A generic worker is never upgraded to a roster identity because its prose resembles a charter or
because the coordinator supplies a roster-like name.

---

## 1. Assignment Envelope

The assignment envelope must appear on a **top-level line** in the worker's prompt as:

```
SQUADDASH_AGENT_ASSIGNMENT_JSON:
{"attemptId":"...","taskId":"...","revision":"...","agentHandle":"...","role":"...","allowGenericChildren":false,"capability":"...","charterSha256":"..."}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `attemptId` | `string` | Host-issued execution attempt ID. Must match the active attempt on the plan task. |
| `taskId` | `string` | Stable identifier of the plan task. |
| `revision` | `string` | Plan file revision hash being executed. |
| `agentHandle` | `string` | Roster key of the target agent (e.g. `mira-quill`). |
| `role` | `string` | Role description, must match the host authorization record exactly. |
| `allowGenericChildren` | `bool` | `false` = the worker must not spawn child workers. |
| `capability` | `string` | Opaque capability token issued by the host. |
| `charterSha256` | `string` | SHA-256 of the **normalized** charter text (see §2). |

`BackgroundAgentLaunchInfoResolver` parses the envelope by scanning for `SQUADDASH_AGENT_ASSIGNMENT_JSON:`, then extracting the first balanced JSON object that follows it.

---

## 2. Charter Transport Normalization

Charters are typically authored on Windows with CRLF line endings and a trailing newline. The prompt transport layer (and any clipboard or file copy path) may normalize line endings and strip trailing newlines.

**Normalization rules** (applied by `NormalizeCharterTransport` in `BackgroundAgentLaunchInfoResolver`):

1. Replace all `\r\n` sequences with `\n`.
2. Replace any remaining bare `\r` with `\n`.
3. Strip all trailing `\n` characters (i.e., `TrimEnd('\n')`).

`charterSha256` is computed against the **normalized** charter text. When verifying, the resolver:

1. Reads the charter file from disk.
2. Normalizes it with the same rules.
3. Verifies the normalized file SHA matches `charterSha256` in the envelope.
4. Verifies the normalized prompt contains the normalized charter as a substring.

Both checks must pass for `IsVerifiedRosterAssignment` to be `true`.

**Test coverage** (`SquadDash.Tests/PlanAgentExecutionContractIntegrationTests.cs`):

- `CrlfCharter_AfterTransportNormalization_IsAccepted` — confirms a CRLF charter with trailing newline is accepted after normalization.
- `ContentModifiedCharter_AfterTransportNormalization_IsRejected` — confirms a single-character modification to the charter causes rejection even after normalization.

---

## 3. `BackgroundAgentLaunchInfoResolver` — Named-Agent Resolution

`TryResolve` performs these steps:

1. Parses the `SQUADDASH_AGENT_ASSIGNMENT_JSON` envelope from the prompt.
2. Looks up the `attemptId`/`taskId`/`revision`/`agentHandle`/`capability` tuple in the `activeAttempt` authorizations.
3. Cross-checks `role` and `allowGenericChildren` against the authorization record.
4. Calls `PromptContainsAuthorizedCharter` to verify the charter SHA and substring containment.
5. Looks up the `agentHandle` in the live roster (case-insensitive, alphanumeric key normalization).

If all checks pass, `IsVerifiedRosterAssignment = true` and the agent's `DisplayName` and accent key are taken from the roster entry. If any check fails, the launch is still created but `IsVerifiedRosterAssignment = false` and `DisplayName` falls back to `"Temporary Agent"`.

---

## 4. `PlanAgentAssignmentValidator` — What It Checks

`Validate(taskId, revision, expected, attempt)` returns `null` on success and an error string on any failure. It checks, in order:

1. **Active attempt** — `attempt.TaskId` and `attempt.Revision` must match the arguments; `Status` must be `"active"`.
2. **No undeclared primaries** — `attempt.UnexpectedPrimaryToolCallIds` must be empty.
3. For each required assignment:
   - A host-observed launch with a non-empty `PrimaryToolCallId` must exist.
   - The assignment must have completed successfully (`CompletedAt` set, `Succeeded = true`).
   - **All required context paths must appear in `ObservedContextPaths`** (path-insensitive comparison via `PlanExecutionAttemptState.PathsEqual`). Paths are resolved to file names for the error message.
   - If `AllowGenericChildren = false`, `ChildToolCallIds` must be empty.

`ValidateWrapUp(...)` additionally checks that the coordinator's `DECOMPOSE_STEP_RESULT_JSON` payload correctly echoes the `executionAttemptId` and the `agentExecutions` array.

---

## 5. Required Context Reads

Every verified-roster worker **must** produce host-observed tool-call reads for the following paths before the host will accept the work as complete:

- `.squad/agents/<handle>/history.md`
- `.squad/decisions.md`

These reads must be **distinct, observable tool calls** — the host records them via `PlanExecutionEvidenceRecorder`. Merely claiming to have read the files is insufficient; the calls must appear in the host-observed tool call trace. If either path is missing, `Validate` returns an error such as:

```
Assignment 'mira-quill' did not produce host-observed reads for: history.md, decisions.md.
```

---

## 6. Coordinator Wrap-Up — `DECOMPOSE_STEP_RESULT_JSON`

After all workers complete, the coordinator emits a result block. For a verified-roster step, the payload must include:

| Field | Requirement |
|---|---|
| `executionAttemptId` | Must equal the host-supplied attempt ID exactly. |
| `agentExecutions` | Array with one entry per required assignment. |
| `agentExecutions[].requestedAgent` | The `agentHandle` declared in the plan assignment. |
| `agentExecutions[].actualPrimaryAgent` | The roster handle of the agent that ran (usually identical to `requestedAgent`). |

**Do not include** `primaryToolCallId`, `children`, or other host-internal fields in the coordinator result — these fields are populated from host evidence, not model-reported values, and model-provided values are intentionally non-authoritative.

---

## 7. Worked Example

### Assignment envelope (in worker prompt)

```
SQUADDASH_AGENT_ASSIGNMENT_JSON:
{"attemptId":"657b229530a04055ac4310e5a47a94c3","taskId":"ROUTEPROBE-20260729-002","revision":"1be54f689db9ae40","agentHandle":"mira-quill","role":"developer documentation author and verifier","allowGenericChildren":false,"capability":"<host-issued-capability>","charterSha256":"<normalized-charter-sha256>"}
```

### Worker obligations

1. Read `.squad/agents/mira-quill/history.md` (distinct tool call).
2. Read `.squad/decisions.md` (distinct tool call).
3. Do the assigned work.
4. Do **not** launch child workers (`allowGenericChildren: false`).

### Coordinator wrap-up payload (excerpt)

```json
{
  "executionAttemptId": "657b229530a04055ac4310e5a47a94c3",
  "agentExecutions": [
    {
      "requestedAgent": "mira-quill",
      "actualPrimaryAgent": "mira-quill"
    }
  ]
}
```

---

## 8. Source Files

| File | Purpose |
|---|---|
| `SquadDash/BackgroundAgentLaunchInfoResolver.cs` | Parses the envelope, verifies charter, resolves roster match |
| `SquadDash/PlanAgentAssignmentValidator.cs` | Validates context reads, lifecycle, wrap-up echo |
| `SquadDash/DecomposePlanningInstructions.cs` | Builds the prompt including the assignment block |
| `SquadDash.Tests/PlanAgentExecutionContractIntegrationTests.cs` | Integration tests for the full transport pipeline |

---

## 9. Live Identity Evidence

The `ROUTEPROBE-20260729` live plan verified both assigned roster identities through the production
prompt and event pipeline. The trace contained these host-resolved launch records:

```text
TaskLaunch.Captured requested=vesper-knox ... display=Vesper Knox ... assignedAgent=vesper-knox verified=True
TaskLaunch.Captured requested=mira-quill ... display=Mira Quill ... assignedAgent=mira-quill verified=True
```

The corresponding agent cards and transcript title bars displayed **Vesper Knox** and **Mira
Quill**. Persisted conversation state recorded `RosterIdentityVerified: true`; the same Mira thread
then reached `AgentThread.Lifecycle status=Completed`. A display name alone is not evidence—the
`assignedAgent` and `verified=True` fields must agree with the host-owned attempt.

---

## 10. Fail-Closed Symptoms

Expected rejection signals include:

- an invalid, stale, modified, or incomplete assignment envelope resolves with `verified=False`
  and retains **Temporary Agent** identity;
- a missing launch, failed worker, undeclared primary, forbidden child, or missing context read
  causes `PlanAgentAssignmentValidator.Validate` to return a specific error;
- a missing/wrong attempt ID, omitted assignment wrap-up, duplicate assignment, or mismatched
  requested/actual handle causes `ValidateWrapUp` to reject the result;
- SquadDash requests one bounded repair and blocks the plan if the repaired result still fails.

Model-reported tool-call IDs and child lineage are ignored for authority. SquadDash accepts those
facts only from its own launch and lifecycle evidence.

---

## 11. Interrupted-Plan Recovery

An interrupted plan remains durable across application restarts. Its plan record should retain the
interrupted task, last accepted task and commit, affected paths, execution progress, and recovery
state. Accepted task provenance (`commit`, `completedAt`, and `completionSummary`) must survive a
resume; starting the next task must not reconstruct completed tasks from `tasks.md` and erase it.

For a clean fresh attempt:

1. Stop the loop and inspect `git status`, the candidate commit, its parent, and changed paths.
2. Adopt an orphan commit only after its task/revision, single-commit boundary, source-only paths,
   and required verification succeed. Otherwise preserve or revert it only with explicit approval.
3. Leave `.squad/tasks.md` as host-owned state; do not include it in a task commit.
4. Ensure no unrelated source changes remain, then use **Resume Plan**. If an earlier task was
   safely adopted, do not use **Continue / Retry Task** for that completed task.
5. SquadDash creates a new execution-attempt ID for the pending task. Evidence from a superseded or
   prior attempt must fail validation.

---

## 12. Deterministic Verification

Run the focused routing and wrap-up contract tests:

```powershell
dotnet test SquadDash.Tests\SquadDash.Tests.csproj --no-restore --filter "FullyQualifiedName~PlanAgentAssignmentValidatorTests|FullyQualifiedName~PlanAgentExecutionContractIntegrationTests" --verbosity minimal
```

Then run the full suite:

```powershell
dotnet test SquadDash.Tests\SquadDash.Tests.csproj --no-restore --verbosity minimal
```

The focused coverage includes CRLF-to-LF charter transport, trailing-newline loss, modified-charter
rejection, stale attempts, undeclared primaries, forbidden children, required context reads,
host-owned internal evidence, and requested/actual roster-handle correlation.

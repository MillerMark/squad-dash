---
configured: true
interval: 0.1
timeout: 60
description: "Filtered Tasks — picks the top open task, implements it, marks it done, repeats"
commands: [stop_loop]
---

# Filtered Tasks

You are running as part of a SquadDash autonomous loop. **Each iteration must complete exactly one task** from `.squad/tasks.md`, then end this iteration's response. Do **not** stop the loop after a successful task; the next iteration will pick up the next task.

> Iteration: {{iteration}}

The iteration number above tells you which iteration this is. In the sequence. Elsewhere I may have a setting that tells you the maximum of iterations. You can compare this number against Max, and if it is you can issue a stop command.

## Step 1 — Find the next **filtered** task

Read `.squad/tasks.md`. Find the **first unchecked (`- [ ]`) item** that is NOT owned by `*(Owner: User)*` and that contains the words or otherwise matches the filter instructions specified below. Work top-to-bottom; higher sections (🔴 High, 🟡 Mid) take priority over lower ones (🟢 Low).

Never execute an item inside a `<!-- decompose-group: ... -->` block or an item carrying
`Group: ... | Branch: ... | Priority: ...` metadata. Those tasks are host-owned structured plans
and may run only through SquadDash's **Executing Plan** engine. If every matching item belongs to
a structured plan, follow Step 2 and stop this generic loop.

[**FILTER**]

## Step 2 — If NO actionable tasks remain

No unchecked tasks remain (or all remaining tasks are Owner: User). Do the following and nothing else:

1. Append this block at the **very end** of your response (after all other content):
   ```
   HOST_COMMAND_JSON:
   [
     { "command": "stop_loop" }
   ]
   ```
2. Only emit `stop_loop` in this no-actionable-task case. Do not emit it after successfully completing a task.
3. Do not attempt any further work this iteration.

## Step 3 — If a task IS found, implement it fully

1. Read `.squad/routing.md` to identify the correct owner/agent for this task.
2. Delegate to that agent and have them complete the work — implementation, decisions, tests, as appropriate.
3. For **"define…" or "decide…" or "architecture" tasks**: document the decision in `.squad/decisions.md` (create if missing) and update relevant architecture docs, then consider the task done.
4. For **implementation tasks**:
   - Run the auto-detected `{{build_command}}` when it is available and verify the build passes.
   - Add or update focused tests when behavior changes, and run the relevant tests before marking the task complete.
   - If build or test verification fails, do not commit or advance to another task; report the failure and stop this iteration.
5. After work is verified, mark the task `[x]` in `.squad/tasks.md`, move it to the "Recently Completed" section at the bottom, then create exactly one atomic commit containing the implementation, tests, and task-list update. Use a clear, descriptive message and include the trailer: `{{copilot_trailer}}`
6. Report a one-line summary of what was done.
7. Do **not** append `HOST_COMMAND_JSON` after completing a task. Simply finish your response and let SquadDash schedule the next iteration.

## Step 4 — Verify tests

Confirm the tests appropriate to this task pass. Do not add tests mechanically when the task has no behavior change, but never skip relevant regression coverage.

## Step 5 — Surface human decisions

If there are any important decisions that need to be made by a human at this point, put those up as quick reply buttons.

## Reference material

- `.squad/tasks.md` — the full task backlog
- `.squad/routing.md` — who owns what
- `.squad/team.md` — squad roster
- `.squad/decisions.md` — architectural decisions log


---
configured: true
options:
  after_task_header:
    type: group
    label: "After Task Completes:"
  build_verify:
    value: true
    type: bool
    label: "Verify build"
    hint: "Run the auto-detected build command and confirm it passes before committing"
  test_after_task:
    value: true
    type: bool
    label: "Write tests"
    hint: "Write comprehensive tests after each implementation task"
  commit_after_task:
    value: never
    type: enum
    choices: [always, never, ask]
    label: "Commit"
    hint: "When to automatically commit completed work"
  loop_params_header:
    type: group
    label: "Loop Parameters:"
  interval:
    value: 1
    type: int
    label: "Interval (min)"
    hint: "Minutes to wait between iterations"
  timeout:
    value: 60
    type: int
    label: "Timeout (min)"
    hint: "Max minutes per iteration before aborting"
  max_iterations:
    value: 0
    type: int
    label: "Max iterations"
    hint: "Stop after this many iterations. 0 = unlimited."
description: "Filtered Tasks — picks the top open task, implements it, marks it done, repeats"
commands: [stop_loop]
---

# Filtered Tasks

You are running as part of a SquadDash autonomous loop. **Each iteration must complete exactly one task** from `.squad/tasks.md`, then stop. The next iteration will pick up the next task.

> Iteration: {{iteration}}

## Step 1 — Find the next **filtered** task

Read `.squad/tasks.md`. Find the **first unchecked (`- [ ]`) item** that is NOT owned by `*(Owner: User)*` and that contains the words or otherwise matches the filter instructions specified below. Work top-to-bottom; higher sections (🔴 High, 🟡 Mid) take priority over lower ones (🟢 Low).

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
2. Do not attempt any further work this iteration.

## Step 3 — If a task IS found, implement it fully

1. Read `.squad/routing.md` to identify the correct specialist for this task.
2. Route to and adopt the role of that specialist per their charter. Complete the work — implementation, decisions, tests, as appropriate.
3. For **"define…" or "decide…" or "architecture" tasks**: document the decision in `.squad/decisions.md` (create if missing) and update relevant architecture docs, then consider the task done.
4. For **implementation tasks**:
{{build_instruction}}{{commit_instruction}}5. After work is complete, mark the task `[x]` in `.squad/tasks.md` and move it to the "Recently Completed" section at the bottom.
6. Report a one-line summary of what was done.

## Step 4 — Write tests (if applicable)

{{test_instruction}}

## Step 5 — Surface human decisions

If there are any important decisions that need to be made by a human at this point, put those up as quick reply buttons.

## Reference material

- `.squad/tasks.md` — the full task backlog
- `.squad/routing.md` — who owns what
- `.squad/team.md` — squad roster
- `.squad/decisions.md` — architectural decisions log


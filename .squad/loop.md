---
configured: true
interval: 1
timeout: 30
description: "General task backlog runner — picks the top open task, implements it, marks it done, repeats"
commands: [stop_loop]
---

# SquadDash Task Backlog Loop

You are running as part of a SquadDash autonomous loop. **Each iteration must complete exactly one task** from `.squad/tasks.md`, then stop. The next iteration will pick up the next task.

## Step 1 — Find the next task

Read `.squad/tasks.md`. Find the **first unchecked (`- [ ]`) item** that is NOT owned by `*(Owner: User)*`. Work top-to-bottom; higher sections (🔴 Critical, 🟠 High, 🟡 Mid) take priority over lower ones (🟢 Low).

## Step 2 — If NO actionable tasks remain

No unchecked tasks remain (or all remaining tasks are Owner: User). Do the following and nothing else:

1. Include this block in your response:
   `{"squadash": {"command": "stop_loop", "notification": "🎉 Task backlog is clear — loop stopped."}}`
2. Do not attempt any further work this iteration.

## Step 3 — If a task IS found, implement it fully

1. Read `.squad/routing.md` to identify the correct owner/agent for this task.
2. Delegate to or become that agent. Complete the work — implementation, decisions, tests, commit.
3. For **"define…" or "decide…" or "architecture" tasks**: document the decision in `.squad/decisions.md` (create if missing) and update relevant architecture docs, then consider the task done.
4. For **implementation tasks**: build (`dotnet build SquadDash\SquadDash.csproj -c Debug`), verify tests pass, commit with the `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` trailer. Report the commit SHA.
5. After work is complete, mark the task `[x]` in `.squad/tasks.md` and move it to the "Recently Completed" section at the bottom.
6. Report a one-line summary of what was done.

## Step 4 — Create comprehensive test cases for what has been built in this turn.

## Reference material

- `.squad/tasks.md` — the full task backlog
- `.squad/routing.md` — who owns what
- `.squad/team.md` — squad roster
- `.squad/decisions.md` — architectural decisions log

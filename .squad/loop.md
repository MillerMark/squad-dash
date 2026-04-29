---
configured: true
interval: 1
timeout: 30
description: "RC mobile task runner — implements open RC tasks one by one until all are complete"
commands: [stop_loop]
---

# RC Mobile Implementation Loop

You are running as part of a SquadDash autonomous loop. **Each iteration must complete exactly one task** from the RC/mobile backlog, then stop. The next iteration will pick up the next task.

## Step 1 — Find the next task

Read `.squad/tasks.md`. Find the **first unchecked (`- [ ]`) item** matching any of these categories (in priority order, top-to-bottom as listed):

- 🟡 Mid: any "RC" task
- 🟢 Low: "Phone push notifications" task
- 🟢 Low: any "RC mobile" task
- 🟢 Low: any "RC —" task

## Step 2 — If NO matching tasks remain

All RC tasks are complete. Do the following and nothing else:

1. Include this push notification marker in your response so the user is notified on their phone:
   `{"notification": "🎉 All RC mobile tasks are complete. You can stop the loop."}`
2. Use a quick reply button so the user can take action:
   **Label:** "All RC tasks done — stop the loop"
3. Do not attempt any further work this iteration.

## Step 3 — If a task IS found, implement it fully

1. Read `.squad/routing.md` to identify the correct owner/agent for this task.
2. Delegate to that agent. Complete the work — implementation, decisions, tests, commit.
3. For **"define…" or "decide…" policy tasks**: document the decision in `.squad/decisions.md`
   and update `.squad/rc-mobile-architecture.md` if relevant, then consider the task done.
4. For **implementation tasks**: build (`dotnet build SquadDash\SquadDash.csproj -c Debug`),
   verify tests pass, commit with the `Co-authored-by: Copilot` trailer. Report the commit SHA.
5. After work is complete, mark the task `[x]` in `.squad/tasks.md` and move it to
   the "Recently Completed" section at the bottom.
6. Report what was done.

## Reference material

- `.squad/rc-mobile-architecture.md` — architectural decisions and findings for all RC/mobile work
- `.squad/routing.md` — who owns what
- `.squad/team.md` — squad roster
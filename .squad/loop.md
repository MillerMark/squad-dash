---
configured: true
interval: 10
timeout: 60
description: "My loop"
commands: [stop_loop]
---

# Loop Instructions

You are running in autonomous loop mode. On each iteration:

1. Check for outstanding tasks in `.squad/tasks.md`
2. Pick the highest-priority unchecked item
3. Work on it and mark it `[x]` when done
4. Report what you accomplished

When all tasks are complete (or all remaining tasks are owned by User), stop the loop:

```
HOST_COMMAND_JSON:
[
  { "command": "stop_loop" }
]
```

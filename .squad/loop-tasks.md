---
configured: true
interval: 10
timeout: 30
description: "Work through open tasks in .squad/tasks.md"
---

# Loop Instructions

You are running in autonomous loop mode. On each iteration:

1. Check for outstanding tasks in `.squad/tasks.md`
2. Pick the highest-priority unchecked item
3. Work on it and mark it `[x]` when done
4. Report what you accomplished

Stop looping when all tasks are complete or when instructed.
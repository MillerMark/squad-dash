using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Defines the built-in <see cref="TriggeredPromptInjection"/> entries that ship with
/// SquadDash.  These are registered automatically in every workspace, so a brand-new
/// install picks up Squad conventions (tasks file location, priority format, etc.)
/// without the AI having to infer them from conversation history.
/// </summary>
internal static class BuiltInPromptInjections {

    // ── Tasks ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires when the user mentions tasks, todos, a backlog, or a checklist.
    /// Teaches the AI exactly where to put the tasks file and what format to use,
    /// so that a fresh install on any project works correctly on the first attempt.
    /// </summary>
    internal static readonly TriggeredPromptInjection Tasks = new(
        Id:      "builtin:tasks-guidance",
        Pattern: @"\b(task|tasks|todo|todos|to-do|to-dos|backlog|checklist|task\s+list|add\s+a\s+task|new\s+task|create\s+a\s+task)\b",
        InjectionText:
            """
            If the user is asking to create, add, update, view, or manage tasks or a task list:
            - The tasks file for this workspace lives at `{workspaceFolder}\.squad\tasks.md`
            - If the file does not exist yet, create it at that exact path — do not create it in a subfolder, repo root, or any other location
            - Use this priority-section format (emoji must match exactly):
              ## 🔴 High Priority
              ## 🟡 Mid Priority
              ## 🟢 Low Priority
            - Each open task is a line: `- [ ] Task description`
            - Completed tasks use `- [x]` and should be placed under a `## ✅ Done` section at the bottom
            - Owner tags are optional and written as ` *(Owner: agent-handle)*` at the end of the task line
            """);

    internal static IReadOnlyList<TriggeredPromptInjection> All { get; } = [
        Tasks,
    ];
}

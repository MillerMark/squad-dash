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
        // Voice dictation can split compound words: "to do", "to dos", "check list", "back log"
        Pattern: @"\b(task|tasks|todo|todos|to[\s\-]do|to[\s\-]dos|backlog|back\s+log|checklist|check\s+list|task\s+list|add\s+a\s+task|new\s+task|create\s+a\s+task)\b",
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
            - If a task has a description or additional context, add it as indented lines immediately below the task line (2-space indent). This description is shown in the Tasks panel hover preview. Example:
              - [ ] Fix the login timeout bug
                Happens when the session token expires mid-request. Check token refresh logic
                in AuthService and add a retry on 401 before showing the error to the user.
            """);

    // ── Code Health ────────────────────────────────────────────────────────

    /// <summary>
    /// Fires when the user mentions code health mode, idle tasks, or the while-you-were-away report.
    /// Teaches the AI where the code health config lives and what format it uses.
    /// </summary>
    internal static readonly TriggeredPromptInjection Maintenance = new(
        Id:      "builtin:code-health-guidance",
        Pattern: @"\b(code[\s-]?health|maintenance\s+mode|maintenance\s+task|maintenance\.md|idle\s+task|while\s+(I\s+was|you\s+were)\s+(away|gone|out)|background\s+task)\b",
        InjectionText:
            """
            For code health tasks that run during idle time:
            - The code health task file for this workspace is at `{workspaceFolder}\.squad\code-health.md`
            - When the AI is idle for the configured threshold, it runs tasks defined in that file
            - Each task has a `safety:` level: `report-only` (no file changes), `branch` (new branch per task), or `direct` (current branch)
            - Task frequency is controlled by `frequency: daily`, `frequency: per-commit`, or `frequency: always`
            - The code health agent's transcript records all activity during a code health session
            """);

    internal static IReadOnlyList<TriggeredPromptInjection> All { get; } = [
        Tasks,
        Maintenance,
    ];
}



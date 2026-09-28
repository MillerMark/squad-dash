namespace SquadDash;

/// <summary>
/// Maps only explicit bridge events to plan-iteration lease renewal. This intentionally avoids
/// timing guesses: thought-stream noise and usage telemetry do not count as useful progress.
/// </summary>
internal static class PlanIterationProgressPolicy
{
    internal static bool TryResolve(SquadSdkEvent evt, out LoopIterationProgressKind kind)
    {
        kind = evt.Type switch
        {
            "response_delta" when !string.IsNullOrWhiteSpace(evt.Chunk) =>
                LoopIterationProgressKind.CoordinatorOutput,
            "tool_start" or "tool_progress" or "tool_complete" =>
                LoopIterationProgressKind.ToolActivity,
            "subagent_started" or "subagent_message" or "subagent_message_delta" or
            "subagent_tool_start" or "subagent_tool_progress" or "subagent_tool_complete" or
            "subagent_completed" or "subagent_resumed" =>
                LoopIterationProgressKind.WorkerActivity,
            "task_complete" => LoopIterationProgressKind.VerificationActivity,
            _ => default,
        };

        return evt.Type switch
        {
            "response_delta" => !string.IsNullOrWhiteSpace(evt.Chunk),
            "tool_start" or "tool_progress" or "tool_complete" or
            "subagent_started" or "subagent_message" or "subagent_message_delta" or
            "subagent_tool_start" or "subagent_tool_progress" or "subagent_tool_complete" or
            "subagent_completed" or "subagent_resumed" or "task_complete" => true,
            _ => false,
        };
    }
}

using System;

namespace SquadDash;

/// <summary>
/// Separates local plan collection from actions that can start work or change branches.
/// Collection is an atomic host-owned persistence action and does not compete for the prompt.
/// </summary>
internal static class DecomposePlanActionDispatchPolicy
{
    internal static bool CanApplyWhilePromptRunning(string? action) =>
        string.Equals(action?.Trim(), "collect", StringComparison.OrdinalIgnoreCase);
}

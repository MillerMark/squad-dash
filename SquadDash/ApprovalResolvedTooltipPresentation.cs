using System;

namespace SquadDash;

/// <summary>
/// Pure-logic helper that builds tooltip text for resolved approval checkmarks.
/// Extracted from PlanViewerWindow for deterministic testability.
/// </summary>
internal static class ApprovalResolvedTooltipPresentation
{
    internal static string Build(PlanApprovalGate? gate, string location) =>
        Build(gate, location, DateTimeOffset.Now);

    internal static string Build(PlanApprovalGate? gate, string location, DateTimeOffset now)
    {
        var text = $"Human approval was granted {location}.";
        if (!string.IsNullOrWhiteSpace(gate?.ResolvedBy))
            text += $"\nApproved by {gate.ResolvedBy}.";
        if (gate?.ResolvedAt is { } resolvedAt)
            text += $"\n{StatusTimingPresentation.FormatRelativeTimestamp(resolvedAt, now)}";
        if (!string.IsNullOrWhiteSpace(gate?.ResolutionNote))
            text += $"\nNote: {gate.ResolutionNote}";
        return text;
    }
}

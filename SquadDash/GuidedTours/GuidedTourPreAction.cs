namespace SquadDash.GuidedTours;

/// <summary>
/// Parsed representation of a step's preAction field (e.g. "LoadLayout:myLayout").
/// </summary>
internal sealed record GuidedTourPreActionDescriptor(GuidedTourPreActionKind Kind, string? Argument = null)
{
    internal static GuidedTourPreActionDescriptor Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            return new GuidedTourPreActionDescriptor(GuidedTourPreActionKind.None);

        if (raw.StartsWith("LoadLayout:", StringComparison.OrdinalIgnoreCase))
            return new GuidedTourPreActionDescriptor(
                GuidedTourPreActionKind.LoadLayout,
                raw["LoadLayout:".Length..].Trim());

        if (raw.StartsWith("OpenPanel:", StringComparison.OrdinalIgnoreCase))
            return new GuidedTourPreActionDescriptor(
                GuidedTourPreActionKind.OpenPanel,
                raw["OpenPanel:".Length..].Trim());

        if (string.Equals(raw, "SaveLayout", StringComparison.OrdinalIgnoreCase))
            return new GuidedTourPreActionDescriptor(GuidedTourPreActionKind.SaveLayout);

        return new GuidedTourPreActionDescriptor(GuidedTourPreActionKind.None);
    }
}

internal enum GuidedTourPreActionKind
{
    None,
    SaveLayout,
    LoadLayout,
    OpenPanel,
}

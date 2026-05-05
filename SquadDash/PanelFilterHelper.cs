namespace SquadDash;

/// <summary>Shared text-filter utilities used by panel controllers.</summary>
internal static class PanelFilterHelper {

    /// <summary>
    /// Returns true when <paramref name="text"/> contains <paramref name="filter"/>
    /// (case-insensitive), or when the filter is empty.
    /// </summary>
    public static bool Matches(string text, string filter) =>
        string.IsNullOrEmpty(filter) ||
        text.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

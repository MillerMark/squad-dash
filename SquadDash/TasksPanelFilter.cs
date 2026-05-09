namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Pure, stateless filter-string parser for the Tasks panel owner/text filter box.
/// Extracted from <see cref="TasksPanelController"/> so it can be tested without WPF.
/// </summary>
internal static class TasksPanelFilter {

    /// <summary>
    /// Sentinel value placed in the owner-candidates list when the filter resolves to "@me"
    /// (i.e. show only tasks owned by the current user).
    /// </summary>
    internal const string UserOwnedSentinel = "\u0002USER_OWNED";

    /// <summary>
    /// Parses <paramref name="filter"/> into an optional owner candidate list and an optional text filter.
    /// <list type="bullet">
    ///   <item><c>""</c> or <c>"@"</c> alone → (null, "") — show everything</item>
    ///   <item><c>"@me"</c> — exact match → ([<see cref="UserOwnedSentinel"/>], "") — user-owned filter</item>
    ///   <item><c>"@handle"</c> — exact match → ([resolvedName], "") — owner-only filter</item>
    ///   <item><c>"@partial"</c> — prefix match → ([name1, name2, …], "") — all matching agents</item>
    ///   <item><c>"@handle text"</c> → (candidates, "text") — both must match</item>
    ///   <item><c>"text"</c> → (null, "text") — plain text filter</item>
    /// </list>
    /// </summary>
    internal static (IReadOnlyList<string>? ownerCandidates, string textFilter) Parse(
        string filter,
        IReadOnlyList<SquadTeamMember>? roster) {

        if (string.IsNullOrEmpty(filter))
            return (null, string.Empty);

        if (!filter.StartsWith('@'))
            return (null, filter);

        // Extract handle = first token after '@'.
        int spaceIdx = filter.IndexOf(' ');
        string handle    = spaceIdx < 0 ? filter[1..] : filter[1..spaceIdx];
        string remaining = spaceIdx < 0
            ? string.Empty
            : string.Join(' ', filter[(spaceIdx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Bare "@" (no handle yet) — show all tasks.
        if (string.IsNullOrEmpty(handle))
            return (null, remaining);

        // "@me" — exact match resolves immediately.
        if (string.Equals(handle, "me", StringComparison.OrdinalIgnoreCase))
            return (new[] { UserOwnedSentinel }, remaining);

        // Collect candidates: roster prefix matches + "@me" sentinel when prefix starts with "m".
        var candidates = new List<string>();

        if ("me".StartsWith(handle, StringComparison.OrdinalIgnoreCase))
            candidates.Add(UserOwnedSentinel);

        if (roster is not null) {
            // Try exact handle match first — if found, return only that agent.
            foreach (var member in roster) {
                var memberHandle = MemberHandle(member);
                if (string.Equals(memberHandle, handle, StringComparison.OrdinalIgnoreCase))
                    return (new[] { member.Name }, remaining);
            }

            // Prefix match — collect all agents whose handle starts with the typed prefix.
            foreach (var member in roster) {
                var memberHandle = MemberHandle(member);
                if (memberHandle.StartsWith(handle, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(member.Name);
            }
        }

        if (candidates.Count > 0)
            return (candidates, remaining);

        // Unresolved handle — treat the whole filter as plain text.
        return (null, filter);
    }

    private static string MemberHandle(SquadTeamMember member)
        => member.FolderPath is not null
            ? Path.GetFileName(member.FolderPath)
            : member.Name.ToLowerInvariant().Replace(" ", "-");
}

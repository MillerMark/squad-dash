namespace SquadDash;

internal static class DecomposeWorktreePolicy
{
    internal static bool HasOnlyAllowedChanges(
        string? porcelainStatus,
        IReadOnlyCollection<string> allowedRepositoryRelativePaths,
        out IReadOnlyList<string> disallowedPaths)
    {
        var allowed = allowedRepositoryRelativePaths
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disallowed = new List<string>();
        foreach (var rawLine in (porcelainStatus ?? string.Empty)
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4) continue;
            var path = rawLine[3..].Trim().Trim('"');
            var renameSeparator = path.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (renameSeparator >= 0) path = path[(renameSeparator + 4)..];
            path = Normalize(path);
            if (!allowed.Contains(path)) disallowed.Add(path);
        }
        disallowedPaths = disallowed;
        return disallowed.Count == 0;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}

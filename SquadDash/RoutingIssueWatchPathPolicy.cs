using System;
using System.IO;

namespace SquadDash;

internal static class RoutingIssueWatchPathPolicy {
    public static bool IsRelevantPath(string squadFolderPath, string? fullPath) {
        if (string.IsNullOrWhiteSpace(squadFolderPath) || string.IsNullOrWhiteSpace(fullPath))
            return false;

        var normalizedSquadFolder = Normalize(squadFolderPath);
        var normalizedPath = Normalize(fullPath);
        if (!normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;

        var squadPrefix = normalizedSquadFolder + Path.DirectorySeparatorChar;
        if (!string.Equals(normalizedPath, normalizedSquadFolder, StringComparison.OrdinalIgnoreCase) &&
            !normalizedPath.StartsWith(squadPrefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var relativePath = Path.GetRelativePath(normalizedSquadFolder, normalizedPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (string.Equals(relativePath, "team.md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relativePath, "routing.md", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3 &&
               string.Equals(segments[0], "agents", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[2], "charter.md", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

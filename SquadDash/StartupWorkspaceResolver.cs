using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace SquadDash;

internal static class StartupWorkspaceResolver {
    public static string? Resolve(
        string? startupFolder,
        string? lastOpenedFolder,
        string? applicationRoot) {
        // If an explicit startup folder was provided (e.g. from --folder / shell context menu),
        // use it directly. Do not fall through to lastOpenedFolder just because the explicit
        // folder doesn't look like a workspace root — that would silently route to the wrong
        // already-open workspace and discard the user's intent.
        if (!string.IsNullOrWhiteSpace(startupFolder) && Directory.Exists(startupFolder))
            return NormalizePath(startupFolder);

        // No explicit folder provided — use heuristics to pick the best candidate.
        var candidates = new List<string?>(2) {
            lastOpenedFolder,
            applicationRoot
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? fallback = null;

        foreach (var candidate in candidates) {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                continue;

            var normalized = NormalizePath(candidate);
            if (!seen.Add(normalized))
                continue;

            fallback ??= normalized;
            if (LooksLikeWorkspaceRoot(normalized))
                return normalized;
        }

        return fallback;
    }

    public static bool LooksLikeWorkspaceRoot(string folderPath) {
        try {
            if (Directory.Exists(Path.Combine(folderPath, ".squad")) ||
                Directory.Exists(Path.Combine(folderPath, ".git"))) {
                return true;
            }

            return Directory.EnumerateFiles(folderPath, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(folderPath, "*.slnx", SearchOption.TopDirectoryOnly).Any();
        }
        catch {
            return false;
        }
    }

    public static string NormalizePath(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

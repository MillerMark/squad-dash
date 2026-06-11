using System.IO;

namespace SquadDash;

internal static class WorkspacePaths {
    /// <summary>
    /// Canonicalises a workspace folder path: resolves to an absolute path and
    /// strips any trailing directory separators.  Returns <see cref="string.Empty"/>
    /// for null or whitespace-only input; callers apply their own policy for that case.
    /// </summary>
    internal static string NormalizeFolder(string? folder) {
        if (string.IsNullOrWhiteSpace(folder)) return string.Empty;
        return Path.GetFullPath(folder.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

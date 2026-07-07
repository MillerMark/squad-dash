using System;
using System.IO;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Determines whether a workspace folder contains any user-created files,
/// or only Squad/SquadDash scaffolding that was auto-generated during squad init.
/// </summary>
internal static class WorkspaceEmptyDetector
{
    /// <summary>
    /// File/folder name patterns that are auto-created by Squad init and do NOT
    /// count as user content. All comparisons are case-insensitive.
    /// Keep this list in sync whenever new scaffold files are added.
    /// </summary>
    internal static readonly string[] AllowlistedNames = [
        ".squad",
        ".github",
        ".git",
        ".copilot",
        "node_modules",
        "package.json",
        "package-lock.json",
        ".mcp.json",
        ".gitattributes",
        ".gitignore",
        ".gitmodules",
        ".editorconfig",
        ".env",
        ".env.local",
    ];

    /// <summary>
    /// Returns <c>true</c> if the workspace folder contains no user files —
    /// i.e. every entry is in the allowlist.
    /// Returns <c>false</c> if the folder doesn't exist or contains user content.
    /// </summary>
    internal static bool IsEmpty(string workspaceFolder)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder) || !Directory.Exists(workspaceFolder))
            return true;

        var entries = Directory.EnumerateFileSystemEntries(workspaceFolder, "*", SearchOption.TopDirectoryOnly);
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (!IsAllowlisted(name))
                return false;
        }
        return true;
    }

    private static bool IsAllowlisted(string name) =>
        AllowlistedNames.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}

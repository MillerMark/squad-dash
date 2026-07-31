using System;
using System.IO;

namespace SquadDash;

internal sealed record AgentWorktreePresentation(string Name, string RootPath);

/// <summary>
/// Resolves an agent working directory to a linked Git worktree that is distinct from the
/// workspace currently displayed by SquadDash. The UI must only claim worktree isolation when
/// the filesystem provides authoritative evidence via a worktree <c>.git</c> pointer file.
/// </summary>
internal static class AgentWorktreePresentationResolver
{
    internal static AgentWorktreePresentation? Resolve(
        string? workingDirectory,
        string? activeWorkspaceDirectory,
        bool isAgentActive)
    {
        if (!isAgentActive || string.IsNullOrWhiteSpace(workingDirectory))
            return null;

        string candidate;
        try
        {
            candidate = Path.GetFullPath(workingDirectory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Directory.Exists(candidate))
            return null;

        var worktreeRoot = FindLinkedWorktreeRoot(candidate);
        if (worktreeRoot is null)
            return null;

        if (PathsEqual(worktreeRoot, activeWorkspaceDirectory))
            return null;

        var name = new DirectoryInfo(worktreeRoot).Name;
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new AgentWorktreePresentation(name, worktreeRoot);
    }

    private static string? FindLinkedWorktreeRoot(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            var gitMarker = Path.Combine(current.FullName, ".git");
            if (File.Exists(gitMarker))
            {
                try
                {
                    var marker = File.ReadAllText(gitMarker).Trim();
                    if (marker.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                        return current.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            }

            // A .git directory identifies a main checkout, not a linked worktree.
            if (Directory.Exists(gitMarker))
                return null;

            current = current.Parent;
        }

        return null;
    }

    private static bool PathsEqual(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right.Trim())),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

using System;
using System.IO;

namespace SquadDash;

internal sealed record WorkspaceIdentityPresentation(
    string WorkspaceFolder,
    string? RepositoryRoot,
    string? Branch)
{
    internal string? GitDirectory { get; init; }
    internal string? GitConfigPath { get; init; }

    internal static WorkspaceIdentityPresentation Resolve(string workspaceFolder, string? branch)
    {
        var normalizedWorkspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceFolder));
        var repository = FindRepository(normalizedWorkspace);
        return new WorkspaceIdentityPresentation(
            normalizedWorkspace,
            repository?.Root,
            string.IsNullOrWhiteSpace(branch)
                ? ReadBranch(repository?.GitDirectory)
                : branch.Trim())
        {
            GitDirectory = repository?.GitDirectory,
            GitConfigPath = repository?.CommonGitDirectory is null
                ? null
                : Path.Combine(repository.CommonGitDirectory, "config"),
        };
    }

    internal string BuildTooltip(bool includeRevealHint)
    {
        var repository = RepositoryRoot ?? "(not a Git repository)";
        var branch = Branch ?? "(none)";
        var details = $"Workspace: {WorkspaceFolder}\nRepository root: {repository}\nBranch: {branch}";
        return includeRevealHint
            ? details + "\n\nClick to reveal the workspace in Explorer."
            : details + "\n\nClick for branch options.";
    }

    private static RepositoryLocation? FindRepository(string workspaceFolder)
    {
        try
        {
            var directory = new DirectoryInfo(workspaceFolder);
            while (directory is not null)
            {
                var gitMarker = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(gitMarker))
                    return new RepositoryLocation(directory.FullName, gitMarker, ResolveCommonGitDirectory(gitMarker));
                if (File.Exists(gitMarker))
                {
                    var marker = File.ReadAllText(gitMarker).Trim();
                    const string gitDirPrefix = "gitdir:";
                    if (marker.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var gitDirectory = marker[gitDirPrefix.Length..].Trim();
                        if (!Path.IsPathRooted(gitDirectory))
                            gitDirectory = Path.GetFullPath(Path.Combine(directory.FullName, gitDirectory));
                        return new RepositoryLocation(
                            directory.FullName,
                            gitDirectory,
                            ResolveCommonGitDirectory(gitDirectory));
                    }

                    return new RepositoryLocation(directory.FullName, null, null);
                }
                directory = directory.Parent;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ResolveCommonGitDirectory(string gitDirectory)
    {
        try
        {
            var commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
            if (!File.Exists(commonDirectoryFile))
                return gitDirectory;

            var commonDirectory = File.ReadAllText(commonDirectoryFile).Trim();
            if (string.IsNullOrWhiteSpace(commonDirectory))
                return gitDirectory;
            return Path.IsPathRooted(commonDirectory)
                ? Path.GetFullPath(commonDirectory)
                : Path.GetFullPath(Path.Combine(gitDirectory, commonDirectory));
        }
        catch
        {
            return gitDirectory;
        }
    }

    private static string? ReadBranch(string? gitDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gitDirectory))
                return null;

            var headPath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headPath))
                return null;

            var content = File.ReadAllText(headPath).Trim();
            const string branchPrefix = "ref: refs/heads/";
            if (content.StartsWith(branchPrefix, StringComparison.Ordinal))
                return content[branchPrefix.Length..].Trim();
            return content.Length >= 7 ? $"(detached {content[..7]})" : "(detached)";
        }
        catch
        {
            return null;
        }
    }

    private sealed record RepositoryLocation(
        string Root,
        string? GitDirectory,
        string? CommonGitDirectory);
}

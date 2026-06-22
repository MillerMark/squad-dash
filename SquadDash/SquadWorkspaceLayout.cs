using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SquadDash;

internal sealed record SquadWorkspaceLayout(
    string ActiveDirectory,
    string ProjectSquadFolderPath,
    string TeamSquadFolderPath,
    string DirectoryName,
    bool IsRemote,
    string? StateBackend,
    string? StateLocation,
    string? ProjectKey,
    string ResolutionReason) {

    public string TeamFilePath => Path.Combine(TeamSquadFolderPath, "team.md");
    public string ProjectConfigPath => Path.Combine(ProjectSquadFolderPath, "config.json");
}

internal static class SquadWorkspaceLayoutResolver {
    private static readonly string[] SquadDirectoryNames = [".squad", ".ai-team"];

    public static string ResolveTeamFilePath(string activeDirectory, params string[] relativeSegments) {
        var normalizedDirectory = Path.GetFullPath(activeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var teamSquadFolder = Resolve(normalizedDirectory)?.TeamSquadFolderPath
            ?? Path.Combine(normalizedDirectory, ".squad");
        return Path.Combine(new[] { teamSquadFolder }.Concat(relativeSegments).ToArray());
    }

    public static SquadWorkspaceLayout? Resolve(string activeDirectory) {
        if (string.IsNullOrWhiteSpace(activeDirectory))
            return null;

        var normalizedDirectory = Path.GetFullPath(activeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolved = FindSquadDirectory(normalizedDirectory);
        if (resolved is null)
            return null;

        var (projectSquadDir, directoryName, reason) = resolved.Value;
        var config = LoadConfig(projectSquadDir);
        var teamRoot = config?.TeamRoot;
        var teamSquadDir = projectSquadDir;
        var isRemote = false;
        var resolutionReason = reason;

        var isExternalState = config is not null &&
            string.Equals(config.StateLocation, "external", StringComparison.OrdinalIgnoreCase);
        if (isExternalState) {
            var externalStateDir = TryResolveExternalStateDir(config?.ProjectKey);
            if (!string.IsNullOrWhiteSpace(externalStateDir)) {
                teamSquadDir = externalStateDir;
                isRemote = true;
                resolutionReason = "Resolved external Squad state from .squad/config.json";
            }
        }

        if (!isExternalState && !isRemote && !string.IsNullOrWhiteSpace(teamRoot)) {
            var projectRoot = Directory.GetParent(projectSquadDir)?.FullName;
            if (!string.IsNullOrWhiteSpace(projectRoot)) {
                teamSquadDir = Path.GetFullPath(Path.Combine(projectRoot, teamRoot));
                isRemote = true;
                resolutionReason = reason;
            }
        }

        return new SquadWorkspaceLayout(
            normalizedDirectory,
            projectSquadDir,
            teamSquadDir,
            directoryName,
            isRemote,
            config?.StateBackend,
            config?.StateLocation,
            config?.ProjectKey,
            resolutionReason);
    }

    private static (string Path, string DirectoryName, string Reason)? FindSquadDirectory(string startDirectory) {
        var current = startDirectory;

        while (true) {
            foreach (var directoryName in SquadDirectoryNames) {
                var candidate = Path.Combine(current, directoryName);
                if (Directory.Exists(candidate)) {
                    return (
                        Path.GetFullPath(candidate),
                        directoryName,
                        $"Found {directoryName} in repository tree");
                }
            }

            var gitMarker = Path.Combine(current, ".git");
            if (Directory.Exists(gitMarker))
                return null;

            if (File.Exists(gitMarker)) {
                var mainCheckout = TryResolveMainWorktreePath(current, gitMarker);
                if (!string.IsNullOrWhiteSpace(mainCheckout)) {
                    foreach (var directoryName in SquadDirectoryNames) {
                        var mainCandidate = Path.Combine(mainCheckout, directoryName);
                        if (Directory.Exists(mainCandidate)) {
                            return (
                                Path.GetFullPath(mainCandidate),
                                directoryName,
                                $"Found {directoryName} in main worktree");
                        }
                    }
                }

                return null;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            current = parent;
        }
    }

    private static string? TryResolveMainWorktreePath(string worktreeDirectory, string gitFilePath) {
        try {
            var content = File.ReadAllText(gitFilePath).Trim();
            const string prefix = "gitdir:";
            if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var rawGitDir = content[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(rawGitDir))
                return null;

            var worktreeGitDir = Path.GetFullPath(Path.Combine(worktreeDirectory, rawGitDir));
            var mainGitDir = Path.GetFullPath(Path.Combine(worktreeGitDir, "..", ".."));
            if (!Directory.Exists(mainGitDir))
                return null;

            return Directory.GetParent(mainGitDir)?.FullName;
        }
        catch {
            return null;
        }
    }

    private static SquadDirectoryConfig? LoadConfig(string projectSquadDir) {
        var configPath = Path.Combine(projectSquadDir, "config.json");
        if (!File.Exists(configPath))
            return null;

        try {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            if (!HasNumericProperty(root, "version") || string.IsNullOrWhiteSpace(TryGetString(root, "teamRoot")))
                return null;

            return new SquadDirectoryConfig(
                TryGetString(root, "teamRoot"),
                TryGetString(root, "stateBackend"),
                TryGetString(root, "stateLocation"),
                TryGetString(root, "projectKey"));
        }
        catch {
            return null;
        }
    }

    private static string? TryResolveExternalStateDir(string? projectKey) {
        var sanitized = SanitizeProjectKey(projectKey);
        if (string.IsNullOrWhiteSpace(sanitized))
            return null;

        var globalDir = ResolveGlobalSquadPath();
        return Path.Combine(globalDir, "projects", sanitized);
    }

    private static string? SanitizeProjectKey(string? projectKey) {
        if (string.IsNullOrWhiteSpace(projectKey) || projectKey.Contains("..", StringComparison.Ordinal))
            return null;

        var builder = new System.Text.StringBuilder(projectKey.Length);
        foreach (var character in projectKey) {
            if ((character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') ||
                character is '.' or '_' or '-') {
                builder.Append(character);
            }
            else if (character is '/' or '\\') {
                builder.Append('-');
            }
            else {
                builder.Append('-');
            }
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static string ResolveGlobalSquadPath() {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrWhiteSpace(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(appData)) {
            appData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(appData))
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(appData)) {
            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            appData = string.IsNullOrWhiteSpace(userProfile)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : Path.Combine(userProfile, "AppData", "Roaming");
        }

        return Path.Combine(appData, "squad");
    }

    private static string? TryGetString(JsonElement element, string propertyName) {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool HasNumericProperty(JsonElement element, string propertyName) {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number;
    }

    private sealed record SquadDirectoryConfig(
        string? TeamRoot,
        string? StateBackend,
        string? StateLocation,
        string? ProjectKey);
}

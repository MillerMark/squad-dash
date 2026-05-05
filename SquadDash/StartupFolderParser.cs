using System;

namespace SquadDash;

internal sealed record StartupArguments(
    string? StartupFolder,
    string? ApplicationRoot,
    bool    RefreshScreenshots     = false,
    string? RefreshScreenshotName  = null,
    bool    NoWorkspaceOnStart     = false);

internal static class StartupFolderParser {
    public static string? Parse(string[] args) {
        return ParseArguments(args).StartupFolder;
    }

    public static StartupArguments ParseArguments(string[] args) {
        if (args.Length == 0)
            return new StartupArguments(null, null);

        string? startupFolder         = null;
        string? applicationRoot       = null;
        var     refreshScreenshots    = false;
        string? refreshScreenshotName = null;
        var     noWorkspaceOnStart    = false;

        for (var index = 0; index < args.Length; index++) {
            var arg = args[index];

            if (string.Equals(arg, "--folder", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-f", StringComparison.OrdinalIgnoreCase)) {
                if (index + 1 < args.Length && startupFolder is null)
                    startupFolder = Normalize(args[index + 1]);

                index++;
                continue;
            }

            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-w", StringComparison.OrdinalIgnoreCase)) {
                if (index + 1 < args.Length && startupFolder is null)
                    startupFolder = Normalize(args[index + 1]);

                index++;
                continue;
            }

            if (string.Equals(arg, "--app-root", StringComparison.OrdinalIgnoreCase)) {
                if (index + 1 < args.Length && applicationRoot is null)
                    applicationRoot = Normalize(args[index + 1]);

                index++;
                continue;
            }

            if (string.Equals(arg, "--refresh-screenshots", StringComparison.OrdinalIgnoreCase)) {
                refreshScreenshots = true;

                // Consume the next argument as the optional name only if it
                // doesn't look like a flag (i.e. does not start with "--").
                if (index + 1 < args.Length &&
                    !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
                    refreshScreenshotName = Normalize(args[index + 1]);
                    index++;
                }

                continue;
            }

            if (string.Equals(arg, "--new-window", StringComparison.OrdinalIgnoreCase)) {
                noWorkspaceOnStart = true;
                continue;
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal) && startupFolder is null)
                startupFolder = Normalize(arg);
        }

        return new StartupArguments(startupFolder, applicationRoot, refreshScreenshots, refreshScreenshotName, noWorkspaceOnStart);
    }

    public static string? Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        trimmed = trimmed.Trim('"');

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }
}

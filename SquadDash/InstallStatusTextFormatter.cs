namespace SquadDash;

internal static class InstallStatusTextFormatter {
    internal static string BuildProgressStatus(string progressMessage, TimeSpan elapsed, string? installSource) {
        return $"{BuildStatusText(progressMessage, installSource)} ({(int)elapsed.TotalSeconds}s)";
    }

    internal static string BuildStatusText(string message, string? installSource) {
        if (string.IsNullOrWhiteSpace(installSource))
            return message;

        return $"{message} [Install source: {installSource}]";
    }

    internal static bool TryInferInstallSourceFromProgressMessage(string message, out string? installSource) {
        installSource = null;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        if (message.Contains("workspace npm", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("npm", StringComparison.OrdinalIgnoreCase)) {
            installSource = "workspace npm";
            return true;
        }

        if (message.Contains("winget", StringComparison.OrdinalIgnoreCase)) {
            installSource = "native (winget)";
            return true;
        }

        return false;
    }
}

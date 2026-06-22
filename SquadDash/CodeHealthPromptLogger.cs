using System;
using System.IO;
using System.Text;

namespace SquadDash;

/// <summary>
/// Saves code health task prompts to disk for diagnostic purposes.
/// Only operates in developer mode.
/// </summary>
internal sealed class CodeHealthPromptLogger {

    private readonly string _workspacePath;
    private readonly string _diagnosticsDir;

    internal CodeHealthPromptLogger(string workspacePath) {
        _workspacePath = workspacePath;
        _diagnosticsDir = SquadWorkspaceLayoutResolver.ResolveTeamFilePath(workspacePath, "diagnostics", "prompts");
    }

    /// <summary>
    /// Saves the fully-evaluated prompt text for a code health task.
    /// Only saves in developer mode.
    /// </summary>
    /// <param name="taskName">The task identifier (e.g., "speed-improvements")</param>
    /// <param name="promptText">The complete prompt text after all template processing</param>
    /// <param name="metadata">Optional metadata to include at the top of the file</param>
    public void LogPrompt(string taskName, string promptText, string? metadata = null) {
        if (!SquadDashEnvironment.IsDeveloperMode)
            return;

        try {
            Directory.CreateDirectory(_diagnosticsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"{SanitizeFileName(taskName)}_{timestamp}.txt";
            var filePath = Path.Combine(_diagnosticsDir, fileName);

            var content = new StringBuilder();
            
            // Add metadata header if provided
            if (!string.IsNullOrEmpty(metadata)) {
                content.AppendLine("=".PadRight(80, '='));
                content.AppendLine("PROMPT DIAGNOSTIC METADATA");
                content.AppendLine("=".PadRight(80, '='));
                content.AppendLine(metadata);
                content.AppendLine();
                content.AppendLine("=".PadRight(80, '='));
                content.AppendLine("EVALUATED PROMPT TEXT");
                content.AppendLine("=".PadRight(80, '='));
                content.AppendLine();
            }

            content.Append(promptText);

            File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);

            SquadDashTrace.Write(
                TraceCategory.General,
                $"CodeHealthPromptLogger: saved prompt to {fileName} ({promptText.Length} chars)");
        }
        catch (Exception ex) {
            SquadDashTrace.Write(
                TraceCategory.General,
                $"CodeHealthPromptLogger: failed to save prompt: {ex.Message}");
        }
    }

    /// <summary>
    /// Sanitizes a task name for use as a file name by removing or replacing invalid characters.
    /// </summary>
    private static string SanitizeFileName(string taskName) {
        if (string.IsNullOrWhiteSpace(taskName))
            return "unknown-task";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(taskName.Length);

        foreach (var c in taskName) {
            if (Array.IndexOf(invalidChars, c) >= 0)
                sanitized.Append('-');
            else
                sanitized.Append(c);
        }

        var result = sanitized.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "unknown-task" : result;
    }
}

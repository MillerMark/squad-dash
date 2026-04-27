using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash;

// ToolTranscriptDescriptor, ToolTranscriptDetail, ToolEditDiffSummary, and
// ToolTranscriptDetailContent have been moved to ToolTranscriptData.cs so that
// the persistence layer (WorkspaceConversationStore) can call
// ToolTranscriptDetailContent.Build() without taking a dependency on this
// display-layer class.

internal static class ToolTranscriptFormatter {
    public const int DefaultPreviewLineLimit = 20;
    private static readonly Regex SystemNotificationRegex = new(
        @"<system_notification>\s*.*?\s*</system_notification>\s*",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static string GetToolEmoji(ToolTranscriptDescriptor descriptor) {
        var toolName = descriptor.ToolName.Trim();
        var hasDisplayText = !string.IsNullOrWhiteSpace(descriptor.DisplayText);

        return toolName switch {
            "glob"          when hasDisplayText => "🔎",
            "grep"          when hasDisplayText => "🔎",
            "view"          when hasDisplayText => "👀",
            "edit"                              => "✏️",
            "web_fetch"     when hasDisplayText => "🌍",
            "create"        when hasDisplayText => "📄",
            "task"          when hasDisplayText => "🤖",
            "skill"         when hasDisplayText => "⚡",
            "store_memory"  when hasDisplayText => "💾",
            "report_intent" when hasDisplayText => "🎯",
            "sql"           when hasDisplayText => "🗄️",
            "powershell"    when hasDisplayText => "💻",
            _               => string.Empty
        };
    }

    /// <summary>Returns the representative emoji for a tool by name (ignores display-text guard).</summary>
    public static string GetToolEmojiByName(string toolName) => toolName.Trim() switch {
        "glob"          => "🔎",
        "grep"          => "🔎",
        "view"          => "👀",
        "edit"          => "✏️",
        "web_fetch"     => "🌍",
        "create"        => "📄",
        "task"          => "🤖",
        "skill"         => "⚡",
        "store_memory"  => "💾",
        "report_intent" => "🎯",
        "sql"           => "🗄️",
        "powershell"    => "💻",
        _               => string.Empty
    };

    public static string BuildRunningText(ToolTranscriptDescriptor descriptor, string? progressText = null) {
        if (TryBuildSpecialToolText(descriptor, success: null, outputText: null, out var specialText))
            return specialText;

        return BuildStatusText(
            descriptor,
            "Running",
            "...",
            progressText);
    }

    public static string BuildCompletedText(
        ToolTranscriptDescriptor descriptor,
        bool success,
        string? progressText = null,
        string? outputText = null) {
        if (TryBuildSpecialToolText(descriptor, success, outputText, out var specialText))
            return specialText;

        if (success) {
            var context = ResolveContext(descriptor);
            if (!string.IsNullOrWhiteSpace(context))
                return TruncateForSummary(context) + ".";

            return HumanizeToolName(descriptor.ToolName) + ".";
        }

        var builder = new StringBuilder();
        var contextText = ResolveContext(descriptor);
        if (!string.IsNullOrWhiteSpace(contextText)) {
            builder.Append(TruncateForSummary(contextText))
                .Append(" failed");
        }
        else {
            builder.Append(HumanizeToolName(descriptor.ToolName))
                .Append(" failed");
        }

        var errorReason = ExtractErrorReason(outputText);
        if (!string.IsNullOrWhiteSpace(errorReason)) {
            builder.Append(" -- ").Append(errorReason);
        }
        else if (!string.IsNullOrWhiteSpace(progressText) &&
            !string.Equals(progressText, contextText, StringComparison.OrdinalIgnoreCase)) {
            builder.Append(" (")
                .Append(TruncateForSummary(progressText, 100))
                .Append(')');
        }

        builder.Append('.');
        return builder.ToString();
    }

    public static string BuildFailurePreview(string? text, int maxLines = DefaultPreviewLineLimit) {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder();
        var visibleLineCount = Math.Min(lines.Length, maxLines);

        for (var index = 0; index < visibleLineCount; index++) {
            if (index > 0)
                builder.AppendLine();

            builder.Append(lines[index]);
        }

        if (lines.Length > maxLines)
            builder.AppendLine().Append("...");

        return builder.ToString();
    }

    /// <summary>
    /// Delegates to <see cref="ToolTranscriptDetailContent.Build"/> so that display-layer
    /// callers can continue using the familiar <c>ToolTranscriptFormatter</c> entry point.
    /// The implementation lives in the data layer to keep
    /// <see cref="WorkspaceConversationStore"/> free of display-layer dependencies.
    /// </summary>
    public static string BuildDetailContent(ToolTranscriptDetail detail) =>
        ToolTranscriptDetailContent.Build(detail);

    public static string HumanizeToolName(string? toolName) {
        if (string.IsNullOrWhiteSpace(toolName))
            return "Tool";

        return toolName.Trim() switch {
            "powershell" => "PowerShell",
            "report_intent" => "Report Intent",
            _ => TitleCaseWords(toolName.Replace('_', ' ').Replace('.', ' '))
        };
    }

    private static string BuildStatusText(
        ToolTranscriptDescriptor descriptor,
        string verb,
        string punctuation,
        string? progressText) {
        var builder = new StringBuilder();
        builder.Append(verb)
            .Append(' ')
            .Append(HumanizeToolName(descriptor.ToolName));

        var context = ResolveContext(descriptor);
        if (!string.IsNullOrWhiteSpace(context)) {
            builder.Append(": ")
                .Append(TruncateForSummary(context));
        }

        if (!string.IsNullOrWhiteSpace(progressText) &&
            !string.Equals(progressText, context, StringComparison.OrdinalIgnoreCase)) {
            builder.Append(" (")
                .Append(TruncateForSummary(progressText, 100))
                .Append(')');
        }

        builder.Append(punctuation);
        return builder.ToString();
    }

    public static string BuildPromptSeparator() => "───";

    public static string BuildAgentTurnStartMarker(string? taskLabel, DateTimeOffset startedAt) {
        var normalizedTaskLabel = NormalizeSingleLine(taskLabel);
        if (string.IsNullOrWhiteSpace(normalizedTaskLabel))
            normalizedTaskLabel = "background task";

        normalizedTaskLabel = normalizedTaskLabel.TrimEnd('.', '!', '?');
        return $"Starting {TruncateForSummary(normalizedTaskLabel, 120)} at {FormatTimestamp(startedAt)}";
    }

    public static string StripSystemNotifications(string? text) {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var stripped = SystemNotificationRegex.Replace(normalized, string.Empty);
        return Regex.Replace(stripped, @"\n{3,}", "\n\n");
    }

    private static bool TryBuildSpecialToolText(
        ToolTranscriptDescriptor descriptor,
        bool? success,
        string? outputText,
        out string text) {
        var toolName = descriptor.ToolName.Trim();

        switch (toolName) {
            case "glob" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildGlobText(descriptor.DisplayText!, success, outputText);
                return true;

            case "grep" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("🔎", descriptor.DisplayText!, success, outputText);
                return true;

            case "view" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("👀", descriptor.DisplayText!, success, outputText);
                return true;

            case "edit" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("✏️", descriptor.DisplayText!, success, outputText);
                return true;

            case "web_fetch" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("🌍 ", descriptor.DisplayText!, success, outputText);
                return true;

            case "create" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("📄 ", descriptor.DisplayText!, success, outputText);
                return true;

            case "task" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("🤖 ", descriptor.DisplayText!, success, outputText);
                return true;

            case "skill" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("⚡ ", descriptor.DisplayText!, success, outputText);
                return true;

            case "store_memory" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("💾 ", descriptor.DisplayText!, success, outputText);
                return true;

            case "report_intent" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("🎯 ", descriptor.DisplayText!, success, outputText);
                return true;

            case "sql" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("🗄️ ", descriptor.DisplayText!, success, outputText);
                return true;

            case "powershell" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("💻 ", descriptor.DisplayText!, success, outputText);
                return true;

            default:
                text = string.Empty;
                return false;
        }
    }

    private static string? ResolveContext(ToolTranscriptDescriptor descriptor) {
        if (!string.IsNullOrWhiteSpace(descriptor.DisplayText))
            return descriptor.DisplayText;
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
            return descriptor.Description;
        if (!string.IsNullOrWhiteSpace(descriptor.Intent))
            return descriptor.Intent;
        if (!string.IsNullOrWhiteSpace(descriptor.Skill))
            return descriptor.Skill;
        if (!string.IsNullOrWhiteSpace(descriptor.Path))
            return descriptor.Path;
        if (!string.IsNullOrWhiteSpace(descriptor.Command))
            return descriptor.Command;

        return null;
    }

    private static string FormatTimestamp(DateTimeOffset value) {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    private static string TitleCaseWords(string value) {
        var words = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
            return "Tool";

        for (var index = 0; index < words.Length; index++) {
            var word = words[index];
            words[index] = word.Length switch {
                0 => word,
                1 => char.ToUpperInvariant(word[0]).ToString(),
                _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
            };
        }

        return string.Join(" ", words);
    }

    private static string TruncateForSummary(string value, int maxLength = 84) {
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;

        return trimmed[..(maxLength - 3)] + "...";
    }

    private static string NormalizeSingleLine(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value.Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string BuildFixedDisplayText(string prefix, string displayText, bool? success, string? outputText = null) {
        var label = prefix + displayText;

        if (success == false) {
            var reason = ExtractErrorReason(outputText);
            return string.IsNullOrWhiteSpace(reason)
                ? label + " failed."
                : label + " failed -- " + reason;
        }

        return label;
    }

    private static string? ExtractErrorReason(string? outputText) {
        if (string.IsNullOrWhiteSpace(outputText))
            return null;

        var lines = outputText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines) {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) &&
                !trimmed.StartsWith("Code:", StringComparison.OrdinalIgnoreCase)) {
                return trimmed;
            }
        }

        return null;
    }

    private static string BuildGlobText(string pattern, bool? success, string? outputText) {
        if (success is null)
            return "🔎" + pattern;

        if (success == false) {
            var reason = ExtractErrorReason(outputText);
            return string.IsNullOrWhiteSpace(reason)
                ? "🔎" + pattern + " failed."
                : "🔎" + pattern + " failed -- " + reason;
        }

        var count = CountGlobMatches(outputText);
        var suffix = count == 1
            ? " -- 1 file found"
            : $" -- {count} files found";

        return "🔎" + pattern + suffix;
    }

    private static int CountGlobMatches(string? outputText) {
        if (string.IsNullOrWhiteSpace(outputText))
            return 0;

        if (outputText.Contains("No files matched the pattern.", StringComparison.OrdinalIgnoreCase))
            return 0;

        return outputText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));
    }

    public static ToolEditDiffSummary? TryBuildEditDiffSummary(ToolTranscriptDescriptor descriptor, string? outputText) {
        if (!string.Equals(descriptor.ToolName, "edit", StringComparison.OrdinalIgnoreCase))
            return null;

        var displayName = ResolveEditDisplayName(descriptor);
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var normalized = (outputText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var addedLineCount = 0;
        var removedLineCount = 0;

        foreach (var line in lines) {
            if (line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal)) {
                continue;
            }

            if (line.StartsWith('+'))
                addedLineCount++;
            else if (line.StartsWith('-'))
                removedLineCount++;
        }

        var isNewFile =
            normalized.Contains("new file mode", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("--- /dev/null", StringComparison.OrdinalIgnoreCase);
        var isDeletedFile =
            normalized.Contains("deleted file mode", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("+++ /dev/null", StringComparison.OrdinalIgnoreCase);

        return new ToolEditDiffSummary(
            displayName,
            addedLineCount,
            removedLineCount,
            isNewFile,
            isDeletedFile);
    }

    private static string? ResolveEditDisplayName(ToolTranscriptDescriptor descriptor) {
        var candidate = descriptor.DisplayText ?? descriptor.Path;
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try {
            var fileName = Path.GetFileName(candidate);
            return string.IsNullOrWhiteSpace(fileName) ? candidate : fileName;
        }
        catch {
            return candidate;
        }
    }
}

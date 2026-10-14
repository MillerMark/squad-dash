using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace SquadDash;

// ToolTranscriptDescriptor, ToolTranscriptDetail, ToolEditDiffSummary, and
// ToolTranscriptDetailContent have been moved to ToolTranscriptData.cs so that
// the persistence layer (WorkspaceConversationStore) can call
// ToolTranscriptDetailContent.Build() without taking a dependency on this
// display-layer class.

internal static class ToolTranscriptFormatter {
    public const int DefaultPreviewLineLimit = 20;
    private const string CapturedContentLabel = "captured full content; previous content unavailable";
    private static readonly Regex SystemNotificationRegex = new(
        @"<system_notification>\s*.*?\s*</system_notification>\s*",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PowerShellFileWriteRegex = new(
        @"(?<![\w-])(?:Set-Content|Add-Content|Out-File|Tee-Object|Export-Csv|Export-Clixml|New-Item|Set-Item|Clear-Content|WriteAllText|WriteAllLines|WriteAllBytes|AppendAllText|AppendAllLines)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PowerShellFileReadRegex = new(
        @"(?<![\w-])(?:Get-Content|gc|cat|type|ReadAllText|ReadAllLines|ReadAllBytes|Import-Csv|Import-Clixml)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    // Matches a true file-redirection operator (optionally prefixed with a stream number or "*"
    // for all streams), but NOT stream-to-stream duplication (e.g. "2>&1") or a redirect to the
    // null device (e.g. "2>$null"), neither of which write to any file on disk.
    private static readonly Regex PowerShellFileRedirectRegex = new(
        @"[0-9*]*>{1,2}(?!&)(?!\s*\$null\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string GetToolEmoji(ToolTranscriptDescriptor descriptor) {
        var toolName = NormalizePresentationToolName(descriptor.ToolName.Trim());
        var hasDisplayText = !string.IsNullOrWhiteSpace(descriptor.DisplayText);

        return toolName switch {
            "glob"          when hasDisplayText => "🔎",
            "grep"          when hasDisplayText => "🔎",
            "view"          when hasDisplayText => "👀",
            "edit"                              => "✏️",
            "apply_patch"                       => "✏️",
            "web_fetch"     when hasDisplayText => "🌍",
            "create"        when hasDisplayText => "📄",
            "task"          when hasDisplayText => "🤖",
            "skill"         when hasDisplayText => "⚡",
            "store_memory"  when hasDisplayText => "💾",
            "report_intent" when hasDisplayText => "🎯",
            "sql"           when hasDisplayText => "🗄️",
            "powershell"         when hasDisplayText => "💻",
            "read_powershell"    when hasDisplayText && HasDelaySuffix(descriptor.DisplayText!) => "⌛",
            "read_powershell"    when hasDisplayText => "💻",
            "write_powershell"   when hasDisplayText => "⌨️",
            "stop_powershell"    when hasDisplayText => "⛔",
            "list_powershell"                        => "📋",
            "read_agent"         when hasDisplayText => "📡",
            "list_agents"                            => "📋",
            "fetch_copilot_cli_documentation"        => "📖",
            _               => string.Empty
        };
    }

    /// <summary>Returns the representative emoji for a tool by name (ignores display-text guard).</summary>
    public static string GetToolEmojiByName(string toolName) => NormalizePresentationToolName(toolName.Trim()) switch {
        "glob"          => "🔎",
        "grep"          => "🔎",
        "view"          => "👀",
        "edit"          => "✏️",
        "apply_patch"   => "✏️",
        "web_fetch"     => "🌍",
        "create"        => "📄",
        "task"          => "🤖",
        "skill"         => "⚡",
        "store_memory"  => "💾",
        "report_intent" => "🎯",
        "sql"           => "🗄️",
        "powershell"                      => "💻",
        "read_powershell"                 => "💻",
        "write_powershell"                => "⌨️",
        "stop_powershell"                 => "⛔",
        "list_powershell"                 => "📋",
        "read_agent"                      => "📡",
        "list_agents"                     => "📋",
        "fetch_copilot_cli_documentation" => "📖",
        _               => string.Empty
    };

    internal static string ResolveToolIconKey(string? toolName) {
        if (NormalizeFileToolName(toolName) is "create" or "create_file")
            return "ToolIcon_create";
        if (IsEditToolName(toolName))
            return "ToolIcon_edit";

        var normalizedToolName = NormalizePresentationToolName(toolName?.Trim() ?? string.Empty);
        return string.IsNullOrWhiteSpace(normalizedToolName)
            ? "ToolIcon_default"
            : $"ToolIcon_{normalizedToolName}";
    }

    internal static string? ClassifyPowerShellFileIconKey(string? command) {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        // A pipeline or multi-statement command that reads and writes is still a write.
        if (PowerShellFileRedirectRegex.IsMatch(command) || PowerShellFileWriteRegex.IsMatch(command))
            return "ToolIcon_edit";

        return PowerShellFileReadRegex.IsMatch(command) ? "ToolIcon_view" : null;
    }

    internal static SpinnerActivityKind ClassifyToolActivityKind(string? toolName) {
        if (string.IsNullOrWhiteSpace(toolName))
            return SpinnerActivityKind.Thinking;

        var name = NormalizePresentationToolName(toolName.Trim());

        if (name.Contains("edit",        StringComparison.OrdinalIgnoreCase) ||
            name.Contains("create",      StringComparison.OrdinalIgnoreCase) ||
            name.Contains("write",       StringComparison.OrdinalIgnoreCase) ||
            name.Contains("delete",      StringComparison.OrdinalIgnoreCase) ||
            name.Contains("move",        StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rename",      StringComparison.OrdinalIgnoreCase) ||
            name.Contains("patch",       StringComparison.OrdinalIgnoreCase) ||
            name.Contains("insert",      StringComparison.OrdinalIgnoreCase) ||
            name.Contains("replace",     StringComparison.OrdinalIgnoreCase) ||
            name.Contains("remove_file", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("overwrite",   StringComparison.OrdinalIgnoreCase))
            return SpinnerActivityKind.Writing;

        if (name.Contains("read",   StringComparison.OrdinalIgnoreCase) ||
            name.Contains("view",   StringComparison.OrdinalIgnoreCase) ||
            name.Contains("glob",   StringComparison.OrdinalIgnoreCase) ||
            name.Contains("grep",   StringComparison.OrdinalIgnoreCase) ||
            name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("fetch",  StringComparison.OrdinalIgnoreCase) ||
            name.Contains("list",   StringComparison.OrdinalIgnoreCase) ||
            name.Contains("get",    StringComparison.OrdinalIgnoreCase))
            return SpinnerActivityKind.Reading;

        return SpinnerActivityKind.Thinking;
    }

    internal static string? BuildRipGrepDisplayText(string? pattern, IReadOnlyList<string>? scopes) {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var normalizedPattern = pattern.Trim();
        var normalizedScopes = scopes is null
            ? []
            : scopes
                .Select(NormalizeRipGrepScope)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => scope!)
                .ToArray();

        if (normalizedScopes.Length == 0)
            return normalizedPattern;
        if (normalizedScopes.Length == 1)
            return $"{normalizedPattern} — {normalizedScopes[0]}";

        return $"{normalizedPattern} — {CompactRipGrepScope(normalizedScopes[0])} +{normalizedScopes.Length - 1}";
    }

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
        // Replace with "\n" rather than "" so that inline tags (placed without surrounding
        // whitespace) don't fuse adjacent words: "first.<tag>Now" → "first.\nNow" rather
        // than "first.Now".  Extra consecutive newlines are collapsed in the next step.
        var stripped = SystemNotificationRegex.Replace(normalized, "\n");
        return Regex.Replace(stripped, @"\n{3,}", "\n\n");
    }

    private static bool TryBuildSpecialToolText(
        ToolTranscriptDescriptor descriptor,
        bool? success,
        string? outputText,
        out string text) {
        var toolName = NormalizePresentationToolName(descriptor.ToolName.Trim());

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

            case "apply_patch" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
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

            case "read_powershell" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                var readEmoji = HasDelaySuffix(descriptor.DisplayText!) ? "⌛" : "💻";
                text = BuildFixedDisplayText(readEmoji + " ", descriptor.DisplayText!, success, outputText);
                return true;

            case "write_powershell" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("⌨️ ", descriptor.DisplayText!, success, outputText);
                return true;

            case "stop_powershell" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                text = BuildFixedDisplayText("⛔ ", descriptor.DisplayText!, success, outputText);
                return true;

            case "read_agent" when !string.IsNullOrWhiteSpace(descriptor.DisplayText):
                var readAgentEmoji = success is null ? "📡 " : "🤖 ";
                text = BuildFixedDisplayText(readAgentEmoji, descriptor.DisplayText!, success, outputText);
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
        return value.ToString("yyyy-MM-dd HH:mm:ss zzz");
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

    public static bool IsEditToolName(string? toolName) =>
        NormalizeFileToolName(toolName) is "edit" or "apply_patch" or "create" or
            "write" or "write_file" or "create_file" or "str_replace_editor";

    private static string NormalizeFileToolName(string? toolName) =>
        (toolName?.Trim().Split('.').LastOrDefault() ?? string.Empty).ToLowerInvariant();

    private static readonly Regex UnifiedDiffHunkRegex = new(
        @"^@@ -\d+(?:,(?<old>\d+))? \+\d+(?:,(?<new>\d+))? @@(?:.*)$",
        RegexOptions.Compiled);

    internal static string? TryExtractEditUnifiedDiff(
        ToolTranscriptDescriptor descriptor, string? outputText, string? argsJson = null) {
        var edits = TryExtractCapturedEdits(descriptor, outputText, argsJson);
        if (edits.Count == 0)
            return null;
        return SelectCapturedEdit(descriptor, edits)?.UnifiedDiff;
    }

    internal static IReadOnlyList<CapturedFileEdit> TryExtractCapturedEdits(
        ToolTranscriptDescriptor descriptor, string? outputText, string? argsJson = null) {
        if (!IsEditToolName(descriptor.ToolName))
            return Array.Empty<CapturedFileEdit>();

        var patches = TryExtractOutputDiffs(outputText);
        if (patches is null || patches.Count == 0) {
            var argumentDiff = TryBuildArgumentDiff(descriptor, argsJson);
            patches = TryExtractOutputDiffs(argumentDiff);
        }
        if (patches is null || patches.Count == 0)
            return Array.Empty<CapturedFileEdit>();

        return patches
            .Select(BuildCapturedFileEdit)
            .GroupBy(edit => edit.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var entries = group.ToArray();
                return entries.Length == 1
                    ? entries[0]
                    : new CapturedFileEdit(
                        entries[0].Path,
                        string.Join("\n\n", entries.Select(entry => entry.UnifiedDiff)),
                        entries.Sum(entry => entry.AddedLineCount),
                        entries.Sum(entry => entry.RemovedLineCount),
                        entries.Any(entry => entry.IsNewFile),
                        entries.Any(entry => entry.IsDeletedFile));
            })
            .ToArray();
    }

    private sealed record ExtractedPatch(string Path, string Text);

    private static IReadOnlyList<ExtractedPatch>? TryExtractOutputDiffs(string? outputText) {
        if (string.IsNullOrWhiteSpace(outputText))
            return null;

        var lines = outputText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // Never present a retained prefix as the complete edit, even if it ends at a hunk boundary.
        if (lines.Any(line => line.StartsWith("[SquadDash truncated ", StringComparison.Ordinal) ||
                              (line.StartsWith("\u2026 [", StringComparison.Ordinal) &&
                               line.EndsWith(" chars truncated]", StringComparison.Ordinal))))
            return null;

        var patches = new List<ExtractedPatch>();
        for (var index = 0; index < lines.Length; index++) {
            var start = index;
            if (lines[index].StartsWith("diff --git ", StringComparison.Ordinal)) {
                index++;
                while (index < lines.Length && IsUnifiedDiffMetadata(lines[index]))
                    index++;
            }
            if (index + 1 >= lines.Length ||
                !lines[index].StartsWith("--- ", StringComparison.Ordinal) ||
                !lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal))
                continue;

            var newPath = NormalizeDiffPath(lines[index + 1][4..]);
            var path = newPath == "/dev/null" ? NormalizeDiffPath(lines[index][4..]) : newPath;
            index += 2;
            var sawHunk = false;
            while (index < lines.Length && lines[index].StartsWith("@@", StringComparison.Ordinal)) {
                var match = UnifiedDiffHunkRegex.Match(lines[index]);
                if (!match.Success ||
                    !TryGetHunkCount(match.Groups["old"], out var oldRemaining) ||
                    !TryGetHunkCount(match.Groups["new"], out var newRemaining))
                    return null;
                sawHunk = true;
                index++;
                while (oldRemaining > 0 || newRemaining > 0) {
                    if (index >= lines.Length || lines[index].Length == 0)
                        return null;
                    var line = lines[index++];
                    switch (line[0]) {
                        case ' ': oldRemaining--; newRemaining--; break;
                        case '-': oldRemaining--; break;
                        case '+': newRemaining--; break;
                        case '\\' when line == @"\ No newline at end of file": continue;
                        default: return null;
                    }
                    if (oldRemaining < 0 || newRemaining < 0)
                        return null;
                }
                if (index < lines.Length && lines[index] == @"\ No newline at end of file")
                    index++;
            }
            if (index < lines.Length && (lines[index].StartsWith('+') || lines[index].StartsWith('-')) &&
                !(lines[index].StartsWith("--- ", StringComparison.Ordinal) &&
                  index + 1 < lines.Length && lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal)))
                return null;
            if (sawHunk && path is not null)
                patches.Add(new ExtractedPatch(path, string.Join("\n", lines[start..index])));
            index--;
        }
        return patches;
    }

    private static string? TryBuildArgumentDiff(ToolTranscriptDescriptor descriptor, string? argsJson) {
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;

        try {
            using var document = JsonDocument.Parse(argsJson);
            var args = document.RootElement;
            var tool = NormalizeFileToolName(descriptor.ToolName);
            if (tool == "apply_patch") {
                var input = args.ValueKind == JsonValueKind.String
                    ? args.GetString()
                    : GetArgumentString(args, "patch", "input");
                var unified = TryConvertApplyPatch(input);
                return unified;
            }

            var path = GetArgumentString(args, "path", "file_path") ?? descriptor.Path;
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(['\r', '\n']) >= 0)
                return null;

            var command = GetArgumentString(args, "command");
            if (tool == "str_replace_editor" && command is not ("create" or "str_replace"))
                return null;

            if (tool is "create" or "create_file" or "write" or "write_file" || command == "create") {
                var content = GetArgumentString(args, "file_text", "content");
                if (content is null)
                    return null;
                // Overwriting tools only capture the new content, not the original file.
                var isNew = tool is "create" or "create_file" || command == "create";
                return BuildCapturedHunk(path, ContentLines(content).Select(line => (isNew ? "+" : " ") + line).ToArray(),
                    isNew, fullContent: !isNew);
            }

            var oldText = GetArgumentString(args, "old_str", "old_string");
            var newText = GetArgumentString(args, "new_str", "new_string");
            if (oldText is null || newText is null)
                return null;
            return BuildCapturedHunk(path,
                ContentLines(oldText).Select(line => "-" + line)
                    .Concat(ContentLines(newText).Select(line => "+" + line)).ToArray(), isNew: false);
        }
        catch (JsonException ex) {
            SquadDashTrace.Write("Transcript", $"Cannot reconstruct captured file edit arguments: {ex.Message}");
            return null;
        }
    }

    private static string NormalizeTargetPath(string target) {
        var normalizedTarget = target.Replace('\\', '/');
        while (normalizedTarget.StartsWith("./", StringComparison.Ordinal) ||
               normalizedTarget.StartsWith("../", StringComparison.Ordinal))
            normalizedTarget = normalizedTarget[(normalizedTarget.IndexOf('/') + 1)..];
        return normalizedTarget;
    }

    private static CapturedFileEdit? SelectCapturedEdit(
        ToolTranscriptDescriptor descriptor,
        IReadOnlyList<CapturedFileEdit> edits) {
        var target = descriptor.Path ?? GetEditDisplayText(descriptor);
        if (string.IsNullOrWhiteSpace(target))
            return edits.FirstOrDefault();

        var normalizedTarget = NormalizeTargetPath(target);
        var matches = edits.Where(edit => MatchesDiffPath(
                edit.Path.Replace('\\', '/'), normalizedTarget))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static CapturedFileEdit BuildCapturedFileEdit(ExtractedPatch patch) {
        var lines = patch.Text.ReplaceLineEndings("\n").Split('\n');
        var added = 0;
        var removed = 0;
        var inHunk = false;
        foreach (var line in lines) {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                inHunk = false;
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                inHunk = true;
                continue;
            }
            if (!inHunk)
                continue;
            if (line.StartsWith('+'))
                added++;
            else if (line.StartsWith('-'))
                removed++;
        }

        var text = patch.Text;
        var isNew = text.Contains("new file mode", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("create file mode", StringComparison.OrdinalIgnoreCase) ||
                    lines.Any(line => line.StartsWith("--- ", StringComparison.Ordinal) &&
                                      NormalizeDiffPath(line[4..]) == "/dev/null");
        var isDeleted = text.Contains("deleted file mode", StringComparison.OrdinalIgnoreCase) ||
                        lines.Any(line => line.StartsWith("+++ ", StringComparison.Ordinal) &&
                                          NormalizeDiffPath(line[4..]) == "/dev/null");
        return new CapturedFileEdit(patch.Path, patch.Text, added, removed, isNew, isDeleted);
    }

    private static string? GetArgumentString(JsonElement args, params string[] names) {
        if (args.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
            if (args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static string[] ContentLines(string content) {
        if (content.Length == 0)
            return [];
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private static string BuildCapturedHunk(
        string path, IReadOnlyList<string> lines, bool isNew, bool fullContent = false) {
        var oldCount = lines.Count(line => line.StartsWith('-') || line.StartsWith(' '));
        var newCount = lines.Count(line => line.StartsWith('+') || line.StartsWith(' '));
        var label = fullContent ? CapturedContentLabel
            : isNew ? "captured new file" : "captured edit; original line location unavailable";
        var normalizedPath = path.Replace('\\', '/');
        return $"--- {(isNew ? "/dev/null" : "a/" + normalizedPath)}\n+++ b/{normalizedPath}\n" +
            $"@@ -{(oldCount == 0 ? 0 : 1)},{oldCount} +{(newCount == 0 ? 0 : 1)},{newCount} @@ {label}" +
            (lines.Count == 0 ? string.Empty : "\n" + string.Join("\n", lines));
    }

    /// <summary>
    /// Builds a synthetic <see cref="CapturedFileEdit"/> for a guided-tour "edit" tool call whose
    /// output carries only loose <c>- old</c>/<c>+ new</c> lines (no real unified-diff headers).
    /// The filename is pulled from the tool <paramref name="description"/> (e.g. "Fix Foo.cs"), and the
    /// loose lines are wrapped into a proper unified diff via <see cref="BuildCapturedHunk"/> so the
    /// resulting edit renders identically to a real capture — same +N/-N chips and same diff viewer.
    /// </summary>
    internal static CapturedFileEdit? BuildSimulatedEdit(string? description, string? output) {
        var path = ExtractSimulatedEditPath(description);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var diffLines = new List<string>();
        foreach (var line in (output ?? string.Empty).ReplaceLineEndings("\n").Split('\n')) {
            if (line.Length == 0)
                continue;
            diffLines.Add(line[0] is '+' or '-' or ' ' ? line : " " + line);
        }
        if (diffLines.Count == 0)
            return null;

        var patch = BuildCapturedHunk(path, diffLines, isNew: false);
        var edits = TryExtractCapturedEdits(new ToolTranscriptDescriptor("edit", DisplayText: path), patch);
        return edits.Count > 0 ? edits[0] : null;
    }

    private static string? ExtractSimulatedEditPath(string? description) {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var tokens = description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var index = tokens.Length - 1; index >= 0; index--) {
            var token = tokens[index].Trim('.', ',', ':', ';', '"', '\'', '`', '(', ')');
            if (token.Length > 0 && token.Contains('.') &&
                !token.EndsWith('.') && !token.EndsWith('/') && !token.EndsWith('\\'))
                return token;
        }
        return null;
    }

    internal static bool IsCapturedContentSnapshot(string? patch) =>
        patch?.ReplaceLineEndings("\n").Split('\n').Any(line =>
            line.StartsWith("@@ ", StringComparison.Ordinal) &&
            line.EndsWith("@@ " + CapturedContentLabel, StringComparison.Ordinal)) == true;

    private static string? TryConvertApplyPatch(string? input) {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var lines = input.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        if (lines[0] != "*** Begin Patch" || lines[^1] != "*** End Patch")
            return null;

        var patches = new List<string>();
        for (var index = 1; index < lines.Length - 1;) {
            var isNew = lines[index].StartsWith("*** Add File: ", StringComparison.Ordinal);
            var isUpdate = lines[index].StartsWith("*** Update File: ", StringComparison.Ordinal);
            if (!isNew && !isUpdate) {
                // Delete arguments contain no previous content to display.
                if (lines[index].StartsWith("*** Delete File: ", StringComparison.Ordinal)) {
                    index++;
                    continue;
                }
                return null;
            }
            var path = lines[index++][(isNew ? "*** Add File: ".Length : "*** Update File: ".Length)..];
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (index < lines.Length && lines[index].StartsWith("*** Move to: ", StringComparison.Ordinal))
                index++;

            var hunks = new List<string>();
            var body = new List<string>();
            var sawHunk = isNew;
            void FlushHunk() {
                if (body.Count == 0 && !isNew)
                    return;
                var patch = BuildCapturedHunk(path, body, isNew);
                hunks.Add(hunks.Count == 0 ? patch : patch[(patch.IndexOf("\n@@", StringComparison.Ordinal) + 1)..]);
                body.Clear();
            }

            while (index < lines.Length - 1 && !lines[index].StartsWith("*** ", StringComparison.Ordinal)) {
                var line = lines[index++];
                if (!isNew && (line == "@@" || line.StartsWith("@@ ", StringComparison.Ordinal))) {
                    FlushHunk();
                    sawHunk = true;
                }
                else if (sawHunk && line.Length > 0 &&
                         (line[0] == '+' || (!isNew && line[0] is '-' or ' '))) {
                    body.Add(line);
                }
                else {
                    return null;
                }
            }
            if (index < lines.Length && lines[index] == "*** End of File")
                index++;
            FlushHunk();
            if (!sawHunk || hunks.Count == 0)
                return null;
            patches.Add(string.Join("\n", hunks));
        }
        return string.Join("\n", patches);
    }

    private static bool MatchesDiffPath(string path, string target) =>
        string.Equals(path, target, StringComparison.OrdinalIgnoreCase) ||
        (!Path.IsPathRooted(target) && path.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase)) ||
        (!Path.IsPathRooted(path) && target.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase));

    internal static bool AreSameEditPath(string first, string second) =>
        MatchesDiffPath(first.Replace('\\', '/'), second.Replace('\\', '/'));

    private static bool TryGetHunkCount(Group group, out int count) {
        count = 1;
        return !group.Success || int.TryParse(group.Value, out count);
    }

    private static bool IsUnifiedDiffMetadata(string line) =>
        line.StartsWith("index ", StringComparison.Ordinal) ||
        line.StartsWith("new file mode ", StringComparison.Ordinal) ||
        line.StartsWith("create file mode ", StringComparison.Ordinal) ||
        line.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
        line.StartsWith("old mode ", StringComparison.Ordinal) ||
        line.StartsWith("new mode ", StringComparison.Ordinal) ||
        line.StartsWith("similarity index ", StringComparison.Ordinal) ||
        line.StartsWith("dissimilarity index ", StringComparison.Ordinal) ||
        line.StartsWith("rename from ", StringComparison.Ordinal) ||
        line.StartsWith("rename to ", StringComparison.Ordinal) ||
        line.StartsWith("copy from ", StringComparison.Ordinal) ||
        line.StartsWith("copy to ", StringComparison.Ordinal);

    public static ToolEditDiffSummary? TryBuildEditDiffSummary(
        ToolTranscriptDescriptor descriptor, string? outputText, string? argsJson = null) {
        var edits = TryExtractCapturedEdits(descriptor, outputText, argsJson);
        var selected = SelectCapturedEdit(descriptor, edits);
        if (edits.Count > 0 && selected is not null) {
            return new ToolEditDiffSummary(
                selected.DisplayName,
                edits.Sum(edit => edit.AddedLineCount),
                edits.Sum(edit => edit.RemovedLineCount),
                selected.IsNewFile,
                selected.IsDeletedFile);
        }

        if (NormalizeFileToolName(descriptor.ToolName) is not ("edit" or "apply_patch"))
            return null;

        var displayName = ResolveEditDisplayName(descriptor, outputText);
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var normalized = (outputText ?? string.Empty).ReplaceLineEndings("\n");
        var lines = normalized.Split('\n');
        var addedLineCount = 0;
        var removedLineCount = 0;

        var inHunk = false;
        foreach (var line in lines) {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                inHunk = false;
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                inHunk = true;
                continue;
            }
            if (!inHunk ||
                line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal))
                continue;

            if (line.StartsWith('+'))
                addedLineCount++;
            else if (line.StartsWith('-'))
                removedLineCount++;
        }

        var isNewFile =
            normalized.Contains("new file mode", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("create file mode", StringComparison.OrdinalIgnoreCase) ||
            lines.Any(line => line.StartsWith("--- ", StringComparison.Ordinal) &&
                NormalizeDiffPath(line[4..]) == "/dev/null");
        var isDeletedFile =
            normalized.Contains("deleted file mode", StringComparison.OrdinalIgnoreCase) ||
            lines.Any(line => line.StartsWith("+++ ", StringComparison.Ordinal) &&
                NormalizeDiffPath(line[4..]) == "/dev/null");

        return new ToolEditDiffSummary(
            displayName,
            addedLineCount,
            removedLineCount,
            isNewFile,
            isDeletedFile);
    }

    private static string? GetEditDisplayText(ToolTranscriptDescriptor descriptor) =>
        string.IsNullOrWhiteSpace(descriptor.DisplayText) || descriptor.DisplayText == "null"
            ? null : descriptor.DisplayText;

    private static string? ResolveEditDisplayName(ToolTranscriptDescriptor descriptor, string? outputText) {
        var candidate = GetEditDisplayText(descriptor) ?? descriptor.Path ?? TryExtractEditTargetPath(outputText);
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try {
            var fileName = Path.GetFileName(candidate);
            return string.IsNullOrWhiteSpace(fileName) ? candidate : fileName;
        }
        catch (ArgumentException ex) {
            SquadDashTrace.Write("Transcript", $"Cannot resolve captured edit filename: {ex.Message}");
            return candidate;
        }
    }

    internal static string? TryExtractEditTargetPath(string? patchOrDiffText) {
        if (string.IsNullOrWhiteSpace(patchOrDiffText))
            return null;

        var lines = patchOrDiffText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');

        string? oldPath = null;
        foreach (var line in lines) {
            foreach (var prefix in new[] { "*** Update File: ", "*** Add File: ", "*** Delete File: " }) {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return NormalizeDiffPath(line[prefix.Length..]);
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) {
                var separator = line.IndexOf(" b/", "diff --git ".Length, StringComparison.Ordinal);
                if (separator >= 0)
                    return NormalizeDiffPath(line[(separator + 3)..]);
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal)) {
                var newPath = NormalizeDiffPath(line[4..]);
                return newPath is null or "/dev/null" ? oldPath : newPath;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal)) {
                var path = NormalizeDiffPath(line[4..]);
                oldPath = path == "/dev/null" ? null : path;
            }
        }

        return oldPath;
    }

    private static string? NormalizeDiffPath(string value) {
        var normalized = value.Trim();
        var tabIndex = normalized.IndexOf('\t');
        if (tabIndex >= 0)
            normalized = normalized[..tabIndex];
        normalized = normalized.Trim('"');

        if (normalized.StartsWith("a/", StringComparison.Ordinal) ||
            normalized.StartsWith("b/", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (normalized == "dev/null")
            return "/dev/null";

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary>Returns true when a read_powershell display text has an appended delay suffix like "(60s)".</summary>
    private static bool HasDelaySuffix(string displayText) =>
        displayText.EndsWith('s') && displayText.Contains('(');

    private static string NormalizePresentationToolName(string toolName) => toolName switch {
        "rg" => "grep",
        "delegate_roster_agent" => "task",
        "delegate_temporary_agent" => "task",
        _ => toolName
    };

    private static string? NormalizeRipGrepScope(string? scope) {
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        var trimmed = scope.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return Path.IsPathRooted(trimmed)
            ? CompactRipGrepScope(trimmed)
            : trimmed;
    }

    private static string CompactRipGrepScope(string scope) {
        var trimmed = scope.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        var fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
    }
}

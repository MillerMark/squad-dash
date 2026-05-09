using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

internal static class LoopMdParser {

    private static readonly Regex _kvPattern =
        new(@"^(\w+):\s*(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a loop.md file and returns configuration.
    /// Returns null if the file does not exist, cannot be read,
    /// or the frontmatter does not contain <c>configured: true</c>.
    /// </summary>
    public static LoopMdConfig? Parse(string loopMdPath) {
        if (!File.Exists(loopMdPath))
            return null;

        string content;
        try {
            content = File.ReadAllText(loopMdPath);
        }
        catch {
            return null;
        }

        // Normalise line endings so CRLF and LF both work.
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        int i = 0;

        // Skip until first ---
        while (i < lines.Length && lines[i].Trim() != "---")
            i++;
        if (i >= lines.Length)
            return null;
        i++; // move past the opening ---

        bool configured = false;
        double intervalMinutes = 10;
        double timeoutMinutes  = 5;
        string description     = "";
        var    commands        = new List<string>();
        var    optionKeys      = new List<string>();
        var    optionsByKey   = new Dictionary<string, LoopOptionBuilder>(StringComparer.Ordinal);

        // Read frontmatter up to the closing ---
        bool inOptionsBlock = false;
        string? currentOptionKey = null;

        while (i < lines.Length && lines[i].Trim() != "---") {
            var line = lines[i];

            // Entering/exiting the options: block
            if (line == "options:") {
                inOptionsBlock = true;
                currentOptionKey = null;
                i++;
                continue;
            }

            if (inOptionsBlock) {
                // A line with exactly 2 spaces indent + key: starts a new option entry
                if (line.Length > 2 && line[0] == ' ' && line[1] == ' ' && line[2] != ' ') {
                    var colonIdx = line.IndexOf(':', 2);
                    if (colonIdx > 2) {
                        currentOptionKey = line[2..colonIdx].Trim();
                        if (!optionsByKey.ContainsKey(currentOptionKey))
                        {
                            optionsByKey[currentOptionKey] = new LoopOptionBuilder { Key = currentOptionKey };
                            optionKeys.Add(currentOptionKey);
                        }
                    }
                    i++;
                    continue;
                }

                // A line with exactly 4 spaces indent = option sub-key
                if (line.Length > 4 && line[0] == ' ' && line[1] == ' ' && line[2] == ' ' && line[3] == ' ' && line[4] != ' ' && currentOptionKey != null) {
                    var colonIdx = line.IndexOf(':', 4);
                    if (colonIdx > 4) {
                        var subKey = line[4..colonIdx].Trim();
                        var subVal = colonIdx + 1 < line.Length ? line[(colonIdx + 1)..].Trim() : "";
                        if (optionsByKey.TryGetValue(currentOptionKey, out var builder)) {
                            switch (subKey) {
                                case "value":   builder.RawValue = subVal; break;
                                case "type":    builder.Type     = subVal.Trim('"', '\''); break;
                                case "label":   builder.Label    = subVal.Trim('"', '\''); break;
                                case "hint":    builder.Hint     = subVal.Trim('"', '\''); break;
                                case "choices":
                                    var choicesRaw = subVal.Trim('[', ']');
                                    var choiceList = new List<string>();
                                    foreach (var ch in choicesRaw.Split(',')) {
                                        var cv = ch.Trim().Trim('"', '\'');
                                        if (!string.IsNullOrEmpty(cv))
                                            choiceList.Add(cv);
                                    }
                                    builder.Choices = choiceList;
                                    break;
                            }
                        }
                    }
                    i++;
                    continue;
                }

                // Non-indented line (not ---) exits options mode
                if (line.Length > 0 && line[0] != ' ') {
                    inOptionsBlock = false;
                    currentOptionKey = null;
                    // fall through to flat key parsing below
                }
            }

            if (!inOptionsBlock) {
                var m = _kvPattern.Match(line);
                if (m.Success) {
                    var key   = m.Groups[1].Value.ToLowerInvariant();
                    var value = m.Groups[2].Value.Trim();
                    switch (key) {
                        case "configured":
                            configured = string.Equals(
                                value.Trim('"', '\''),
                                "true",
                                StringComparison.OrdinalIgnoreCase);
                            break;
                        case "interval":
                            if (double.TryParse(
                                    value,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var iv))
                                intervalMinutes = iv;
                            break;
                        case "timeout":
                            if (double.TryParse(
                                    value,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var tv))
                                timeoutMinutes = tv;
                            break;
                        case "description":
                            description = value.Trim('"', '\'');
                            break;
                        case "commands":
                            // Accepts: [stop_loop, start_loop] or "stop_loop, start_loop"
                            var raw = value.Trim('[', ']', '"', '\'');
                            foreach (var cmd in raw.Split(',')) {
                                var c = cmd.Trim();
                                if (!string.IsNullOrEmpty(c))
                                    commands.Add(c);
                            }
                            break;
                    }
                }
            }

            i++;
        }

        // Build the LoopOption list and derive interval/timeout from options block if present
        List<LoopOption>? builtOptions = null;
        if (optionsByKey.Count > 0) {
            builtOptions = new List<LoopOption>(optionsByKey.Count);
            foreach (var key in optionKeys) {
                var b = optionsByKey[key];
                builtOptions.Add(new LoopOption(
                    b.Key,
                    b.RawValue ?? "",
                    b.Type ?? "string",
                    b.Label,
                    b.Hint,
                    b.Choices));
            }

            if (optionsByKey.TryGetValue("interval", out var intervalOpt) &&
                double.TryParse(intervalOpt.RawValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ivOpt))
                intervalMinutes = ivOpt;

            if (optionsByKey.TryGetValue("timeout", out var timeoutOpt) &&
                double.TryParse(timeoutOpt.RawValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var tvOpt))
                timeoutMinutes = tvOpt;
        }

        if (!configured)
            return null;

        // Everything after the closing --- is the instructions body.
        if (i >= lines.Length)
            return new LoopMdConfig(intervalMinutes, timeoutMinutes, description, "", commands, builtOptions);

        i++; // move past the closing ---

        var sb = new StringBuilder();
        for (; i < lines.Length; i++) {
            sb.AppendLine(lines[i]);
        }

        return new LoopMdConfig(
            intervalMinutes,
            timeoutMinutes,
            description,
            sb.ToString().Trim(),
            commands,
            builtOptions);
    }

    /// <summary>
    /// Scans <paramref name="squadFolder"/> for all loop*.md files and returns
    /// display entries sorted with loop.md first, then alphabetically.
    /// Display name = Description from frontmatter, or filename without extension as fallback.
    /// </summary>
    public static IReadOnlyList<LoopFileEntry> ScanForLoopFiles(string squadFolder)
    {
        if (!Directory.Exists(squadFolder))
            return Array.Empty<LoopFileEntry>();

        var files = Directory.GetFiles(squadFolder, "loop*.md");
        var entries = new List<LoopFileEntry>(files.Length);
        foreach (var path in files)
        {
            var config = Parse(path);
            string rawDescription = config is { Description.Length: > 0 }
                ? config.Description
                : Path.GetFileNameWithoutExtension(path);

            string displayName;
            string tooltipText = "";
            var dashIdx = rawDescription.IndexOfAny(['-', '\u2013', '\u2014', '\u2012', '\u2015']);
            if (dashIdx > 0)
            {
                displayName = rawDescription[..dashIdx].Trim();
                tooltipText = rawDescription[(dashIdx + 1)..].Trim();
            }
            else
            {
                displayName = rawDescription;
            }

            entries.Add(new LoopFileEntry(path, displayName, tooltipText));
        }

        entries.Sort((a, b) =>
        {
            bool aIsDefault = string.Equals(Path.GetFileName(a.FilePath), "loop.md", StringComparison.OrdinalIgnoreCase);
            bool bIsDefault = string.Equals(Path.GetFileName(b.FilePath), "loop.md", StringComparison.OrdinalIgnoreCase);
            if (aIsDefault != bIsDefault) return aIsDefault ? -1 : 1;
            return string.Compare(Path.GetFileName(a.FilePath), Path.GetFileName(b.FilePath), StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    /// <summary>Returns the file content with any leading YAML frontmatter block removed.</summary>
    public static string StripFrontmatter(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length && lines[i].Trim() != "---")
            i++;
        if (i >= lines.Length) return content; // no opening ---
        i++; // skip opening ---
        while (i < lines.Length && lines[i].Trim() != "---")
            i++;
        if (i >= lines.Length) return content; // no closing ---
        i++; // skip closing ---
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
            i++;
        return string.Join("\n", lines, i, lines.Length - i);
    }

    /// <summary>
    /// Returns the instructions body with all <c>{{optionKey}}</c> placeholders
    /// replaced by the current <c>RawValue</c> of the matching option, plus any
    /// <paramref name="extraSubstitutions"/> (e.g. system variables like {{iteration}}).
    /// Options of type "group" are skipped (they are UI headers, not values).
    /// </summary>
    public static string BuildMergedBody(LoopMdConfig config, IReadOnlyDictionary<string, string>? extraSubstitutions = null)
    {
        var body = config.Instructions;
        if (config.Options is not null)
        {
            foreach (var opt in config.Options)
            {
                if (opt.Type == "group") continue;
                body = body.Replace($"{{{{{opt.Key}}}}}", opt.RawValue, StringComparison.Ordinal);
            }
        }
        if (extraSubstitutions is not null)
        {
            foreach (var kvp in extraSubstitutions)
                body = body.Replace($"{{{{{kvp.Key}}}}}", kvp.Value, StringComparison.Ordinal);
        }
        return body;
    }

    /// <summary>
    /// Returns the complete loop file content (frontmatter + substituted body) suitable for
    /// Squad CLI mode preview. The frontmatter is reconstructed from the parsed config.
    /// </summary>
    public static string BuildMergedFull(LoopMdConfig config, string substitutedBody)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("configured: true");
        sb.AppendLine($"interval: {config.IntervalMinutes}");
        sb.AppendLine($"timeout: {config.TimeoutMinutes}");
        if (!string.IsNullOrEmpty(config.Description))
            sb.AppendLine($"description: \"{config.Description.Replace("\"", "\\\"")}\"");
        if (config.Commands is { Count: > 0 })
            sb.AppendLine($"commands: [{string.Join(", ", config.Commands)}]");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(substitutedBody);
        return sb.ToString();
    }
    /// <summary>
    /// Updates (or inserts) the <c>description:</c> line in the loop file's YAML frontmatter.
    /// If a <c>description:</c> key already exists it is replaced in-place; otherwise one is
    /// inserted after the <c>configured:</c> line, or at the end of the frontmatter.
    /// Does nothing if the file is not found or has no frontmatter.
    /// </summary>
    public static void UpdateDescription(string loopMdPath, string newDescription) {
        if (!File.Exists(loopMdPath))
            return;

        string[] lines;
        try {
            lines = File.ReadAllLines(loopMdPath);
        }
        catch {
            return;
        }

        // Find opening ---
        int i = 0;
        while (i < lines.Length && lines[i].Trim() != "---")
            i++;
        if (i >= lines.Length) return;
        int frontmatterStart = i;
        i++;

        // Find closing ---
        int frontmatterEnd = -1;
        while (i < lines.Length) {
            if (lines[i].Trim() == "---") { frontmatterEnd = i; break; }
            i++;
        }
        if (frontmatterEnd < 0) return;

        var escaped    = newDescription.Replace("\"", "\\\"");
        var newLine    = $"description: \"{escaped}\"";

        // Try to update existing description: line
        for (int j = frontmatterStart + 1; j < frontmatterEnd; j++) {
            if (lines[j].TrimStart().StartsWith("description:", StringComparison.OrdinalIgnoreCase)) {
                lines[j] = newLine;
                try { File.WriteAllLines(loopMdPath, lines); } catch { /* best-effort */ }
                return;
            }
        }

        // No existing description: line — insert after configured: or at end of frontmatter
        var insertAfter = frontmatterEnd - 1;
        for (int j = frontmatterStart + 1; j < frontmatterEnd; j++) {
            if (lines[j].TrimStart().StartsWith("configured:", StringComparison.OrdinalIgnoreCase)) {
                insertAfter = j;
                break;
            }
        }

        var newLines = new List<string>(lines);
        newLines.Insert(insertAfter + 1, newLine);
        try { File.WriteAllLines(loopMdPath, newLines); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Updates a single option's <c>value:</c> line in the loop file's YAML frontmatter.
    /// Reads the file, finds the <c>options:</c> block, locates <paramref name="optionKey"/>,
    /// updates the first <c>value:</c> sub-key under it, and writes back.
    /// Does nothing if the file or key is not found.
    /// </summary>
    public static void UpdateOptionValue(string loopMdPath, string optionKey, string newRawValue) {
        if (!File.Exists(loopMdPath))
            return;

        string[] lines;
        try {
            lines = File.ReadAllLines(loopMdPath);
        }
        catch {
            return;
        }

        // Find opening ---
        int i = 0;
        while (i < lines.Length && lines[i].Trim() != "---")
            i++;
        if (i >= lines.Length) return;
        int frontmatterStart = i;
        i++;

        // Find closing ---
        int frontmatterEnd = -1;
        while (i < lines.Length) {
            if (lines[i].Trim() == "---") { frontmatterEnd = i; break; }
            i++;
        }
        if (frontmatterEnd < 0) return;

        // Find options: at indent 0
        int optionsLine = -1;
        for (int j = frontmatterStart + 1; j < frontmatterEnd; j++) {
            if (lines[j] == "options:") { optionsLine = j; break; }
        }
        if (optionsLine < 0) return;

        // Find "  {optionKey}:" (2-space indent)
        string optionHeader = $"  {optionKey}:";
        int optionHeaderLine = -1;
        for (int j = optionsLine + 1; j < frontmatterEnd; j++) {
            if (lines[j] == optionHeader || lines[j].StartsWith(optionHeader + " ", StringComparison.Ordinal)) {
                optionHeaderLine = j;
                break;
            }
        }
        if (optionHeaderLine < 0) return;

        // Find the first "    value:" line after the option key, before the next 2-space-indent option
        for (int j = optionHeaderLine + 1; j < frontmatterEnd; j++) {
            var line = lines[j];
            // Stop if we hit the next 2-space-indent option key
            if (line.Length >= 2 && line[0] == ' ' && line[1] == ' ' && (line.Length < 3 || line[2] != ' '))
                break;
            if (line.StartsWith("    value:", StringComparison.Ordinal)) {
                lines[j] = $"    value: {newRawValue}";
                try { File.WriteAllLines(loopMdPath, lines); } catch { /* best-effort */ }
                return;
            }
        }
    }

    /// <summary>Mutable builder used during options block parsing.</summary>
    private sealed class LoopOptionBuilder {
        public string  Key      { get; init; } = "";
        public string? RawValue { get; set; }
        public string? Type     { get; set; }
        public string? Label    { get; set; }
        public string? Hint     { get; set; }
        public List<string>? Choices { get; set; }
    }
}

internal sealed record LoopFileEntry(string FilePath, string DisplayName, string TooltipText = "");

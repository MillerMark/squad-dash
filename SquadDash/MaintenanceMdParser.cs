using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SquadDash;

/// <summary>
/// Parses a maintenance.md file. Tasks are embedded as a YAML list inside the frontmatter.
/// </summary>
internal static class MaintenanceMdParser {

    /// <summary>
    /// Returns null if the file is missing or unreadable.
    /// When the frontmatter lacks <c>configured: true</c> the config is still returned
    /// (with <see cref="MaintenanceMdConfig.Configured"/> == false) so the panel can
    /// show tasks for browsing.
    /// </summary>
    public static MaintenanceMdConfig? Parse(string maintenanceMdPath) {
        if (!File.Exists(maintenanceMdPath))
            return null;

        string content;
        try {
            content = File.ReadAllText(maintenanceMdPath);
        }
        catch {
            return null;
        }

        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int i = 0;

        // Skip to opening ---
        while (i < lines.Length && lines[i].Trim() != "---")
            i++;
        if (i >= lines.Length) return null;
        i++;

        bool   configured    = false;
        bool   enabledOnIdle = false;
        double idleTimeout   = 15;
        int    maxTasks      = 5;
        string safety        = "branch";

        var tasks                      = new List<MaintenanceTaskBuilder>();
        bool inTasksList               = false;
        MaintenanceTaskBuilder? current = null;
        bool inOptionsBlock            = false;
        string? currentOptionKey       = null;
        var optionKeys                 = new List<string>();
        var optionBuilders             = new Dictionary<string, MaintenanceOptionBuilder>(StringComparer.Ordinal);
        bool inChoicesList             = false;
        MaintenanceOptionChoice? currentChoice = null;
        bool inMultiLineInstructions   = false;
        int  multiLineBaseIndent       = 6;
        var  multiLineAccumulator      = new StringBuilder();

        while (i < lines.Length && lines[i].Trim() != "---") {
            var line = lines[i];
            i++;

            // Count leading spaces
            int indent = CountLeadingSpaces(line);
            string trimmed = line.TrimStart();

            // ── Multi-line block scalar accumulation ──────────────────────────
            if (inMultiLineInstructions) {
                bool isBlank = string.IsNullOrWhiteSpace(line);
                if (isBlank || indent >= multiLineBaseIndent) {
                    if (multiLineAccumulator.Length > 0) multiLineAccumulator.Append('\n');
                    if (!isBlank) multiLineAccumulator.Append(line[multiLineBaseIndent..]);
                    continue;
                }
                // Non-blank line at shallower indent ends the block scalar.
                if (current is not null)
                    current.Instructions = multiLineAccumulator.ToString().TrimEnd('\n');
                inMultiLineInstructions = false;
                multiLineAccumulator.Clear();
                // Fall through to process the current line normally.
            }
            // ──────────────────────────────────────────────────────────────────

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!inTasksList) {
                // Global frontmatter key at column 0
                if (trimmed == "tasks:") {
                    inTasksList = true;
                    continue;
                }

                ParseGlobalKV(trimmed, ref configured, ref enabledOnIdle, ref idleTimeout, ref maxTasks, ref safety);
                continue;
            }

            // ── inside tasks list ─────────────────────────────────────────────

            // New task item: "  - id: xxx"  (indent 2, followed by "- ")
            if (indent == 2 && trimmed.StartsWith("- ")) {
                // Commit any pending choice before finalizing the task.
                if (inChoicesList && currentChoice is not null && currentOptionKey is not null
                        && optionBuilders.TryGetValue(currentOptionKey, out var pendingChoiceCb))
                    pendingChoiceCb.Choices.Add(currentChoice);

                FinalizeCurrentTask(current, optionKeys, optionBuilders, tasks);
                current                = new MaintenanceTaskBuilder();
                inOptionsBlock         = false;
                currentOptionKey       = null;
                inChoicesList          = false;
                currentChoice          = null;
                inMultiLineInstructions = false;
                multiLineAccumulator.Clear();
                optionKeys.Clear();
                optionBuilders.Clear();

                // Parse first key-value on the same line, e.g. "id: cleanup"
                ParseTaskKV(trimmed[2..], current);
                continue;
            }

            if (current is null) continue;

            // Task field: indent == 4
            if (indent == 4) {
                if (trimmed == "options:" || trimmed.StartsWith("options: ")) {
                    inOptionsBlock   = true;
                    currentOptionKey = null;
                }
                else if (trimmed.StartsWith("instructions:") &&
                         trimmed[(trimmed.IndexOf(':') + 1)..].Trim().Trim('"', '\'') == "|") {
                    // YAML block scalar: collect subsequent indented lines.
                    inMultiLineInstructions = true;
                    multiLineBaseIndent     = indent + 2;
                    multiLineAccumulator.Clear();
                    inOptionsBlock = false;
                }
                else {
                    inOptionsBlock = false;
                    ParseTaskKV(trimmed, current);
                }
                continue;
            }

            // Option key: indent == 6  e.g. "strategy:"
            if (inOptionsBlock && indent == 6) {
                // Commit any choice that was still being built before switching option keys.
                if (inChoicesList && currentChoice is not null && currentOptionKey is not null
                        && optionBuilders.TryGetValue(currentOptionKey, out var pendingCb))
                    pendingCb.Choices.Add(currentChoice);

                if (trimmed.EndsWith(':') && !trimmed.Contains(' ')) {
                    currentOptionKey = trimmed.TrimEnd(':');
                    inChoicesList = false;
                    currentChoice = null;
                    if (!optionBuilders.ContainsKey(currentOptionKey)) {
                        optionBuilders[currentOptionKey] = new MaintenanceOptionBuilder { Key = currentOptionKey };
                        optionKeys.Add(currentOptionKey);
                    }
                }
                continue;
            }

            // Choices list items: indent 10 (- value:) or 12 (tooltip:)
            if (inOptionsBlock && currentOptionKey is not null && inChoicesList) {
                if (indent == 10 && trimmed.StartsWith("- ")) {
                    // Commit previous choice if any
                    if (currentChoice is not null && optionBuilders.TryGetValue(currentOptionKey, out var cb))
                        cb.Choices.Add(currentChoice);
                    currentChoice = new MaintenanceOptionChoice();
                    // Parse "- value: fix" → value = "fix"
                    var rest = trimmed[2..]; // strip "- "
                    var colonIdx2 = rest.IndexOf(':');
                    if (colonIdx2 >= 0 && rest[..colonIdx2].Trim() == "value")
                        currentChoice.Value = rest[(colonIdx2 + 1)..].Trim().Trim('"', '\'');
                    continue;
                }
                if (indent == 12 && currentChoice is not null) {
                    var colonIdx2 = trimmed.IndexOf(':');
                    if (colonIdx2 >= 0 && trimmed[..colonIdx2].Trim() == "tooltip")
                        currentChoice.Tooltip = trimmed[(colonIdx2 + 1)..].Trim().Trim('"', '\'');
                    continue;
                }
                // Exiting choices list — commit last choice and fall through.
                if (currentChoice is not null && optionBuilders.TryGetValue(currentOptionKey, out var cb2))
                    cb2.Choices.Add(currentChoice);
                currentChoice = null;
                inChoicesList = false;
                // Fall through to normal processing.
            }

            // Option sub-field: indent == 8  e.g. "type: radio"
            if (inOptionsBlock && currentOptionKey is not null && indent == 8) {
                if (optionBuilders.TryGetValue(currentOptionKey, out var builder)) {
                    bool enterChoicesList = ParseOptionSubfield(trimmed, builder);
                    if (enterChoicesList) {
                        inChoicesList = true;
                        currentChoice = null;
                    }
                }
                continue;
            }
        }

        // Finalize any pending multi-line block scalar that ran up to the closing ---.
        if (inMultiLineInstructions && current is not null)
            current.Instructions = multiLineAccumulator.ToString().TrimEnd('\n');

        // Finalize any choice still being collected.
        if (inChoicesList && currentChoice is not null && currentOptionKey is not null
                && optionBuilders.TryGetValue(currentOptionKey, out var lastCb))
            lastCb.Choices.Add(currentChoice);

        FinalizeCurrentTask(current, optionKeys, optionBuilders, tasks);

        var builtTasks = tasks
            .Select(t => t.Build(safety))
            .ToList();

        return new MaintenanceMdConfig(configured, enabledOnIdle, idleTimeout, maxTasks, safety, builtTasks);
    }

    // ── Safety floor enforcement ───────────────────────────────────────────────

    /// <summary>
    /// Enforces the safety floor. A per-task safety may not be more permissive
    /// than the global safety setting.
    /// </summary>
    internal static string EnforceSafetyFloor(string globalSafety, string taskSafety) {
        if (string.IsNullOrEmpty(taskSafety))
            return globalSafety;

        return globalSafety switch {
            "report-only" => "report-only",
            "branch"      => taskSafety == "direct" ? "branch" : taskSafety,
            _             => taskSafety,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void FinalizeCurrentTask(
        MaintenanceTaskBuilder? current,
        List<string> optionKeys,
        Dictionary<string, MaintenanceOptionBuilder> optionBuilders,
        List<MaintenanceTaskBuilder> tasks) {

        if (current is null) return;

        if (optionKeys.Count > 0) {
            current.BuiltOptions = optionKeys
                .Select(k => {
                    var b = optionBuilders[k];
                    return new MaintenanceOption(b.Key, b.RawValue ?? "", b.Type ?? "string",
                        b.Label, b.Tooltip, b.Choices.Count > 0 ? b.Choices : null);
                })
                .ToList();
        }

        tasks.Add(current);
    }

    private static void ParseGlobalKV(
        string line,
        ref bool configured, ref bool enabledOnIdle, ref double idleTimeout, ref int maxTasks, ref string safety) {

        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0) return;
        var key = line[..colonIdx].Trim();
        var val = line[(colonIdx + 1)..].Trim().Trim('"', '\'');

        switch (key) {
            case "configured":
                configured = string.Equals(val, "true", System.StringComparison.OrdinalIgnoreCase);
                break;
            case "enabled_on_idle":
                enabledOnIdle = string.Equals(val, "true", System.StringComparison.OrdinalIgnoreCase);
                break;
            case "idle_timeout":
                if (double.TryParse(val,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var tv))
                    idleTimeout = tv;
                break;
            case "max_tasks_per_session":
                if (int.TryParse(val,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var mv))
                    maxTasks = mv;
                break;
            case "safety":
                safety = val;
                break;
        }
    }

    private static void ParseTaskKV(string field, MaintenanceTaskBuilder task) {
        var colonIdx = field.IndexOf(':');
        if (colonIdx < 0) return;
        var key = field[..colonIdx].Trim();
        var val = field[(colonIdx + 1)..].Trim().Trim('"', '\'');

        switch (key) {
            case "id":           task.Id           = val; break;
            case "enabled":      task.Enabled      = string.Equals(val, "true", System.StringComparison.OrdinalIgnoreCase); break;
            case "frequency":    task.Frequency    = val; break;
            case "safety":       task.Safety       = val; break;
            case "title":        task.Title        = val; break;
            case "instructions": task.Instructions = val; break;
        }
    }

    /// <returns>
    /// <see langword="true"/> if the <c>choices:</c> key has no inline value, indicating
    /// the parser should switch to YAML-list collection mode.
    /// </returns>
    private static bool ParseOptionSubfield(string field, MaintenanceOptionBuilder opt) {
        var colonIdx = field.IndexOf(':');
        if (colonIdx < 0) return false;
        var key = field[..colonIdx].Trim();
        var val = field[(colonIdx + 1)..].Trim();

        switch (key) {
            case "type":    opt.Type     = val;                     break;
            case "label":   opt.Label    = val.Trim('"', '\'');     break;
            case "hint":    opt.Tooltip  = val.Trim('"', '\'');     break;  // backward compat
        case "tooltip": opt.Tooltip  = val.Trim('"', '\'');     break;
            case "value":   opt.RawValue = val;                     break;
            case "default": opt.RawValue = val;                     break;
            case "choices":
                if (val.Length == 0)
                    return true; // signal: enter YAML-list mode
                // Backward-compat bracket format: choices: [fix, report]
                var stripped = val.Trim('[', ']');
                foreach (var s in stripped.Split(',')) {
                    var v = s.Trim().Trim('"', '\'');
                    if (v.Length > 0)
                        opt.Choices.Add(new MaintenanceOptionChoice { Value = v });
                }
                break;
        }
        return false;
    }

    /// <summary>
    /// Finds the task with <paramref name="taskId"/> in the maintenance.md frontmatter,
    /// then updates the <c>value:</c> sub-key under <paramref name="optionKey"/> and writes back.
    /// Does nothing if the file or key is not found.
    /// </summary>
    public static void UpdateOptionValue(string maintenanceMdPath, string taskId, string optionKey, string newValue) {
        if (!File.Exists(maintenanceMdPath))
            return;

        string[] lines;
        try {
            lines = File.ReadAllLines(maintenanceMdPath);
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

        // Find "  - id: {taskId}" at indent 2
        int taskLine = -1;
        for (int j = frontmatterStart + 1; j < frontmatterEnd; j++) {
            var line = lines[j];
            if (line.Length >= 4 && line[0] == ' ' && line[1] == ' ' && line[2] == '-' && line[3] == ' ') {
                var rest = line[4..].TrimStart();
                if (rest.StartsWith("id:", StringComparison.Ordinal)) {
                    var idVal = rest["id:".Length..].Trim().Trim('"', '\'');
                    if (string.Equals(idVal, taskId, StringComparison.Ordinal)) {
                        taskLine = j;
                        break;
                    }
                }
            }
        }
        if (taskLine < 0) return;

        // Find "    options:" (indent 4) within the task, stopping at the next "  - " task start
        int optionsLine = -1;
        for (int j = taskLine + 1; j < frontmatterEnd; j++) {
            var line = lines[j];
            if (line.Length >= 4 && line[0] == ' ' && line[1] == ' ' && line[2] == '-' && line[3] == ' ')
                break;
            if (line == "    options:") { optionsLine = j; break; }
        }
        if (optionsLine < 0) return;

        // Find "      {optionKey}:" (indent 6) after options:
        string optionHeader = $"      {optionKey}:";
        int optionHeaderLine = -1;
        for (int j = optionsLine + 1; j < frontmatterEnd; j++) {
            var line = lines[j];
            if (line.Length >= 4 && line[0] == ' ' && line[1] == ' ' && line[2] == '-' && line[3] == ' ')
                break;
            if (line == optionHeader || line.StartsWith(optionHeader + " ", StringComparison.Ordinal)) {
                optionHeaderLine = j;
                break;
            }
        }
        if (optionHeaderLine < 0) return;

        // Find "        value:" (indent 8) under the option key
        for (int j = optionHeaderLine + 1; j < frontmatterEnd; j++) {
            var line = lines[j];
            // Stop at next task (indent 2) or next indent-6 option key or shallower
            if (line.Trim().Length > 0 && CountLeadingSpaces(line) <= 6)
                break;
            if (line.StartsWith("        value:", StringComparison.Ordinal)) {
                lines[j] = $"        value: {newValue}";
                try { File.WriteAllLines(maintenanceMdPath, lines); } catch { /* best-effort */ }
                return;
            }
        }
    }

    /// <summary>Updates the <c>enabled_on_idle:</c> value in the frontmatter.</summary>
    internal static void UpdateEnabledOnIdle(string mdPath, bool value) {
        if (!File.Exists(mdPath)) return;
        var raw      = File.ReadAllText(mdPath);
        var le       = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines    = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        bool found   = false;
        bool inFm    = false, pastFirst = false;
        for (int i = 0; i < lines.Length; i++) {
            var t = lines[i].TrimStart();
            if (t == "---") {
                if (!pastFirst) { pastFirst = true; inFm = true; }
                else            { inFm = false; }
                continue;
            }
            if (!inFm) continue;
            if (t.StartsWith("enabled_on_idle:")) {
                lines[i] = "enabled_on_idle: " + (value ? "true" : "false");
                found = true;
                break;
            }
        }
        if (!found) {
            inFm = false; pastFirst = false;
            for (int i = 0; i < lines.Length; i++) {
                var t = lines[i].TrimStart();
                if (t == "---") {
                    if (!pastFirst) { pastFirst = true; inFm = true; }
                    else {
                        lines[i] = "enabled_on_idle: " + (value ? "true" : "false") + le + lines[i];
                        break;
                    }
                    continue;
                }
                if (inFm && (t.StartsWith("tasks:") || t.StartsWith("configured:"))) {
                    var newLines = new string[lines.Length + 1];
                    Array.Copy(lines, 0, newLines, 0, i);
                    newLines[i] = "enabled_on_idle: " + (value ? "true" : "false");
                    Array.Copy(lines, i, newLines, i + 1, lines.Length - i);
                    lines = newLines;
                    break;
                }
            }
        }
        File.WriteAllText(mdPath, string.Join(le, lines));
    }

    /// <summary>
    /// Finds the task with <paramref name="taskId"/> in the maintenance.md frontmatter and
    /// updates its <c>frequency:</c> value, then writes the file back. Does nothing if the
    /// file or task is not found.
    /// </summary>
    public static void UpdateFrequency(string maintenanceMdPath, string taskId, string newFrequency) {
        if (!File.Exists(maintenanceMdPath)) return;
        var raw   = File.ReadAllText(maintenanceMdPath);
        var le    = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        bool inFrontmatter = false, pastFirst = false;
        bool inTasksList   = false, inTargetTask = false;

        for (int i = 0; i < lines.Length; i++) {
            var line    = lines[i];
            var trimmed = line.TrimStart();
            int indent  = line.Length - trimmed.Length;

            if (trimmed == "---") {
                if (!pastFirst) { pastFirst = true; inFrontmatter = true; }
                else            { inFrontmatter = false; }
                continue;
            }

            if (!inFrontmatter) continue;
            if (indent == 0 && trimmed == "tasks:") { inTasksList = true; continue; }
            if (!inTasksList) continue;
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (indent == 0) { inTargetTask = false; inTasksList = false; continue; }

            if (indent == 2 && trimmed.StartsWith("- ")) {
                var rest = trimmed[2..];
                inTargetTask = rest.StartsWith("id:") &&
                    string.Equals(rest["id:".Length..].Trim().Trim('"', '\''), taskId, StringComparison.Ordinal);
                continue;
            }

            if (!inTargetTask) continue;

            if (indent == 4 && trimmed.StartsWith("frequency:")) {
                lines[i] = "    frequency: " + newFrequency;
                File.WriteAllText(maintenanceMdPath, string.Join(le, lines));
                return;
            }
        }
    }

    private static int CountLeadingSpaces(string line) {
        int n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    // ── Mutable builder ───────────────────────────────────────────────────────

    private sealed class MaintenanceTaskBuilder {
        public string  Id           { get; set; } = "";
        public bool    Enabled      { get; set; } = false;
        public string  Frequency    { get; set; } = "daily";
        public string  Safety       { get; set; } = "";
        public string  Title        { get; set; } = "";
        public string  Instructions { get; set; } = "";
        public List<MaintenanceOption>? BuiltOptions { get; set; }

        public MaintenanceTask Build(string globalSafety) =>
            new(
                Id:           Id,
                Enabled:      Enabled,
                Frequency:    Frequency,
                Safety:       EnforceSafetyFloor(globalSafety, Safety),
                Title:        Title.Length > 0 ? Title : Id,
                Instructions: Instructions,
                Options:      BuiltOptions);
    }

    private sealed class MaintenanceOptionBuilder {
        public string Key      { get; init; } = "";
        public string? RawValue { get; set; }
        public string? Type     { get; set; }
        public string? Label    { get; set; }
        public string? Tooltip  { get; set; }
        public List<MaintenanceOptionChoice> Choices { get; } = new();
    }
}

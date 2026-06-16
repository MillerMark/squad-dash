using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SquadDash;

/// <summary>
/// Parses a code-health.md file. Tasks are embedded as a YAML list inside the frontmatter.
/// </summary>
internal static class CodeHealthMdParser {

    /// <summary>
    /// Returns null if the file is missing or unreadable.
    /// When the frontmatter lacks <c>configured: true</c> the config is still returned
    /// (with <see cref="CodeHealthMdConfig.Configured"/> == false) so the panel can
    /// show tasks for browsing.
    /// </summary>
    /// <summary>
    /// Loads code health configuration and tasks from all three sources (system, custom, overrides)
    /// with appropriate precedence: overrides > custom > system.
    /// Returns the configuration with merged tasks from all sources.
    /// </summary>
    public static CodeHealthMdConfig? ParseWithAllSources(string workspacePath) {
        var systemPath = Path.Combine(workspacePath, ".squad", "code-health.md");
        
        // Get base config from system file (for global settings)
        var systemConfig = Parse(systemPath);
        if (systemConfig == null)
            return null;

        // Get merged tasks from all sources
        var mergedTasks = ParseAllSources(workspacePath);

        // Return config with system settings but merged tasks
        return new CodeHealthMdConfig(
            systemConfig.Configured,
            systemConfig.EnabledOnIdle,
            systemConfig.IdleTimeout,
            systemConfig.MaxTasksPerSession,
            systemConfig.Safety,
            mergedTasks);
    }

    public static CodeHealthMdConfig? Parse(string codeHealthMdPath) {
        // Migration: check for old maintenance.md file if code-health.md doesn't exist
        var actualPath = codeHealthMdPath;
        if (!File.Exists(actualPath)) {
            var oldPath = codeHealthMdPath.Replace("code-health.md", "maintenance.md");
            if (File.Exists(oldPath)) {
                actualPath = oldPath;
            }
        }
        
        if (!File.Exists(actualPath))
            return null;

        string content;
        try {
            content = File.ReadAllText(actualPath);
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

        var tasks                      = new List<CodeHealthTaskBuilder>();
        bool inTasksList               = false;
        CodeHealthTaskBuilder? current = null;
        bool inOptionsBlock            = false;
        string? currentOptionKey       = null;
        var optionKeys                 = new List<string>();
        var optionBuilders             = new Dictionary<string, CodeHealthOptionBuilder>(StringComparer.Ordinal);
        bool inChoicesList             = false;
        CodeHealthOptionChoice? currentChoice = null;
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
                current                = new CodeHealthTaskBuilder();
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
                        optionBuilders[currentOptionKey] = new CodeHealthOptionBuilder { Key = currentOptionKey };
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
                    currentChoice = new CodeHealthOptionChoice();
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
            .Select(t => t.Build(safety, codeHealthMdPath))
            .ToList();

        return new CodeHealthMdConfig(configured, enabledOnIdle, idleTimeout, maxTasks, safety, builtTasks);
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
        CodeHealthTaskBuilder? current,
        List<string> optionKeys,
        Dictionary<string, CodeHealthOptionBuilder> optionBuilders,
        List<CodeHealthTaskBuilder> tasks) {

        if (current is null) return;

        if (optionKeys.Count > 0) {
            current.BuiltOptions = optionKeys
                .Select(k => {
                    var b = optionBuilders[k];
                    return new CodeHealthOption(b.Key, b.RawValue ?? "", b.Type ?? "string",
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

    private static void ParseTaskKV(string field, CodeHealthTaskBuilder task) {
        var colonIdx = field.IndexOf(':');
        if (colonIdx < 0) return;
        var key = field[..colonIdx].Trim();
        var val = field[(colonIdx + 1)..].Trim().Trim('"', '\'');

        switch (key) {
            case "id":                   task.Id               = val; break;
            case "enabled":              task.Enabled          = string.Equals(val, "true", System.StringComparison.OrdinalIgnoreCase); break;
            case "frequency":            task.Frequency        = val; break;
            case "safety":               task.Safety           = val; break;
            case "safety_default":       task.SafetyDefault    = val; break;
            case "title":                task.Title            = val; break;
            case "instructions":         task.Instructions     = val; break;
            case "has_safety_options":   task.HasSafetyOptions = string.Equals(val, "true", System.StringComparison.OrdinalIgnoreCase); break;
        }
    }

    /// <returns>
    /// <see langword="true"/> if the <c>choices:</c> key has no inline value, indicating
    /// the parser should switch to YAML-list collection mode.
    /// </returns>
    private static bool ParseOptionSubfield(string field, CodeHealthOptionBuilder opt) {
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
                        opt.Choices.Add(new CodeHealthOptionChoice { Value = v });
                }
                break;
        }
        return false;
    }

    /// <summary>
    /// Finds the task with <paramref name="taskId"/> in the code-health.md frontmatter,
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
    /// Replaces the task block identified by <paramref name="taskId"/> in the maintenance
    /// file at <paramref name="filePath"/> with the values from <paramref name="updated"/>.
    /// Preserves all file content outside the task block (other tasks, global config, comments,
    /// blank lines, YAML body below the closing ---).
    /// </summary>
    public static void UpdateTask(string filePath, string taskId, CodeHealthTask updated) {
        if (!File.Exists(filePath)) return;

        string raw;
        try { raw = File.ReadAllText(filePath); }
        catch { return; }

        var lineEnding = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // Find frontmatter opening ---
        int fmStart = -1;
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].Trim() == "---") { fmStart = i; break; }
        }
        if (fmStart < 0) return;

        // Find frontmatter closing ---
        int fmEnd = -1;
        for (int i = fmStart + 1; i < lines.Length; i++) {
            if (lines[i].Trim() == "---") { fmEnd = i; break; }
        }
        if (fmEnd < 0) return;

        // Find the task block: "  - id: {taskId}" at indent 2
        int taskStart = -1;
        for (int i = fmStart + 1; i < fmEnd; i++) {
            var line = lines[i];
            if (line.Length >= 4 && line[0] == ' ' && line[1] == ' ' && line[2] == '-' && line[3] == ' ') {
                var rest = line[4..].TrimStart();
                if (rest.StartsWith("id:", StringComparison.Ordinal)) {
                    var idVal = rest["id:".Length..].Trim().Trim('"', '\'');
                    if (string.Equals(idVal, taskId, StringComparison.Ordinal)) {
                        taskStart = i;
                        break;
                    }
                }
            }
        }
        if (taskStart < 0) return;

        // Find end of task block: next "  - " task entry or the closing ---
        int taskEnd = fmEnd;
        for (int i = taskStart + 1; i < fmEnd; i++) {
            var line = lines[i];
            if (line.Length >= 4 && line[0] == ' ' && line[1] == ' ' && line[2] == '-' && line[3] == ' ') {
                taskEnd = i;
                break;
            }
        }

        // Serialize updated task to YAML lines
        var newTaskLines = SerializeTask(updated);

        // Reconstruct file lines
        var result = new List<string>(lines.Length - (taskEnd - taskStart) + newTaskLines.Count);
        for (int i = 0; i < taskStart; i++)
            result.Add(lines[i]);
        result.AddRange(newTaskLines);
        for (int i = taskEnd; i < lines.Length; i++)
            result.Add(lines[i]);

        try { File.WriteAllText(filePath, string.Join(lineEnding, result)); }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Appends a new task entry to the <c>tasks:</c> block in <paramref name="filePath"/>.
    /// If no <c>tasks:</c> key is present in the frontmatter, one is inserted before the
    /// closing <c>---</c>. Does nothing if the file cannot be read or has no frontmatter.
    /// </summary>
    public static void AppendTask(string filePath, CodeHealthTask task) {
        if (!File.Exists(filePath)) return;

        string raw;
        try { raw = File.ReadAllText(filePath); }
        catch { return; }

        var lineEnding = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();

        // Locate frontmatter opening and closing ---
        int fmStart = -1, fmEnd = -1;
        for (int i = 0; i < lines.Count; i++) {
            if (lines[i].Trim() == "---") {
                if (fmStart < 0) fmStart = i;
                else { fmEnd = i; break; }
            }
        }
        if (fmEnd < 0) return;

        // Check whether a tasks: key already exists inside the frontmatter
        bool hasTasksKey = false;
        for (int i = fmStart + 1; i < fmEnd; i++) {
            if (lines[i].TrimStart() == "tasks:") { hasTasksKey = true; break; }
        }

        var taskLines = SerializeTask(task);

        if (!hasTasksKey) {
            // Insert "tasks:" followed by the new task block just before the closing ---
            lines.Insert(fmEnd, "tasks:");
            fmEnd++;  // closing --- shifted by one
        }

        // Insert the new task block just before the closing ---
        for (int k = taskLines.Count - 1; k >= 0; k--)
            lines.Insert(fmEnd, taskLines[k]);

        try { File.WriteAllText(filePath, string.Join(lineEnding, lines)); }
        catch { /* best-effort */ }
    }

    private static List<string> SerializeTask(CodeHealthTask t) {
        var lines = new List<string>();
        lines.Add($"  - id: {t.Id}");
        lines.Add($"    enabled: {t.Enabled.ToString().ToLower()}");
        lines.Add($"    frequency: {t.Frequency}");
        lines.Add($"    safety: {t.Safety}");
        if (!string.IsNullOrEmpty(t.SafetyDefault))
            lines.Add($"    safety_default: {t.SafetyDefault}");
        lines.Add($"    title: {t.Title}");
        if (t.HasSafetyOptions)
            lines.Add($"    has_safety_options: true");
        lines.Add("    instructions: |");

        // Instructions block scalar: content indented 6 spaces
        var instrText = t.Instructions ?? "";
        var instrLines = instrText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        foreach (var instrLine in instrLines)
            lines.Add($"      {instrLine}");

        if (t.Options is { Count: > 0 }) {
            lines.Add("    options:");
            foreach (var opt in t.Options) {
                lines.Add($"      {opt.Key}:");
                if (!string.IsNullOrEmpty(opt.Type))
                    lines.Add($"        type: {opt.Type}");
                if (!string.IsNullOrEmpty(opt.Label))
                    lines.Add($"        label: {opt.Label}");
                if (!string.IsNullOrEmpty(opt.Tooltip))
                    lines.Add($"        tooltip: {opt.Tooltip}");
                if (!string.IsNullOrEmpty(opt.RawValue))
                    lines.Add($"        value: {opt.RawValue}");
                if (opt.Choices is { Count: > 0 }) {
                    lines.Add("        choices:");
                    foreach (var choice in opt.Choices) {
                        lines.Add($"          - value: {choice.Value}");
                        if (!string.IsNullOrEmpty(choice.Tooltip))
                            lines.Add($"            tooltip: {choice.Tooltip}");
                    }
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// Finds the task with <paramref name="taskId"/> in the code-health.md frontmatter and
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

    /// <summary>
    /// Flips the <c>enabled:</c> field for <paramref name="taskId"/> in a single pass and
    /// writes the file back.  Returns the new enabled value, or <see langword="null"/> if the
    /// task or its <c>enabled:</c> field was not found.
    /// </summary>
    internal static bool? ToggleTaskEnabled(string mdPath, string taskId) {
        if (!File.Exists(mdPath)) return null;
        var raw   = File.ReadAllText(mdPath);
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

            if (indent == 4 && trimmed.StartsWith("enabled:")) {
                var val      = trimmed["enabled:".Length..].Trim().Trim('"', '\'');
                bool newVal  = !string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                lines[i]     = "    enabled: " + (newVal ? "true" : "false");
                File.WriteAllText(mdPath, string.Join(le, lines));
                return newVal;
            }
        }

        return null;
    }

    private static int CountLeadingSpaces(string line) {
        int n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    // ── Override Pattern: Three-file loading with precedence ──────────────────

    /// <summary>
    /// Loads all code health tasks from system, custom, and overrides files with precedence:
    /// 1. Overrides file (highest priority - user edits to system tasks)
    /// 2. Custom file (medium priority - user-created tasks)
    /// 3. System file (lowest priority - our provisioned tasks)
    /// Returns merged dictionary keyed by task ID, then converted to sorted list.
    /// </summary>
    public static List<CodeHealthTask> ParseAllSources(string workspacePath) {
        var allTasks = new Dictionary<string, CodeHealthTask>(StringComparer.Ordinal);

        // 1. Load system tasks first (lowest priority)
        var systemPath = Path.Combine(workspacePath, ".squad", "code-health.md");
        var systemConfig = Parse(systemPath);
        if (systemConfig?.Tasks is { Count: > 0 }) {
            foreach (var task in systemConfig.Tasks) {
                allTasks[task.Id] = task;
            }
        }

        // 2. Load custom tasks (medium priority)
        var customPath = Path.Combine(workspacePath, ".squad", "code-health-custom.md");
        var customConfig = File.Exists(customPath) ? Parse(customPath) : null;
        if (customConfig?.Tasks is { Count: > 0 }) {
            foreach (var task in customConfig.Tasks) {
                allTasks[task.Id] = task;
            }
        }

        // 3. Load overrides (highest priority, overwrites system/custom)
        var overridesPath = Path.Combine(workspacePath, ".squad", "code-health-overrides.md");
        var overridesConfig = File.Exists(overridesPath) ? Parse(overridesPath) : null;
        if (overridesConfig?.Tasks is { Count: > 0 }) {
            foreach (var task in overridesConfig.Tasks) {
                allTasks[task.Id] = task;
            }
        }

        return allTasks.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Checks if a task with the given ID has been overridden by the user.
    /// Returns true if the task exists in the overrides file.
    /// </summary>
    public static bool IsTaskOverridden(string taskId, string workspacePath) {
        var overridesPath = Path.Combine(workspacePath, ".squad", "code-health-overrides.md");
        if (!File.Exists(overridesPath))
            return false;

        var overridesConfig = Parse(overridesPath);
        return overridesConfig?.Tasks?.Any(t => string.Equals(t.Id, taskId, StringComparison.Ordinal)) ?? false;
    }

    /// <summary>
    /// Checks if a task should show the "Revert to Default Implementation" context menu item.
    /// This is true when the task exists in the overrides file.
    /// </summary>
    public static bool ShouldShowRevertOption(string taskId, string workspacePath) {
        return IsTaskOverridden(taskId, workspacePath);
    }

    /// <summary>
    /// Saves a task override to the code-health-overrides.md file.
    /// If the task already exists in overrides, it is replaced.
    /// </summary>
    public static void SaveTaskOverride(CodeHealthTask editedTask, string workspacePath) {
        var overridesPath = Path.Combine(workspacePath, ".squad", "code-health-overrides.md");

        // Load existing overrides
        var existingOverrides = File.Exists(overridesPath)
            ? Parse(overridesPath)?.Tasks?.ToList() ?? new List<CodeHealthTask>()
            : new List<CodeHealthTask>();

        // Remove existing version of this task (if any)
        existingOverrides = existingOverrides
            .Where(t => !string.Equals(t.Id, editedTask.Id, StringComparison.Ordinal))
            .ToList();

        // Add edited version
        existingOverrides.Add(editedTask);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(overridesPath);
        if (dir != null && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }

        // Write back to file
        WriteCodeHealthFile(overridesPath, existingOverrides);

        SquadDashTrace.Write(TraceCategory.General,
            $"Saved task override for '{editedTask.Id}' to code-health-overrides.md");
    }

    /// <summary>
    /// Reverts a task to its default (system) version by removing it from the overrides file.
    /// If no more overrides exist after removal, deletes the overrides file.
    /// </summary>
    public static void RevertTaskToDefault(string taskId, string workspacePath) {
        var overridesPath = Path.Combine(workspacePath, ".squad", "code-health-overrides.md");
        if (!File.Exists(overridesPath))
            return;

        var overridesConfig = Parse(overridesPath);
        if (overridesConfig?.Tasks == null || overridesConfig.Tasks.Count == 0)
            return;

        var remainingTasks = overridesConfig.Tasks
            .Where(t => !string.Equals(t.Id, taskId, StringComparison.Ordinal))
            .ToList();

        if (remainingTasks.Count == 0) {
            try {
                File.Delete(overridesPath);
            }
            catch {
                // Best-effort cleanup
            }
        }
        else {
            WriteCodeHealthFile(overridesPath, remainingTasks);
        }

        SquadDashTrace.Write(TraceCategory.General,
            $"Reverted task '{taskId}' to default from code-health-overrides.md");
    }

    /// <summary>
    /// Promotes a task override to the system code-health.md file.
    /// Reads the task from code-health-overrides.md, writes it to code-health.md,
    /// then removes it from the overrides file.
    /// </summary>
    public static void PromoteOverrideToSystemFile(string taskId, string workspacePath) {
        var systemPath    = Path.Combine(workspacePath, ".squad", "code-health.md");
        var overridesPath = Path.Combine(workspacePath, ".squad", "code-health-overrides.md");

        if (!File.Exists(overridesPath)) return;

        var overridesConfig = Parse(overridesPath);
        var overriddenTask  = overridesConfig?.Tasks?.FirstOrDefault(
            t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
        if (overriddenTask is null) return;

        if (File.Exists(systemPath))
            UpdateTask(systemPath, taskId, overriddenTask);

        RevertTaskToDefault(taskId, workspacePath);

        SquadDashTrace.Write(TraceCategory.General,
            $"Promoted task '{taskId}' override to system code-health.md");
    }

    /// <summary>
    /// Migrates user edits from code-health.md to code-health-overrides.md on startup.
    /// Compares current code-health.md against the embedded system version to detect differences.
    /// Only called if migration is needed (user had edited code-health.md directly).
    /// </summary>
    public static void MigrateUserEditsToOverrides(string workspacePath) {
        var systemPath = Path.Combine(workspacePath, ".squad", "code-health.md");
        var overridesPath = Path.Combine(workspacePath, ".squad", "code-health-overrides.md");

        // If overrides already exist, skip migration
        if (File.Exists(overridesPath))
            return;

        var currentConfig = Parse(systemPath);
        if (currentConfig?.Tasks == null || currentConfig.Tasks.Count == 0)
            return;

        var currentTasks = currentConfig.Tasks.ToList();

        // To detect edits, we would need the embedded system version.
        // For now, we don't have access to the original system version,
        // so this method serves as a placeholder for future implementation
        // that would compare against a version hash or embedded snapshot.

        // If in the future we have a way to detect modifications,
        // we would extract edited tasks and save them to overrides.
    }

    /// <summary>
    /// Writes a list of code health tasks to a file in YAML format with the expected frontmatter.
    /// </summary>
    private static void WriteCodeHealthFile(string filePath, List<CodeHealthTask> tasks) {
        var lines = new List<string>();

        // Frontmatter opening
        lines.Add("---");
        lines.Add("configured: true");
        lines.Add("enabled_on_idle: false");
        lines.Add("idle_timeout: 15");
        lines.Add("max_tasks_per_session: 5");
        lines.Add("safety: branch");

        if (tasks.Count > 0) {
            lines.Add("tasks:");
            foreach (var task in tasks.OrderBy(t => t.Id, StringComparer.Ordinal)) {
                lines.AddRange(SerializeTask(task));
            }
        }

        // Frontmatter closing
        lines.Add("---");
        lines.Add("");

        // Determine line ending from file if it exists, otherwise use \n
        string lineEnding = "\n";
        if (File.Exists(filePath)) {
            try {
                var content = File.ReadAllText(filePath);
                if (content.Contains("\r\n")) {
                    lineEnding = "\r\n";
                }
            }
            catch {
                // Use default
            }
        }

        try {
            File.WriteAllText(filePath, string.Join(lineEnding, lines));
        }
        catch {
            // Best-effort write
        }
    }

    // ── Mutable builder ───────────────────────────────────────────────────────

    private sealed class CodeHealthTaskBuilder {
        public string  Id               { get; set; } = "";
        public bool    Enabled          { get; set; } = false;
        public string  Frequency        { get; set; } = "daily";
        public string  Safety           { get; set; } = "";
        public string  SafetyDefault    { get; set; } = "";
        public string  Title            { get; set; } = "";
        public string  Instructions     { get; set; } = "";
        public bool    HasSafetyOptions { get; set; } = false;
        public List<CodeHealthOption>? BuiltOptions { get; set; }

        public CodeHealthTask Build(string globalSafety, string sourceFilePath = "") =>
            new(
                Id:                 Id,
                Enabled:            Enabled,
                Frequency:          Frequency,
                Safety:             EnforceSafetyFloor(globalSafety, Safety),
                Title:              Title.Length > 0 ? Title : Id,
                Instructions:       Instructions,
                Options:            BuiltOptions,
                HasSafetyOptions:   HasSafetyOptions,
                SourceFilePath:     sourceFilePath,
                SafetyDefault:      SafetyDefault);
    }

    private sealed class CodeHealthOptionBuilder {
        public string Key      { get; init; } = "";
        public string? RawValue { get; set; }
        public string? Type     { get; set; }
        public string? Label    { get; set; }
        public string? Tooltip  { get; set; }
        public List<CodeHealthOptionChoice> Choices { get; } = new();
    }
}


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

        return ParseFromContent(content);
    }

    /// <summary>
    /// Parses loop.md content supplied as a string (e.g. from an embedded resource).
    /// Returns null when the frontmatter does not contain <c>configured: true</c>.
    /// </summary>
    public static LoopMdConfig? ParseFromContent(string content) {
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

    // Equality form: {{#if key == "value"}} or {{#unless key == "value"}}
    // Group 1 = prefix text (may be empty or whitespace-only)
    // Group 2 = "if" or "unless"
    // Group 3 = key
    // Group 4 = expected value
    private static readonly Regex _conditionalOpenEqualityPattern =
        new(@"^(.*?)\{\{#(if|unless)\s+(\w+)\s*==\s*""([^""]*)""\s*\}\}\s*$",
            RegexOptions.Compiled);

    // Bare boolean form: {{#if key}} or {{#unless key}}  (no == "value")
    // Group 1 = prefix, Group 2 = "if"|"unless", Group 3 = key
    private static readonly Regex _conditionalOpenBoolPattern =
        new(@"^(.*?)\{\{#(if|unless)\s+(\w+)\s*\}\}\s*$",
            RegexOptions.Compiled);

    // Matches {{/if}} or {{/unless}}
    private static readonly Regex _conditionalClosePattern =
        new(@"^\s*\{\{/(if|unless)\s*\}\}\s*$", RegexOptions.Compiled);

    // Matches any remaining stray conditional syntax line (opening or closing)
    private static readonly Regex _anyConditionalLinePattern =
        new(@"^\s*\{\{[/#](?:if|unless)[^}]*\}\}\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // Inline same-line form: {{#if [!]key}}content{{/if}}
    // Group 1 = optional "!" negation, Group 2 = key, Group 3 = inner content
    private static readonly Regex _inlineIfPattern =
        new(@"\{\{#if\s+(!?)(\w+)\s*\}\}(.*?)\{\{/if\s*\}\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

    // Inline same-line unless form: {{#unless key}}content{{/unless}}
    // Group 1 = key, Group 2 = inner content
    private static readonly Regex _inlineUnlessPattern =
        new(@"\{\{#unless\s+(\w+)\s*\}\}(.*?)\{\{/unless\s*\}\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Returns true when <paramref name="value"/> is considered truthy —
    /// i.e. non-empty, not <c>"false"</c>, and not <c>"0"</c>.</summary>
    internal static bool IsOptionTruthy(string? value) =>
        !string.IsNullOrEmpty(value) &&
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "0",     StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tries to match a conditional-open tag at <paramref name="line"/>.
    /// Returns <see langword="true"/> on a match and sets the out parameters;
    /// <paramref name="expectedValue"/> is <see langword="null"/> for the bare
    /// boolean form <c>{{#if key}}</c>.
    /// </summary>
    private static bool TryMatchConditionalOpen(
        string line,
        out string prefix, out string verb, out string key, out string? expectedValue)
    {
        var m = _conditionalOpenEqualityPattern.Match(line);
        if (m.Success)
        {
            prefix        = m.Groups[1].Value;
            verb          = m.Groups[2].Value;
            key           = m.Groups[3].Value;
            expectedValue = m.Groups[4].Value;
            return true;
        }
        var m2 = _conditionalOpenBoolPattern.Match(line);
        if (m2.Success)
        {
            prefix        = m2.Groups[1].Value;
            verb          = m2.Groups[2].Value;
            key           = m2.Groups[3].Value;
            expectedValue = null;
            return true;
        }
        prefix = verb = key = string.Empty;
        expectedValue = null;
        return false;
    }

    /// <summary>
    /// Resolves inline same-line conditionals of the form
    /// <c>{{#if [!]key}}content{{/if}}</c> and <c>{{#unless key}}content{{/unless}}</c>.
    /// This pass must run before block-level processing.
    /// </summary>
    private static string ResolveInlineConditionals(string text, IReadOnlyDictionary<string, string> values)
    {
        // Inline {{#unless key}}...{{/unless}}
        text = _inlineUnlessPattern.Replace(text, m =>
        {
            var key     = m.Groups[1].Value;
            var content = m.Groups[2].Value;
            var actual  = values.TryGetValue(key, out var v) ? v : string.Empty;
            return IsOptionTruthy(actual) ? string.Empty : content;
        });

        // Inline {{#if [!]key}}...{{/if}}
        text = _inlineIfPattern.Replace(text, m =>
        {
            bool negate  = m.Groups[1].Value == "!";
            var key      = m.Groups[2].Value;
            var content  = m.Groups[3].Value;
            var actual   = values.TryGetValue(key, out var v) ? v : string.Empty;
            bool truthy  = IsOptionTruthy(actual);
            bool include = negate ? !truthy : truthy;
            return include ? content : string.Empty;
        });

        return text;
    }

    /// <summary>
    /// Evaluates <c>{{#if key == "value"}}…{{/if}}</c>,
    /// <c>{{#if key}}…{{/if}}</c>, <c>{{#if !key}}…{{/if}}</c>,
    /// and <c>{{#unless key}}…{{/unless}}</c> conditional blocks in
    /// <paramref name="text"/>, including or discarding their inner content based on
    /// the current option values.  Must be called <em>before</em> plain
    /// <c>{{key}}</c> substitution so that variable tokens inside included blocks
    /// are resolved in the subsequent substitution pass.
    /// <para>
    /// After block evaluation, any remaining stray conditional-syntax lines are
    /// stripped and runs of three or more consecutive blank lines are collapsed to two.
    /// </para>
    /// </summary>
    public static string PreprocessConditionals(string text, IReadOnlyList<LoopOption>? options)
    {
        // Build a key→value lookup; skip group-type options (UI-only headers, no value).
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (options is not null)
            foreach (var opt in options)
                if (opt.Type != "group")
                    values[opt.Key] = opt.RawValue;

        return PreprocessConditionals(text, (IReadOnlyDictionary<string, string>)values);
    }

    /// <summary>
    /// Like <see cref="PreprocessConditionals(string,IReadOnlyDictionary{string,string})"/> but
    /// returns a list of <c>(text, isConditional)</c> segments instead of a single resolved string.
    /// Segments where <c>isConditional</c> is <see langword="true"/> contain content that was
    /// inside an included <c>{{#if}}</c> or <c>{{#unless}}</c> block — callers can render these
    /// with a visually distinct style. Excluded blocks are dropped entirely.
    /// </summary>
    public static List<(string Text, bool IsConditional)> ResolveSegments(
        string text, IReadOnlyDictionary<string, string> values)
    {
        text = ResolveInlineConditionals(text, values);

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var segments  = new List<(string, bool)>();
        var normalBuf = new List<string>();
        var condBuf   = new List<string>();
        bool inBlock      = false;
        bool includeBlock = false;
        string? blockVerb = null;

        void FlushNormal() {
            if (normalBuf.Count == 0) return;
            segments.Add((string.Join("\n", normalBuf), false));
            normalBuf.Clear();
        }
        void FlushCond() {
            if (condBuf.Count == 0) return;
            var joined = string.Join("\n", condBuf).Trim('\n');
            if (!string.IsNullOrWhiteSpace(joined))
                segments.Add((joined, true));
            condBuf.Clear();
        }

        foreach (var line in lines)
        {
            if (!inBlock)
            {
                if (TryMatchConditionalOpen(line, out var prefix, out var verb, out var key, out var expectedValue))
                {
                    blockVerb    = verb;
                    var actual   = values.TryGetValue(key, out var v) ? v : string.Empty;
                    bool met     = expectedValue != null
                        ? string.Equals(actual, expectedValue, StringComparison.Ordinal)
                        : IsOptionTruthy(actual);
                    includeBlock = blockVerb == "if" ? met : !met;
                    inBlock      = true;
                    if (!string.IsNullOrWhiteSpace(prefix))
                        normalBuf.Add(prefix);
                }
                else
                {
                    normalBuf.Add(line);
                }
            }
            else
            {
                var m = _conditionalClosePattern.Match(line);
                if (m.Success && m.Groups[1].Value == blockVerb)
                {
                    if (includeBlock) { FlushNormal(); FlushCond(); }
                    inBlock   = false;
                    blockVerb = null;
                }
                else if (includeBlock)
                {
                    condBuf.Add(line);
                }
                // else: excluded block — discard
            }
        }

        FlushNormal();

        return segments;
    }

    /// <summary>
    /// Overload that accepts an already-resolved key→value dictionary. Use this
    /// when the caller has its own option type (e.g. <see cref="MaintenanceOption"/>).
    /// </summary>
    public static string PreprocessConditionals(string text, IReadOnlyDictionary<string, string> values)
    {
        text = ResolveInlineConditionals(text, values);

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        bool inBlock      = false;
        bool includeBlock = false;
        string? blockVerb     = null;   // "if" or "unless" — which close tag to wait for
        string? pendingPrefix = null;   // prefix held until first non-empty content line

        foreach (var line in lines)
        {
            if (!inBlock)
            {
                if (TryMatchConditionalOpen(line, out var prefix, out var verb, out var key, out var expectedValue))
                {
                    blockVerb    = verb;
                    var actual   = values.TryGetValue(key, out var v) ? v : string.Empty;
                    bool met     = expectedValue != null
                        ? string.Equals(actual, expectedValue, StringComparison.Ordinal)
                        : IsOptionTruthy(actual);
                    includeBlock = blockVerb == "if" ? met : !met;
                    inBlock      = true;
                    // When the block is included and the prefix has non-whitespace content,
                    // hold it so it can be prepended to the first non-empty content line.
                    if (includeBlock && prefix.Trim().Length > 0)
                        pendingPrefix = prefix;
                }
                else
                {
                    output.Add(line);
                }
            }
            else
            {
                var m = _conditionalClosePattern.Match(line);
                if (m.Success && m.Groups[1].Value == blockVerb)
                {
                    // Block ended — if prefix was never consumed (no non-empty content), emit as-is.
                    if (pendingPrefix != null)
                    {
                        output.Add(pendingPrefix);
                        pendingPrefix = null;
                    }
                    inBlock   = false;
                    blockVerb = null;
                }
                else if (includeBlock)
                {
                    if (pendingPrefix != null && !string.IsNullOrWhiteSpace(line))
                    {
                        output.Add(pendingPrefix + line.TrimStart());
                        pendingPrefix = null;
                    }
                    else
                    {
                        output.Add(line);
                    }
                }
                // else: content of a false block → silently discard
            }
        }

        var processed = string.Join("\n", output);

        // Safety cleanup: strip any stray unmatched conditional-syntax lines
        processed = _anyConditionalLinePattern.Replace(processed, string.Empty);

        // Collapse runs of 3+ consecutive blank lines down to 2
        processed = Regex.Replace(processed, @"\n{3,}", "\n\n");

        return processed;
    }

    /// <summary>
    /// Returns the instructions body with all <c>{{optionKey}}</c> placeholders
    /// replaced by the current <c>RawValue</c> of the matching option, plus any
    /// <paramref name="extraSubstitutions"/> (e.g. system variables like {{iteration}}).
    /// Options of type "group" are skipped (they are UI headers, not values).
    /// Conditional blocks (<c>{{#if}}</c>/<c>{{#unless}}</c>) are evaluated first.
    /// <para>
    /// <paramref name="extraSubstitutions"/> entries whose key matches <c>{{key}}</c>
    /// syntax are replaced via the double-brace wrapper. The special key
    /// <c>"[**FILTER**]"</c> is substituted literally (bracket syntax, not
    /// double-brace).
    /// </para>
    /// </summary>
    public static string BuildMergedBody(LoopMdConfig config, IReadOnlyDictionary<string, string>? extraSubstitutions = null)
    {
        // Conditionals must be resolved before plain substitution so that {{key}} tokens
        // inside included blocks are substituted in the pass below.
        var body = PreprocessConditionals(config.Instructions, config.Options);
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

            // [**FILTER**] uses bracket syntax, not {{...}}, so handle it directly.
            if (extraSubstitutions.TryGetValue("[**FILTER**]", out var filterInstruction))
                body = body.Replace("[**FILTER**]", filterInstruction, StringComparison.Ordinal);
        }
        return body;
    }

    private static readonly Regex _decomposeGroupIdPattern =
        new(@"^[A-Z]+-\d{8}$", RegexOptions.Compiled);

    /// <summary>
    /// Converts a raw filter string (as typed in the Tasks panel filter box, or a decompose
    /// group ID) into a human-readable instruction suitable for substituting the
    /// <c>[**FILTER**]</c> placeholder in loop templates.
    /// <para>
    /// When <paramref name="filterText"/> matches <c>^[A-Z]+-\d{8}$</c> (a decompose group ID),
    /// returns a structured group-scoped execution instruction instead of the standard filter text.
    /// </para>
    /// </summary>
    /// <param name="filterText">
    /// The raw filter value. May contain <c>@agent-handle</c> mentions, keywords, a decompose
    /// group ID, or both. Pass an empty string (or null) to produce the "no filter" instruction.
    /// </param>
    public static string BuildFilterInstruction(string? filterText)
    {
        filterText = filterText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(filterText))
            return "No filter — process any unchecked task not owned by User.";

        // Decompose group ID — emit a structured eligibility-scoped instruction.
        if (_decomposeGroupIdPattern.IsMatch(filterText))
        {
            return $"Active decompose group: {filterText}\n\n" +
                   "Read `.squad/tasks.md`. Execute the first subtask where:\n" +
                   "- Status marker is `[ ]` (pending)\n" +
                   "- All IDs in `dependsOn` are `[x]` (complete)";
        }

        var mentionMatches= Regex.Matches(filterText, @"@(\w[\w\-\.]*)", RegexOptions.IgnoreCase);
        var agentNames     = new List<string>();
        var seen           = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in mentionMatches)
            if (seen.Add(m.Groups[1].Value))
                agentNames.Add(m.Groups[1].Value);

        var keyword = Regex.Replace(filterText, @"@\w[\w\-\.]*", "").Trim();

        var parts = new List<string>();
        if (agentNames.Count > 0)
        {
            var agentList = string.Join(", ", agentNames.Select(a => $"**@{a}**"));
            var ownerList = string.Join(" or ", agentNames.Select(a => $"`*(Owner: {a})*`"));
            parts.Add($"Only process tasks assigned to {agentList} — i.e., tasks that include {ownerList} on the task line or in its description.");
        }
        if (!string.IsNullOrWhiteSpace(keyword))
            parts.Add($"Only process tasks whose description or content contains: **{keyword}**.");

        return string.Join(" ", parts);
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

}

internal sealed record LoopFileEntry(string FilePath, string DisplayName, string TooltipText = "");

/// <summary>Mutable builder used during options block parsing in loop and maintenance md parsers.</summary>
internal sealed class LoopOptionBuilder {
    public string        Key      { get; init; } = "";
    public string?       RawValue { get; set; }
    public string?       Type     { get; set; }
    public string?       Label    { get; set; }
    public string?       Hint     { get; set; }
    public List<string>? Choices  { get; set; }
}

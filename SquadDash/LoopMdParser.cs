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

        // Read key: value pairs up to the closing ---
        while (i < lines.Length && lines[i].Trim() != "---") {
            var m = _kvPattern.Match(lines[i]);
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
            i++;
        }

        if (!configured)
            return null;

        // Everything after the closing --- is the instructions body.
        if (i >= lines.Length)
            return new LoopMdConfig(intervalMinutes, timeoutMinutes, description, "", commands);

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
            commands);
    }
}

namespace SquadDash;

internal static class CliCmdEscaping
{
    /// <summary>
    /// Wraps <paramref name="token"/> in double quotes with correct cmd.exe internal escaping.
    /// This is the sole canonical CMD quoting implementation in the codebase.
    /// Both ProbeVersionAsync (SquadCliResolutionService, task 002) and BuildCommandStartInfo
    /// (SquadCliCommands, task 004) must call this method for every cmd.exe NpmShim invocation.
    /// No separate or diverging CMD quoting logic is permitted elsewhere.
    /// </summary>
    /// <remarks>
    /// Exact escape sequences are validated by real cmd.exe round-trip invocation tests in task 008,
    /// not by string-comparison-only assertions.
    /// </remarks>
    public static string CmdQuoteToken(string token)
    {
        // Unconditionally wrap in double quotes.
        // Escape backslashes that immediately precede a double-quote (and the closing quote),
        // then escape internal double-quotes with backslash.
        // Contract: must survive cmd.exe round-trip — validated in task 008.
        var escaped = EscapeForCmd(token);
        return $"\"{escaped}\"";
    }

    private static string EscapeForCmd(string token)
    {
        // Escape internal double-quotes: replace " with \"
        // Escape backslash sequences before quotes per cmd.exe rules
        var sb = new System.Text.StringBuilder(token.Length + 4);
        int backslashCount = 0;
        foreach (char c in token)
        {
            if (c == '\\')
            {
                backslashCount++;
            }
            else if (c == '"')
            {
                // Double all preceding backslashes, then escape the quote
                sb.Append('\\', backslashCount * 2);
                backslashCount = 0;
                sb.Append('\\');
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
                sb.Append(c);
            }
        }

        // Double trailing backslashes (they precede the closing quote)
        sb.Append('\\', backslashCount * 2);
        return sb.ToString();
    }
}

using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Pure, UI-free logic for finding a complete triple-backtick fenced block within a sequence
/// of lines, and for serializing region content back into fenced text. Separated from
/// <see cref="EmbeddedCodeRegionManager"/> (which performs the actual FlowDocument surgery)
/// so the detection rules are unit-testable without a live <c>RichTextBox</c>/STA thread.
/// </summary>
internal static class EmbeddedFenceDetector
{
    // A fence-opening line is "```" optionally followed immediately by a language tag
    // (letters, digits, underscore, plus, hyphen — e.g. "```python", "```c++"). Nothing else
    // may follow on that line.
    private static readonly Regex FenceOpenRegex = new(@"^```([A-Za-z0-9_+\-]*)$", RegexOptions.Compiled);

    internal readonly record struct FenceSpan(int OpenLineIndex, int CloseLineIndex, string Language)
    {
        /// Joins the lines strictly between the opening and closing fence markers.
        public string ExtractContent(string[] lines)
            => string.Join("\n", lines.Skip(OpenLineIndex + 1).Take(CloseLineIndex - OpenLineIndex - 1));
    }

    /// <summary>
    /// Finds the first complete fence (an opening ``` / ```&lt;language&gt; line followed,
    /// somewhere later, by a bare ``` closing line) within <paramref name="lines"/>.
    /// Returns <see langword="null"/> if no complete fence is present (e.g. an opening marker
    /// with no matching close yet — still being typed).
    /// </summary>
    internal static FenceSpan? FindFirstFence(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var openMatch = FenceOpenRegex.Match(lines[i].Trim());
            if (!openMatch.Success) continue;

            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim() != "```") continue;
                return new FenceSpan(i, j, openMatch.Groups[1].Value);
            }
            // Opening marker found but no closing marker yet — fence is incomplete.
            // Keep scanning from i+1 in case a later line starts a different, complete fence.
        }
        return null;
    }

    internal readonly record struct OpenFence(int LineIndex, string Language);

    /// <summary>
    /// Finds the first opening fence line (``` or ```&lt;language&gt;, and nothing else on
    /// that line) that has no corresponding closing ``` anywhere later in
    /// <paramref name="lines"/> — i.e. the opening marker the user just finished typing,
    /// before any content or closing fence exists. Used to trigger the "instant open fence"
    /// conversion (create an empty, expanded region immediately) as distinct from
    /// <see cref="FindFirstFence"/>, which requires a complete open+close pair (e.g. a
    /// pasted block).
    /// </summary>
    internal static OpenFence? FindOpenFenceWithNoClose(string[] lines)
    {
        var i = 0;
        while (i < lines.Length)
        {
            var openMatch = FenceOpenRegex.Match(lines[i].Trim());
            if (!openMatch.Success)
            {
                i++;
                continue;
            }

            var closeLineIndex = -1;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim() != "```") continue;
                closeLineIndex = j;
                break;
            }

            if (closeLineIndex < 0)
                return new OpenFence(i, openMatch.Groups[1].Value);

            // This opener is already closed — skip past its closing marker so that marker
            // line is never itself re-examined as a candidate opening fence.
            i = closeLineIndex + 1;
        }
        return null;
    }

    /// <summary>
    /// Serializes code-region content back into an equivalent triple-backtick fenced block:
    /// a bare fence if <paramref name="language"/> is null/empty, or ```&lt;language&gt;
    /// immediately followed by the content and a closing ``` otherwise.
    /// </summary>
    internal static string BuildFencedText(string content, string? language)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n");
        return string.IsNullOrEmpty(language)
            ? $"```\n{normalized}\n```"
            : $"```{language}\n{normalized}\n```";
    }
}

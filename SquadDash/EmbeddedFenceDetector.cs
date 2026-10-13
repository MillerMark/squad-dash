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

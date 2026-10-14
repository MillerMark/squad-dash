using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Pure, UI-free logic for finding a complete backtick-fenced block within a sequence
/// of lines, and for serializing region content back into fenced text. Separated from
/// <see cref="EmbeddedCodeRegionManager"/> (which performs the actual FlowDocument surgery)
/// so the detection rules are unit-testable without a live <c>RichTextBox</c>/STA thread.
/// <para>
/// Fences are variable-length so that region content which itself contains backtick fence
/// lines (a bare <c>```</c>, or a nested fenced block) round-trips losslessly through both
/// copy-to-clipboard and send-the-prompt. The outer fence is always chosen strictly longer
/// than the longest run of backticks anywhere inside the content (see
/// <see cref="BuildFencedText"/>), and a closing fence is any bare-backtick line whose run
/// length is &gt;= the opener's length.
/// </para>
/// </summary>
internal static class EmbeddedFenceDetector
{
    // A fence-opening line is a run of 3+ backticks optionally followed immediately by a
    // language tag (letters, digits, underscore, plus, hyphen, dot, hash — e.g. "```python",
    // "````c++", "```c#", "```f#", "```asp.net"). Nothing else may follow on that line.
    // Group 1 captures the backtick run (its length is the fence length N); group 2 captures
    // the language tag. Leading indentation is NOT allowed: callers match against a
    // TrimEnd()'d line so an indented marker (e.g. inside a list or block-quote) is treated
    // as literal text, not a fence.
    private static readonly Regex FenceOpenRegex = new(@"^(`{3,})([A-Za-z0-9_+.#\-]*)$", RegexOptions.Compiled);

    internal readonly record struct FenceSpan(int OpenLineIndex, int CloseLineIndex, string Language, int FenceLength)
    {
        /// Joins the lines strictly between the opening and closing fence markers. Any
        /// carriage returns left over from CRLF-pasted text are stripped so region content
        /// never carries stray '\r' characters (keeping GetPlainText round-trips exact).
        public string ExtractContent(string[] lines)
            => string.Join("\n", lines.Skip(OpenLineIndex + 1).Take(CloseLineIndex - OpenLineIndex - 1))
                .Replace("\r", string.Empty);
    }

    /// <summary>
    /// Finds the first complete fence (an opening <c>```</c> / <c>```&lt;language&gt;</c> line
    /// of some length N followed, somewhere later, by a bare backtick line of length &gt;= N)
    /// within <paramref name="lines"/>. Returns <see langword="null"/> if no complete fence is
    /// present (e.g. an opening marker with no matching close yet — still being typed).
    /// </summary>
    internal static FenceSpan? FindFirstFence(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var openMatch = FenceOpenRegex.Match(lines[i].TrimEnd());
            if (!openMatch.Success) continue;

            var fenceLength = openMatch.Groups[1].Value.Length;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (!IsClosingFence(lines[j], fenceLength)) continue;
                return new FenceSpan(i, j, openMatch.Groups[2].Value, fenceLength);
            }
            // Opening marker found but no qualifying closing marker yet — fence is incomplete.
            // Keep scanning from i+1 in case a later line starts a different, complete fence.
        }
        return null;
    }

    internal readonly record struct OpenFence(int LineIndex, string Language, int FenceLength);

    /// <summary>
    /// Finds the first opening fence line (a run of 3+ backticks of length N, optionally with a
    /// language tag, and nothing else on that line) that has no corresponding closing backtick
    /// line of length &gt;= N anywhere later in <paramref name="lines"/> — i.e. the opening
    /// marker the user just finished typing, before any closing fence exists. Used to trigger
    /// the "instant open fence" conversion (create an expanded region immediately, consuming any
    /// remaining content through end-of-input) as distinct from <see cref="FindFirstFence"/>,
    /// which requires a complete open+close pair (e.g. a pasted block).
    /// </summary>
    internal static OpenFence? FindOpenFenceWithNoClose(string[] lines)
    {
        var i = 0;
        while (i < lines.Length)
        {
            var openMatch = FenceOpenRegex.Match(lines[i].TrimEnd());
            if (!openMatch.Success)
            {
                i++;
                continue;
            }

            var fenceLength = openMatch.Groups[1].Value.Length;
            var closeLineIndex = -1;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (!IsClosingFence(lines[j], fenceLength)) continue;
                closeLineIndex = j;
                break;
            }

            if (closeLineIndex < 0)
                return new OpenFence(i, openMatch.Groups[2].Value, fenceLength);

            // This opener is already closed — skip past its closing marker so that marker
            // line is never itself re-examined as a candidate opening fence.
            i = closeLineIndex + 1;
        }
        return null;
    }

    /// <summary>
    /// Serializes code-region content back into an equivalent backtick-fenced block. The outer
    /// fence length is <c>max(3, longestBacktickRun(content) + 1)</c> so it is always strictly
    /// longer than the longest run of backticks anywhere inside the content — guaranteeing no
    /// inner line can be mistaken for the region's closing fence (e.g. content with a bare 3
    /// backtick line is wrapped in 4; content with a 20-backtick line is wrapped in 21). The
    /// opening fence is immediately followed by the optional <paramref name="language"/> tag,
    /// then the content, then a closing fence of the same length on its own line.
    /// </summary>
    internal static string BuildFencedText(string content, string? language)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        var fenceLength = Math.Max(3, LongestBacktickRun(normalized) + 1);
        var fence = new string('`', fenceLength);
        var languageTag = language ?? string.Empty;
        return $"{fence}{languageTag}\n{normalized}\n{fence}";
    }

    /// Length of the longest run of consecutive '`' characters anywhere in <paramref name="text"/>.
    /// Deliberately counts runs anywhere (not only pure-backtick lines) — this conservative,
    /// simple rule is what guarantees the outer fence can never collide with inner content.
    internal static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var ch in text)
        {
            if (ch == '`')
            {
                current++;
                if (current > longest) longest = current;
            }
            else
            {
                current = 0;
            }
        }
        return longest;
    }

    /// A closing fence is a TrimEnd()'d line consisting solely of backticks whose run length is
    /// &gt;= the opener's <paramref name="fenceLength"/>. A shorter bare-backtick line (or any
    /// line containing non-backtick characters) does NOT close the fence.
    private static bool IsClosingFence(string line, int fenceLength)
    {
        var trimmed = line.TrimEnd();
        if (trimmed.Length < fenceLength) return false;
        foreach (var ch in trimmed)
        {
            if (ch != '`') return false;
        }
        return true;
    }
}

using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record MarkdownDetailRegion(int StartLine, int EndLine, string Label, string Body, string? Diagnostic);

internal static class MarkdownDetailParser
{
    private static readonly Regex Opening = new(@"^:::detail(?:\s+(.*))?$", RegexOptions.CultureInvariant);

    internal static bool IsMarker(string line) => line.Trim() == ":::" || Opening.IsMatch(line.Trim());

    // Keep fence recognition identical to the shared builder's supported code fences.
    internal static bool IsFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    internal static IReadOnlyList<MarkdownDetailRegion> Parse(string[] lines)
    {
        var regions = new List<MarkdownDetailRegion>();
        var inCode = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (IsFence(lines[index]))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode)
                continue;
            var line = lines[index].Trim();
            if (line == ":::")
            {
                regions.Add(new(index, index, "", "", "Stray detail closing marker."));
                continue;
            }
            var match = Opening.Match(line);
            if (!match.Success)
                continue;

            var start = index;
            var depth = 1;
            var nested = false;
            var bodyCode = false;
            for (index++; index < lines.Length; index++)
            {
                if (IsFence(lines[index]))
                {
                    bodyCode = !bodyCode;
                    continue;
                }
                if (bodyCode)
                    continue;
                if (Opening.IsMatch(lines[index].Trim()))
                {
                    depth++;
                    nested = true;
                }
                else if (lines[index].Trim() == ":::" && --depth == 0)
                    break;
            }
            var end = Math.Min(index, lines.Length - 1);
            var label = match.Groups[1].Value.Trim();
            regions.Add(new(start, end, label.Length == 0 ? "More detail" : label,
                string.Join("\n", lines.Skip(start + 1).Take(Math.Max(0, index - start - 1))),
                depth != 0 ? "Unbalanced detail region; showing literal source."
                    : nested ? "Nested detail regions are unsupported; showing literal source." : null));
        }
        return regions;
    }
}

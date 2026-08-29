using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record CommitReferenceTextSegment(string Text, string? FullSha = null);

/// <summary>
/// Finds complete Git object IDs embedded in host- or AI-authored prose. The full value remains
/// available as the navigation target while human-facing text uses the standard seven characters.
/// </summary>
internal static class CommitReferenceText
{
    private static readonly Regex FullCommitPattern = new(
        @"(?<![0-9a-fA-F])[0-9a-fA-F]{40}(?![0-9a-fA-F])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static IReadOnlyList<CommitReferenceTextSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var segments = new List<CommitReferenceTextSegment>();
        var position = 0;
        foreach (Match match in FullCommitPattern.Matches(text))
        {
            if (match.Index > position)
                segments.Add(new CommitReferenceTextSegment(text[position..match.Index]));
            segments.Add(new CommitReferenceTextSegment(match.Value[..7], match.Value));
            position = match.Index + match.Length;
        }

        if (position < text.Length)
            segments.Add(new CommitReferenceTextSegment(text[position..]));
        return segments;
    }

    internal static string Shorten(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var parsed = Parse(text);
        if (parsed.Count == 0)
            return text;
        var result = new StringBuilder(text.Length);
        foreach (var segment in parsed)
            result.Append(segment.Text);
        return result.ToString();
    }
}

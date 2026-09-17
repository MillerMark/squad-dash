using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Rejects approval procedures that merely point at another procedure. Artifact links are useful
/// at the action that needs them, but the approval message itself must contain every required step.
/// </summary>
internal static class ApprovalOperatorGuidePolicy
{
    private static readonly Regex DelegatedProcedurePattern = new(
        @"\b(?:follow|see|consult|refer\s+to|restore\b.{0,40}\bthrough)\b[^\r\n]{0,140}\b(?:runbook(?:\.md)?|inbox|kit(?:'s)?|instructions?|steps?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex VagueArtifactPattern = new(
        @"\b(?:delivered|linked|attached|declared)\s+(?:runbook(?:\.md)?|kit|instructions?|steps?|expected(?:-after)?\s+file|capture\s+location)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BareMarkdownFilePattern = new(
        @"(?<![/:\\\w.-])[\w.-]+\.md\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static IReadOnlyList<string> Validate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var errors = new List<string>();
        foreach (var rawLine in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (DelegatedProcedurePattern.IsMatch(line) || VagueArtifactPattern.IsMatch(line))
            {
                errors.Add(
                    $"Required approval text delegates work to another procedure instead of stating the action directly: “{Abbreviate(line)}”");
                continue;
            }

            if (BareMarkdownFilePattern.IsMatch(line) &&
                !line.Contains("app://open-path:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Required approval text names a Markdown file without a contextual link or its necessary action: “{Abbreviate(line)}”");
            }
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Abbreviate(string value) =>
        value.Length <= 180 ? value : value[..177] + "…";
}

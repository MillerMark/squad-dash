using System.IO;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Validates file and folder targets embedded in Inbox Markdown. Relative targets use the
/// active SquadDash workspace root, matching <see cref="InboxMessageWindow"/> link handling.
/// </summary>
internal static partial class InboxOpenPathValidator
{
    private const string Prefix = "app://open-path:";

    internal static string? Validate(string? markdown, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        foreach (Match match in OpenPathLinkRegex().Matches(markdown))
        {
            var encodedTarget = match.Groups["target"].Value.Trim();
            if (encodedTarget.StartsWith('<') && encodedTarget.EndsWith('>'))
                encodedTarget = encodedTarget[1..^1].Trim();

            if (string.IsNullOrWhiteSpace(encodedTarget))
                return "A delivered Inbox app://open-path link has an empty target.";

            try
            {
                var rawTarget = Uri.UnescapeDataString(encodedTarget);
                var expanded = Environment.ExpandEnvironmentVariables(rawTarget);
                var resolved = Path.GetFullPath(Path.IsPathRooted(expanded)
                    ? expanded
                    : Path.Combine(workspaceRoot, expanded));
                if (File.Exists(resolved) || Directory.Exists(resolved))
                    continue;

                return
                    $"A delivered Inbox link does not resolve to an existing file or folder. " +
                    $"Target: `{Prefix}{encodedTarget}`. Resolved from the active SquadDash " +
                    $"workspace as: `{resolved}`. Relative Inbox paths are workspace-relative, " +
                    "not Git-repository-relative; use an absolute path when the target is outside " +
                    "the active workspace.";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
            {
                return $"A delivered Inbox app://open-path target is invalid: `{encodedTarget}` ({ex.Message}).";
            }
        }

        return null;
    }

    [GeneratedRegex(@"\]\(\s*app://open-path:(?<target>[^)\r\n]+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenPathLinkRegex();
}

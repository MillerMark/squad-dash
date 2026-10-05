using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record ButtonPlaceholder(
    string Token,
    string Description,
    string PowerShellExample,
    string CmdExample);

internal static class ButtonPlaceholders {
    public const string SubstitutionRule =
        "Tokens are case-sensitive and replaced once with literal values, never shell-quoted or escaped. " +
        "Unknown tokens and script-block braces stay unchanged. Quote tokens yourself for the selected shell. " +
        "Examples assume values contain no quote characters: PowerShell single quotes need embedded apostrophes doubled; " +
        "cmd double quotes group paths with spaces, but percent expansion and other cmd syntax still apply. " +
        "Use a script tailored to your workspace if names contain shell syntax.";

    public static IReadOnlyList<ButtonPlaceholder> Catalog { get; } = Array.AsReadOnly(new[] {
        new ButtonPlaceholder("{workspaceRoot}", "Opened workspace FolderPath, not its AppData state directory.",
            "$root = '{workspaceRoot}'", "cd /d \"{workspaceRoot}\""),
        new ButtonPlaceholder("{workspaceName}", "Final folder name of the normalized opened workspace path.",
            "Write-Output '{workspaceName}'", "echo \"{workspaceName}\""),
        new ButtonPlaceholder("{branch}", "Current Git branch read at invocation; unavailable/detached HEAD is an error.",
            "Write-Output '{branch}'", "echo \"{branch}\"")
    });

    private static readonly Regex Tokens = new(
        string.Join("|", Catalog.Select(item => Regex.Escape(item.Token))),
        RegexOptions.CultureInvariant);

    /// <summary>One pass: replacement values containing tokens are not expanded again.</summary>
    public static string Resolve(string text, string workspaceRoot, string workspaceName, string branch) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(workspaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        return Tokens.Replace(text, match => match.Value switch {
            "{workspaceRoot}" => workspaceRoot,
            "{workspaceName}" => workspaceName,
            "{branch}" => branch,
            _ => throw new InvalidOperationException("A catalog token has no resolver.")
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash;

public enum RegionLanguageMode
{
    AutoDetect,
    PlainText,
    ExplicitLanguage,
}

/// <summary>Maps a picker entry's persistent fence token to its installed grammar name.</summary>
public sealed record RegionLanguageOption(string FenceToken, string DisplayName, string? GrammarName)
{
    public RegionLanguageMode Mode => RegionLanguagePicker.GetMode(FenceToken);
}

public static class RegionLanguagePicker
{
    public const string PlainTextToken = "text";

    public static IReadOnlyList<RegionLanguageOption> Options { get; } = BuildOptions();

    public static RegionLanguageMode GetMode(string token) => token.Length == 0
        ? RegionLanguageMode.AutoDetect
        : string.Equals(token, PlainTextToken, StringComparison.OrdinalIgnoreCase)
            ? RegionLanguageMode.PlainText
            : RegionLanguageMode.ExplicitLanguage;

    public static string GetLabel(string token) => GetMode(token) switch
    {
        RegionLanguageMode.AutoDetect => "Auto detect -- Plain text",
        RegionLanguageMode.PlainText => "Plain text",
        _ => Options.FirstOrDefault(option =>
            string.Equals(option.FenceToken, token, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? token,
    };

    private static IReadOnlyList<RegionLanguageOption> BuildOptions()
    {
        var options = new List<RegionLanguageOption>
        {
            new("", "Auto detect", null),
            new(PlainTextToken, "Plain text", null),
        };
        foreach (var grammar in HighlightingManager.Instance.HighlightingDefinitions
                     .OrderBy(grammar => grammar.Name, StringComparer.OrdinalIgnoreCase))
        {
            // Registry display names can include spaces/slashes that are not legal fence tags.
            var token = Regex.Replace(grammar.Name.ToLowerInvariant(), @"[^a-z0-9_+.#\-]", "");
            if (token.Length == 0 || options.Any(option => option.FenceToken == token))
                throw new InvalidOperationException($"Grammar '{grammar.Name}' has no unique fence token.");
            options.Add(new RegionLanguageOption(token, grammar.Name, grammar.Name));
        }
        return options.AsReadOnly();
    }
}

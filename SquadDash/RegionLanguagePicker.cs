using System;
using System.Collections.Generic;
using System.Linq;

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

    public static IReadOnlyList<RegionLanguageOption> Options => RegionHighlightingEngine.Default.Options;

    public static RegionLanguageMode GetMode(string token) => token.Length == 0
        ? RegionLanguageMode.AutoDetect
        : string.Equals(token, PlainTextToken, StringComparison.OrdinalIgnoreCase)
            ? RegionLanguageMode.PlainText
            : RegionLanguageMode.ExplicitLanguage;

    public static string GetLabel(string token, string? detectedGrammar = null) => GetMode(token) switch
    {
        RegionLanguageMode.AutoDetect => $"Auto detect -- {detectedGrammar ?? "Plain text"}",
        RegionLanguageMode.PlainText => "Plain text",
        _ => Options.FirstOrDefault(option =>
            string.Equals(option.FenceToken, token, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? token,
    };
}

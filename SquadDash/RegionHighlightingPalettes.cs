using System;
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace SquadDash;

/// <summary>UI-free light/dark syntax color selection, independent of resource dictionary timing.</summary>
public static class RegionHighlightingPalettes
{
    public static bool IsDark(Color surface) =>
        (0.2126 * surface.R + 0.7152 * surface.G + 0.0722 * surface.B) < 128;

    public static IReadOnlyDictionary<string, HighlightingColor> CreateColors(bool isDark)
    {
        return new Dictionary<string, HighlightingColor>(StringComparer.Ordinal)
        {
            ["$Keyword"] = Color(isDark ? 0x8AB4F8u : 0x174EA6u),
            ["$Comment"] = Color(isDark ? 0x9DB88Fu : 0x426536u),
            ["$String"] = Color(isDark ? 0xE8AD91u : 0x8F3520u),
            ["$Number"] = Color(isDark ? 0xC7ACF0u : 0x6841A5u),
            ["$Type"] = Color(isDark ? 0x71C9B8u : 0x00695Cu),
            ["$Method"] = Color(isDark ? 0xE4CD8Bu : 0x795B00u),
            ["$Tag"] = Color(isDark ? 0x8AB4F8u : 0x174EA6u),
            ["$Attribute"] = Color(isDark ? 0xA9D4F5u : 0x245B80u),
            ["$Punctuation"] = Color(isDark ? 0xD8C8B0u : 0x493827u),
            ["$Added"] = Color(isDark ? 0xA0D69Bu : 0x246B2Eu),
            ["$Removed"] = Color(isDark ? 0xF4A3A3u : 0xA12626u),
        };
    }

    public static IReadOnlyDictionary<string, HighlightingColor> CreateErrorMessageColors(bool isDark) =>
        new Dictionary<string, HighlightingColor>(StringComparer.Ordinal)
        {
            ["Error message/Method"] = Color(isDark ? 0x8AB4F8u : 0x174EA6u),
            ["Error message/Time"] = Color(isDark ? 0xE4CD8Bu : 0x795B00u),
            ["Error message/MonthDay"] = Color(isDark ? 0x71C9B8u : 0x00695Cu),
            ["Error message/FileName"] = Color(isDark ? 0xC7ACF0u : 0x6841A5u),
            ["Error message/LineNumber"] = Color(isDark ? 0x78966Du : 0x527647u),
        };

    private static HighlightingColor Color(uint rgb) => new()
    {
        Foreground = new RegionHighlightingBrush(System.Windows.Media.Color.FromRgb(
            (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)),
    };

    internal static string GetRole(string? name)
    {
        var value = name?.ToLowerInvariant() ?? "";
        if (value.Contains("comment")) return "$Comment";
        if (value.Contains("added")) return "$Added";
        if (value.Contains("removed") || value.Contains("broken")) return "$Removed";
        if (value.Contains("string") || value.Contains("char") || value.Contains("attributevalue") ||
            value.Contains("regex") || value.Contains("cdata")) return "$String";
        if (value.Contains("digit") || value.Contains("number") || value.Contains("literal") ||
            value.Contains("constant") || value.Contains("truefalse")) return "$Number";
        if (value.Contains("type")) return "$Type";
        if (value.Contains("method") || value.Contains("function") || value == "command") return "$Method";
        if (value.Contains("attribute") || value.Contains("property") || value.Contains("variable")) return "$Attribute";
        if (value.Contains("tag") || value.Contains("selector") || value == "class") return "$Tag";
        if (value.Contains("punctuation") || value.Contains("brace") || value is "colon" or "slash" or
            "assignment" or "default" or "unchangedtext") return "$Punctuation";
        return "$Keyword";
    }
}

// AvalonEdit 4.3 exposes HighlightingBrush but keeps its solid-color implementation internal.
internal sealed class RegionHighlightingBrush : HighlightingBrush
{
    private readonly Brush _brush;

    public RegionHighlightingBrush(Color color) : this(new SolidColorBrush(color))
    {
    }

    public RegionHighlightingBrush(Brush brush)
    {
        _brush = brush.IsFrozen ? brush : brush.CloneCurrentValue();
        if (_brush.CanFreeze)
            _brush.Freeze();
    }

    public override Brush GetBrush(ITextRunConstructionContext context) => _brush;
}

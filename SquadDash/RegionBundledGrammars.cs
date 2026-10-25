using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace SquadDash;

internal static class RegionBundledGrammars
{
    public static void Register(RegionHighlightingEngine engine)
    {
        // Preserve Json's canonical name so the existing parser-backed detector still resolves it.
        Register(engine, "JSON", "Json", "json");
        Register(engine, "TypeScript", "TypeScript", "typescript", "ts");
        Register(engine, "YAML", "YAML", "yaml", "yml");
        Register(engine, "SQL", "SQL", "sql");
        Register(engine, "Go", "Go", "go", "golang");
        Register(engine, "Rust", "Rust", "rust", "rs");
        Register(engine, "Bash", "Bash", "bash", "sh", "shell");
        var xaml = new Lazy<IHighlightingDefinition>(() => Load("XAML"));
        engine.Register(new RegionLanguageOption("xaml", "XAML", "XAML"),
            () => xaml.Value, aliases: new[] { ".xaml" });
        var razor = new Lazy<IHighlightingDefinition>(() => Load("Razor"));
        engine.Register(new RegionLanguageOption("razor", "Razor (Blazor)", "Razor"),
            () => razor.Value, CreatePalette, "blazor", "cshtml", ".razor", ".cshtml");
        var markdown = new Lazy<IHighlightingDefinition>(() => Load("Markdown"));
        engine.Register(new RegionLanguageOption("markdown", "Markdown", "Markdown"),
            () => markdown.Value, CreateMarkdownPalette, "md");
        var markdownDirectives = new Lazy<IHighlightingDefinition>(() => Load("MarkdownDirectives"));
        engine.Register(new RegionLanguageOption("markdowndirectives", "Markdown directives", "MarkdownDirectives"),
            () => markdownDirectives.Value, internalOnly: true,
            palette: isDark => CreateMarkdownPalette(isDark)
                .Concat(RegionHighlightingPalettes.CreateMarkdownDirectivesColors(isDark)
                    .Select(pair => new KeyValuePair<string, HighlightingColor>(
                        pair.Key["MarkdownDirectives/".Length..], pair.Value)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        var errorMessage = new Lazy<IHighlightingDefinition>(() => Load("ErrorMessage"));
        engine.Register(new RegionLanguageOption("error", "Error message", "Error message"),
            () => errorMessage.Value,
            isDark => RegionHighlightingPalettes.CreateErrorMessageColors(isDark)
                .ToDictionary(pair => pair.Key["Error message/".Length..], pair => pair.Value, StringComparer.Ordinal));
    }

    private static void Register(
        RegionHighlightingEngine engine, string resourceName, string grammarName, string token,
        params string[] aliases)
    {
        var definition = new Lazy<IHighlightingDefinition>(() => Load(resourceName));
        engine.Register(new RegionLanguageOption(token, resourceName, grammarName),
            () => definition.Value, CreatePalette, aliases);
    }

    private static IHighlightingDefinition Load(string resourceName)
    {
        var name = $"SquadDash.Grammars.{resourceName}.xshd";
        using var stream = typeof(RegionBundledGrammars).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing bundled region grammar resource '{name}'.");
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        });
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static IReadOnlyDictionary<string, HighlightingColor> CreatePalette(bool isDark) =>
        RegionHighlightingPalettes.CreateColors(isDark)
            .ToDictionary(pair => pair.Key[1..], pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, HighlightingColor> CreateMarkdownPalette(bool isDark) =>
        new Dictionary<string, HighlightingColor>(CreatePalette(isDark), StringComparer.Ordinal)
        {
            ["InlineCode"] = RegionHighlightingPalettes.CreateMarkdownColors(isDark)["Markdown/InlineCode"],
        };
}

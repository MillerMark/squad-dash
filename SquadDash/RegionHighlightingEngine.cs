using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash;

/// <summary>
/// Shared grammar/palette registration point. Register additional grammars before opening regions;
/// factories supply standard AvalonEdit definitions, while palettes map their color names for each theme.
/// Editors clone the rule/color graph, never mutating HighlightingManager's shared definitions.
/// </summary>
public sealed class RegionHighlightingEngine
{
    private sealed record Registration(
        RegionLanguageOption Option,
        Func<IHighlightingDefinition> Definition,
        Func<bool, IReadOnlyDictionary<string, HighlightingColor>>? Palette);

    private readonly Dictionary<string, Registration> _grammars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    public static RegionHighlightingEngine Default { get; } = new();

    public RegionHighlightingEngine()
    {
        RegionBundledGrammars.Register(this);
        foreach (var definition in HighlightingManager.Instance.HighlightingDefinitions)
        {
            // This redundant AvalonEdit built-in is superseded by our bundled Markdown grammar.
            if (string.Equals(definition.Name, "MarkDownWithFontSize", StringComparison.OrdinalIgnoreCase))
                continue;
            // Bundled definitions take precedence only in this engine, never in the shared registry.
            if (_grammars.ContainsKey(definition.Name))
                continue;
            var token = Regex.Replace(definition.Name.ToLowerInvariant(), @"[^a-z0-9_+.#\-]", "");
            Register(new RegionLanguageOption(token, definition.Name, definition.Name), () => definition);
        }

        AddAliases("C#", "cs", "csharp");
        AddAliases("C++", "cpp", "cxx", "cc", "c");
        AddAliases("JavaScript", "js", "javascript");
        AddAliases("PowerShell", "ps", "ps1");
        AddAliases("VB", "vb", "visualbasic");
        AddAliases("XML", "xaml");
        AddAliases("HTML", "htm");
        AddAliases("ASP/XHTML", "asp", "aspx", "xhtml");
        AddAliases("TeX", "latex");
        AddAliases("Patch", "diff");
    }

    public IReadOnlyList<RegionLanguageOption> Options =>
        new[] { new RegionLanguageOption("", "Auto detect", null),
            new RegionLanguageOption(RegionLanguagePicker.PlainTextToken, "Plain text", null) }
        .Concat(_grammars.Values.Select(item => item.Option)
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)).ToArray();

    public void Register(
        RegionLanguageOption option,
        Func<IHighlightingDefinition> definitionFactory,
        Func<bool, IReadOnlyDictionary<string, HighlightingColor>>? palette = null,
        params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(definitionFactory);
        ArgumentNullException.ThrowIfNull(aliases);
        if (string.IsNullOrWhiteSpace(option.GrammarName) ||
            !Regex.IsMatch(option.FenceToken, @"\A[a-zA-Z0-9_+.#\-]+\z") ||
            option.Mode != RegionLanguageMode.ExplicitLanguage)
            throw new ArgumentException("A grammar needs a name and an explicit, serializable fence token.", nameof(option));
        if (aliases.Any(alias => string.IsNullOrWhiteSpace(alias) ||
            !Regex.IsMatch(alias, @"\A[a-zA-Z0-9_+.#\-]+\z") ||
            RegionLanguagePicker.GetMode(alias) != RegionLanguageMode.ExplicitLanguage))
            throw new ArgumentException("Aliases must be explicit, serializable language tokens.", nameof(aliases));
        if (_grammars.ContainsKey(option.GrammarName) ||
            new[] { option.FenceToken }.Concat(aliases).Any(_tokens.ContainsKey))
            throw new ArgumentException("The grammar or one of its fence tokens is already registered.", nameof(option));

        _grammars.Add(option.GrammarName, new Registration(option, definitionFactory, palette));
        _tokens.Add(option.FenceToken, option.GrammarName);
        AddAliases(option.GrammarName, aliases);
    }

    private void AddAliases(string grammar, params string[] aliases)
    {
        if (!_grammars.ContainsKey(grammar))
            return;
        foreach (var alias in aliases)
            _tokens[alias] = grammar;
    }

    public string? ResolveFenceToken(string token) => _tokens.GetValueOrDefault(token);

    public string? Resolve(string token, string content) =>
        RegionGrammarResolver.Resolve(token, content, ResolveFenceToken,
            sample => RegionGrammarResolver.Detect(sample, _grammars.ContainsKey));

    public IReadOnlyDictionary<string, HighlightingColor> CreatePaletteColors(bool isDark)
    {
        var colors = new Dictionary<string, HighlightingColor>(RegionHighlightingPalettes.CreateColors(isDark));
        foreach (var (name, registration) in _grammars)
        {
            if (registration.Palette is null)
                continue;
            foreach (var (colorName, color) in registration.Palette(isDark))
                colors[$"{name}/{colorName}"] = color;
        }
        return colors;
    }

    public RegionHighlightingDefinition CreateDefinition(string grammarName, RegionEditorPalette palette)
    {
        if (!_grammars.TryGetValue(grammarName, out var registration))
            throw new ArgumentException($"Grammar '{grammarName}' is not registered.", nameof(grammarName));
        var definition = registration.Definition();
        if (definition is null || !string.Equals(definition.Name, registration.Option.GrammarName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Grammar factory must return a definition named '{registration.Option.GrammarName}'.");
        return new RegionHighlightingDefinition(definition, palette);
    }
}

/// <summary>An editor-local copy of the complete grammar graph, including imported/nested rules.</summary>
public sealed class RegionHighlightingDefinition : IHighlightingDefinition
{
    private readonly IHighlightingDefinition _source;
    private readonly Dictionary<HighlightingRuleSet, HighlightingRuleSet> _ruleSets = new();
    private readonly Dictionary<HighlightingColor, HighlightingColor> _colors = new();
    private RegionEditorPalette _palette;

    public RegionHighlightingDefinition(IHighlightingDefinition source, RegionEditorPalette palette)
    {
        _source = source;
        _palette = palette;
        MainRuleSet = CloneRuleSet(source.MainRuleSet)!;
        foreach (var color in source.NamedHighlightingColors)
            CloneColor(color);
    }

    public string Name => _source.Name;
    public IDictionary<string, string> Properties => _source.Properties;
    public HighlightingRuleSet MainRuleSet { get; }
    public IEnumerable<HighlightingColor> NamedHighlightingColors =>
        _source.NamedHighlightingColors.Select(color => _colors[color]);
    public HighlightingRuleSet GetNamedRuleSet(string name) => CloneRuleSet(_source.GetNamedRuleSet(name))!;
    public HighlightingColor GetNamedColor(string name) => CloneColor(_source.GetNamedColor(name))!;

    public void ApplyPalette(RegionEditorPalette palette)
    {
        _palette = palette;
        foreach (var (source, target) in _colors)
            ApplyColor(source, target);
    }

    private HighlightingRuleSet? CloneRuleSet(HighlightingRuleSet? source)
    {
        if (source is null)
            return null;
        if (_ruleSets.TryGetValue(source, out var existing))
            return existing;
        var target = new HighlightingRuleSet { Name = source.Name };
        _ruleSets.Add(source, target); // Imports can form cycles.
        foreach (var rule in source.Rules)
            target.Rules.Add(new HighlightingRule { Regex = rule.Regex, Color = CloneColor(rule.Color) });
        foreach (var span in source.Spans)
        {
            target.Spans.Add(new HighlightingSpan
            {
                StartExpression = span.StartExpression,
                EndExpression = span.EndExpression,
                RuleSet = CloneRuleSet(span.RuleSet),
                StartColor = CloneColor(span.StartColor),
                SpanColor = CloneColor(span.SpanColor),
                EndColor = CloneColor(span.EndColor),
                SpanColorIncludesStart = span.SpanColorIncludesStart,
                SpanColorIncludesEnd = span.SpanColorIncludesEnd,
            });
        }
        return target;
    }

    private HighlightingColor? CloneColor(HighlightingColor? source)
    {
        if (source is null)
            return null;
        if (_colors.TryGetValue(source, out var existing))
            return existing;
        var target = new HighlightingColor { Name = source.Name };
        _colors.Add(source, target);
        ApplyColor(source, target);
        return target;
    }

    private void ApplyColor(HighlightingColor source, HighlightingColor target)
    {
        var colors = _palette.HighlightColors;
        colors.TryGetValue($"{Name}/{source.Name}", out var replacement);
        if (replacement is null && source.Name is not null)
            colors.TryGetValue(source.Name, out replacement);
        if (replacement is null && (source.Foreground is not null || source.Background is not null))
            colors.TryGetValue(RegionHighlightingPalettes.GetRole(source.Name), out replacement);

        target.Foreground = replacement?.Foreground ??
            (source.Foreground is null ? null : new RegionHighlightingBrush(_palette.Text));
        // Do not carry light-only grammar background colors into the dark theme.
        target.Background = replacement?.Background;
        target.FontWeight = replacement?.FontWeight ?? source.FontWeight;
        target.FontStyle = replacement?.FontStyle ?? source.FontStyle;
        target.FontFamily = replacement?.FontFamily ?? source.FontFamily;
        target.FontSize = replacement?.FontSize ?? source.FontSize;
    }
}

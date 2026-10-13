using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// A collapsible embedded code region hosted inside a plain-text <see cref="RichTextBox"/>'s
/// <see cref="System.Windows.Documents.FlowDocument"/> via a
/// <see cref="System.Windows.Documents.BlockUIContainer"/>. Created by
/// <see cref="EmbeddedCodeRegionManager"/> when the user types or pastes a triple-backtick
/// fenced block into the prompt editor (Step 2 of the "Upgrade Prompt Editor with Embedded
/// Code Regions" plan).
/// <para>
/// Visually reuses the established Consolas + "CodeSurface"/"CodeText" code-block convention
/// from <see cref="CopyableCodeBlockBuilder"/> so embedded regions look consistent with the
/// read-only code blocks already rendered in transcripts.
/// </para>
/// <para>
/// <see cref="Language"/> is a data-model field only, per plan scope — it participates in
/// fence serialization (<see cref="ToFencedText"/>) but no language-picker UI is provided in
/// this step; a future plan may add one.
/// </para>
/// </summary>
internal sealed class EmbeddedCodeRegionBlock : Border
{
    /// Fence content, excluding the opening/closing ``` markers and the language tag.
    public string Content { get; set; }

    /// Optional language tag (e.g. "python"). Empty string means no language tag.
    public string Language { get; set; }

    public bool IsCollapsed { get; private set; }

    private readonly TextBox _contentBox;
    private readonly TextBlock _summaryText;
    private readonly ToggleButton _toggle;

    public static EmbeddedCodeRegionBlock Build(string content, string language, bool startCollapsed = false)
        => new(content, language, startCollapsed);

    private EmbeddedCodeRegionBlock(string content, string language, bool startCollapsed)
    {
        Content = content ?? string.Empty;
        Language = language ?? string.Empty;
        Tag = "embedded-code-region";

        _contentBox = new TextBox
        {
            Text = Content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Margin = new Thickness(0),
        };
        _contentBox.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        _contentBox.SetResourceReference(Control.ForegroundProperty, "CodeText");

        _summaryText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontStyle = FontStyles.Italic,
            FontSize = 12,
            Margin = new Thickness(8, 6, 8, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _summaryText.SetResourceReference(TextBlock.ForegroundProperty, "CodeText");

        _toggle = new ToggleButton
        {
            Content = "▾",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Focusable = false,
            ToolTip = "Collapse/expand embedded code region",
        };
        _toggle.Click += (_, _) => SetCollapsed(!IsCollapsed);

        var headerRow = new DockPanel();
        DockPanel.SetDock(_toggle, Dock.Left);
        headerRow.Children.Add(_toggle);
        headerRow.Children.Add(_summaryText);

        var stack = new StackPanel();
        stack.Children.Add(headerRow);
        stack.Children.Add(_contentBox);

        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        Margin = new Thickness(0, 2, 0, 2);
        SetResourceReference(BackgroundProperty, "CodeSurface");
        SetResourceReference(BorderBrushProperty, "SubtleBorder");
        Child = stack;

        SetCollapsed(startCollapsed);
    }

    /// Toggles between the compact single-line summary and the full fenced content.
    public void SetCollapsed(bool collapsed)
    {
        IsCollapsed = collapsed;
        _toggle.Content = collapsed ? "▸" : "▾";
        _toggle.IsChecked = collapsed;
        _contentBox.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _summaryText.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        var lineCount = Content.Length == 0 ? 0 : Content.Split('\n').Length;
        var languageTag = string.IsNullOrEmpty(Language) ? "" : $" ({Language})";
        _summaryText.Text = $"``` {lineCount} line{(lineCount == 1 ? "" : "s")} of code{languageTag} — click to expand";
    }

    /// Serializes this region back into an equivalent triple-backtick fenced block:
    /// a bare fence if no language is set, or ```&lt;language&gt; if one is present.
    /// This is the contract consumed by <see cref="RichTextBoxExtensions.GetPlainText"/>.
    public string ToFencedText() => EmbeddedFenceDetector.BuildFencedText(Content, Language);
}

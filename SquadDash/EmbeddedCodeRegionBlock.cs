using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    public new string Language { get; set; }

    public bool IsCollapsed { get; private set; }

    private readonly TextBox _contentBox;
    private readonly TextBlock _summaryText;
    private readonly ToggleButton _toggle;
    private bool _isClosingFromTrailingFenceLine;

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
            AcceptsReturn = true,
            AcceptsTab = true,
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

        // Keep Content (the serialization source of truth read by ToFencedText/GetPlainText)
        // in sync with every edit made directly inside the region. _contentBox's TextChanged
        // never bubbles up as the host RichTextBox's own TextChanged (it only fires for
        // FlowDocument/TextContainer content changes, not a nested control's Text property),
        // so this is isolated from EmbeddedCodeRegionManager's fence-scanning rebuilds — and
        // because those rebuilds reattach this exact same BlockUIContainer/control instance
        // (see EmbeddedCodeRegionManager.RenderSegments's ExistingContainer branch) rather
        // than recreating it, in-progress edits are never lost across a rebuild.
        _contentBox.TextChanged += (_, _) =>
        {
            Content = _contentBox.Text;
            if (IsCollapsed)
                UpdateSummaryText();
            else
                TryCloseOnTrailingFenceLine();
        };

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
        UpdateSummaryText();
    }

    /// Refreshes the collapsed-state summary line's line count (recomputed from the current
    /// <see cref="Content"/>) so edits made while collapsed — or just before re-collapsing —
    /// are reflected the next time the summary is shown.
    private void UpdateSummaryText()
    {
        var lineCount = Content.Length == 0 ? 0 : Content.Split('\n').Length;
        var languageTag = string.IsNullOrEmpty(Language) ? "" : $" ({Language})";
        _summaryText.Text = $"``` {lineCount} line{(lineCount == 1 ? "" : "s")} of code{languageTag} — click to expand";
    }

    /// Serializes this region back into an equivalent triple-backtick fenced block:
    /// a bare fence if no language is set, or ```&lt;language&gt; if one is present.
    /// This is the contract consumed by <see cref="RichTextBoxExtensions.GetPlainText"/>.
    public string ToFencedText() => EmbeddedFenceDetector.BuildFencedText(Content, Language);

    /// <summary>
    /// Gives keyboard focus to this region's content box and places the caret at the end of
    /// its (ordinarily empty) text. Called immediately after
    /// <see cref="EmbeddedCodeRegionManager.TryConvertOpenFence"/> creates a region from a
    /// just-typed opening fence marker, so the user's very next keystroke lands in the
    /// region's content instead of back out in the surrounding prompt text.
    /// </summary>
    public void FocusContent()
    {
        _contentBox.CaretIndex = _contentBox.Text.Length;
        // FocusManager.SetFocusedElement sets logical/keyboard focus within the control's
        // focus scope even before the element has gone through a layout pass against a real
        // PresentationSource (e.g. right after this region is created, before WPF has had a
        // chance to render it) — plain Focus()/Keyboard.Focus() require IsVisible to already
        // be true, which isn't guaranteed synchronously at creation time. Keyboard.Focus is
        // still attempted afterward so a live, already-rendered app also gets real OS
        // keyboard input focus, not just the logical bookkeeping.
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_contentBox), _contentBox);
        Keyboard.Focus(_contentBox);
    }

    /// <summary>
    /// Judgment call for "how does the user finish an instantly-opened region": if the last
    /// line the user typed into the content box is a bare closing ``` by itself, treat that
    /// as "done" — strip the marker line back out of <see cref="Content"/> (it's implicit in
    /// <see cref="ToFencedText"/> already), collapse the region to its compact summary, and
    /// move keyboard focus back out to whatever's next in the surrounding prompt so ordinary
    /// typing resumes immediately after the region, mirroring how a pasted/complete fence
    /// already behaves. Guarded against re-entrancy because it mutates <c>_contentBox.Text</c>
    /// from within that same TextBox's own TextChanged handler.
    /// </summary>
    private void TryCloseOnTrailingFenceLine()
    {
        if (_isClosingFromTrailingFenceLine) return;

        var lines = Content.Split('\n');
        if (lines[^1].Trim() != "```") return;

        _isClosingFromTrailingFenceLine = true;
        try
        {
            var withoutClosingLine = string.Join("\n", lines.Take(lines.Length - 1));
            Content = withoutClosingLine;
            _contentBox.Text = withoutClosingLine;
            SetCollapsed(true);
            _contentBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
        finally
        {
            _isClosingFromTrailingFenceLine = false;
        }
    }
}

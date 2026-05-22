using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shell;

namespace SquadDash;

internal sealed class TasksStatusWindow : ChromedWindow {
    private readonly RichTextBox _contentRichBox;
    private string _rawContent = string.Empty;

    private static readonly Regex s_emojiSplitter =
        new(@"(🔴|🟡|🟢)", RegexOptions.Compiled);

    public TasksStatusWindow() {
        Title = "Live Tasks";
        Width = 560;
        Height = 420;
        MinWidth = 420;
        MinHeight = 260;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;

        var root = new Grid {
            Margin = new Thickness(12, 8, 12, 12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ApplyOuterBorder().Child = root;

        var header = new DockPanel {
            LastChildFill = false,
            Background    = Brushes.Transparent,
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Copy button docked right, with margin to align its right edge with the close button's left edge.
        // Close button = 38px wide; root right margin = 12px → offset = 38 - 12 = 26px.
        var copyButton = new Button {
            Content  = "Copy",
            MinWidth = 76,
            Height   = 30,
            Margin   = new Thickness(0, 0, 26, 0),
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) => {
            if (!string.IsNullOrEmpty(_rawContent))
                Clipboard.SetText(_rawContent);
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var titleBlock = new TextBlock {
            Text              = "Live Tasks",
            FontSize          = (double)Application.Current.Resources["FontSizeSubtitle"],
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        DockPanel.SetDock(titleBlock, Dock.Left);
        header.Children.Add(titleBlock);

        var hintBlock = new TextBlock {
            Text = "Use /dropTasks to hide this window.",
            Margin = new Thickness(0, 8, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(hintBlock, 1);
        root.Children.Add(hintBlock);

        var contentBorder = new Border {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 2);
        root.Children.Add(contentBorder);

        _contentRichBox = new RichTextBox {
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _contentRichBox.SetResourceReference(RichTextBox.BackgroundProperty, "CardSurface");
        _contentRichBox.SetResourceReference(RichTextBox.ForegroundProperty, "LabelText");
        contentBorder.Child = _contentRichBox;
    }

    public void UpdateContent(string content) {
        _rawContent = content ?? string.Empty;

        var doc = new FlowDocument {
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            PagePadding = new Thickness(0),
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "LabelText");

        foreach (var rawLine in _rawContent.Split('\n')) {
            var line = rawLine.TrimEnd('\r');
            var para = new Paragraph {
                Margin = new Thickness(0),
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            AppendColoredInlines(para.Inlines, line);
            doc.Blocks.Add(para);
        }

        _contentRichBox.Document = doc;
        _contentRichBox.ScrollToHome();
    }

    private void AppendColoredInlines(InlineCollection inlines, string text) {
        var parts = s_emojiSplitter.Split(text);
        foreach (var part in parts) {
            var key = EmojiResourceKey(part);
            if (key is not null) {
                // Colored emoji glyphs ignore Run.Foreground — use a real Ellipse instead.
                var ellipse = new System.Windows.Shapes.Ellipse {
                    Width  = 11,
                    Height = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, -1),
                };
                ellipse.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
                inlines.Add(new InlineUIContainer(ellipse));
            } else {
                var run = new Run(part);
                run.SetResourceReference(Run.ForegroundProperty, "LabelText");
                inlines.Add(run);
            }
        }
    }

    internal static string? EmojiResourceKey(string segment) => segment switch {
        "🔴" => "TaskPriorityHigh",
        "🟡" => "TaskPriorityMid",
        "🟢" => "TaskPriorityLow",
        _    => null
    };
}

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Live preview of every ToolIcon_* DrawingImage registered in the application resources.
/// Open from View > Tool Icon Gallery...
/// </summary>
internal sealed class ToolIconPreviewWindow : Window {
    private static readonly double[] PreviewSizes = [16, 24, 32, 48];

    private ToolIconPreviewWindow(Window owner) {
        Title = "Tool Icon Preview";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Height;
        Width = 640;
        MinWidth = 400;
        MaxHeight = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Owner = owner;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(16)
        };

        var root = new StackPanel { Margin = new Thickness(0) };
        scroll.Content = root;
        Content = scroll;

        var entries = CollectIcons(owner);

        var header = BuildRow(isHeader: true);
        header.Children.Add(MakeHeaderCell("Key", 220));
        header.Children.Add(MakeHeaderCell("Emoji", 52));
        header.Children.Add(MakeHeaderCell("Tool Name", 140));
        foreach (var sz in PreviewSizes)
            header.Children.Add(MakeHeaderCell($"{sz:0}px", 52));
        root.Children.Add(header);

        var sep = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 4) };
        sep.SetResourceReference(Border.BackgroundProperty, "InputBorder");
        root.Children.Add(sep);

        foreach (var (key, toolName, source) in entries) {
            var row = BuildRow(isHeader: false);

            var keyBlock = new TextBlock {
                Text = key,
                Width = 220,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            keyBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            row.Children.Add(keyBlock);

            var emojiBlock = new TextBlock {
                Text = ToolTranscriptFormatter.GetToolEmojiByName(toolName),
                Width = 52,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 16
            };
            row.Children.Add(emojiBlock);

            var nameBlock = new TextBlock {
                Text = toolName,
                Width = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
            row.Children.Add(nameBlock);

            foreach (var sz in PreviewSizes) {
                var cell = new Border {
                    Width = 52,
                    Height = 52,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                if (source is not null) {
                    var img = new Image {
                        Source = source,
                        Width = sz,
                        Height = sz,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Stretch.Uniform
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    cell.Child = img;
                } else {
                    var missing = new TextBlock {
                        Text = "?",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 18,
                        Opacity = 0.4
                    };
                    missing.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
                    cell.Child = missing;
                }
                row.Children.Add(cell);
            }

            root.Children.Add(row);

            var rowSep = new Border { Height = 1, Opacity = 0.3 };
            rowSep.SetResourceReference(Border.BackgroundProperty, "InputBorder");
            root.Children.Add(rowSep);
        }

        if (entries.Count == 0) {
            var empty = new TextBlock {
                Text = "No ToolIcon_* resources found.",
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.5
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            root.Children.Add(empty);
        }
    }

    private static StackPanel BuildRow(bool isHeader) =>
        new() {
            Orientation = Orientation.Horizontal,
            Height = isHeader ? 28 : 60,
            Margin = new Thickness(0, isHeader ? 0 : 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center
        };

    private TextBlock MakeHeaderCell(string text, double width) {
        var tb = new TextBlock {
            Text = text,
            Width = width,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        return tb;
    }

    private static List<(string Key, string ToolName, ImageSource? Source)> CollectIcons(Window owner) {
        const string prefix = "ToolIcon_";
        // Application.Current.Resources.Keys does NOT recurse into MergedDictionaries.
        // Walk the dictionary tree manually.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        CollectKeysFromDictionary(Application.Current.Resources, prefix, keys);

        return keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(key => (key, ToolName: key[prefix.Length..], Source: owner.TryFindResource(key) as ImageSource))
            .ToList();
    }

    private static void CollectKeysFromDictionary(ResourceDictionary dict, string prefix, HashSet<string> results) {
        foreach (var key in dict.Keys.OfType<string>().Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            results.Add(key);
        foreach (var merged in dict.MergedDictionaries)
            CollectKeysFromDictionary(merged, prefix, results);
    }

    public static void Show(Window owner) {
        new ToolIconPreviewWindow(owner).Show();
    }
}
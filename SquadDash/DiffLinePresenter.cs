using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal enum DiffLineKind {
    Added,
    Removed,
    Context,
    Header
}

internal static class DiffLinePresenter {
    internal static TextBlock Create(string text, DiffLineKind kind) {
        var textBlock = new TextBlock {
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Padding = new Thickness(4, 1, 4, 1),
            TextWrapping = TextWrapping.NoWrap
        };
        switch (kind) {
            case DiffLineKind.Added:
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "DiffAddedText");
                textBlock.Background = new SolidColorBrush(Color.FromArgb(26, 107, 174, 214));
                break;
            case DiffLineKind.Removed:
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "DiffRemovedText");
                textBlock.Background = new SolidColorBrush(Color.FromArgb(26, 224, 112, 112));
                break;
            case DiffLineKind.Header:
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                textBlock.FontSize = (double)Application.Current.Resources["FontSizeSmall"];
                textBlock.Opacity = 0.8;
                break;
            default:
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
                break;
        }

        // Keep the diff marker visually subordinate to the changed code. The marker
        // inherits the themed added/removed brush, but at half its normal contrast.
        if ((kind == DiffLineKind.Added || kind == DiffLineKind.Removed) &&
            text.Length > 0 && (text[0] == '+' || text[0] == '-')) {
            var marker = new Run(text[..1]);
            marker.SetResourceReference(TextElement.ForegroundProperty,
                kind == DiffLineKind.Added ? "DiffAddedMarker" : "DiffRemovedMarker");
            textBlock.Inlines.Add(marker);
            if (text.Length > 1)
                textBlock.Inlines.Add(new Run(text[1..]));
        }
        else {
            textBlock.Text = text;
        }
        return textBlock;
    }
}

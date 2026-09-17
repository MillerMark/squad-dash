using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

/// <summary>Builds the shared transcript/Inbox code-sample surface and copy affordance.</summary>
internal static class CopyableCodeBlockBuilder
{
    internal static Block Build(string text, double baseFontSize)
    {
        var copyButtonSize = baseFontSize * 1.5;
        var codeTopPadding = Math.Max(30, copyButtonSize + 8);
        var textBox = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = baseFontSize * 0.9,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, codeTopPadding, 10, 8),
            Tag = "codeblock",
        };
        textBox.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        textBox.SetResourceReference(Control.ForegroundProperty, "CodeText");

        var copyButton = new CopyButton
        {
            CopyTooltip = "Copy code",
            Width = copyButtonSize,
            Height = copyButtonSize,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        copyButton.Click += (_, _) => TrySetClipboard(text);

        var copyButtonBackdrop = new Border
        {
            CornerRadius = new CornerRadius(2),
            Opacity = 0.5,
            BorderThickness = new Thickness(0),
        };
        copyButtonBackdrop.SetResourceReference(Border.BackgroundProperty, "CodeSurface");

        var stickyCopyHost = new Grid
        {
            Width = copyButtonSize,
            Height = copyButtonSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 2, 4, 2),
        };
        stickyCopyHost.Children.Add(copyButtonBackdrop);
        stickyCopyHost.Children.Add(copyButton);

        var copyMenuItem = new MenuItem { Header = "Copy" };
        copyMenuItem.Click += (_, _) => TrySetClipboard(
            textBox.SelectionLength > 0 ? textBox.SelectedText : text);
        var selectAllMenuItem = new MenuItem { Header = "Select All" };
        selectAllMenuItem.Click += (_, _) => textBox.SelectAll();
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(copyMenuItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(selectAllMenuItem);
        contextMenu.Opened += (_, _) =>
            copyMenuItem.Header = textBox.SelectionLength > 0 ? "Copy Selection" : "Copy All";
        textBox.ContextMenu = contextMenu;

        var container = new Grid();
        container.Children.Add(textBox);
        StickyCodeBlockBehavior.Attach(container, stickyCopyHost);
        return new BlockUIContainer(container) { Margin = new Thickness(0, 2, 0, 10) };
    }

    private static bool TrySetClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(UiTimingConstants.ClipboardRetryBackoffUnitMs);
            }
        }
        return false;
    }
}

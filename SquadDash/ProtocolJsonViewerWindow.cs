using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Read-only viewer for a host protocol JSON payload hidden from normal transcript prose.
/// </summary>
internal sealed class ProtocolJsonViewerWindow : ChromedWindow
{
    private readonly string _json;

    internal ProtocolJsonViewerWindow(string marker, string json)
    {
        _json = FormatJson(json);

        Title = $"JSON received — {marker}";
        Width = 760;
        Height = 560;
        MinWidth = 440;
        MinHeight = 300;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ApplyOuterBorder().Child = root;

        var header = new DockPanel { LastChildFill = true };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var copyButton = new Button
        {
            Content = "Copy JSON",
            MinWidth = 92,
            Height = 30,
            Margin = new Thickness(10, 0, 0, 8),
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        copyButton.Click += (_, _) => Clipboard.SetText(_json);
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var title = new TextBlock
        {
            Text = marker,
            FontWeight = FontWeights.SemiBold,
            FontSize = (double)Application.Current.Resources["FontSizeSubtitle"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        header.Children.Add(title);

        var contentBorder = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 1);
        root.Children.Add(contentBorder);

        var jsonBox = new TextBox
        {
            Text = _json,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
        };
        jsonBox.SetResourceReference(Control.BackgroundProperty, "CardSurface");
        jsonBox.SetResourceReference(Control.ForegroundProperty, "BodyText");
        contentBorder.Child = jsonBox;
    }

    private static string FormatJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}

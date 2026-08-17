using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Reusable, read-only text viewer used for help and diagnostic content.
/// MainWindow owns the singleton policy; this window supports refreshing the
/// displayed content when the same help topic is requested again.
/// </summary>
internal sealed class TextDisplayWindow : ChromedWindow
{
    private readonly TextBox _textBox;

    internal TextDisplayWindow(string title, string content)
        : base(captionHeight: CloseButtonHeight, resizeMode: ResizeMode.CanResize)
    {
        Title = title;
        Width = 900;
        Height = 700;
        MinWidth = 640;
        MinHeight = 480;
        ShowInTaskbar = false;

        _textBox = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
        };
        _textBox.SetResourceReference(Control.BackgroundProperty, "AppSurface");
        _textBox.SetResourceReference(Control.ForegroundProperty, "LabelText");

        var contentBorder = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(8),
            Child = _textBox,
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        ApplyOuterBorder(titleText: title).Child = contentBorder;
    }

    internal string DisplayedContent => _textBox.Text;

    internal void UpdateContent(string content)
    {
        if (!string.Equals(_textBox.Text, content, StringComparison.Ordinal))
            _textBox.Text = content;
    }
}

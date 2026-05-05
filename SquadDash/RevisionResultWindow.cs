using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

/// <summary>
/// Shown when the original selection has changed since Revise with AI was invoked,
/// so the AI result can't be auto-applied.  Lets the user copy the result manually.
/// </summary>
internal sealed class RevisionResultWindow : Window
{
    internal RevisionResultWindow(string revisedText)
    {
        WindowStyle     = WindowStyle.ToolWindow;
        ResizeMode      = ResizeMode.CanResize;
        SizeToContent   = SizeToContent.Manual;
        Width           = 520;
        Height          = 340;
        ShowInTaskbar   = false;
        Title           = "✏  AI Revision — Original text has changed";

        this.SetResourceReference(BackgroundProperty, "CardSurface");

        var panel = new DockPanel { Margin = new Thickness(12) };

        var notice = new TextBlock {
            Text        = "The original selection was edited while AI was working. The revised text couldn't be applied automatically — copy it below.",
            TextWrapping = TextWrapping.Wrap,
            FontSize    = 12,
            Margin      = new Thickness(0, 0, 0, 8)
        };
        notice.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        DockPanel.SetDock(notice, Dock.Top);
        panel.Children.Add(notice);

        var buttonRow = new StackPanel {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var copyBtn = new Button {
            Content  = "Copy & Close",
            MinWidth = 100,
            Height   = 28,
            FontSize = 12,
            Padding  = new Thickness(12, 0, 12, 0),
            Margin   = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        copyBtn.Click += (_, _) => {
            try { Clipboard.SetText(revisedText); } catch { }
            Close();
        };

        var dismissBtn = new Button {
            Content  = "Dismiss",
            MinWidth = 80,
            Height   = 28,
            FontSize = 12,
            Padding  = new Thickness(12, 0, 12, 0),
            IsCancel = true,
        };
        dismissBtn.Click += (_, _) => Close();

        buttonRow.Children.Add(copyBtn);
        buttonRow.Children.Add(dismissBtn);
        panel.Children.Add(buttonRow);

        var resultBox = new TextBox {
            Text                     = revisedText,
            IsReadOnly               = true,
            AcceptsReturn            = true,
            TextWrapping             = TextWrapping.Wrap,
            FontSize                 = 12,
            Padding                  = new Thickness(6),
            BorderThickness          = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        resultBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        resultBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        resultBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        panel.Children.Add(resultBox);

        Content = panel;
    }
}

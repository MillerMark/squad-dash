using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Selectable, copyable details for a rejected plan recovery assessment response.
/// </summary>
internal sealed class RecoveryAssessmentErrorReportWindow : ChromedWindow
{
    internal RecoveryAssessmentErrorReportWindow(string report)
    {
        Title = "Recovery assessment validation report";
        Width = 760;
        Height = 560;
        MinWidth = 480;
        MinHeight = 320;
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
            Content = "Copy report",
            MinWidth = 104,
            Height = 30,
            Margin = new Thickness(10, 0, 0, 8),
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) => Clipboard.SetText(report);
        System.Windows.Automation.AutomationProperties.SetName(copyButton, "Copy validation report");
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var title = new TextBlock
        {
            Text = "Recovery assessment validation report",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSubtitle");
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

        var reportBox = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
        };
        reportBox.SetResourceReference(Control.BackgroundProperty, "CardSurface");
        reportBox.SetResourceReference(Control.ForegroundProperty, "BodyText");
        reportBox.SetResourceReference(Control.FontSizeProperty, "FontSizeNormal");
        System.Windows.Automation.AutomationProperties.SetName(reportBox, "Recovery assessment validation details");
        contentBorder.Child = reportBox;
    }
}

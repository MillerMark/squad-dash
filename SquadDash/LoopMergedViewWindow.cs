using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Floating read-only window that shows the merged body of the currently-selected loop.md file.
/// Open/close is toggled via the loop panel context menu; content is refreshed whenever a loop
/// option changes via <see cref="MainWindow.RefreshLoopMergedView"/>.
/// </summary>
internal sealed class LoopMergedViewWindow : ChromedWindow
{
    private readonly TextBox _bodyTextBox;

    public LoopMergedViewWindow()
    {
        Title          = "Loop Preview";
        Width          = 600;
        Height         = 450;
        MinWidth       = 300;
        MinHeight      = 200;
        ShowInTaskbar  = false;
        ShowActivated  = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ApplyOuterBorder().Child = root;

        // ── Header ──────────────────────────────────────────────────────────────────────────

        var header = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var closeButton = new Button
        {
            Content = "×",
            Width   = 28,
            Height  = 28,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);

        var titleBlock = new TextBlock
        {
            Text                = "Loop Preview",
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            FontWeight          = FontWeights.SemiBold,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        header.Children.Add(titleBlock);

        // ── Content ─────────────────────────────────────────────────────────────────────────

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);

        _bodyTextBox = new TextBox
        {
            IsReadOnly                    = true,
            TextWrapping                  = TextWrapping.NoWrap,
            BorderThickness               = new Thickness(0),
            Background                    = Brushes.Transparent,
            FontFamily                    = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = (double)Application.Current.Resources["FontSizeBody"],
            AcceptsReturn                 = true,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _bodyTextBox.SetResourceReference(TextBox.ForegroundProperty, "BodyText");
        scrollViewer.Content = _bodyTextBox;
    }

    public void UpdateContent(string text) => _bodyTextBox.Text = text;
}

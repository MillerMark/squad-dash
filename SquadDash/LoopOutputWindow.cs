using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Floating window that streams loop output in real time.
/// Open via right-click "Show Loop Output" on the Loop panel, or auto-shown
/// when loop output first arrives.  Dismiss with the × button (hides, preserves history).
/// </summary>
internal sealed class LoopOutputWindow : ChromedWindow
{
    private readonly TextBox _logTextBox = null!;

    public LoopOutputWindow()
    {
        Title      = "Loop Output";
        Width      = 530;
        Height     = 480;
        MinWidth   = 320;
        MinHeight  = 200;
        ShowInTaskbar      = false;
        ShowActivated      = false;
        Topmost            = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ApplyOuterBorder().Child = root;

        // ── Header ──────────────────────────────────────────────────────────────────────────

        var header = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var titleBlock = new TextBlock
        {
            Text              = "Loop Output",
            FontSize          = (double)Application.Current.Resources["FontSizeSubtitle"],
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        DockPanel.SetDock(titleBlock, Dock.Left);
        header.Children.Add(titleBlock);

        var copyButton = new Button { Content = "Copy", MinWidth = 76, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_logTextBox.Text))
                Clipboard.SetText(_logTextBox.Text);
        };
        DockPanel.SetDock(copyButton, Dock.Left);
        header.Children.Add(copyButton);

        var clearButton = new Button { Content = "Clear", MinWidth = 76, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        clearButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(clearButton, true);
        clearButton.Click += (_, _) => SaveAndClear();
        DockPanel.SetDock(clearButton, Dock.Left);
        header.Children.Add(clearButton);

        // ── Log area ────────────────────────────────────────────────────────────────────────

        var contentBorder = new Border
        {
            Padding         = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(12),
            Margin          = new Thickness(0, 8, 0, 0),
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 1);
        root.Children.Add(contentBorder);

        _logTextBox = new TextBox
        {
            IsReadOnly                    = true,
            AcceptsReturn                 = true,
            TextWrapping                  = TextWrapping.NoWrap,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness               = new Thickness(0),
            FontFamily                    = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
        };
        _logTextBox.SetResourceReference(TextBox.BackgroundProperty, "CardSurface");
        _logTextBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        _logTextBox.Loaded += (_, _) => RefreshCornerBrush();
        contentBorder.Child = _logTextBox;
    }

    /// <summary>Re-applies the scrollbar corner brush after a theme switch.</summary>
    public void NotifyThemeChanged()
    {
        if (_logTextBox.IsLoaded)
            RefreshCornerBrush();
    }

    private void RefreshCornerBrush()
    {
        if (_logTextBox.TryFindResource("CardSurface") is Brush cardBrush)
            _logTextBox.Resources[SystemColors.ControlBrushKey] = cardBrush;
    }

    /// <summary>Appends a line of text to the log. Thread-safe.</summary>
    public void AppendLine(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLine(text));
            return;
        }

        if (_logTextBox.Text.Length > 0)
            _logTextBox.AppendText(Environment.NewLine);
        _logTextBox.AppendText(text);
        _logTextBox.ScrollToEnd();
    }

    /// <summary>Saves current content to a log file then clears the display.</summary>
    public void SaveAndClear()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(SaveAndClear);
            return;
        }

        var text = _logTextBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
            LoopOutputStore.SaveLog(text);
        _logTextBox.Clear();
    }

    /// <summary>Hides rather than closes the window so output history is preserved for re-opening.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

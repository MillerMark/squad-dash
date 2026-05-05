using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Floating window that streams loop CLI output in real time.
/// Open via right-click "View Loop Output" on the Loop panel, or auto-shown
/// when CLI loop output first arrives.  Close with the × button or "Close".
/// </summary>
internal sealed class LoopOutputWindow : Window
{
    private readonly TextBox _logTextBox = null!;

    public LoopOutputWindow()
    {
        Title      = "Loop Output";
        Width      = 530;
        Height     = 480;
        MinWidth   = 320;
        MinHeight  = 200;
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        ResizeMode         = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar      = false;
        ShowActivated      = false;
        Topmost            = false;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(4),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        var outerBorder = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4) };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outerBorder.Child = root;
        Content = outerBorder;

        // ── Header ──────────────────────────────────────────────────────────────────────────

        var header = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var closeButton = new Button { Content = "Close", MinWidth = 76, Height = 30 };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);

        var copyButton = new Button { Content = "Copy", MinWidth = 76, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_logTextBox.Text))
                Clipboard.SetText(_logTextBox.Text);
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var clearButton = new Button { Content = "Clear", MinWidth = 76, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        clearButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(clearButton, true);
        clearButton.Click += (_, _) => SaveAndClear();
        DockPanel.SetDock(clearButton, Dock.Right);
        header.Children.Add(clearButton);

        var titleBlock = new TextBlock
        {
            Text              = "Loop Output",
            FontSize          = 18,
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        header.Children.Add(titleBlock);

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
            FontSize                      = 11,
        };
        _logTextBox.SetResourceReference(TextBox.BackgroundProperty, "CardSurface");
        _logTextBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        _logTextBox.Loaded += (_, _) =>
        {
            if (_logTextBox.TryFindResource("CardSurface") is Brush cardBrush)
                _logTextBox.Resources[SystemColors.ControlBrushKey] = cardBrush;
        };
        contentBorder.Child = _logTextBox;
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
}

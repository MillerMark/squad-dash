using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Themed confirmation dialog anchored above a queue tab.
/// Uses WindowStyle.None so the title bar follows the app active theme
/// (WindowStyle.ToolWindow uses OS-rendered chrome that ignores app resources).
/// </summary>
internal sealed class QueueItemDeleteConfirmWindow : Window {
    private readonly string _fullText;

    public QueueItemDeleteConfirmWindow(
        string itemLabel,
        string previewText,
        Rect anchorScreenRect,
        string fullText = "") {

        _fullText = fullText;

        Width         = 400;
        SizeToContent = SizeToContent.Height;
        MinWidth      = 340;
        ResizeMode    = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStyle   = WindowStyle.None;   // custom chrome — follows app theme
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        if (anchorScreenRect == default)
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            ContentRendered += (_, _) =>
            {
                Left = anchorScreenRect.Right - ActualWidth;
                Top  = anchorScreenRect.Top - ActualHeight - 6;
            };
        }

        var outer = new DockPanel();
        Content = outer;

        // ── Custom title bar (ChromeSurface bg, LabelText fg) ──
        var titleBar = new Grid { Height = 36 };
        titleBar.SetResourceReference(BackgroundProperty, "ChromeSurface");
        DockPanel.SetDock(titleBar, Dock.Top);
        outer.Children.Add(titleBar);

        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text              = "Confirmation required",
            FontSize          = 12,
            FontWeight        = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(14, 0, 0, 0),
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        // Close button: 4px margin from top and right, 28x28 so it's vertically centred in 36px bar
        var closeBtn = new Button
        {
            Content           = "✕",
            FontSize          = 11,
            Width             = 28,
            Height            = 28,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 4, 4, 0),
            BorderThickness   = new Thickness(0),
            Background        = Brushes.Transparent,
            Cursor            = Cursors.Hand,
        };
        closeBtn.SetResourceReference(Button.ForegroundProperty, "LabelText");
        closeBtn.Click += (_, _) => { DialogResult = false; };
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

        // Allow dragging the window by the title bar
        titleBar.MouseLeftButtonDown += (_, _) => DragMove();

        // ── Body ──────────────────────────────────────────────────────────
        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
        outer.Children.Add(body);

        var heading = new TextBlock
        {
            Text         = $"Delete queued item {itemLabel}?",
            FontSize     = 15,
            FontWeight   = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        body.Children.Add(heading);

        if (!string.IsNullOrWhiteSpace(previewText))
        {
            var preview = new TextBlock
            {
                Text         = previewText,
                FontSize     = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 16),
            };
            preview.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            body.Children.Add(preview);
        }
        else
        {
            body.Children.Add(new Border { Height = 16 });
        }

        // Button row: [Copy] ──spacer── [Cancel] [Delete]
        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(buttonRow);

        var copyBtn = new Button
        {
            Content = "Copy",
            Height  = 30,
            Padding = new Thickness(12, 0, 12, 0),
            ToolTip = "Copy full prompt text to clipboard",
        };
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_fullText))
                Clipboard.SetText(_fullText);
        };
        Grid.SetColumn(copyBtn, 0);
        buttonRow.Children.Add(copyBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width   = 80,
            Height  = 30,
            Margin  = new Thickness(0, 0, 8, 0),
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        Grid.SetColumn(cancelBtn, 2);
        buttonRow.Children.Add(cancelBtn);

        var deleteBtn = new Button
        {
            Content = "Delete",
            Width   = 80,
            Height  = 30,
        };
        deleteBtn.Click += (_, _) => { DialogResult = true; };
        Grid.SetColumn(deleteBtn, 3);
        buttonRow.Children.Add(deleteBtn);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            if (e.Key == Key.Enter)  { DialogResult = true;  e.Handled = true; }
        };
    }
}

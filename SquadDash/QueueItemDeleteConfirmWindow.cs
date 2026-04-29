using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Confirmation dialog anchored above a queue tab — asks the user to confirm
/// deletion of a queued prompt item.
/// Uses WindowStyle.None + WindowChrome so the title bar is fully app-themed.
/// </summary>
internal sealed class QueueItemDeleteConfirmWindow : Window {
    private readonly string _fullText;

    public QueueItemDeleteConfirmWindow(string itemLabel, string previewText, Rect anchorScreenRect, string fullText = "") {
        _fullText = fullText;

        Width             = 400;
        SizeToContent     = SizeToContent.Height;
        MinWidth          = 340;
        ResizeMode        = ResizeMode.NoResize;
        ShowInTaskbar     = false;
        WindowStyle       = WindowStyle.None;
        AllowsTransparency = true;
        Background        = Brushes.Transparent;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        if (anchorScreenRect == default)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            ContentRendered += (_, _) =>
            {
                Left = anchorScreenRect.Right - ActualWidth;
                Top  = anchorScreenRect.Top - ActualHeight - 6;
            };
        }

        // ── Outer chrome border ───────────────────────────────────────────────
        var outerBorder = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4) };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        Content = outerBorder;

        var root = new StackPanel();
        outerBorder.Child = root;

        // ── Title bar ────────────────────────────────────────────────────────
        var titleBar = new DockPanel
        {
            LastChildFill = false,
            Background    = Brushes.Transparent,
            Margin        = new Thickness(12, 6, 6, 0),
        };
        root.Children.Add(titleBar);

        // Close (×) button — moves up/left 4px per spec (Margin top/right = 4)
        var closeBtn = new Button
        {
            Content = "✕",
            Width   = 28,
            Height  = 28,
            Margin  = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Cancel",
        };
        closeBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        closeBtn.Click += (_, _) => { DialogResult = false; };
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleBar.Children.Add(closeBtn);

        var titleText = new TextBlock
        {
            Text              = "Confirmation required",
            FontSize          = 13,
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(titleText, Dock.Left);
        titleBar.Children.Add(titleText);

        // ── Separator ────────────────────────────────────────────────────────
        var sep = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 0) };
        sep.SetResourceReference(Border.BackgroundProperty, "PanelBorder");
        root.Children.Add(sep);

        // ── Body ─────────────────────────────────────────────────────────────
        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        root.Children.Add(body);

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
            preview.SetResourceReference(TextBlock.ForegroundProperty, "QueueTabInactiveText");
            body.Children.Add(preview);
        }
        else
        {
            body.Children.Add(new Border { Height = 16 });
        }

        // ── Button row: [Copy]  spacer  [Cancel] [Delete] ───────────────────
        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Copy
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // spacer
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Cancel
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Delete
        body.Children.Add(buttonRow);

        // Copy button (bottom left)
        var copyBtn = new Button
        {
            Content = "Copy",
            Height  = 30,
            Padding = new Thickness(12, 0, 12, 0),
        };
        copyBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        copyBtn.Click += (_, _) =>
        {
            var text = string.IsNullOrEmpty(_fullText) ? previewText : _fullText;
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        };
        Grid.SetColumn(copyBtn, 0);
        buttonRow.Children.Add(copyBtn);

        // Cancel
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Height  = 30,
            Padding = new Thickness(12, 0, 12, 0),
            Margin  = new Thickness(0, 0, 8, 0),
        };
        cancelBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        Grid.SetColumn(cancelBtn, 2);
        buttonRow.Children.Add(cancelBtn);

        // Delete
        var deleteBtn = new Button
        {
            Content = "Delete",
            Height  = 30,
            Padding = new Thickness(12, 0, 12, 0),
        };
        deleteBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        deleteBtn.Click += (_, _) => { DialogResult = true; };
        Grid.SetColumn(deleteBtn, 3);
        buttonRow.Children.Add(deleteBtn);

        // Keyboard shortcuts
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            if (e.Key == Key.Enter)  { DialogResult = true;  e.Handled = true; }
        };
    }
}

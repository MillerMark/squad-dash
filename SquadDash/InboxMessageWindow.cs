using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>Modeless pop-up window that displays a single <see cref="InboxMessage"/>.</summary>
internal sealed class InboxMessageWindow : Window
{
    public InboxMessageWindow(
        InboxMessage message,
        Action<InboxAction, InboxMessage> onActionClicked)
    {
        Title                   = message.Subject;
        WindowStyle             = WindowStyle.ToolWindow;
        ResizeMode              = ResizeMode.CanResize;
        SizeToContent           = SizeToContent.Manual;
        Width                   = 640;
        Height                  = 520;
        MinWidth                = 400;
        MinHeight               = 300;
        Topmost                 = false;
        WindowStartupLocation   = WindowStartupLocation.CenterOwner;
        ShowInTaskbar           = true;
        this.SetResourceReference(BackgroundProperty, "InputSurface");

        // Root grid: header / attachments / actions / body
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0 header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1 attachments
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2 actions
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 body
        Content = root;

        // ── Header ────────────────────────────────────────────────────────────
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin      = new Thickness(12, 10, 12, 6),
        };
        Grid.SetRow(headerPanel, 0);
        root.Children.Add(headerPanel);

        var subjectLabel = new TextBlock
        {
            Text       = message.Subject,
            FontWeight = FontWeights.Bold,
            FontSize   = 14,
            TextWrapping = TextWrapping.Wrap,
        };
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        headerPanel.Children.Add(subjectLabel);

        var ts = StatusTimingPresentation.FormatRelativeTimestamp(message.Timestamp);
        var metaLabel = new TextBlock
        {
            Text   = $"{message.From} · {ts}",
            Margin = new Thickness(0, 2, 0, 0),
        };
        metaLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        metaLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        headerPanel.Children.Add(metaLabel);

        // Separator
        var sep = new Separator { Margin = new Thickness(0, 4, 0, 0) };
        headerPanel.Children.Add(sep);

        // ── Attachments ───────────────────────────────────────────────────────
        var attachmentsPanel = new WrapPanel
        {
            Margin      = new Thickness(12, 4, 12, 0),
            Orientation = Orientation.Horizontal,
            Visibility  = message.Attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetRow(attachmentsPanel, 1);
        root.Children.Add(attachmentsPanel);

        foreach (var att in message.Attachments)
            attachmentsPanel.Children.Add(BuildAttachmentChip(att));

        // ── Actions ───────────────────────────────────────────────────────────
        var actionsPanel = new WrapPanel
        {
            Margin      = new Thickness(12, 4, 12, 0),
            Orientation = Orientation.Horizontal,
            Visibility  = message.Actions is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetRow(actionsPanel, 2);
        root.Children.Add(actionsPanel);

        foreach (var action in message.Actions)
            actionsPanel.Children.Add(BuildActionButton(action, message, onActionClicked));

        // ── Body ──────────────────────────────────────────────────────────────
        var bodyViewer = new FlowDocumentScrollViewer
        {
            Margin             = new Thickness(8, 6, 8, 8),
            VerticalAlignment  = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Document           = MarkdownFlowDocumentBuilder.Build(message.Body ?? string.Empty),
        };
        Grid.SetRow(bodyViewer, 3);
        root.Children.Add(bodyViewer);
    }

    private static Button BuildActionButton(
        InboxAction action,
        InboxMessage msg,
        Action<InboxAction, InboxMessage> onActionClicked)
    {
        var btn = new Button
        {
            Content = action.Label,
            Margin  = new Thickness(0, 0, 6, 4),
        };
        btn.SetResourceReference(Button.StyleProperty, "FlatButtonStyle");

        bool alreadyUsed = msg.UsedActions.Contains(action.Label);
        if (alreadyUsed)
        {
            btn.IsEnabled = false;
            btn.Opacity   = 0.5;
        }

        if (action.RouteMode == "done")
            btn.SetResourceReference(Button.ForegroundProperty, "SubtleText");

        btn.Click += (_, _) =>
        {
            btn.IsEnabled = false;
            onActionClicked(action, msg);
        };

        return btn;
    }

    private static UIElement BuildAttachmentChip(InboxAttachment att)
    {
        var icon = att.Type switch
        {
            "file"  => "📄",
            "link"  => "🔗",
            "task"  => "✅",
            "image" => "🖼",
            _       => "📎",
        };

        var chip = new Border
        {
            Margin          = new Thickness(0, 0, 4, 4),
            Padding         = new Thickness(6, 2, 6, 2),
            CornerRadius    = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
        };
        chip.SetResourceReference(Border.BackgroundProperty,  "InputSurface");
        chip.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

        var label = new TextBlock
        {
            Text         = $"{icon} {att.Label}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth     = 160,
        };
        label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        chip.Child = label;

        var target = att.Href ?? att.Path;
        if (target is not null)
        {
            chip.MouseLeftButtonUp += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                catch { }
            };
        }

        return chip;
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>Modeless pop-up window that displays a single <see cref="InboxMessage"/>.</summary>
internal sealed class InboxMessageWindow : Window
{
    public string MessageId { get; }

    private readonly Func<string, TaskItem?>? _lookupTask;

    public InboxMessageWindow(
        InboxMessage message,
        Action<InboxAction, InboxMessage> onActionClicked,
        Func<string, TaskItem?>? lookupTask = null)
    {
        _lookupTask             = lookupTask;
        MessageId               = message.Id;
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
            attachmentsPanel.Children.Add(BuildAttachmentChip(att, this, _lookupTask));

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
            Content         = action.Label,
            Margin          = new Thickness(0, 0, 8, 8),
            Padding         = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
            MinHeight       = 28,
        };
        if (Application.Current.TryFindResource("QuickReplyButtonStyle") is Style qrStyle)
            btn.Style = qrStyle;
        btn.SetResourceReference(Button.BackgroundProperty,   "QuickReplySurface");
        btn.SetResourceReference(Button.ForegroundProperty,   "QuickReplyText");
        btn.SetResourceReference(Button.BorderBrushProperty,  "QuickReplyBorder");

        bool alreadyUsed = msg.UsedActions.Contains(action.Label);
        if (alreadyUsed)
            btn.IsEnabled = false;

        btn.Click += (_, _) =>
        {
            btn.IsEnabled = false;
            onActionClicked(action, msg);
        };

        return btn;
    }

    private static string GetPriorityLabel(string emoji) => emoji switch {
        "🔴" => "High Priority",
        "🟡" => "Mid Priority",
        "🟢" => "Low Priority",
        _    => "Unknown Priority",
    };

    private static UIElement BuildAttachmentChip(InboxAttachment att, Window? owner, Func<string, TaskItem?>? lookupTask = null)
    {
        var icon = att.Type switch
        {
            "url"      => "🔗",
            "file"     => "📄",
            "image"    => "🖼",
            "task-ref" => "✅",
            "text"     => "📝",
            _          => "📎",
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

        switch (att.Type)
        {
            case "url":
                if (att.Href is not null)
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo(att.Href) { UseShellExecute = true }); }
                        catch { }
                    };
                break;

            case "file":
            {
                var resolved = System.IO.Path.GetFullPath(att.Path!);
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        if (resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            MarkdownDocumentWindow.Show(owner, att.Label, resolved);
                        else
                            Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
                    }
                    catch { }
                };
                break;
            }

            case "image":
            {
                string? imagePath = att.Path is not null ? System.IO.Path.GetFullPath(att.Path) : null;
                string? imageHref = att.Href;
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        Uri? uri = imagePath is not null ? new Uri(imagePath) :
                                   imageHref is not null ? new Uri(imageHref) : null;
                        if (uri is null) return;

                        if (imagePath is not null && !File.Exists(imagePath))
                        {
                            MessageBox.Show($"Image not found:\n{imagePath}", att.Label, MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        var bmp = new BitmapImage(uri);
                        var img = new System.Windows.Controls.Image
                        {
                            Source  = bmp,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Margin  = new Thickness(8),
                        };
                        var win = new Window
                        {
                            Title         = att.Label,
                            Content       = img,
                            Width         = Math.Min(bmp.PixelWidth  > 0 ? bmp.PixelWidth  + 32 : 800, SystemParameters.PrimaryScreenWidth  * 0.9),
                            Height        = Math.Min(bmp.PixelHeight > 0 ? bmp.PixelHeight + 56 : 600, SystemParameters.PrimaryScreenHeight * 0.9),
                            Owner         = owner,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        };
                        win.Show();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, att.Label, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                break;
            }

            case "task-ref":
            {
                chip.ToolTip = $"Task: {att.TaskId}";
                chip.Cursor  = Cursors.Hand;
                if (lookupTask is not null && att.TaskId is not null)
                {
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        try
                        {
                            var task = lookupTask(att.TaskId);
                            if (task is null)
                            {
                                MessageBox.Show($"Task not found: {att.TaskId}", att.Label,
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }

                            var status   = task.IsChecked ? "✅ Done" : "⬜ Open";
                            var priority = $"{task.Emoji} {GetPriorityLabel(task.Emoji)}";
                            var owner    = task.Owner is not null ? $"\nOwner: {task.Owner}" : "";
                            var desc     = task.Description is not null ? $"\n\n{task.Description}" : "";
                            MessageBox.Show(
                                $"{status}  |  {priority}{owner}\n\n{task.Text}{desc}",
                                att.Label,
                                MessageBoxButton.OK,
                                MessageBoxImage.None);
                        }
                        catch { }
                    };
                }
                break;
            }

            case "text":
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    try { MarkdownDocumentWindow.ShowContent(owner, att.Label, att.Content ?? ""); }
                    catch { }
                };
                break;
        }

        return chip;
    }
}

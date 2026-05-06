using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;

/// <summary>
/// Resizable viewer window that shows the full content of one or more prompt attachments.
/// Multiple attachments are displayed as tabs.
/// </summary>
internal sealed class PromptAttachmentViewerWindow : Window
{
    internal static void Show(IReadOnlyList<FollowUpAttachment> attachments, Window? owner)
    {
        if (attachments.Count == 0) return;
        var win = new PromptAttachmentViewerWindow(attachments);
        if (owner is not null)
            win.Owner = owner;
        win.Show();
    }

    internal static void ShowRaw(string rawHeaderText, Window? owner)
    {
        var synthetic = new FollowUpAttachment("", "Attachment", rawHeaderText, null);
        Show([synthetic], owner);
    }

    private PromptAttachmentViewerWindow(IReadOnlyList<FollowUpAttachment> attachments)
    {
        Title         = attachments.Count == 1 ? "Prompt Attachment" : "Prompt Attachments";
        Width         = 600;
        Height        = 420;
        MinWidth      = 320;
        MinHeight     = 200;
        WindowStyle   = WindowStyle.ToolWindow;
        ResizeMode    = ResizeMode.CanResize;
        ShowInTaskbar = false;

        this.SetResourceReference(BackgroundProperty, "CardSurface");

        UIElement content;
        if (attachments.Count == 1)
        {
            content = WrapInMargin(BuildAttachmentContent(attachments[0]));
        }
        else
        {
            var tabs = new TabControl { Margin = new Thickness(4) };
            foreach (var att in attachments)
                tabs.Items.Add(BuildTab(att));
            content = tabs;
        }

        Content = content;
    }

    private static UIElement WrapInMargin(UIElement inner) =>
        new Border { Padding = new Thickness(12), Child = inner };

    private static TabItem BuildTab(FollowUpAttachment att)
    {
        string label;
        if (att.ImagePath is not null)
            label = "📷 " + TruncateLabel(att.Description, 30);
        else if (att.TranscriptQuote is not null)
            label = "💬 " + TruncateLabel(att.Description, 30);
        else if (string.IsNullOrWhiteSpace(att.CommitSha))
            label = "📎 " + TruncateLabel(att.Description, 30);
        else
            label = $"📌 {SafeSha(att.CommitSha)} — {TruncateLabel(att.Description, 26)}";

        return new TabItem
        {
            Header  = label,
            Padding = new Thickness(6, 3, 6, 3),
            Content = WrapInMargin(BuildAttachmentContent(att))
        };
    }

    private static UIElement BuildAttachmentContent(FollowUpAttachment att)
    {
        if (att.ImagePath is not null)
        {
            if (!File.Exists(att.ImagePath))
            {
                return new TextBlock
                {
                    Text                = "This image has expired and been deleted.",
                    FontStyle           = FontStyles.Italic,
                    VerticalAlignment   = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(20)
                };
            }

            try
            {
                var img = new System.Windows.Controls.Image
                {
                    Stretch = Stretch.Uniform,
                    Margin  = new Thickness(8),
                    Source  = new BitmapImage(new Uri(att.ImagePath, UriKind.Absolute))
                };
                return new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    Content = img
                };
            }
            catch
            {
                return new TextBlock { Text = "Could not load image.", FontStyle = FontStyles.Italic };
            }
        }

        string text;
        if (att.TranscriptQuote is not null)
        {
            text = att.TranscriptQuote;
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(att.CommitSha))
                sb.AppendLine($"Commit:  {att.CommitSha}");
            if (!string.IsNullOrWhiteSpace(att.Description) && att.Description != "Attachment")
                sb.AppendLine($"Summary: {att.Description}");
            if (!string.IsNullOrWhiteSpace(att.OriginalPrompt))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Original prompt:");
                }
                sb.Append(att.OriginalPrompt);
            }
            text = sb.ToString().TrimEnd();
        }

        var textBox = new TextBox
        {
            Text                          = text,
            IsReadOnly                    = true,
            TextWrapping                  = TextWrapping.Wrap,
            AcceptsReturn                 = true,
            BorderThickness               = new Thickness(0),
            Background                    = Brushes.Transparent,
            FontSize                      = 12,
            Padding                       = new Thickness(2),
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        textBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        var scroll = new ScrollViewer
        {
            Content                       = textBox,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return scroll;
    }

    private static string SafeSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    private static string TruncateLabel(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length > maxLen ? text[..maxLen].TrimEnd() + "…" : text;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;

/// <summary>
/// Resizable viewer window that shows the full content of one or more prompt attachments.
/// Multiple attachments are displayed as tabs.
/// Image attachments auto-size to fit the screen, support Ctrl+scroll zoom, and close on Escape or Enter.
/// </summary>
internal sealed class PromptAttachmentViewerWindow : Window
{
    internal static void Show(IReadOnlyList<FollowUpAttachment> attachments, Window? owner)
    {
        if (attachments.Count == 0) return;
        var win = new PromptAttachmentViewerWindow(attachments, owner);
        if (owner is not null)
            win.Owner = owner;
        win.Show();
    }

    internal static void ShowRaw(string rawHeaderText, Window? owner)
    {
        var synthetic = new FollowUpAttachment("", "Attachment", rawHeaderText, null);
        Show([synthetic], owner);
    }

    private PromptAttachmentViewerWindow(IReadOnlyList<FollowUpAttachment> attachments, Window? owner)
    {
        Title         = attachments.Count == 1 ? "Prompt Attachment" : "Prompt Attachments";
        MinWidth      = 320;
        MinHeight     = 200;
        WindowStyle   = WindowStyle.ToolWindow;
        ResizeMode    = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.SetResourceReference(BackgroundProperty, "CardSurface");

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Return or Key.Enter)
            {
                Close();
                e.Handled = true;
            }
        };

        UIElement content;
        BitmapImage? singleImage = null;

        if (attachments.Count == 1)
        {
            content = WrapInMargin(BuildAttachmentContent(attachments[0], out singleImage));
        }
        else
        {
            var tabs = new TabControl { Margin = new Thickness(4) };
            foreach (var att in attachments)
                tabs.Items.Add(BuildTab(att));
            content = tabs;
        }

        Content = content;

        // Auto-size the window to the image when we have a single image attachment.
        if (singleImage is not null)
        {
            var bmp = singleImage;
            Loaded += (_, _) => SizeToImage(bmp, owner);
        }
        else
        {
            Width  = 600;
            Height = 420;
        }
    }

    /// <summary>
    /// Sizes the window to fit the image, always on the same monitor as the owner.
    /// Each dimension is clamped independently to 90% of the work area so a tall
    /// but narrow image never forces a wide window, and vice-versa.
    /// </summary>
    private void SizeToImage(BitmapImage bmp, Window? owner)
    {
        double imgW = bmp.Width;
        double imgH = bmp.Height;

        var workArea = GetOwnerWorkArea(owner);

        const double MaxFraction = 0.90;
        const double ChromeW = 24;  // scrollbar + border
        const double ChromeH = 70;  // title bar + toolbar + border

        double wantW = imgW + ChromeW;
        double wantH = imgH + ChromeH;

        // Clamp each dimension independently — never force full width just because height overflows.
        double finalW = Math.Min(wantW, workArea.Width  * MaxFraction);
        double finalH = Math.Min(wantH, workArea.Height * MaxFraction);

        Width  = finalW;
        Height = finalH;

        // Center on the correct monitor.
        Left = workArea.Left + (workArea.Width  - finalW) / 2;
        Top  = workArea.Top  + (workArea.Height - finalH) / 2;
    }

    private static Rect GetOwnerWorkArea(Window? owner)
    {
        // Try to get the work area of the monitor the owner window is on.
        if (owner is not null)
        {
            try
            {
                var hwnd = new WindowInteropHelper(owner).Handle;
                if (hwnd != nint.Zero)
                {
                    var physRect = NativeMethods.GetWorkAreaForWindow(hwnd);
                    // Convert from physical pixels to WPF logical units using the owner's DPI.
                    var origin = DpiHelper.PhysicalToLogical(owner, new Point(physRect.Left, physRect.Top));
                    var corner = DpiHelper.PhysicalToLogical(owner, new Point(physRect.Right, physRect.Bottom));
                    return new Rect(origin, corner);
                }
            }
            catch { }
        }

        // Fallback: primary monitor logical work area.
        var wa = SystemParameters.WorkArea;
        return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    private static UIElement WrapInMargin(UIElement inner) =>
        new Border { Padding = new Thickness(4), Child = inner };

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
            Content = WrapInMargin(BuildAttachmentContent(att, out _))
        };
    }

    private static UIElement BuildAttachmentContent(FollowUpAttachment att, out BitmapImage? loadedImage)
    {
        loadedImage = null;

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
                var bmp = new BitmapImage(new Uri(att.ImagePath, UriKind.Absolute));
                loadedImage = bmp;

                var scale = new ScaleTransform(1.0, 1.0);
                var img = new System.Windows.Controls.Image
                {
                    Source        = bmp,
                    Stretch       = Stretch.None,
                    Margin        = new Thickness(4),
                    LayoutTransform = scale
                };

                var scroll = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    Content = img
                };

                // Ctrl+scroll: zoom in / out.
                scroll.PreviewMouseWheel += (_, e) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                    var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
                    var newScale = Math.Max(0.05, Math.Min(8.0, scale.ScaleX * factor));
                    scale.ScaleX = newScale;
                    scale.ScaleY = newScale;
                    e.Handled = true;
                };

                return scroll;
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
        else if (!string.IsNullOrWhiteSpace(att.ContentBlock))
        {
            // Task/topic/doc/note attachments store their full rendered block here.
            // Prefer it directly over the generic Summary/OriginalPrompt fallback.
            text = att.ContentBlock!.TrimEnd();
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

        var textScroll = new ScrollViewer
        {
            Content                       = textBox,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return textScroll;
    }

    private static string SafeSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    private static string TruncateLabel(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length > maxLen ? text[..maxLen].TrimEnd() + "…" : text;
    }
}

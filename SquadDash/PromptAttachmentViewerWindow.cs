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
internal sealed class PromptAttachmentViewerWindow : ChromedWindow
{
    // Tracks the last-opened viewer so repeated clicks bring it to front rather than
    // stacking duplicate windows.
    private static WeakReference<PromptAttachmentViewerWindow>? s_lastViewer;

    internal static void Show(IReadOnlyList<FollowUpAttachment> attachments, Window? owner, Action<string>? openInboxMessage = null)
    {
        if (attachments.Count == 0) return;

        // If a viewer is already open, activate it instead of opening a duplicate.
        if (s_lastViewer is not null && s_lastViewer.TryGetTarget(out var existing) && existing.IsVisible)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var win = new PromptAttachmentViewerWindow(attachments, owner, openInboxMessage);
        if (owner is not null)
            win.Owner = owner;
        s_lastViewer = new WeakReference<PromptAttachmentViewerWindow>(win);
        win.Show();
    }

    internal static void ShowRaw(string rawHeaderText, Window? owner)
    {
        var synthetic = new FollowUpAttachment("", "Attachment", rawHeaderText, null);
        Show([synthetic], owner);
    }

    private readonly Action<string>? _openInboxMessage;

    private PromptAttachmentViewerWindow(IReadOnlyList<FollowUpAttachment> attachments, Window? owner, Action<string>? openInboxMessage = null)
        : base(captionHeight: CloseButtonHeight, resizeMode: ResizeMode.CanResize)
    {
        _openInboxMessage = openInboxMessage;
        Title         = attachments.Count == 1 ? "Text Attachment" : "Text Attachments";
        MinWidth      = 320;
        MinHeight     = 200;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
            content = WrapInMargin(BuildAttachmentContent(attachments[0], out singleImage, openInboxMessage));
        }
        else
        {
            var tabs = new TabControl { Margin = new Thickness(4) };
            tabs.SetResourceReference(Control.StyleProperty, "ThemedTabControlStyle");
            foreach (var att in attachments)
            {
                var tab = BuildTab(att, openInboxMessage);
                tab.SetResourceReference(Control.StyleProperty, "ThemedTabItemStyle");
                tabs.Items.Add(tab);
            }
            content = tabs;
        }

        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(content, true);
        var contentBorder = ApplyOuterBorder(titleText: Title);
        contentBorder.Child = content;

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

    private static TabItem BuildTab(FollowUpAttachment att, Action<string>? openInboxMessage = null)
    {
        string label;
        if (att.FileReferencePath is not null)
            label = "📁 " + TruncateLabel(att.Description, 30);
        else if (att.ImagePath is not null)
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
            Content = WrapInMargin(BuildAttachmentContent(att, out _, openInboxMessage))
        };
    }

    private static UIElement BuildAttachmentContent(FollowUpAttachment att, out BitmapImage? loadedImage, Action<string>? openInboxMessage = null)
    {
        loadedImage = null;

        // ── File-reference attachment ──────────────────────────────────────────
        if (att.FileReferencePath is not null)
        {
            var filePath = att.FileReferencePath;
            var stack = new StackPanel { Margin = new Thickness(4) };

            // Image files: show inline preview
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        var bmp = new BitmapImage(new Uri(filePath, UriKind.Absolute));
                        loadedImage = bmp;
                        var scale = new ScaleTransform(1.0, 1.0);
                        var img = new System.Windows.Controls.Image
                        {
                            Source          = bmp,
                            Stretch         = Stretch.None,
                            Margin          = new Thickness(0, 0, 0, 6),
                            LayoutTransform = scale
                        };
                        var fileImgScroll = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                            Content                       = img,
                            MaxHeight                     = 340,
                        };
                        fileImgScroll.SetResourceReference(ScrollViewer.StyleProperty, "RosterScrollViewerStyle");
                        fileImgScroll.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");
                        fileImgScroll.PreviewMouseWheel += (_, e) =>
                        {
                            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                            var factor   = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
                            var newScale = Math.Max(0.05, Math.Min(8.0, scale.ScaleX * factor));
                            scale.ScaleX = newScale;
                            scale.ScaleY = newScale;
                            e.Handled    = true;
                        };
                        stack.Children.Add(fileImgScroll);
                    }
                    catch
                    {
                        stack.Children.Add(new TextBlock { Text = "Could not load image.", FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 6) });
                    }
                }
                else
                {
                    stack.Children.Add(new TextBlock { Text = "Image file not found.", FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 6) });
                }
            }

            // Path text (selectable, monospace)
            var pathBox = new TextBox
            {
                Text                          = filePath,
                IsReadOnly                    = true,
                TextWrapping                  = TextWrapping.Wrap,
                FontFamily                    = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
                BorderThickness               = new Thickness(0),
                Background                    = Brushes.Transparent,
                Margin                        = new Thickness(0, 0, 0, 6),
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            pathBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
            stack.Children.Add(pathBox);

            // Open in Explorer button
            var openBtn = new Button
            {
                Content             = "Open in Explorer",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding             = new Thickness(10, 4, 10, 4),
            };
            openBtn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
            openBtn.Click += (_, _) =>
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            stack.Children.Add(openBtn);

            var outerScroll = new ScrollViewer
            {
                Content                       = stack,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            return outerScroll;
        }

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
                scroll.SetResourceReference(ScrollViewer.StyleProperty, "RosterScrollViewerStyle");
                scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");

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
            // Strip outer <attachment> XML tags from the new typed format for cleaner display.
            text = StripAttachmentXmlTags(att.ContentBlock!.TrimEnd());
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
            FontSize = (double)Application.Current.Resources["FontSizeBody"],
            Padding                       = new Thickness(2),
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        textBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        // For inbox-excerpt attachments, add an "Open in Inbox" button when the callback is available.
        bool isInboxExcerpt = att.ContentBlock is not null
            && att.ContentBlock.Contains("type=\"inbox-excerpt\"", StringComparison.Ordinal)
            && att.InboxMessageId is not null;

        if (isInboxExcerpt && openInboxMessage is not null)
        {
            var capturedMsgId = att.InboxMessageId!;
            var openBtn = new Button
            {
                Content             = "Open in Inbox",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding             = new Thickness(10, 4, 10, 4),
                Margin              = new Thickness(0, 6, 0, 0),
            };
            openBtn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
            openBtn.Click += (_, _) => openInboxMessage(capturedMsgId);

            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(openBtn, Dock.Bottom);
            panel.Children.Add(openBtn);

            var textScroll = new ScrollViewer
            {
                Content                       = textBox,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            panel.Children.Add(textScroll);
            return panel;
        }

        var textScroll2 = new ScrollViewer
        {
            Content                       = textBox,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return textScroll2;
    }

    private static string SafeSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    private static string TruncateLabel(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length > maxLen ? text[..maxLen].TrimEnd() + "…" : text;
    }

    /// <summary>
    /// Strips the outer <c>&lt;attachment …&gt;…&lt;/attachment&gt;</c> wrapper from a typed
    /// attachment block, returning just the inner content with any escaped close tags restored.
    /// Falls back to returning the original string unchanged if the format is not recognised
    /// (e.g. old SQUADCLIP blocks).
    /// </summary>
    private static string StripAttachmentXmlTags(string block) =>
        AttachmentBlockFormatter.ExtractAttachmentContent(block);
}

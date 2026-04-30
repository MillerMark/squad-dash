using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Interop;

namespace SquadDash;

/// <summary>
/// Non-modal floating window that shows the current RC access URL(s),
/// a reveal-on-demand QR code per URL, and a Stop Remote Access button.
/// </summary>
internal sealed class RcStatusPanel : Window
{
    private readonly Action _onStopRemoteAccess;

    // LAN / direct URL row
    private readonly TextBox    _urlBox;
    private readonly DockPanel  _urlRow;
    private readonly Button     _qrToggleButton;
    private readonly Image      _qrImage;

    // Tunnel URL row (added dynamically when tunnel arrives)
    private StackPanel? _tunnelSection;
    private TextBox?    _tunnelUrlBox;
    private DockPanel?  _tunnelUrlRow;
    private Button?     _tunnelQrToggleButton;
    private Image?      _tunnelQrImage;

    private readonly StackPanel _root;

    private bool _qrVisible        = false;
    private bool _tunnelQrVisible  = false;

    // ── Construction ──────────────────────────────────────────────────────

    public RcStatusPanel(string primaryUrl, Action onStopRemoteAccess)
    {
        _onStopRemoteAccess = onStopRemoteAccess;

        Title               = "Remote Access";
        Width               = 360;
        SizeToContent       = SizeToContent.Height;
        MinWidth            = 300;
        WindowStyle         = WindowStyle.None;
        AllowsTransparency  = true;
        Background          = Brushes.Transparent;
        ResizeMode          = ResizeMode.NoResize;
        ShowInTaskbar       = false;
        ShowActivated       = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        // ── Outer chrome border ──────────────────────────────────────────
        var outerBorder = new Border
        {
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(4),
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty,   "PopupSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty,  "PanelBorder");
        Content = outerBorder;

        _root = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        outerBorder.Child = _root;

        // ── Title row ────────────────────────────────────────────────────
        var titleRow = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        _root.Children.Add(titleRow);

        var titleText = new TextBlock
        {
            Text       = "📡 Remote Access Active",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin     = new Thickness(0, 0, 0, 0),
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(titleText, Dock.Left);
        titleRow.Children.Add(titleText);

        var closeButton = new Button { Content = "×", Width = 28, Height = 28 };
        closeButton.SetResourceReference(Control.StyleProperty, "PanelCloseButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        titleRow.Children.Add(closeButton);

        // ── Separator ────────────────────────────────────────────────────
        _root.Children.Add(MakeSeparator());

        // ── Primary URL section ──────────────────────────────────────────
        (_urlBox, _urlRow, _qrToggleButton, _qrImage) = BuildUrlSection(primaryUrl, isPrimary: true);

        // ── Bottom row: Stop (left) + OK/Close (right) ───────────────────
        _root.Children.Add(MakeSeparator());

        var bottomRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
        WindowChrome.SetIsHitTestVisibleInChrome(bottomRow, true);

        var stopButton = new Button
        {
            Content             = "Stop Remote Access",
            Height              = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        stopButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(stopButton, true);
        stopButton.Click += (_, _) =>
        {
            _onStopRemoteAccess();
            Close();
        };
        DockPanel.SetDock(stopButton, Dock.Left);
        bottomRow.Children.Add(stopButton);

        var okButton = new Button
        {
            Content             = "OK",
            Width               = 60,
            Height              = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        okButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(okButton, true);
        okButton.Click += (_, _) => Close();
        DockPanel.SetDock(okButton, Dock.Right);
        bottomRow.Children.Add(okButton);

        _root.Children.Add(bottomRow);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a warning banner when the Windows Firewall rule could not be
    /// added automatically (requires elevation). Call from the UI thread.
    /// </summary>
    public void ShowFirewallWarning()
    {
        // Don't add the banner twice
        if (_firewallWarningShown) return;
        _firewallWarningShown = true;

        var banner = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(10, 8, 10, 8),
            Margin          = new Thickness(0, 10, 0, 0),
        };
        banner.SetResourceReference(Border.BackgroundProperty,  "AlertSurface");
        banner.SetResourceReference(Border.BorderBrushProperty, "AlertBorder");

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight   = 18,
            Text         = "⚠️ Windows Firewall rule could not be added automatically — " +
                           "run SquadDash as Administrator once, or add an inbound TCP rule " +
                           "for this port manually in Windows Defender Firewall.",
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AlertBodyText");
        banner.Child = text;

        // Insert before the separator + bottom row (last two children)
        int insertAt = _root.Children.Count - 2;
        _root.Children.Insert(insertAt, banner);
    }

    private bool _firewallWarningShown = false;
    public void SetPrimaryUrl(string url)
    {
        _urlBox.Text = url;
        ResetQr(_qrImage, _urlRow, ref _qrVisible, _qrToggleButton);
    }

    /// <summary>Adds (or updates) a tunnel URL row in the panel.</summary>
    public void SetTunnelUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (_tunnelSection is not null)
        {
            // Already have a tunnel row — just update it.
            _tunnelUrlBox!.Text = url;
            ResetQr(_tunnelQrImage!, _tunnelUrlRow!, ref _tunnelQrVisible, _tunnelQrToggleButton!);
            return;
        }

        // Insert the tunnel section before the separator + bottom row.
        // The last two children are the separator and the bottom row.
        // We insert before the separator (index = count - 2).
        int insertAt = _root.Children.Count - 2;

        _tunnelSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var tunnelLabel = new TextBlock
        {
            Text   = "🌐 Tunnel URL",
            Margin = new Thickness(0, 0, 0, 4),
        };
        tunnelLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _tunnelSection.Children.Add(tunnelLabel);

        (_tunnelUrlBox, _tunnelUrlRow, _tunnelQrToggleButton, _tunnelQrImage) =
            BuildUrlSection(url, isPrimary: false, container: _tunnelSection);

        _root.Children.Insert(insertAt, _tunnelSection);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Builds the URL textbox + Copy button + QR toggle + QR image group.
    /// Returns the three key controls for later manipulation.
    /// </summary>
    private (TextBox urlBox, DockPanel urlRow, Button qrToggle, Image qrImage) BuildUrlSection(
        string url, bool isPrimary, StackPanel? container = null)
    {
        container ??= _root;

        if (isPrimary)
        {
            var label = new TextBlock
            {
                Text   = "URL",
                Margin = new Thickness(0, 8, 0, 4),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            container.Children.Add(label);
        }

        // URL row: TextBox + Copy button — always visible
        var urlRow = new DockPanel { LastChildFill = true };
        WindowChrome.SetIsHitTestVisibleInChrome(urlRow, true);

        var copyButton = new Button
        {
            Content = "Copy",
            Width   = 52,
            Height  = 28,
            Margin  = new Thickness(6, 0, 0, 0),
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        DockPanel.SetDock(copyButton, Dock.Right);

        var urlBox = new TextBox
        {
            Text             = url,
            IsReadOnly       = true,
            Padding          = new Thickness(6, 4, 6, 4),
            Height           = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        urlBox.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        urlBox.SetResourceReference(Control.ForegroundProperty, "BodyText");

        urlRow.Children.Add(copyButton);
        urlRow.Children.Add(urlBox);
        container.Children.Add(urlRow);

        copyButton.Click += (_, _) =>
        {
            try { Clipboard.SetText(urlBox.Text); }
            catch { /* clipboard contention — ignore */ }
        };

        // QR toggle button — reveals QR code only; URL is always visible
        var qrToggleButton = new Button
        {
            Content             = "Show QR Code ▼",
            HorizontalAlignment = HorizontalAlignment.Left,
            Height              = 28,
            Margin              = new Thickness(0, 6, 0, 0),
        };
        qrToggleButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(qrToggleButton, true);
        container.Children.Add(qrToggleButton);

        // QR image (hidden initially)
        var qrImage = new Image
        {
            Width               = 200,
            Height              = 200,
            Stretch             = Stretch.Uniform,
            Margin              = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true,
            Visibility          = Visibility.Collapsed,
        };
        RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);
        container.Children.Add(qrImage);

        bool visible = false;

        qrToggleButton.Click += (_, _) =>
        {
            visible = !visible;
            if (visible)
            {
                if (qrImage.Source is null)
                    qrImage.Source = GenerateQrBitmap(urlBox.Text);
                qrImage.Visibility     = Visibility.Visible;
                qrToggleButton.Content = "Hide QR Code ▲";
            }
            else
            {
                qrImage.Visibility     = Visibility.Collapsed;
                qrToggleButton.Content = "Show QR Code ▼";
            }
        };

        return (urlBox, urlRow, qrToggleButton, qrImage);
    }

    private static void ResetQr(Image qrImage, DockPanel urlRow, ref bool visible, Button toggleButton)
    {
        visible                = false;
        qrImage.Source         = null;
        qrImage.Visibility     = Visibility.Collapsed;
        toggleButton.Content   = "Show QR Code ▼";
    }

    private static FrameworkElement MakeSeparator()
    {
        var sep = new Border
        {
            Height    = 1,
            Margin    = new Thickness(0, 10, 0, 2),
        };
        sep.SetResourceReference(Border.BackgroundProperty, "PanelBorder");
        return sep;
    }

    private static BitmapImage? GenerateQrBitmap(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var qrGenerator = new QRCoder.QRCodeGenerator();
            var qrData      = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var qrCode      = new QRCoder.BitmapByteQRCode(qrData);
            var bitmapBytes = qrCode.GetGraphic(4);

            using var ms  = new MemoryStream(bitmapBytes);
            var bitmap    = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("UI", $"RC QR generation failed: {ex.Message}");
            return null;
        }
    }
}

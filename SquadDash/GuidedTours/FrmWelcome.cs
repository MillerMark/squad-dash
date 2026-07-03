using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash.GuidedTours;

/// <summary>
/// First-run welcome splash shown when the user has not yet taken the guided tour.
/// </summary>
internal sealed class FrmWelcome : Window
{
    public event EventHandler? StartTourClicked;
    public event EventHandler? SkipClicked;

    private const double WinW   = 760;
    private const double WinH   = 440;
    // The window is made wider than the visible border so the landing image
    // can bleed off the left edge of the rounded background.
    private const double BleedW = 50;

    public FrmWelcome()
    {
        Width              = WinW + BleedW;
        Height             = WinH;
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode         = ResizeMode.NoResize;
        ShowInTaskbar      = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background         = Brushes.Transparent;

        Content = BuildContent();

        // Only drag when clicking on non-interactive areas.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
    }

    private UIElement BuildContent()
    {
        // Outer canvas: WinW+BleedW wide so the landing image can start at x=0
        // and bleed off the left of the visible rounded border.
        var outerCanvas = new Canvas { Width = WinW + BleedW, Height = WinH };

        // Root border — rounded, colored background, shifted 5px left of BleedW
        var root = new Border
        {
            CornerRadius    = new CornerRadius(16),
            Background      = new SolidColorBrush(Color.FromRgb(0x43, 0x3A, 0x64)),
            ClipToBounds    = false,
            Width           = WinW + 5,
            Height          = WinH,
        };
        Canvas.SetLeft(root, BleedW - 5);
        Canvas.SetTop(root, 0);
        outerCanvas.Children.Add(root);

        var canvas = new Canvas { Width = WinW + BleedW, Height = WinH, Background = Brushes.Transparent };
        // canvas sits on top of the border, spanning the full outer width
        Canvas.SetLeft(canvas, 0);
        Canvas.SetTop(canvas, 0);
        outerCanvas.Children.Add(canvas);

        // ── GuidedTourLanding image (bleeds off left edge of the border) ────
        double imgH = WinH * 0.955;
        double imgAspect = 859.0 / 837.0;
        double imgW = imgH * imgAspect;
        // Start image at x=0 in the outer canvas — the first BleedW pixels are
        // outside the visible border area, so the image appears to bleed off.
        double imgLeft = 0;
        double imgTop  = (WinH - imgH) / 2.0;

        var landingImg = MakePngImage(AssetPath("GuidedTourLanding.png"), imgW, imgH);
        if (landingImg != null)
        {
            Canvas.SetLeft(landingImg, imgLeft);
            Canvas.SetTop(landingImg, imgTop);
            canvas.Children.Add(landingImg);
        }

        // ── Right column ────────────────────────────────────────────────────
        double colLeft  = BleedW - 5 + 370;   // offset by bleed so it's 370px from left edge of border
        double colWidth = WinW - 370 - 30;
        double colTop   = 40;

        // "Welcome to"
        var welcomeLabel = new TextBlock
        {
            Text            = "Welcome to",
            Foreground      = Brushes.White,
            FontSize        = 34,
            FontWeight      = FontWeights.Normal,
            TextAlignment   = TextAlignment.Center,
            Width           = colWidth,
        };
        Canvas.SetLeft(welcomeLabel, colLeft);
        Canvas.SetTop(welcomeLabel, colTop);
        canvas.Children.Add(welcomeLabel);

        // SquadDashTitle.png
        double titleW = 372;
        double titleH = titleW * (219.0 / 971.0);
        var titleImg = MakePngImage(AssetPath("SquadDashTitle.png"), titleW, titleH);
        if (titleImg != null)
        {
            Canvas.SetLeft(titleImg, colLeft + (colWidth - titleW) / 2.0);
            Canvas.SetTop(titleImg, colTop + 30);
            canvas.Children.Add(titleImg);
        }

        // Subtitle: left-aligns with Start Tour button, right-aligns with Skip button's right edge
        double startBtnW = 320;
        double subtitleLeft  = colLeft + (colWidth - startBtnW) / 2.0;
        double subtitleWidth = (BleedW + WinW - 16) - subtitleLeft;
        var subtitle = new TextBlock
        {
            Text            = "Take a quick tour and learn how to direct your Squad agents, manage work, and move faster.",
            Foreground      = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xEE)),
            FontSize        = 21,
            TextWrapping    = TextWrapping.Wrap,
            TextAlignment   = TextAlignment.Left,
            Width           = subtitleWidth,
        };
        Canvas.SetLeft(subtitle, subtitleLeft);
        Canvas.SetTop(subtitle, colTop + 30 + titleH + 18);
        canvas.Children.Add(subtitle);

        double subtitleBottom = colTop + 30 + titleH + 18 + 60; // approximate subtitle height

        // Start Guided Tour button
        double startBtnH = startBtnW * (147.0 / 585.0);
        var startBtn = MakeImageButton(
            AssetPath("StartGuidedTourButton-Normal.png"),
            AssetPath("StartGuidedTourButton-MouseOver.png"),
            startBtnW, startBtnH);
        Canvas.SetLeft(startBtn, colLeft + (colWidth - startBtnW) / 2.0);
        Canvas.SetTop(startBtn, subtitleBottom + 20);
        startBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); StartTourClicked?.Invoke(this, EventArgs.Empty); };
        canvas.Children.Add(startBtn);

        // Skip for now button
        double skipBtnW = 160;
        double skipBtnH = skipBtnW * (82.0 / 238.0);
        var skipBtn = MakeImageButton(
            AssetPath("SkipForNowButton.png"),
            AssetPath("SkipForNowButton-MouseOver.png"),
            skipBtnW, skipBtnH);
        Canvas.SetLeft(skipBtn, BleedW + WinW - skipBtnW - 16);
        Canvas.SetTop(skipBtn, WinH - skipBtnH - 16);
        skipBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); SkipClicked?.Invoke(this, EventArgs.Empty); };
        canvas.Children.Add(skipBtn);

        // Close button (×)
        var closeBtn = new TextBlock
        {
            Text            = "×",
            Foreground      = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontSize        = 20,
            Width           = 24,
            Height          = 24,
            TextAlignment   = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor          = Cursors.Hand,
        };
        closeBtn.MouseEnter += (_, _) => closeBtn.Foreground = Brushes.White;
        closeBtn.MouseLeave += (_, _) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        closeBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
        closeBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); SkipClicked?.Invoke(this, EventArgs.Empty); };
        Canvas.SetLeft(closeBtn, BleedW + WinW - 14 - 24);
        Canvas.SetTop(closeBtn, 7);
        canvas.Children.Add(closeBtn);

        return outerCanvas;
    }

    /// <summary>Creates an Image element from a file path, or null if the file is missing.</summary>
    private static Image? MakePngImage(string path, double width, double height)
    {
        if (!File.Exists(path)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource    = new Uri(path, UriKind.Absolute);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        return new Image
        {
            Source  = bmp,
            Width   = width,
            Height  = height,
            Stretch = Stretch.Uniform,
        };
    }

    /// <summary>
    /// Creates a Canvas-based image button that swaps normal/hover images.
    /// Hit-testing considers only pixels with alpha > 20.
    /// </summary>
    private static Canvas MakeImageButton(string normalPath, string hoverPath, double width, double height)
    {
        var container = new Canvas
        {
            Width      = width,
            Height     = height,
            Cursor     = Cursors.Hand,
            Background = Brushes.Transparent,
        };

        var normalImg = MakePngImage(normalPath, width, height);
        var hoverImg  = MakePngImage(hoverPath,  width, height);

        if (normalImg != null) container.Children.Add(normalImg);
        if (hoverImg  != null)
        {
            hoverImg.Visibility = Visibility.Collapsed;
            container.Children.Add(hoverImg);
        }

        container.MouseEnter += (_, _) =>
        {
            if (normalImg != null) normalImg.Visibility = Visibility.Collapsed;
            if (hoverImg  != null) hoverImg.Visibility  = Visibility.Visible;
        };
        container.MouseLeave += (_, _) =>
        {
            if (normalImg != null) normalImg.Visibility = Visibility.Visible;
            if (hoverImg  != null) hoverImg.Visibility  = Visibility.Collapsed;
        };

        container.MouseLeftButtonDown += (_, e) => e.Handled = true;
        container.IsHitTestVisible = true;

        return container;
    }

    private static string AssetPath(string fileName)
        => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets", "Welcome", fileName);
}

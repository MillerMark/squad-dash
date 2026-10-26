using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PromptTextStylingTests
{
    [Test]
    public void OverlayVisibility_TranscriptFullscreenRoundTripPreservesPromptAndCachedFormatting()
    {
        using var host = new Host(string.Join("\n", Enumerable.Repeat(
            "# Heading **bold** \"quote\" {name} `code` #abcdef", 30)));
        host.SetTheme("Light");
        host.Editor.IsUndoEnabled = false;
        host.Enable();
        host.Editor.SelectRange(12, 3);
        host.Editor.ScrollToVerticalOffset(160);
        host.Drain();
        var adorner = host.Adorner;
        var document = host.Editor.Document;
        var snapshot = PromptUndoManager.CaptureSnapshot(host.Editor);
        var offset = host.Editor.VerticalOffset;
        var glyphBuilds = adorner.GlyphCacheBuildCount;
        var inkBuilds = adorner.InkCacheBuildCount;
        var drawingBuilds = adorner.DrawingBuildCount;
        var textChanges = 0;
        host.Editor.TextChanged += (_, _) => textChanges++;
        Assert.That(OverlayPixels(host), Has.Some.Not.EqualTo((byte)0));

        for (var transition = 0; transition < 2; transition++)
        {
            PromptTextStyling.SetIsOverlayVisible(host.Editor, false);
            host.Drain();
            Assert.Multiple(() =>
            {
                Assert.That(adorner.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(adorner.IsVisible, Is.False);
                Assert.That(OverlayPixels(host), Has.All.EqualTo((byte)0),
                    "Even cached formatting must not paint in the shared adorner layer.");
                Assert.That(PromptTextStyling.GetIsEnabled(host.Editor), Is.True);
                Assert.That(host.Editor.IsVisible, Is.True,
                    "Suppression also applies while the transcript fullscreen prompt peek is visible.");
            });

            PromptTextStyling.SetIsOverlayVisible(host.Editor, true);
            host.Drain();
            Assert.Multiple(() =>
            {
                Assert.That(host.Adorner, Is.SameAs(adorner));
                Assert.That(adorner.IsVisible, Is.True);
                Assert.That(OverlayPixels(host), Has.Some.Not.EqualTo((byte)0));
                Assert.That(host.Editor.Document, Is.SameAs(document));
                Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(snapshot));
                Assert.That(host.Editor.VerticalOffset, Is.EqualTo(offset));
                Assert.That(adorner.GlyphCacheBuildCount, Is.EqualTo(glyphBuilds));
                Assert.That(adorner.InkCacheBuildCount, Is.EqualTo(inkBuilds));
                Assert.That(adorner.DrawingBuildCount, Is.EqualTo(drawingBuilds));
                Assert.That(textChanges, Is.Zero);
            });
        }
    }

    [Test]
    public void OverlayVisibility_TranscriptFullscreenBeforeAttachStartsHiddenAndRestores()
    {
        using var host = new Host("# Heading **bold** \"quote\" {name} `code` #abcdef");
        PromptTextStyling.SetIsOverlayVisible(host.Editor, false);
        host.Enable();
        Assert.That(host.Adorner.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(OverlayPixels(host), Has.All.EqualTo((byte)0));

        PromptTextStyling.SetIsOverlayVisible(host.Editor, true);
        host.Drain();
        Assert.That(host.Adorner.IsVisible, Is.True);
        Assert.That(OverlayPixels(host), Has.Some.Not.EqualTo((byte)0));
    }

    private static byte[] OverlayPixels(Host host)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.Decorator.ActualWidth),
            (int)Math.Ceiling(host.Decorator.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host.Layer);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    [TestCase(1, 1.48)]
    [TestCase(2, 1.32)]
    [TestCase(3, 1.16)]
    [TestCase(4, 1.0)]
    public void Styling_Headers_UseNativeLevelSizesAndSeparateThemeInk(int level, double scale)
    {
        var text = new string('#', level) + " Heading";
        using var host = new Host(text);
        host.Editor.IsUndoEnabled = false;
        host.Editor.SelectRange(text.Length, 0);
        host.Enable();
        Assert.That(PromptTextAdorner.HeadingSizeScale(level), Is.EqualTo(scale));
        foreach (var theme in new[] { "Light", "Dark" })
        {
            host.SetTheme(theme);
            var snapshot = PromptUndoManager.CaptureSnapshot(host.Editor);
            foreach (var size in new[] { 16d, 24d })
            {
                host.Editor.FontSize = size;
                host.Drain();
                for (var offset = 0; offset < text.Length; offset++)
                    Assert.That(SizeAt(host.Editor, offset), Is.EqualTo(size * scale).Within(0.001));
                AssertColoredCells(host, host.HeadingDelimiterBrush, i => i < level);
                AssertColoredCells(host, host.HeadingBrush, i => i >= level);
                Assert.That(host.Drawings().Where(d => ReferenceEquals(d.Brush, host.HeadingDelimiterBrush))
                    .All(d => d.Geometry is not RectangleGeometry && d.Pen is null), Is.True,
                    "Hash markers must have glyph ink, not a tint rectangle or underline.");
                Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(snapshot));
            }
            var background = ((SolidColorBrush)host.Editor.Background).Color;
            AssertBlend(host.HeadingDelimiterBrush.Color, host.HeadingBrush.Color, background, 0.6);
            Assert.That(ContrastRatio(host.HeadingBrush.Color, background), Is.GreaterThanOrEqualTo(4.5));
            Assert.That(ContrastRatio(host.HeadingDelimiterBrush.Color, background), Is.GreaterThanOrEqualTo(3));
            Assert.That(ContrastRatio(host.HeadingDelimiterBrush.Color, background),
                Is.LessThan(ContrastRatio(host.HeadingBrush.Color, background)));
            var color = host.HeadingBrush.Color;
            var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
            var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255d;
            var saturation = (max - min) / (1 - Math.Abs(max + min - 1));
            Assert.That(saturation, Is.EqualTo(0.7).Within(0.02));
        }
        host.Editor.SelectRange(0, text.Length);
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty, "Selected headings use native selection ink.");
    }

    [Test]
    public void Styling_Headers_ReserveWrappedLineHeightAndResetAfterEditingAndDisabling()
    {
        const string text = "# Heading words wrap onto several lines\nordinary";
        using var host = new Host(text);
        host.Editor.IsUndoEnabled = false;
        host.Editor.Width = 220;
        host.Enable();
        var newline = text.IndexOf('\n');
        var first = host.Editor.GetTextPointerAt(2).GetCharacterRect(LogicalDirection.Forward);
        var last = host.Editor.GetTextPointerAt(newline - 1).GetCharacterRect(LogicalDirection.Forward);
        var normal = host.Editor.GetTextPointerAt(newline + 1).GetCharacterRect(LogicalDirection.Forward);
        Assert.Multiple(() =>
        {
            Assert.That(last.Top, Is.GreaterThan(first.Top), "The heading must wrap.");
            Assert.That(first.Height / normal.Height, Is.EqualTo(1.48).Within(0.06));
            Assert.That(normal.Top, Is.GreaterThanOrEqualTo(last.Bottom - 0.01));
            Assert.That(SizeAt(host.Editor, newline + 1), Is.EqualTo(host.Editor.FontSize));
        });
        var previous = first;
        for (var offset = 3; offset < newline; offset++)
        {
            var cell = host.Editor.GetTextPointerAt(offset).GetCharacterRect(LogicalDirection.Forward);
            if (cell.Top > previous.Top + 0.5)
                Assert.That(cell.Top, Is.GreaterThanOrEqualTo(previous.Bottom - 0.01));
            previous = cell;
        }

        host.Editor.SelectRange(0, 1);
        host.Editor.ReplaceSelection("####");
        host.Drain();
        Assert.That(SizeAt(host.Editor, 5), Is.EqualTo(host.Editor.FontSize));
        host.Editor.SelectRange(0, 4);
        host.Editor.ReplaceSelection("#");
        host.Drain();
        Assert.That(SizeAt(host.Editor, 2), Is.EqualTo(host.Editor.FontSize * 1.48));
        host.Editor.SelectRange(0, 1);
        host.Editor.ReplaceSelection("");
        host.Drain();
        Assert.That(SizeAt(host.Editor, 1), Is.EqualTo(host.Editor.FontSize));
        host.Editor.SelectRange(0, 0);
        host.Editor.ReplaceSelection("#");
        host.Drain();
        PromptTextStyling.SetIsEnabled(host.Editor, false);
        host.Editor.FontSize = 20;
        host.Drain();
        Assert.That(SizeAt(host.Editor, 2), Is.EqualTo(20));
        host.Enable();
        Assert.That(SizeAt(host.Editor, 2), Is.EqualTo(20 * 1.48));
        host.Editor.SetPlainText("# Heading");
        host.Drain();
        host.Editor.SelectRange(9, 0);
        host.Editor.ReplaceSelection("\nplain");
        host.Drain();
        Assert.That(SizeAt(host.Editor, 10), Is.EqualTo(20),
            "Typing a new normal line must not inherit the heading size.");
    }

    [Test]
    public void Styling_Headers_PreserveInlinePrecedenceAndSharedUndo()
    {
        const string text = "# plain **bold** \"quote\" {name} `code` #abcdef";
        using var host = new Host(text);
        host.SetTheme("Light");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        editor.Selection.Select(editor.GetTextPointerAt(8), editor.GetTextPointerAt(2));
        var before = PromptUndoManager.CaptureSnapshot(editor);
        var history = new PromptUndoHistory(editor);
        history.CaptureBaseline();
        editor.TextChanged += (_, _) =>
        {
            if (!PromptTextStyling.IsApplyingFormatting(editor))
                history.PushSnapshot();
        };
        host.Enable();
        Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(before));
        Assert.That(history.Count, Is.EqualTo(1));
        editor.SelectRange(text.Length, 0);
        host.Drain();
        AssertColoredCells(host, host.HeadingDelimiterBrush, i => i == 0);
        AssertColoredCells(host, host.QuoteBrush, i => i >= 18 && i < 23);
        AssertColoredCells(host, host.PlaceholderBrush, i => i >= 26 && i < 30);
        Assert.That(FontAt(editor, 33).Source, Is.EqualTo("Consolas"));
        Assert.That(SizeAt(editor, 33), Is.EqualTo(editor.FontSize * 1.48));
        Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.HeadingBrush) &&
            d.Pen?.Thickness == editor.FontSize * 1.48 / 24), Is.True);
        Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b &&
            b.Color == Color.FromRgb(0xAB, 0xCD, 0xEF)), Is.True);
        editor.SelectRange(0, 1);
        editor.ReplaceSelection("####");
        host.Drain();
        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(SizeAt(editor, 5), Is.EqualTo(editor.FontSize));
        Assert.That(history.Undo(), Is.True);
        host.Drain();
        Assert.That(editor.GetPlainText(), Is.EqualTo(text));
        Assert.That(SizeAt(editor, 2), Is.EqualTo(editor.FontSize * 1.48));
        Assert.That(history.Redo(), Is.True);
        host.Drain();
        Assert.That(SizeAt(editor, 5), Is.EqualTo(editor.FontSize));
    }

    [TestCase("Light", "\"", "\"", "#B36679", "#8F85CC")]
    [TestCase("Light", "\u201C", "\u201D", "#B36679", "#8F85CC")]
    [TestCase("Dark", "\"", "\"", "#926772", "#746A9F")]
    [TestCase("Dark", "\u201C", "\u201D", "#926772", "#746A9F")]
    public void Styling_Delimiters_UseMutedThemeBrushesAndSubtleBoldMarkers(
        string theme, string open, string close, string quoteColor, string placeholderColor)
    {
        var text = open + "word" + close + " {name} **bold**";
        using var host = new Host(text);
        host.SetTheme(theme);
        var snapshot = PromptUndoManager.CaptureSnapshot(host.Editor);
        host.Enable();

        AssertColoredCells(host, host.QuoteDelimiterBrush, i => i is 0 or 5);
        AssertQuoteCells(host, i => i >= 1 && i < 5);
        AssertColoredCells(host, host.PlaceholderDelimiterBrush, i => i is 7 or 12);
        AssertColoredCells(host, host.PlaceholderBrush, i => i >= 8 && i < 12);
        AssertColoredCells(host, host.BoldDelimiterBrush, i => i is 14 or 15 or 20 or 21);
        Assert.Multiple(() =>
        {
            Assert.That(host.QuoteDelimiterBrush.Color, Is.EqualTo(ColorConverter.ConvertFromString(quoteColor)));
            Assert.That(host.PlaceholderDelimiterBrush.Color, Is.EqualTo(ColorConverter.ConvertFromString(placeholderColor)));
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.Editor.Foreground) &&
                d.Pen?.Thickness == host.Editor.FontSize / 24), Is.True);
            Assert.That(host.Drawings().Where(d => ReferenceEquals(d.Brush, host.BoldDelimiterBrush))
                .All(d => d.Pen is null), Is.True);
            Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(snapshot));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
        var background = ((SolidColorBrush)host.Editor.Background).Color;
        foreach (var (content, delimiter) in new[]
        {
            (host.QuoteBrush.Color, host.QuoteDelimiterBrush.Color),
            (host.PlaceholderBrush.Color, host.PlaceholderDelimiterBrush.Color),
        })
        {
            Assert.That(ContrastRatio(delimiter, background), Is.LessThan(ContrastRatio(content, background)));
            Assert.That(delimiter.A, Is.EqualTo(255));
            AssertBlend(delimiter, content, background, 0.6);
        }
        Assert.That(ContrastRatio(host.BoldDelimiterBrush.Color, background),
            Is.LessThan(ContrastRatio(host.SubtleBrush.Color, background)));
        AssertBlend(host.BoldDelimiterBrush.Color, host.SubtleBrush.Color, background, 0.9);
        foreach (var brush in new[] { host.QuoteBrush, host.PlaceholderBrush,
            host.QuoteDelimiterBrush, host.PlaceholderDelimiterBrush, host.BoldDelimiterBrush })
        {
            Assert.That(ContrastRatio(brush.Color, background), Is.GreaterThanOrEqualTo(3));
            Assert.That(PromptTextAdorner.EnsureVisibleBrush(brush, host.Editor.Background), Is.SameAs(brush));
        }
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Styling_UnsafeBrushes_KeepDelimitersAndContentVisible(string theme)
    {
        using var host = new Host("# heading **bold** \"quote\" {name} `code`");
        host.SetTheme(theme);
        host.Editor.SelectRange(host.Editor.GetPlainText().Length, 0);
        host.Enable();
        var background = ((SolidColorBrush)host.Editor.Background).Color;
        foreach (var unsafeBrush in new[]
        {
            new SolidColorBrush(background),
            new SolidColorBrush(Color.FromArgb(1, background.R, background.G, background.B)) { Opacity = 0.01 },
            new SolidColorBrush(Color.FromScRgb(0, 1, 1, 1)) { Opacity = 0 },
        })
        {
            foreach (var key in new[] { "PromptQuotedText", "PromptPlaceholderText",
                "PromptQuoteDelimiter", "PromptPlaceholderDelimiter", "PromptBoldDelimiter",
                "PromptHeadingText", "PromptHeadingDelimiter" })
                host.SetBrush(key, unsafeBrush);
            host.Drain();
            var ink = host.Drawings().Where(d => d.Geometry is not RectangleGeometry).ToArray();
            Assert.That(ink, Is.Not.Empty);
            foreach (var drawing in ink)
            {
                var brush = (SolidColorBrush)drawing.Brush;
                Assert.That(brush.Color.A, Is.EqualTo(255));
                Assert.That(brush.Opacity, Is.EqualTo(1));
                Assert.That(ContrastRatio(brush.Color, background), Is.GreaterThanOrEqualTo(3));
            }
            AssertVisibleCharactersPainted(host, "unsafe resource");
        }
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Styling_NestedDelimiters_PreserveBoldWeightAndNativeSelection(string theme)
    {
        using var host = new Host("**\"{name}\"**");
        host.SetTheme(theme);
        host.Enable();
        AssertColoredCells(host, host.BoldDelimiterBrush, i => i is 0 or 1 or 10 or 11);
        AssertColoredCells(host, host.QuoteDelimiterBrush, i => i is 2 or 9);
        AssertColoredCells(host, host.PlaceholderDelimiterBrush, i => i is 3 or 8);
        AssertColoredCells(host, host.PlaceholderBrush, i => i >= 4 && i < 8);
        AssertQuoteCells(host, _ => false);
        foreach (var brush in new[] { host.QuoteDelimiterBrush, host.PlaceholderDelimiterBrush, host.PlaceholderBrush })
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, brush) &&
                d.Pen?.Thickness == host.Editor.FontSize / 24), Is.True);

        host.Editor.SelectRange(1, 10);
        host.Drain();
        AssertColoredCells(host, host.BoldDelimiterBrush, i => i is 0 or 11);
        AssertColoredCells(host, host.QuoteDelimiterBrush, _ => false);
        AssertPlaceholderCells(host, _ => false);
        host.Editor.SelectRange(0, 12);
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty);
        Assert.That(host.Editor.GetSelectedText(), Is.EqualTo("**\"{name}\"**"));
    }

    [Test]
    public void Styling_Delimiters_RefreshThemeAndResourceWithoutEditingOrReattaching()
    {
        using var host = new Host("\"word\" {name} **bold** `code`");
        host.SetTheme("Light");
        host.Enable();
        var adorner = host.Adorner;
        var snapshot = PromptUndoManager.CaptureSnapshot(host.Editor);
        var changes = 0;
        host.Editor.TextChanged += (_, _) => changes++;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var previous = new[] { host.QuoteDelimiterBrush, host.PlaceholderDelimiterBrush, host.BoldDelimiterBrush };
            host.SetTheme(theme);
            AssertColoredCells(host, host.QuoteDelimiterBrush, i => i is 0 or 5);
            AssertColoredCells(host, host.PlaceholderDelimiterBrush, i => i is 7 or 12);
            AssertColoredCells(host, host.BoldDelimiterBrush, i => i is 14 or 15 or 20 or 21 or 23 or 28);
            Assert.That(host.Drawings().Any(d => previous.Any(b => ReferenceEquals(b, d.Brush))), Is.False);
        }
        var builds = adorner.InkCacheBuildCount;
        foreach (var (key, brush, offsets) in new[]
        {
            ("PromptQuoteDelimiter", Brushes.IndianRed, new[] { 0, 5 }),
            ("PromptPlaceholderDelimiter", Brushes.SlateBlue, new[] { 7, 12 }),
            ("PromptBoldDelimiter", Brushes.Gray, new[] { 14, 15, 20, 21, 23, 28 }),
        })
        {
            host.SetBrush(key, brush);
            host.Drain();
            AssertColoredCells(host, brush, offsets.Contains);
        }
        Assert.Multiple(() =>
        {
            Assert.That(host.Adorner, Is.SameAs(adorner));
            Assert.That(adorner.InkCacheBuildCount, Is.EqualTo(builds));
            Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(snapshot));
            Assert.That(changes, Is.Zero);
        });
    }

    [Test]
    public void Styling_MissingDelimiterResources_FallBackToExistingForegroundAndPlaceholderBrush()
    {
        using var host = new Host("\"word\" {name} **bold**");
        host.Editor.Background = Brushes.White;
        host.Editor.Foreground = Brushes.Teal;
        host.SetQuoteBrush(Brushes.Maroon);
        host.SetPlaceholderBrush(Brushes.Purple);
        host.Enable();
        AssertColoredCells(host, Brushes.Maroon, i => i >= 1 && i < 5);
        AssertColoredCells(host, Brushes.Purple, i => i >= 7 && i < 13);
        AssertColoredCells(host, Brushes.Teal, i => i is 0 or 5 || i >= 14);
        host.SetPlaceholderBrush(Brushes.BlueViolet);
        host.Drain();
        AssertColoredCells(host, Brushes.BlueViolet, i => i >= 7 && i < 13);
        host.SetBrush("PromptQuoteDelimiter", Brushes.IndianRed);
        host.SetBrush("PromptPlaceholderDelimiter", Brushes.SlateBlue);
        host.SetBrush("PromptBoldDelimiter", Brushes.Gray);
        host.Drain();
        AssertColoredCells(host, Brushes.IndianRed, i => i is 0 or 5);
        AssertColoredCells(host, Brushes.SlateBlue, i => i is 7 or 12);
        AssertColoredCells(host, Brushes.Gray, i => i is 14 or 15 or 20 or 21);
    }

    [TestCase("`\"word\" {name} **bold**`", 1)]
    [TestCase("``\u201Cword\u201D {name} **bold**``", 2)]
    [TestCase("__bold__", 0)]
    [TestCase("**unfinished", 0)]
    [TestCase("\\**escaped**", 0)]
    [TestCase("`unfinished", 0)]
    [TestCase("`mismatched``", 0)]
    [TestCase("\\`escaped\\`", 0)]
    public void Styling_LiteralDelimiters_OnlyMatchedCodeMarkersUseDelimiterBrush(string text, int codeMarkerWidth)
    {
        using var host = new Host(text);
        host.SetTheme("Light");
        host.Enable();
        AssertColoredCells(host, host.QuoteDelimiterBrush, _ => false);
        AssertColoredCells(host, host.PlaceholderDelimiterBrush, _ => false);
        AssertColoredCells(host, host.BoldDelimiterBrush, i => i < codeMarkerWidth || i >= text.Length - codeMarkerWidth);
    }

    [TestCase("Light", "`")]
    [TestCase("Dark", "`")]
    [TestCase("Light", "``")]
    [TestCase("Dark", "``")]
    public void Styling_InlineCodeDelimiters_ShareBoldBrushInsideQuotesAndPreserveSelection(string theme, string ticks)
    {
        var text = "\"before " + ticks + "code" + ticks + " after\" **bold**";
        using var host = new Host(text);
        host.SetTheme(theme);
        host.Enable();
        var opening = 8;
        var closing = opening + ticks.Length + 4;
        var bold = text.IndexOf("**", StringComparison.Ordinal);
        bool IsCodeMarker(int i) => i >= opening && i < opening + ticks.Length ||
            i >= closing && i < closing + ticks.Length;
        bool IsBoldMarker(int i) => i >= bold && i < bold + 2 || i >= text.Length - 2;
        AssertColoredCells(host, host.BoldDelimiterBrush, i => IsCodeMarker(i) || IsBoldMarker(i));
        AssertQuoteCells(host, i => i >= 1 && i < opening ||
            i >= closing + ticks.Length && i < bold - 2);
        Assert.That(FontAt(host.Editor, opening + ticks.Length).Source, Is.EqualTo("Consolas"));
        Assert.That(FontAt(host.Editor, opening), Is.EqualTo(host.Editor.FontFamily));
        Assert.That(host.Drawings().Where(d => ReferenceEquals(d.Brush, host.BoldDelimiterBrush))
            .All(d => d.Pen is null), Is.True);

        host.Editor.SelectRange(opening, closing + ticks.Length - opening);
        host.Drain();
        AssertColoredCells(host, host.BoldDelimiterBrush, IsBoldMarker);
        Assert.That(host.Editor.GetSelectedText(), Is.EqualTo(ticks + "code" + ticks));
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
    }

    [TestCase("Light", "#4433AA", "#FFFFFF", 9.131327)]
    [TestCase("Dark", "#B5A6FF", "#131110", 8.843170)]
    public void Styling_QuotedPlaceholder_ExactExampleUsesAccessibleBluePurpleAndKeepsProseMaroon(
        string theme, string foreground, string background, double expectedContrast)
    {
        const string text = "\"This is a test for {userName}\"";
        using var host = new Host(text);
        host.SetTheme(theme);
        host.Enable();
        var brush = host.PlaceholderBrush;
        var actualBackground = (SolidColorBrush)host.Editor.Background;
        var contrast = ContrastRatio(brush.Color, actualBackground.Color);
        Assert.Multiple(() =>
        {
            Assert.That(brush.Color, Is.EqualTo(ColorConverter.ConvertFromString(foreground)));
            Assert.That(actualBackground.Color, Is.EqualTo(ColorConverter.ConvertFromString(background)));
            Assert.That(brush.Color.A, Is.EqualTo(255));
            Assert.That(actualBackground.Color.A, Is.EqualTo(255));
            Assert.That(contrast, Is.GreaterThanOrEqualTo(7));
            Assert.That(contrast, Is.EqualTo(expectedContrast).Within(0.000001));
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, brush)), Is.True);
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
        });
        AssertPlaceholderCells(host, i => i >= 20 && i < 30);
        AssertQuoteCells(host, i => i >= 1 && i < 20);
        TestContext.Out.WriteLine($"{theme} placeholder: {brush.Color} on {actualBackground.Color}: {contrast:F6}:1.");
    }

    [TestCase("\"", "\"", "Light")]
    [TestCase("\u201C", "\u201D", "Dark")]
    public void Styling_AdjacentQuotedPlaceholders_PaintBracesOnceWithoutMaroonOverlap(string open, string close, string theme)
    {
        var text = open + "a{one}{two}b" + close + "{outside}";
        using var host = new Host(text);
        host.SetTheme(theme);
        host.Enable();
        AssertPlaceholderCells(host, i => (i >= 2 && i < 12) || i >= 14);
        AssertQuoteCells(host, i => i is 1 or 12);
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
    }

    [TestCase("Light", "Hello {user.name}.", "{user.name}")]
    [TestCase("Dark", "Hello {user.name}.", "{user.name}")]
    [TestCase("Light", "{one}{two}{three}", "{one}", "{two}", "{three}")]
    [TestCase("Dark", "'single {quoted}' {outside}", "{quoted}", "{outside}")]
    [TestCase("Light", "\"unfinished {userName}", "{userName}")]
    [TestCase("Dark", "\u201Cunfinished {user.name}", "{user.name}")]
    [TestCase("Light", "{before} \"unfinished {after}", "{before}", "{after}")]
    [TestCase("Dark", "\"unfinished\n{after}", "{after}")]
    [TestCase("Light", "({user.name!?:/@-$%&+=,;()[]}), {\u7528\u6237}", "{user.name!?:/@-$%&+=,;()[]}", "{\u7528\u6237}")]
    [TestCase("Dark", "{{nested}}{}\\{escaped}{space name}{last}", "{last}")]
    [TestCase("Light", "\\\\{even}{last}", "{even}", "{last}")]
    public void Styling_PlaceholdersWithoutCompletedQuotes_PaintExactBraceInclusiveCellsWithoutEditing(
        string theme, string text, params string[] expected)
    {
        using var host = new Host(text);
        host.SetTheme(theme);
        var snapshot = PromptUndoManager.CaptureSnapshot(host.Editor);
        var inlines = host.Editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).ToArray();
        var changes = 0;
        host.Editor.TextChanged += (_, _) => changes++;
        var ranges = expected.Select(content => (Start: text.IndexOf(content, StringComparison.Ordinal), content.Length)).ToArray();
        host.Enable();
        AssertPlaceholderCells(host, i => ranges.Any(r => i >= r.Start && i < r.Start + r.Length));
        AssertQuoteCells(host, _ => false);
        Assert.Multiple(() =>
        {
            Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(snapshot));
            Assert.That(host.Editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines), Is.EqualTo(inlines));
            Assert.That(changes, Is.Zero);
            Assert.That(host.Editor.CanUndo, Is.False);
        });
        for (var i = 0; i < text.Length; i++)
            Assert.That(FontAt(host.Editor, i), Is.EqualTo(host.Editor.FontFamily));
    }

    [TestCase("\"{} {space name} {tab\tname} {incomplete\"")]
    [TestCase("\"{{nested}} {outer{inner}tail} {doubled}}\"")]
    [TestCase("\"\\{{inner}} {outer\\{inner}tail} \\{escaped} {end\\}\"")]
    [TestCase("\"{user\\\"name} {user\u201Cname}\"")]
    [TestCase("\"{cross\" \"quote}\"")]
    [TestCase("\"{hard\nbreak}\"")]
    [TestCase("`\"{code}\"`")]
    public void Styling_InvalidPlaceholders_DoNotPaintBluePurple(string text)
    {
        using var host = new Host(text);
        host.SetTheme("Light");
        host.Editor.IsUndoEnabled = false;
        host.Enable();
        Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.PlaceholderBrush)), Is.False);
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
    }

    [TestCase("Light", "\"{before}`{code}`{after}\"", 1, 9, 17, 24)]
    [TestCase("Dark", "\"{before}``{code}`tick``{after}\"", 1, 9, 24, 31)]
    [TestCase("Light", "\"{left`code`right}\"", 1, 6, 12, 18)]
    [TestCase("Light", "{before}`{code}`{after}", 0, 8, 16, 23)]
    [TestCase("Dark", "{left`code`right}", 0, 5, 11, 17)]
    [TestCase("Light", "`{unclosed`{after}", 11, 18, 0, 0)]
    public void Styling_QuotedPlaceholders_CodeAndBacktickDelimitersWin(
        string theme, string text, int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        using var host = new Host(text);
        host.SetTheme(theme);
        host.Editor.IsUndoEnabled = false;
        host.Enable();
        AssertPlaceholderCells(host,
            i => (i >= firstStart && i < firstEnd) || (i >= secondStart && i < secondEnd));
        AssertQuoteCells(host, _ => false);
        var code = PromptTextTokenParser.Parse(text).Single(t => t.Kind == PromptTextTokenKind.InlineCodeContent);
        for (var i = code.Start; i < code.Start + code.Length; i++)
            Assert.That(FontAt(host.Editor, i).Source, Is.EqualTo("Consolas"));
        foreach (var offset in Enumerable.Range(0, text.Length)
            .Where(i => text[i] == '`' && (i < code.Start || i >= code.Start + code.Length)))
            Assert.That(FontAt(host.Editor, offset), Is.EqualTo(host.Editor.FontFamily),
                "Code delimiters must retain the prose font and native foreground.");
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Styling_QuotedPlaceholders_PreserveBoldAndHexSwatches(string theme)
    {
        const string text = "\"**{#abc}** prose #def\"";
        using var host = new Host(text);
        host.SetTheme(theme);
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.PlaceholderBrush) &&
                d.Pen?.Thickness == host.Editor.FontSize / 24), Is.True);
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b &&
                b.Color == Color.FromRgb(0xAA, 0xBB, 0xCC)), Is.EqualTo(1));
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b &&
                b.Color == Color.FromRgb(0xDD, 0xEE, 0xFF)), Is.EqualTo(1));
            Assert.That(Groups(VisualTreeHelper.GetDrawing(host.Adorner)!)
                .Where(g => g.Transform is ScaleTransform)
                .SelectMany(g => Groups(g).SelectMany(child => child.Children)).OfType<GeometryDrawing>()
                .Any(d => ReferenceEquals(d.Brush, host.PlaceholderBrush)), Is.True,
                "The superscript hash inside a placeholder must use blue-purple, not quote maroon.");
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
        AssertQuoteCells(host, i => i >= 11 && i < text.Length - 1);
        AssertColoredCells(host, host.BoldDelimiterBrush, i => i is 1 or 2 or 9 or 10);
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Styling_UnquotedPlaceholders_PreserveBoldSuperscriptSwatchesAndNativeFonts(string theme)
    {
        const string text = "**{#abc}** {plain} `#def`";
        using var host = new Host(text);
        host.SetTheme(theme);
        host.Editor.IsUndoEnabled = false;
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.PlaceholderBrush) &&
                d.Pen?.Thickness == host.Editor.FontSize / 24), Is.True);
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b &&
                b.Color == Color.FromRgb(0xAA, 0xBB, 0xCC)), Is.EqualTo(1));
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b &&
                b.Color == Color.FromRgb(0xDD, 0xEE, 0xFF)), Is.EqualTo(1));
            Assert.That(Groups(VisualTreeHelper.GetDrawing(host.Adorner)!)
                .Where(g => g.Transform is ScaleTransform)
                .SelectMany(g => Groups(g).SelectMany(child => child.Children)).OfType<GeometryDrawing>()
                .Any(d => ReferenceEquals(d.Brush, host.PlaceholderBrush)), Is.True);
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
            Assert.That(FontAt(host.Editor, text.IndexOf("plain", StringComparison.Ordinal)), Is.EqualTo(host.Editor.FontFamily));
            Assert.That(FontAt(host.Editor, text.IndexOf("#def", StringComparison.Ordinal)).Source, Is.EqualTo("Consolas"));
        });
        AssertQuoteCells(host, _ => false);
    }

    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    public void Styling_UnquotedPlaceholderSelection_LeavesSelectedCellsNativeAndRestoresInk(string theme, bool backward)
    {
        const string text = "Hi {user.name}.";
        using var host = new Host(text);
        host.SetTheme(theme);
        var editor = host.Editor;
        editor.Selection.Select(editor.GetTextPointerAt(backward ? 9 : 5), editor.GetTextPointerAt(backward ? 5 : 9));
        var snapshot = PromptUndoManager.CaptureSnapshot(editor);
        var selectionBrush = editor.SelectionBrush;
        host.Enable();
        AssertPlaceholderCells(host, i => (i >= 3 && i < 5) || (i >= 9 && i < 14));
        Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(snapshot));
        editor.SelectRange(3, 11);
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings(), Is.Empty);
            Assert.That(editor.GetSelectedText(), Is.EqualTo("{user.name}"));
            Assert.That(editor.SelectionBrush, Is.SameAs(selectionBrush));
        });
        editor.SelectRange(0, 0);
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 3 && i < 14);
        Assert.That(editor.GetPlainText(), Is.EqualTo(text));
        Assert.That(editor.CanUndo, Is.False);
    }

    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    public void Styling_QuotedPlaceholderSelection_PreservesNativeInkDirectionAndSource(string theme, bool backward)
    {
        const string text = "\"Say hello to {username}\".";
        using var host = new Host(text);
        host.SetTheme(theme);
        var editor = host.Editor;
        editor.Selection.Select(editor.GetTextPointerAt(backward ? 20 : 12), editor.GetTextPointerAt(backward ? 12 : 20));
        var before = PromptUndoManager.CaptureSnapshot(editor);
        var blocks = editor.Document.Blocks.ToArray();
        var runs = editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).ToArray();
        var positions = Enumerable.Range(0, text.Length)
            .Select(i => editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)).ToArray();
        var selectionBrush = editor.SelectionBrush;
        var foreground = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
            .GetPropertyValue(TextElement.ForegroundProperty);
        var changes = 0;
        editor.TextChanged += (_, _) => changes++;
        host.Enable();
        AssertPlaceholderCells(host, i => i >= 20 && i < 24);
        AssertQuoteCells(host, i => i >= 1 && i < 12);
        Assert.Multiple(() =>
        {
            Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(before));
            Assert.That(editor.Document.Blocks.ToArray(), Is.EqualTo(blocks));
            Assert.That(editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines), Is.EqualTo(runs));
            Assert.That(Enumerable.Range(0, text.Length)
                .Select(i => editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)), Is.EqualTo(positions));
            Assert.That(changes, Is.Zero);
            Assert.That(editor.CanUndo, Is.False);
        });
        editor.SelectRange(1, 23);
        host.Drain();
        Assert.Multiple(() =>
        {
            AssertQuoteCells(host, _ => false);
            AssertPlaceholderCells(host, _ => false);
            AssertColoredCells(host, host.QuoteDelimiterBrush, i => i is 0 or 24);
            Assert.That(editor.GetSelectedText(), Is.EqualTo("Say hello to {username}"));
            Assert.That(editor.SelectionBrush, Is.SameAs(selectionBrush));
            Assert.That(new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
                .GetPropertyValue(TextElement.ForegroundProperty), Is.EqualTo(foreground));
        });
        editor.SelectRange(0, 0);
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 14 && i < 24);
        AssertQuoteCells(host, i => i >= 1 && i < 14);
        for (var i = 0; i <= text.Length; i++)
            Assert.That(editor.GetCharOffset(editor.GetTextPointerAt(i)), Is.EqualTo(i));
    }

    [TestCase(14)]
    [TestCase(18)]
    [TestCase(24)]
    public void Styling_QuotedPlaceholderCaret_IsExcludedFromAllOverlayPaint(int offset)
    {
        using var host = new Host("\"Say hello to {username}\".");
        host.SetTheme("Dark");
        host.Editor.SelectRange(offset, 0);
        host.Enable();
        var caret = host.Editor.CaretPosition.GetCharacterRect(host.Editor.CaretPosition.LogicalDirection);
        var clip = Groups(VisualTreeHelper.GetDrawing(host.Adorner)!)
            .Select(g => g.ClipGeometry).OfType<CombinedGeometry>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.GeometryCombineMode, Is.EqualTo(GeometryCombineMode.Exclude));
            Assert.That(clip.FillContains(new Point(caret.Left, caret.Top + caret.Height / 2)), Is.False);
            Assert.That(host.Editor.GetCaretOffset(), Is.EqualTo(offset));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [Test]
    public void Styling_QuotedPlaceholders_RefreshLiveThemeAndPlaceholderOnlyResource()
    {
        using var host = new Host("\"Say hello to {username}\". {user.name}");
        host.SetTheme("Light");
        host.Enable();
        var adorner = host.Adorner;
        var before = PromptUndoManager.CaptureSnapshot(host.Editor);
        var changes = 0;
        host.Editor.TextChanged += (_, _) => changes++;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var oldBrush = host.PlaceholderBrush;
            host.SetTheme(theme);
            host.Drain();
            AssertPlaceholderCells(host, i => (i >= 14 && i < 24) || i >= 27);
            AssertQuoteCells(host, i => i >= 1 && i < 14);
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, oldBrush)), Is.False);
            Assert.That(host.Adorner, Is.SameAs(adorner));
        }
        var builds = adorner.InkCacheBuildCount;
        host.SetPlaceholderBrush(Brushes.MediumPurple);
        host.Drain();
        AssertPlaceholderCells(host, i => (i >= 14 && i < 24) || i >= 27);
        AssertQuoteCells(host, i => i >= 1 && i < 14);
        Assert.Multiple(() =>
        {
            Assert.That(adorner.InkCacheBuildCount, Is.EqualTo(builds));
            Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(before));
            Assert.That(changes, Is.Zero);
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Styling_UnquotedPlaceholderEdits_UndoRedoRestoresIncompleteInputWithoutExtraHistory(bool sharedHistory)
    {
        const string before = "Hello {user.name";
        const string after = "Hello {user.name}";
        using var host = new Host(before);
        host.SetTheme("Dark");
        var editor = host.Editor;
        editor.IsUndoEnabled = !sharedHistory;
        editor.SelectRange(before.Length, 0);
        var history = new PromptUndoHistory(editor);
        history.CaptureBaseline();
        if (sharedHistory)
            editor.TextChanged += (_, _) =>
            {
                if (!PromptTextStyling.IsApplyingFormatting(editor))
                    history.PushSnapshot();
            };
        host.Enable();
        Assert.That(host.Drawings(), Is.Empty);
        editor.ReplaceSelection("}");
        editor.SelectRange(after.Length, 0);
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 6);
        Assert.That(editor.GetPlainText(), Is.EqualTo(after));
        if (sharedHistory)
        {
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history.Undo(), Is.True);
        }
        else
            editor.Undo();
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings(), Is.Empty);
            Assert.That(editor.GetPlainText(), Is.EqualTo(before));
            Assert.That(sharedHistory ? history.CanUndo : editor.CanUndo, Is.False);
        });
        if (sharedHistory)
            Assert.That(history.Redo(), Is.True);
        else
            editor.Redo();
        editor.SelectRange(after.Length, 0);
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 6);
        Assert.That(editor.GetPlainText(), Is.EqualTo(after));
    }

    [Test]
    public void Styling_QuotedPlaceholder_WrapAndSplitRunsPreserveWholeBraceRange()
    {
        const string text = "\"a {averylongplaceholderthatwrapsacrosslines} b\"";
        using var host = new Host("");
        host.SetTheme("Light");
        var paragraph = new Paragraph(new Run(text[..8]));
        paragraph.Inlines.Add(new Run(text[8..26]));
        paragraph.Inlines.Add(new Run(text[26..]));
        host.Editor.Document.Blocks.Clear();
        host.Editor.Document.Blocks.Add(paragraph);
        host.Editor.Width = 180;
        host.Enable();
        var end = text.IndexOf('}') + 1;
        Assert.That(ColoredClips(host, host.PlaceholderBrush).Select(r => r.Top).Distinct().Count(), Is.GreaterThan(1));
        AssertPlaceholderCells(host, i => i >= 3 && i < end);
        AssertQuoteCells(host, i => i > 0 && i < text.Length - 1 && (i < 3 || i >= end));
        host.Editor.Width = 440;
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 3 && i < end);
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
        host.Editor.SetPlainText("\"{hard\nbreak}\"");
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty, "Hard breaks must discard both old blue-purple and maroon ink.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Styling_QuotedPlaceholderEdits_UndoRedoRestoresColorWithoutExtraHistory(bool sharedHistory)
    {
        const string before = "\"Say hello to {username\".";
        const string after = "\"Say hello to {username}\".";
        using var host = new Host(before);
        host.SetTheme("Light");
        var editor = host.Editor;
        editor.IsUndoEnabled = !sharedHistory;
        editor.SelectRange(23, 0);
        var history = new PromptUndoHistory(editor);
        history.CaptureBaseline();
        if (sharedHistory)
            editor.TextChanged += (_, _) =>
            {
                if (!PromptTextStyling.IsApplyingFormatting(editor))
                    history.PushSnapshot();
            };
        host.Enable();
        AssertPlaceholderCells(host, _ => false);
        editor.ReplaceSelection("}");
        host.Drain();
        Assert.That(editor.GetSelectedText(), Is.EqualTo("}"));
        AssertPlaceholderCells(host, i => i >= 14 && i < 23);
        editor.SelectRange(24, 0);
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 14 && i < 24);
        Assert.That(editor.GetPlainText(), Is.EqualTo(after));
        if (sharedHistory)
        {
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history.Undo(), Is.True);
        }
        else
            editor.Undo();
        host.Drain();
        AssertPlaceholderCells(host, _ => false);
        AssertQuoteCells(host, i => i >= 1 && i < 23);
        Assert.That(editor.GetPlainText(), Is.EqualTo(before));
        Assert.That(sharedHistory ? history.CanUndo : editor.CanUndo, Is.False);
        if (sharedHistory)
            Assert.That(history.Redo(), Is.True);
        else
            editor.Redo();
        host.Drain();
        editor.SelectRange(24, 0);
        host.Drain();
        AssertPlaceholderCells(host, i => i >= 14 && i < 24);
        Assert.That(editor.GetPlainText(), Is.EqualTo(after));
        editor.SelectRange(18, 0);
        editor.ReplaceSelection(" ");
        editor.SelectRange(19, 0);
        host.Drain();
        AssertPlaceholderCells(host, _ => false);
        AssertQuoteCells(host, i => i >= 1 && i < 25);
        editor.SetPlainText("{outside}");
        host.Drain();
        AssertPlaceholderCells(host, _ => true);
        AssertQuoteCells(host, _ => false);
    }

    [Test]
    public void Styling_QuotedPlaceholders_SkipEmbeddedCodeWithoutCrossFenceMatches()
    {
        using var host = new Host("\"{unfinished\n```text\n\"{code}\" #00ff00\n```\ntail}\"\n\"{after}\"");
        host.SetTheme("Dark");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(host.Editor), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(host.Editor).Single();
        var text = host.Editor.GetPlainText();
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Count(d => ReferenceEquals(d.Brush, host.PlaceholderBrush)), Is.EqualTo(1));
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.False);
            Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Lime), Is.False);
            Assert.That(RichTextBoxExtensions.EnumerateEmbeddedRegions(host.Editor).Single(), Is.SameAs(region));
            Assert.That(region.Content, Is.EqualTo("\"{code}\" #00ff00"));
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
        });
    }

    [TestCase("Light", "#800020", "#FFFFFF", 10.832)]
    [TestCase("Dark", "#E6A0B4", "#131110", 9.012)]
    public void Styling_Quotes_UseRealThemeForegroundAndPromptBackgroundWithAccessibleContrast(
        string theme, string foreground, string background, double expectedContrast)
    {
        using var host = new Host("before \"quoted prose\" after");
        host.SetTheme(theme);
        host.Enable();
        var quoteBrush = host.QuoteBrush;
        var actualBackground = (SolidColorBrush)host.Editor.Background;
        var contrast = ContrastRatio(quoteBrush.Color, actualBackground.Color);
        Assert.Multiple(() =>
        {
            Assert.That(quoteBrush.Color, Is.EqualTo(ColorConverter.ConvertFromString(foreground)));
            Assert.That(actualBackground.Color, Is.EqualTo(ColorConverter.ConvertFromString(background)));
            Assert.That(quoteBrush.Color.A, Is.EqualTo(255));
            Assert.That(actualBackground.Color.A, Is.EqualTo(255));
            Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5));
            Assert.That(contrast, Is.EqualTo(expectedContrast).Within(0.001));
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, quoteBrush)), Is.True,
                "Contrast must use the brush actually selected by the painter, not an unused theme token.");
        });
        AssertQuoteCells(host, i => i >= 8 && i < 20);
        TestContext.Out.WriteLine($"{theme}: {quoteBrush.Color} on {actualBackground.Color}: {contrast:F3}:1.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Styling_Quotes_PreserveSourceRunsSelectionDirectionCaretAndUndo(bool backward)
    {
        using var host = new Host("before \"quoted prose\" after");
        host.SetTheme("Light");
        var editor = host.Editor;
        editor.Selection.Select(editor.GetTextPointerAt(backward ? 14 : 9), editor.GetTextPointerAt(backward ? 9 : 14));
        var snapshot = PromptUndoManager.CaptureSnapshot(editor);
        var blocks = editor.Document.Blocks.ToArray();
        var runs = editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).ToArray();
        var positions = Enumerable.Range(0, snapshot.SerializedText.Length)
            .Select(i => editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)).ToArray();
        var changes = 0;
        editor.TextChanged += (_, _) => changes++;
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(snapshot));
            Assert.That(editor.Document.Blocks.ToArray(), Is.EqualTo(blocks));
            Assert.That(editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines), Is.EqualTo(runs));
            Assert.That(Enumerable.Range(0, snapshot.SerializedText.Length)
                .Select(i => editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)), Is.EqualTo(positions));
            Assert.That(changes, Is.Zero);
            Assert.That(editor.CanUndo, Is.False);
        });
        for (var i = 0; i <= snapshot.SerializedText.Length; i++)
            Assert.That(editor.GetCharOffset(editor.GetTextPointerAt(i)), Is.EqualTo(i));
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Styling_QuotedSelection_LeavesSelectedCellsToNativeForeground(string theme)
    {
        using var host = new Host("\"abcdef\"");
        host.SetTheme(theme);
        host.Enable();
        var selectionBrush = host.Editor.SelectionBrush;
        var foreground = new TextRange(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd)
            .GetPropertyValue(TextElement.ForegroundProperty);
        host.Editor.SelectRange(3, 2);
        host.Drain();
        AssertQuoteCells(host, i => i is 1 or 2 or 5 or 6);
        host.Editor.SelectRange(1, 6);
        host.Drain();
        Assert.Multiple(() =>
        {
            AssertQuoteCells(host, _ => false);
            AssertColoredCells(host, host.QuoteDelimiterBrush, i => i is 0 or 7);
            Assert.That(host.Editor.SelectionBrush, Is.SameAs(selectionBrush));
            Assert.That(new TextRange(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd)
                .GetPropertyValue(TextElement.ForegroundProperty), Is.EqualTo(foreground));
            Assert.That(host.Editor.GetSelectedText(), Is.EqualTo("abcdef"));
        });
        host.Editor.SelectRange(0, 0);
        host.Drain();
        AssertQuoteCells(host, i => i >= 1 && i <= 6);
    }

    [Test]
    public void Styling_QuotedCaret_IsExcludedFromOverlay()
    {
        using var host = new Host("\"abcdef\"");
        host.SetTheme("Light");
        host.Editor.SelectRange(4, 0);
        host.Enable();
        var caret = host.Editor.CaretPosition.GetCharacterRect(host.Editor.CaretPosition.LogicalDirection);
        var clip = Groups(VisualTreeHelper.GetDrawing(host.Adorner)!)
            .Select(g => g.ClipGeometry).OfType<CombinedGeometry>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.GeometryCombineMode, Is.EqualTo(GeometryCombineMode.Exclude));
            Assert.That(clip.FillContains(new Point(caret.Left, caret.Top + caret.Height / 2)), Is.False);
            Assert.That(host.Editor.GetCaretOffset(), Is.EqualTo(4));
        });
    }

    [TestCase("\"unfinished")]
    [TestCase("\u201Cmismatched\"")]
    [TestCase("\" \t \"")]
    [TestCase("'literal' don't \u2018single\u2019")]
    [TestCase("\\\"escaped\\\"")]
    [TestCase("\"hard\nbreak\"")]
    [TestCase("`\"inside code\"`")]
    public void Styling_InvalidQuotes_DoNotPaintMaroon(string text)
    {
        using var host = new Host(text);
        host.SetTheme("Light");
        host.Enable();
        Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.False);
    }

    [TestCase("\"before `code \"literal\"` after\"")]
    [TestCase("\"before ``code ` \"literal\"`` after\"")]
    [TestCase("\u201Cbefore `code \u201Cinside\u201D` after\u201D")]
    public void Styling_QuotesAroundCode_MaskCodeAndAllBacktickDelimiters(string text)
    {
        using var host = new Host(text);
        host.SetTheme("Dark");
        host.Editor.IsUndoEnabled = false;
        host.Enable();
        var firstTick = text.IndexOf('`');
        var lastTick = text.LastIndexOf('`');
        AssertQuoteCells(host, i => i > 0 && i < text.Length - 1 && (i < firstTick || i > lastTick));
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
    }

    [Test]
    public void Styling_QuotedBoldAndSwatches_PreserveWeightAndChipColors()
    {
        using var host = new Host("\"plain **bold** #abc\" #def");
        host.SetTheme("Light");
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush) &&
                d.Pen?.Thickness == host.Editor.FontSize / 24), Is.True);
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b &&
                b.Color == Color.FromRgb(0xAA, 0xBB, 0xCC)), Is.EqualTo(1));
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b &&
                b.Color == Color.FromRgb(0xDD, 0xEE, 0xFF)), Is.EqualTo(1));
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo("\"plain **bold** #abc\" #def"));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [Test]
    public void Styling_Quotes_SkipEmbeddedFenceAndDoNotMatchAcrossIt()
    {
        using var host = new Host("\"unfinished\n```text\n\"code\" **bold** #00ff00\n```\ntail\"\n\"after\"");
        host.SetTheme("Dark");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(host.Editor), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(host.Editor).Single();
        var text = host.Editor.GetPlainText();
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Count(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.EqualTo(1));
            Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Lime), Is.False);
            Assert.That(host.Drawings().Any(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.False);
            Assert.That(RichTextBoxExtensions.EnumerateEmbeddedRegions(host.Editor).Single(), Is.SameAs(region));
            Assert.That(region.Content, Is.EqualTo("\"code\" **bold** #00ff00"));
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
        });
    }

    [Test]
    public void Styling_QuotedContent_SurvivesVisualWrapButStopsAtHardLineBreak()
    {
        const string text = "\"words that wrap onto several visual lines\"";
        using var host = new Host(text);
        host.SetTheme("Light");
        host.Editor.Width = 180;
        host.Enable();
        Assert.That(QuoteClips(host).Select(r => r.Top).Distinct().Count(), Is.GreaterThan(1));
        AssertQuoteCells(host, i => i > 0 && i < text.Length - 1);

        var paragraph = new Paragraph(new Run("\"words"));
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("next line\""));
        host.Editor.Document.Blocks.Clear();
        host.Editor.Document.Blocks.Add(paragraph);
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty, "Hard LineBreak must remove old wrapped quote paint.");
    }

    [Test]
    public void Styling_QuotesAcrossSplitRuns_StyleDelimitersAndKeepInlineUiLiteral()
    {
        using var host = new Host("");
        host.SetTheme("Light");
        var paragraph = new Paragraph(new Run("\"ab"));
        var inlineUi = new InlineUIContainer(new Border { Width = 8, Height = 8 });
        paragraph.Inlines.Add(inlineUi);
        paragraph.Inlines.Add(new Run("cd\" tail"));
        host.Editor.Document.Blocks.Clear();
        host.Editor.Document.Blocks.Add(paragraph);
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.True);
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo("\"abcd\" tail"));
            Assert.That(paragraph.Inlines.Contains(inlineUi), Is.True);
        });
    }

    [Test]
    public void Styling_ClosingQuoteEdit_UndoRedoDoesNotLeakColorOrAddNativeUndoUnits()
    {
        using var host = new Host("\"word");
        host.SetTheme("Light");
        host.Enable();
        Assert.That(host.Drawings(), Is.Empty);
        host.Editor.SelectRange(5, 0);
        host.Editor.ReplaceSelection("\"");
        host.Drain();
        AssertQuoteCells(host, i => i >= 1 && i <= 4);
        host.Editor.Undo();
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo("\"word"));
            Assert.That(host.Drawings(), Is.Empty);
            Assert.That(host.Editor.CanUndo, Is.False);
            Assert.That(host.Editor.CanRedo, Is.True);
        });
        host.Editor.Redo();
        host.Drain();
        AssertQuoteCells(host, i => i >= 1 && i <= 4);
        host.Editor.SelectRange(5, 1);
        host.Editor.ReplaceSelection("");
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty);
        host.Editor.SetPlainText("plain replacement");
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty);
    }

    [Test]
    public void Styling_ClosingQuoteEdit_PreservesSharedUndoCoalescingAndRedo()
    {
        using var host = new Host("\"word");
        host.SetTheme("Light");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        editor.SelectRange(5, 0);
        var history = new PromptUndoHistory(editor);
        history.CaptureBaseline();
        editor.TextChanged += (_, _) =>
        {
            if (!PromptTextStyling.IsApplyingFormatting(editor))
                history.PushSnapshot();
        };
        host.Enable();
        editor.ReplaceSelection("\"");
        host.Drain();
        editor.SelectRange(6, 0);
        editor.ReplaceSelection(" tail");
        host.Drain();
        AssertQuoteCells(host, i => i >= 1 && i <= 4);
        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(history.Undo(), Is.True);
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(editor.GetPlainText(), Is.EqualTo("\"word"));
            Assert.That(host.Drawings(), Is.Empty);
            Assert.That(history.CanRedo, Is.True);
        });
        Assert.That(history.Redo(), Is.True);
        host.Drain();
        AssertQuoteCells(host, i => i >= 1 && i <= 4);
        Assert.That(editor.GetPlainText(), Is.EqualTo("\"word\" tail"));
        Assert.That(history.Count, Is.EqualTo(2));
    }

    [Test]
    public void Styling_Quotes_RefreshLiveThemeAndResourceWithoutTextEditOrReattach()
    {
        using var host = new Host("\"quoted\"");
        host.SetTheme("Light");
        host.Enable();
        var adorner = host.Adorner;
        var original = PromptUndoManager.CaptureSnapshot(host.Editor);
        var changes = 0;
        host.Editor.TextChanged += (_, _) => changes++;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var previous = host.QuoteBrush;
            host.SetTheme(theme);
            host.Drain();
            Assert.Multiple(() =>
            {
                Assert.That(host.Adorner, Is.SameAs(adorner));
                Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.True);
                Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, previous)), Is.False);
                Assert.That(PromptUndoManager.CaptureSnapshot(host.Editor), Is.EqualTo(original));
                Assert.That(changes, Is.Zero);
            });
        }
        var builds = host.Adorner.InkCacheBuildCount;
        host.SetQuoteBrush(Brushes.Maroon);
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, Brushes.Maroon)), Is.True);
            Assert.That(host.Adorner.InkCacheBuildCount, Is.EqualTo(builds),
                "A quote-only resource replacement must repaint without rebuilding native ink.");
            Assert.That(changes, Is.Zero);
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [Test]
    public void Styling_EmptyInkBoundsRemainEmptyWhenTranslated()
    {
        Assert.DoesNotThrow(() => PromptTextAdorner.OffsetInkBounds(Rect.Empty, new Vector(25, 50)));
        Assert.That(PromptTextAdorner.OffsetInkBounds(Rect.Empty, new Vector(25, 50)), Is.EqualTo(Rect.Empty));
    }

    [Test]
    public void Styling_DrawsMarkersAndRgbChips_WithoutChangingDocumentOrSelection()
    {
        using var host = new Host("## Heading **bold** #aBc #123456");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        editor.SelectRange(3, 7);
        var originalText = editor.GetPlainText();
        var blocks = editor.Document.Blocks.ToArray();
        var caret = editor.GetCaretOffset();
        var changes = 0;
        editor.TextChanged += (_, _) =>
        {
            if (!PromptTextStyling.IsApplyingFormatting(editor))
                changes++;
        };

        host.Enable();

        var drawings = host.Drawings();
        Assert.Multiple(() =>
        {
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0xAA, 0xBB, 0xCC)), Is.EqualTo(1));
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0x12, 0x34, 0x56)), Is.EqualTo(1));
            Assert.That(drawings.Any(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.True);
            Assert.That(drawings.Count(d => d.Pen?.Thickness == editor.FontSize * 1.32 / 24), Is.EqualTo(1),
                "One contiguous bold word should require one native-ink draw.");
            Assert.That(drawings.All(d => d.Bounds.Width > 0 && d.Bounds.Height > 0), Is.True);
            Assert.That(editor.GetPlainText(), Is.EqualTo(originalText));
            Assert.That(editor.Document.Blocks.ToArray(), Is.EqualTo(blocks));
            Assert.That(editor.GetCaretOffset(), Is.EqualTo(caret));
            Assert.That(editor.GetSelectedText(), Is.EqualTo("Heading"));
            Assert.That(editor.CanUndo, Is.False);
            Assert.That(changes, Is.Zero);
            Assert.That(host.Adorner.IsHitTestVisible, Is.False);
            Assert.That(host.Adorner.Focusable, Is.False);
        });
        for (var offset = 0; offset <= originalText.Length; offset++)
            Assert.That(editor.GetCharOffset(editor.GetTextPointerAt(offset)), Is.EqualTo(offset));
    }

    [Test]
    public void Styling_HandlesSplitRunsLineBreaksAndExistingInlineUi()
    {
        using var host = new Host("");
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("*"));
        paragraph.Inlines.Add(new InlineUIContainer(new Border { Width = 8, Height = 8 }));
        paragraph.Inlines.Add(new Run("*bold**"));
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("## #"));
        paragraph.Inlines.Add(new Run("f00"));
        host.Editor.Document.Blocks.Clear();
        host.Editor.Document.Blocks.Add(paragraph);

        host.Enable();

        Assert.Multiple(() =>
        {
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo("**bold**\n## #f00"));
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b && b.Color == Colors.Red), Is.EqualTo(1));
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.True);
            Assert.That(SizeAt(host.Editor, 9), Is.EqualTo(host.Editor.FontSize * 1.32));
            Assert.That(host.Drawings().Count(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.EqualTo(1),
                "The contiguous bold word should draw once even when its document content is split across runs.");
        });
    }

    [Test]
    public void Styling_NeverTouchesEmbeddedEditors_AndSurvivesFenceConversion()
    {
        using var host = new Host("# Before #f00\n```markdown\n## code **bold** #00ff00\n```\n## After #00f");
        host.Enable();
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(host.Editor), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(host.Editor).Single();
        var text = host.Editor.GetPlainText();
        host.Drain();

        var drawings = host.Drawings();
        Assert.Multiple(() =>
        {
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Colors.Lime), Is.Zero);
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Colors.Red), Is.EqualTo(1));
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Colors.Blue), Is.EqualTo(1));
            Assert.That(drawings.Count(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.GreaterThanOrEqualTo(2));
            Assert.That(drawings.Any(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.False);
            Assert.That(RichTextBoxExtensions.EnumerateEmbeddedRegions(host.Editor).Single(), Is.SameAs(region));
            Assert.That(region.Content, Is.EqualTo("## code **bold** #00ff00"));
            Assert.That(region.Language, Is.EqualTo("markdown"));
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
        });
    }

    [Test]
    public void Styling_RefreshesAfterEditsAndUndoRedo_WithoutExtraUndoUnits()
    {
        using var host = new Host("plain");
        host.Enable();
        host.Editor.SelectRange(0, 5);
        host.Editor.ReplaceSelection("**bold** #abc");
        host.Drain();
        Assert.That(host.Drawings(), Is.Not.Empty);

        host.Editor.Undo();
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo("plain"));
            Assert.That(host.Drawings(), Is.Empty);
            Assert.That(host.Editor.CanUndo, Is.False);
        });
        host.Editor.Redo();
        host.Drain();
        Assert.That(host.Editor.GetPlainText(), Is.EqualTo("**bold** #abc"));
        Assert.That(host.Drawings(), Is.Not.Empty);

        host.Editor.SetPlainText("unformatted");
        host.Drain();
        Assert.That(host.Drawings(), Is.Empty, "Replacing the document must discard stale token pointers.");
    }

    [Test]
    public void Styling_RepaintsAfterScrollingResizingFontAndThemeChanges()
    {
        using var host = new Host("# Heading #f00\n" + string.Join("\n", Enumerable.Repeat("plain line", 30)) + "\n# End #00f");
        host.Enable();
        Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Red), Is.True);
        host.Editor.ScrollToEnd();
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Red), Is.False);
            Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Blue), Is.True);
        });

        host.Editor.ScrollToHome();
        host.Editor.FontSize = 24;
        host.Editor.Width = 180;
        host.SetMarkerBrush(Brushes.Magenta);
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, Brushes.Magenta)), Is.True);
            Assert.That(host.Drawings().Any(d => d.Bounds.Height > 20), Is.True);
            Assert.That(host.Drawings().Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Red), Is.True);
        });
    }

    [Test]
    public void Styling_ReusesDocumentGeometryForSmallScrollsAndSelectionChanges()
    {
        using var host = new Host("## Heading **bold** #f00\n" +
            string.Join("\n", Enumerable.Repeat("plain line", 80)));
        host.Enable();
        _ = host.Drawings();
        var initialBuilds = host.Adorner.InkCacheBuildCount;

        host.Editor.ScrollToVerticalOffset(32);
        host.Drain();
        host.Editor.SelectRange(3, 7);
        host.Drain();
        _ = host.Drawings();

        Assert.That(host.Adorner.InkCacheBuildCount, Is.EqualTo(initialBuilds));
    }

    [Test]
    public void Styling_PlainSingleLineEdit_RetainsAccentsAndUnchangedGeometry()
    {
        const string text = "## Heading **bold** #f00\nplain middle line\n\"quoted below\" {name}";
        using var host = new Host(text);
        host.SetTheme("Light");
        host.Enable();
        var insertion = text.IndexOf("plain middle line", StringComparison.Ordinal) +
            "plain middle line".Length;
        host.Editor.SelectRange(insertion, 0);
        host.Drain();
        _ = host.Drawings();
        var inkBuilds = host.Adorner.InkCacheBuildCount;
        var glyphBuilds = host.Adorner.GlyphCacheBuildCount;
        var drawingBuilds = host.Adorner.DrawingBuildCount;

        host.Editor.ReplaceSelection(" extended");
        host.Editor.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.HeadingBrush)), Is.True,
                "The last complete highlighting frame above the edit must remain visible.");
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.True,
                "The last complete highlighting frame below the edit must remain visible.");
        });

        host.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(host.Adorner.GlyphCacheBuildCount, Is.EqualTo(glyphBuilds + 1),
                "Only the changed paragraph should rebuild its glyph index.");
            Assert.That(host.Adorner.InkCacheBuildCount, Is.EqualTo(inkBuilds),
                "An unwrapped plain-line edit must reuse native ink above and below it.");
            Assert.That(host.Adorner.DrawingBuildCount, Is.EqualTo(drawingBuilds),
                "Typing in unaffected plain text must replay the retained drawing, not its geometry.");
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.HeadingBrush)), Is.True);
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.QuoteBrush)), Is.True);
        });
    }

    [TestCase("Light", false, false)]
    [TestCase("Dark", false, false)]
    [TestCase("Light", true, false)]
    [TestCase("Dark", true, false)]
    [TestCase("Light", false, true)]
    [TestCase("Dark", false, true)]
    [TestCase("Light", true, true)]
    [TestCase("Dark", true, true)]
    public void Styling_ScrollingDelimitedText_KeepsEveryVisibleCharacterPainted(string theme, bool wrap, bool headings)
    {
        var text = string.Join("\n", Enumerable.Range(0, 80).Select(i =>
            (headings ? new string('#', i % 4 + 1) + " heading " : "") + "**bold** \"quote\" {name} `code`"));
        using var host = new Host(text);
        host.SetTheme(theme);
        if (wrap)
            host.Editor.Width = 190;
        host.Editor.SelectRange(text.Length, 0);
        host.Enable();
        foreach (var offset in new[] { 0, 32, 160, 480, 960, 480, 160, 32, 0, 960, 0 })
        {
            host.Editor.ScrollToVerticalOffset(offset);
            host.Editor.UpdateLayout();
            AssertVisibleCharactersPainted(host, $"pending scroll {offset}");
            host.Drain();
            AssertVisibleCharactersPainted(host, $"scroll {offset}");
            AssertVisibleCharactersPainted(host, $"accent scroll {offset}", accentsOnly: true);
        }
        host.SetTheme(theme == "Light" ? "Dark" : "Light");
        host.Editor.ScrollToVerticalOffset(480);
        host.Drain();
        AssertVisibleCharactersPainted(host, "theme switch while scrolling");
        host.Editor.SelectRange(text.Length, 0);
        host.Editor.ReplaceSelection("!");
        host.Drain();
        host.Editor.Undo();
        host.Editor.ScrollToVerticalOffset(160);
        host.Drain();
        AssertVisibleCharactersPainted(host, "edit/undo followed by scrolling");
    }

    private static void AssertVisibleCharactersPainted(Host host, string context, bool accentsOnly = false)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.Decorator.ActualWidth),
            (int)Math.Ceiling(host.Decorator.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var origin = host.Editor.TranslatePoint(new Point(), host.Decorator);
        if (accentsOnly)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(host.Editor.Background, null, new Rect(host.Decorator.RenderSize));
                drawing.PushTransform(new TranslateTransform(origin.X, origin.Y));
                drawing.DrawDrawing(VisualTreeHelper.GetDrawing(host.Adorner));
                drawing.Pop();
            }
            bitmap.Render(visual);
        }
        else
        {
            bitmap.Render(host.Decorator);
        }
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var background = ((SolidColorBrush)host.Editor.Background).Color;
        var scrollViewer = (ScrollViewer)host.Editor.Template.FindName("PART_ContentHost", host.Editor);
        var presenter = (FrameworkElement)scrollViewer.Template.FindName("PART_ScrollContentPresenter", scrollViewer);
        var viewport = presenter.TransformToAncestor(host.Editor).TransformBounds(new Rect(presenter.RenderSize));
        var text = host.Editor.GetPlainText();
        var nativeCode = new bool[text.Length];
        foreach (var token in PromptTextTokenParser.Parse(text))
            if (token.Kind == PromptTextTokenKind.InlineCodeContent)
                Array.Fill(nativeCode, true, token.Start, token.Length);
        var checkedCells = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) || accentsOnly && nativeCode[i])
                continue;
            var start = host.Editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward);
            var end = host.Editor.GetTextPointerAt(i + 1).GetCharacterRect(LogicalDirection.Backward);
            if (start.Top < viewport.Top || start.Bottom > viewport.Bottom)
                continue;
            var left = (int)Math.Ceiling(Math.Min(start.Left, end.Left));
            var right = (int)Math.Floor(Math.Max(start.Left, end.Left));
            if (left < 0 || right >= bitmap.PixelWidth || right <= left)
                continue;
            var visiblePixels = 0;
            for (var y = (int)Math.Ceiling(start.Top); y < (int)Math.Floor(start.Bottom); y++)
                for (var x = left; x < right; x++)
                {
                    var index = (y + (int)origin.Y) * stride + (x + (int)origin.X) * 4;
                    var color = Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
                    if (ContrastRatio(color, background) >= 2)
                        visiblePixels++;
                }
            Assert.That(visiblePixels, Is.GreaterThanOrEqualTo(2),
                $"{context}: missing visible ink at offset {i} ('{text[i]}'), cell {start}.");
            checkedCells++;
        }
        Assert.That(checkedCells, Is.GreaterThan(20), "The viewport must contain delimited text.");
    }

    [Test]
    public void Styling_IsOptIn_AndReloadDoesNotDuplicateAdorners()
    {
        using var host = new Host("#abc");
        Assert.That(host.Layer.GetAdorners(host.Editor), Is.Null.Or.Empty);
        host.Enable();
        var first = host.Adorner;
        host.Decorator.Child = null;
        host.Drain();
        Assert.That(host.Layer.GetAdorners(host.Editor), Is.Null.Or.Empty);

        host.Decorator.Child = host.Editor;
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Layer.GetAdorners(host.Editor), Has.Length.EqualTo(1));
            Assert.That(host.Adorner, Is.Not.SameAs(first));
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0xAA, 0xBB, 0xCC)), Is.EqualTo(1));
        });

        host.Editor.SetPlainText("#def");
        PromptTextStyling.SetIsEnabled(host.Editor, false);
        host.Drain();
        Assert.That(host.Layer.GetAdorners(host.Editor), Is.Null.Or.Empty);
    }

    [Test]
    public void Styling_FontFamilyChange_RepositionsMarkersWithoutEditingText()
    {
        using var host = new Host("prefix text\n## heading");
        host.SetTheme("Light");
        host.Enable();
        host.Editor.FontFamily = new FontFamily("Consolas");
        host.Drain();

        AssertColoredCells(host, host.HeadingDelimiterBrush, i => i is 12 or 13);
    }

    [TestCase(12)]
    [TestCase(16)]
    [TestCase(24)]
    public void Styling_HexHash_IsEightyFourPercentTopRightAligned_WithDoubleHeightSwatch(double fontSize)
    {
        using var host = new Host("color #ff5366");
        host.Editor.FontSize = fontSize;
        host.Drain();
        var originalText = host.Editor.GetPlainText();
        var positions = Enumerable.Range(0, originalText.Length)
            .Select(i => host.Editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)).ToArray();
        host.Enable();
        var start = positions[6];
        var end = host.Editor.GetTextPointerAt(7).GetCharacterRect(LogicalDirection.Backward);
        var cell = new Rect(start.Left, start.Top, end.Left - start.Left, start.Height);
        var drawing = VisualTreeHelper.GetDrawing(host.Adorner)!;
        var scale = Groups(drawing).Select(g => g.Transform).OfType<ScaleTransform>().Single();
        var scaledCell = scale.TransformBounds(cell);
        var previousCell = new ScaleTransform(0.7, 0.7, cell.Left, cell.Top).TransformBounds(cell);
        var chip = host.Drawings().Single(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0xff, 0x53, 0x66));
        Assert.Multiple(() =>
        {
            Assert.That(scale.ScaleX, Is.EqualTo(0.84));
            Assert.That(scale.ScaleY, Is.EqualTo(0.84));
            Assert.That(scale.CenterX, Is.EqualTo(cell.Right).Within(0.01));
            Assert.That(scale.CenterY, Is.EqualTo(cell.Top).Within(0.01));
            Assert.That(scaledCell.Height, Is.EqualTo(previousCell.Height * 1.2).Within(0.01));
            Assert.That(scaledCell.Top, Is.EqualTo(cell.Top).Within(0.01));
            Assert.That(scaledCell.Bottom, Is.LessThan(cell.Bottom), "The hash must remain superscripted.");
            Assert.That(scaledCell.Right, Is.EqualTo(cell.Right).Within(0.01));
            Assert.That(scaledCell.Right, Is.GreaterThan(previousCell.Right), "Reduce the gap before the hex digits.");
            Assert.That(scaledCell.Left, Is.EqualTo(cell.Left + cell.Width * 0.16).Within(0.01));
            Assert.That(chip.Geometry.Bounds.Height, Is.EqualTo(6));
            Assert.That(chip.Geometry.Bounds.Bottom, Is.EqualTo(cell.Bottom).Within(0.01));
            Assert.That(chip.Geometry.Bounds.Left, Is.EqualTo(cell.Left).Within(0.01));
            Assert.That(chip.Geometry.Bounds.Width, Is.EqualTo(cell.Width).Within(0.01));
            Assert.That(Groups(drawing).Single(g => ReferenceEquals(g.Transform, scale)).Children, Is.Not.Empty,
                "The smaller hash must contain native glyph ink, not just an empty transform.");
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(originalText));
            Assert.That(Enumerable.Range(0, originalText.Length)
                .Select(i => host.Editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)), Is.EqualTo(positions));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [TestCase("**bold**", 2, 4)]
    [TestCase("__bold__", 2, 4)]
    [TestCase("**first\nsecond**", 2, 12)]
    public void Styling_BoldContent_UsesNativeInk_WithoutReformattingRuns(string text, int start, int length)
    {
        using var host = new Host("");
        var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0) };
        host.Editor.Document.Blocks.Clear();
        host.Editor.Document.Blocks.Add(paragraph);
        host.Drain();
        var positions = Enumerable.Range(0, text.Length)
            .Select(i => host.Editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)).ToArray();
        host.Enable();
        var bold = host.Drawings().Where(d => d.Pen?.Thickness == host.Editor.FontSize / 24).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(bold, Is.Not.Empty);
            Assert.That(host.Drawings().Any(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.False);
            Assert.That(((Run)paragraph.Inlines.FirstInline).FontWeight, Is.EqualTo(FontWeights.Normal));
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo(text));
            Assert.That(Enumerable.Range(0, text.Length)
                .Select(i => host.Editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward)), Is.EqualTo(positions));
            var clips = Groups(VisualTreeHelper.GetDrawing(host.Adorner)!).Where(g => g.ClipGeometry is RectangleGeometry)
                .Select(g => g.ClipGeometry.Bounds).ToArray();
            Assert.That(clips.Any(r => r.Left == positions[start].Left && r.Top == positions[start].Top), Is.True);
        });
        host.Editor.SelectRange(start, length);
        host.Drain();
        Assert.That(host.Editor.GetSelectedText(), Is.EqualTo(text.Substring(start, length)));
        Assert.That(host.Drawings().Any(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.False,
            "Native selected text must not be overpainted with normal foreground ink.");
    }

    [Test]
    public void Styling_BoldContent_CoalescesEachVisibleUnselectedLineSpan()
    {
        using var host = new Host("**abcdef**");
        host.Enable();
        Assert.That(host.Drawings().Count(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.EqualTo(1));

        // Selecting the middle two letters leaves two disjoint native-ink spans. They must not be
        // merged across the selection because WPF owns the selected glyph rendering.
        host.Editor.SelectRange(4, 2);
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(host.Editor.GetSelectedText(), Is.EqualTo("cd"));
            Assert.That(host.Drawings().Count(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.EqualTo(2));
        });
    }

    [Test]
    public void Styling_OverlappingBoldAndColors_ArePaintedOnce_AcrossSplitRunsAndLineBreaks()
    {
        using var host = new Host("");
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("**outer __inner #"));
        paragraph.Inlines.Add(new Run("ff5366 color__"));
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("tail** #abc"));
        host.Editor.Document.Blocks.Clear();
        host.Editor.Document.Blocks.Add(paragraph);
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0xff, 0x53, 0x66)), Is.EqualTo(1));
            Assert.That(host.Drawings().Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0xaa, 0xbb, 0xcc)), Is.EqualTo(1));
            Assert.That(host.Drawings().Any(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.True);
        });
    }

    [Test]
    public void Styling_SelectedHash_LeavesNativeSelectionInk_AndRestoresOnDeselect()
    {
        using var host = new Host("color #ff5366");
        host.Enable();
        host.Editor.SelectRange(6, 7);
        host.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(Groups(VisualTreeHelper.GetDrawing(host.Adorner)!).Any(g => g.Transform is ScaleTransform), Is.False);
            Assert.That(host.Editor.GetSelectedText(), Is.EqualTo("#ff5366"));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
        host.Editor.SelectRange(0, 0);
        host.Drain();
        Assert.That(Groups(VisualTreeHelper.GetDrawing(host.Adorner)!).Any(g => g.Transform is ScaleTransform), Is.True);
    }

    [TestCase(6)]
    [TestCase(7)]
    public void Styling_HashAndSwatch_NeverPaintOverNativeCaret(int offset)
    {
        using var host = new Host("color #ff5366");
        host.Editor.SelectRange(offset, 0);
        host.Enable();
        var caret = host.Editor.CaretPosition.GetCharacterRect(host.Editor.CaretPosition.LogicalDirection);
        var clip = Groups(VisualTreeHelper.GetDrawing(host.Adorner)!)
            .Select(g => g.ClipGeometry).OfType<CombinedGeometry>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.GeometryCombineMode, Is.EqualTo(GeometryCombineMode.Exclude));
            Assert.That(clip.FillContains(new Point(caret.Left, caret.Top + caret.Height / 2)), Is.False);
            Assert.That(host.Editor.GetCaretOffset(), Is.EqualTo(offset));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [Test]
    public void Styling_NativeInk_RefreshesAfterFontThemeAndWrapChanges()
    {
        using var host = new Host("**bold content that wraps across lines** #ff5366");
        host.Enable();
        host.Editor.FontFamily = new FontFamily("Consolas");
        host.Editor.FontSize = 24;
        host.Editor.Width = 180;
        host.Editor.Foreground = Brushes.White;
        host.Editor.Background = Brushes.Black;
        host.Drain();

        var drawings = host.Drawings();
        var bold = drawings.Where(d => d.Pen?.Thickness == 1).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(bold, Is.Not.Empty);
            Assert.That(bold.All(d => d.Brush is SolidColorBrush b && b.Color == Colors.White), Is.True);
            Assert.That(bold.Select(d => d.Geometry.Bounds.Top).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(drawings.Any(d => d.Brush is SolidColorBrush b && b.Color == Colors.Black && d.Pen is null), Is.True,
                "The hash mask must track the host theme instead of painting the previous background.");
            Assert.That(host.Editor.GetPlainText(), Is.EqualTo("**bold content that wraps across lines** #ff5366"));
            Assert.That(host.Editor.CanUndo, Is.False);
        });
    }

    [TestCase(12)]
    [TestCase(24)]
    public void Styling_InlineCode_UsesNativeMonospaceMetricsAndInheritedSize(double size)
    {
        using var host = new Host("plain `WiWi` **bold**");
        var editor = host.Editor;
        editor.FontFamily = new FontFamily("Segoe UI");
        editor.IsUndoEnabled = false;
        editor.SelectRange(7, 4);
        var before = PromptUndoManager.CaptureSnapshot(editor);
        host.Enable();
        editor.FontSize = size;
        host.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(FontAt(editor, 7).Source, Is.EqualTo("Consolas"));
            Assert.That(FontAt(editor, 0), Is.EqualTo(editor.FontFamily));
            Assert.That(FontAt(editor, 6), Is.EqualTo(editor.FontFamily), "Opening delimiter is not code content.");
            Assert.That(FontAt(editor, 11), Is.EqualTo(editor.FontFamily), "Closing delimiter is not code content.");
            Assert.That(new TextRange(editor.GetTextPointerAt(7), editor.GetTextPointerAt(11))
                .GetPropertyValue(TextElement.FontSizeProperty), Is.EqualTo(size));
            Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(before));
            Assert.That(host.Drawings().Any(d => d.Pen?.Thickness == size / 24), Is.True,
                "Bold must retain its existing paint path next to natively formatted code.");
        });
        var cells = Enumerable.Range(7, 4).Select(offset =>
        {
            var start = editor.GetTextPointerAt(offset).GetCharacterRect(LogicalDirection.Forward);
            var end = editor.GetTextPointerAt(offset + 1).GetCharacterRect(LogicalDirection.Backward);
            return new Rect(start.Left, start.Top, end.Left - start.Left, start.Height);
        }).ToArray();
        Assert.That(cells[0].Width, Is.GreaterThan(0));
        foreach (var cell in cells)
            Assert.That(cell.Width, Is.EqualTo(cells[0].Width).Within(0.01), "W and i must have equal native advances.");
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            var pointer = editor.GetPositionFromPoint(new Point(cell.Left + cell.Width * 0.2, cell.Top + cell.Height / 2), true)!;
            Assert.That(editor.GetCharOffset(pointer), Is.EqualTo(7 + i), "Native hit testing must follow the code glyphs.");
        }
        for (var offset = 0; offset <= before.SerializedText.Length; offset++)
            Assert.That(editor.GetCharOffset(editor.GetTextPointerAt(offset)), Is.EqualTo(offset));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Styling_InlineCode_PreservesSelectionDirectionAndResetsRemovedDelimiters(bool backward)
    {
        using var host = new Host("before `code` after");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        editor.Selection.Select(editor.GetTextPointerAt(backward ? 12 : 8), editor.GetTextPointerAt(backward ? 8 : 12));
        var before = PromptUndoManager.CaptureSnapshot(editor);
        host.Enable();
        Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(before));
        editor.SelectRange(12, 1);
        editor.ReplaceSelection("");
        host.Drain();
        Assert.That(FontAt(editor, 8), Is.EqualTo(editor.FontFamily));
        editor.SelectRange(12, 0);
        editor.ReplaceSelection("`");
        host.Drain();
        Assert.That(FontAt(editor, 8).Source, Is.EqualTo("Consolas"));
        editor.SelectRange(8, 4);
        editor.ReplaceSelection("new");
        host.Drain();
        Assert.That(editor.GetPlainText(), Is.EqualTo("before `new` after"));
        Assert.That(FontAt(editor, 8).Source, Is.EqualTo("Consolas"));
        editor.SelectRange(12, 0);
        editor.ReplaceSelection("plain");
        host.Drain();
        Assert.That(FontAt(editor, 12), Is.EqualTo(editor.FontFamily), "Typing outside code must not leak its font.");
        editor.FontFamily = new FontFamily("Arial");
        host.Drain();
        Assert.That(FontAt(editor, 12), Is.EqualTo(editor.FontFamily));
        Assert.That(FontAt(editor, 8).Source, Is.EqualTo("Consolas"));
    }

    [Test]
    public void Styling_InlineCode_HandlesSplitRunsLineBreaksAndInlineUi()
    {
        using var host = new Host("");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        var inlineUi = new InlineUIContainer(new Border { Width = 8, Height = 8 });
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("`fi"));
        paragraph.Inlines.Add(inlineUi);
        paragraph.Inlines.Add(new Run("rst"));
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("second` plain"));
        editor.Document.Blocks.Clear();
        editor.Document.Blocks.Add(paragraph);
        host.Enable();
        Assert.Multiple(() =>
        {
            Assert.That(editor.GetPlainText(), Is.EqualTo("`first\nsecond` plain"));
            Assert.That(FontAt(editor, 1).Source, Is.EqualTo("Consolas"));
            Assert.That(FontAt(editor, 7).Source, Is.EqualTo("Consolas"));
            Assert.That(FontAt(editor, 14), Is.EqualTo(editor.FontFamily));
            Assert.That(paragraph.Inlines.Contains(inlineUi), Is.True);
        });
        editor.Width = 100;
        host.Drain();
        Assert.That(editor.GetCharOffset(editor.GetTextPointerAt(10)), Is.EqualTo(10));
    }

    [Test]
    public void Styling_InlineCode_PreservesSharedUndoRedoAndTypingCoalescing()
    {
        using var host = new Host("`code");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        editor.SelectRange(5, 0);
        var history = new PromptUndoHistory(editor);
        history.CaptureBaseline();
        editor.TextChanged += (_, _) =>
        {
            if (!PromptTextStyling.IsApplyingFormatting(editor))
                history.PushSnapshot();
        };
        host.Enable();
        Assert.That(history.Count, Is.EqualTo(1), "Initial formatting must not become an edit.");
        editor.ReplaceSelection("`");
        host.Drain();
        editor.SelectRange(5, 0);
        editor.ReplaceSelection("1");
        host.Drain();
        editor.SelectRange(6, 0);
        editor.ReplaceSelection("2");
        host.Drain();
        Assert.That(history.Count, Is.EqualTo(2), "Formatting must not break a typing burst.");
        var edited = editor.GetPlainText();
        Assert.That(edited, Is.EqualTo("`code12`"));
        Assert.That(history.Undo(), Is.True);
        host.Drain();
        Assert.That(editor.GetPlainText(), Is.EqualTo("`code"));
        Assert.That(history.CanUndo, Is.False);
        Assert.That(FontAt(editor, 1), Is.EqualTo(editor.FontFamily));
        Assert.That(history.CanRedo, Is.True, "Deferred formatting must preserve the redo tail.");
        Assert.That(history.Redo(), Is.True);
        host.Drain();
        Assert.That(editor.GetPlainText(), Is.EqualTo(edited));
        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(FontAt(editor, 1).Source, Is.EqualTo("Consolas"));
        Assert.That(editor.IsUndoEnabled, Is.False);
    }

    [Test]
    public void Styling_InlineCode_DisablingRestoresInheritedFont_AndReloadReappliesCodeFont()
    {
        using var host = new Host("`code` plain");
        var editor = host.Editor;
        editor.IsUndoEnabled = false;
        host.Enable();
        Assert.That(FontAt(editor, 1).Source, Is.EqualTo("Consolas"));
        var changes = 0;
        editor.TextChanged += (_, _) => changes++;
        host.Adorner.RefreshTokens();
        host.Drain();
        Assert.That(changes, Is.Zero, "Unchanged font ranges should not be reformatted.");

        PromptTextStyling.SetIsEnabled(editor, false);
        editor.FontFamily = new FontFamily("Arial");
        host.Drain();
        Assert.That(FontAt(editor, 1), Is.EqualTo(editor.FontFamily));
        Assert.That(FontAt(editor, 8), Is.EqualTo(editor.FontFamily));
        host.Enable();
        Assert.That(FontAt(editor, 1).Source, Is.EqualTo("Consolas"));
        Assert.That(FontAt(editor, 8), Is.EqualTo(editor.FontFamily));
        Assert.That(editor.GetPlainText(), Is.EqualTo("`code` plain"));
    }

    private static FontFamily FontAt(RichTextBox editor, int offset) =>
        (FontFamily)new TextRange(editor.GetTextPointerAt(offset), editor.GetTextPointerAt(offset + 1))
            .GetPropertyValue(TextElement.FontFamilyProperty);

    private static double SizeAt(RichTextBox editor, int offset) =>
        (double)new TextRange(editor.GetTextPointerAt(offset), editor.GetTextPointerAt(offset + 1))
            .GetPropertyValue(TextElement.FontSizeProperty);

    private static Rect[] QuoteClips(Host host) => ColoredClips(host, host.QuoteBrush);

    private static Rect[] ColoredClips(Host host, Brush brush) =>
        Groups(VisualTreeHelper.GetDrawing(host.Adorner)!)
            .Where(g => g.ClipGeometry is RectangleGeometry &&
                !Groups(g).Skip(1).Any(child => child.ClipGeometry is RectangleGeometry) &&
                Groups(g).SelectMany(child => child.Children).OfType<GeometryDrawing>()
                    .Any(d => ReferenceEquals(d.Brush, brush)))
            .Select(g => g.ClipGeometry.Bounds).ToArray();

    private static void AssertQuoteCells(Host host, Func<int, bool> expected)
        => AssertColoredCells(host, host.QuoteBrush, expected);

    private static void AssertPlaceholderCells(Host host, Func<int, bool> expected)
    {
        var text = host.Editor.GetPlainText();
        AssertColoredCells(host, host.PlaceholderBrush, i => expected(i) && text[i] is not ('{' or '}'));
        AssertColoredCells(host, host.PlaceholderDelimiterBrush, i => expected(i) && text[i] is '{' or '}');
    }

    private static void AssertColoredCells(Host host, Brush brush, Func<int, bool> expected)
    {
        // Ink geometry may contain an entire native glyph run; its paint clip, not raw bounds,
        // determines which character cells actually receive the quote foreground.
        var clips = ColoredClips(host, brush);
        var text = host.Editor.GetPlainText();
        for (var i = 0; i < text.Length; i++)
        {
            var start = host.Editor.GetTextPointerAt(i).GetCharacterRect(LogicalDirection.Forward);
            var end = host.Editor.GetTextPointerAt(i + 1).GetCharacterRect(LogicalDirection.Backward);
            var center = new Point((start.Left + end.Left) / 2, start.Top + start.Height / 2);
            Assert.That(clips.Any(r => r.Contains(center)), Is.EqualTo(expected(i)),
                $"{brush} paint coverage at offset {i} ('{text[i]}').");
        }
    }

    private static void AssertBlend(Color actual, Color foreground, Color background, double amount)
    {
        Assert.That(actual.A, Is.EqualTo(255));
        Assert.That((int)actual.R, Is.EqualTo(foreground.R * amount + background.R * (1 - amount)).Within(1));
        Assert.That((int)actual.G, Is.EqualTo(foreground.G * amount + background.G * (1 - amount)).Within(1));
        Assert.That((int)actual.B, Is.EqualTo(foreground.B * amount + background.B * (1 - amount)).Within(1));
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static IEnumerable<DrawingGroup> Groups(Drawing drawing)
    {
        if (drawing is not DrawingGroup group)
            yield break;
        yield return group;
        foreach (var child in group.Children)
            foreach (var item in Groups(child))
                yield return item;
    }

    private sealed class Host : IDisposable
    {
        internal RichTextBox Editor { get; } = new()
        {
            FontSize = 16,
            Padding = new Thickness(10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        internal Brush MarkerBrush { get; } = Brushes.SteelBlue;
        internal SolidColorBrush HeadingBrush => (SolidColorBrush)_window.FindResource("PromptHeadingText");
        internal SolidColorBrush HeadingDelimiterBrush => (SolidColorBrush)_window.FindResource("PromptHeadingDelimiter");
        internal SolidColorBrush QuoteBrush => (SolidColorBrush)_window.FindResource("PromptQuotedText");
        internal SolidColorBrush PlaceholderBrush => (SolidColorBrush)_window.FindResource("PromptPlaceholderText");
        internal SolidColorBrush QuoteDelimiterBrush => (SolidColorBrush)_window.FindResource("PromptQuoteDelimiter");
        internal SolidColorBrush PlaceholderDelimiterBrush => (SolidColorBrush)_window.FindResource("PromptPlaceholderDelimiter");
        internal SolidColorBrush SubtleBrush => (SolidColorBrush)_window.FindResource("SubtleText");
        internal SolidColorBrush BoldDelimiterBrush => (SolidColorBrush)_window.FindResource("PromptBoldDelimiter");
        internal AdornerDecorator Decorator { get; }
        internal AdornerLayer Layer => Decorator.AdornerLayer;
        internal PromptTextAdorner Adorner => Layer.GetAdorners(Editor)!.OfType<PromptTextAdorner>().Single();
        private readonly Window _window;

        internal Host(string text)
        {
            Editor.SetPlainText(text);
            Decorator = new AdornerDecorator { Child = Editor };
            _window = new Window
            {
                Content = Decorator, Width = 500, Height = 260,
                ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000,
            };
            _window.Resources["PromptHeadingDelimiter"] = MarkerBrush;
            _window.Resources["LabelText"] = Brushes.Black;
            _window.Show();
            Drain();
        }

        internal void Enable()
        {
            PromptTextStyling.SetIsEnabled(Editor, true);
            Drain();
        }

        internal void SetMarkerBrush(Brush brush) => _window.Resources["PromptHeadingDelimiter"] = brush;

        internal void SetQuoteBrush(Brush brush) => _window.Resources["PromptQuotedText"] = brush;

        internal void SetPlaceholderBrush(Brush brush) => _window.Resources["PromptPlaceholderText"] = brush;

        internal void SetBrush(string key, Brush brush) => _window.Resources[key] = brush;

        internal void SetTheme(string theme)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
                directory = directory.Parent;
            var source = Path.Combine(directory?.FullName ??
                throw new DirectoryNotFoundException("SquadDash source not found."), "SquadDash");
            using var stream = File.OpenRead(Path.Combine(source, "Themes", theme + ".xaml"));
            var resources = (ResourceDictionary)XamlReader.Load(stream);
            _window.Resources.Remove("LabelText");
            _window.Resources.Remove("PromptHeadingDelimiter");
            _window.Resources.MergedDictionaries.Clear();
            _window.Resources.MergedDictionaries.Add(resources);

            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var prompt = XDocument.Load(Path.Combine(source, "MainWindow.xaml"))
                .Descendants(presentation + "RichTextBox")
                .Single(e => (string?)e.Attribute(x + "Name") == "PromptTextBox");
            Assert.That((string?)prompt.Attribute("Background"), Is.EqualTo("{DynamicResource TextBoxBackground}"));
            Assert.That((string?)prompt.Attribute("Foreground"), Is.EqualTo("{DynamicResource LabelText}"));
            Editor.SetResourceReference(Control.BackgroundProperty, "TextBoxBackground");
            Editor.SetResourceReference(Control.ForegroundProperty, "LabelText");
            Drain();
        }

        internal void Drain()
        {
            _window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            _window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        internal List<GeometryDrawing> Drawings() => Flatten(VisualTreeHelper.GetDrawing(Adorner)).ToList();

        private static IEnumerable<GeometryDrawing> Flatten(Drawing? drawing)
        {
            if (drawing is GeometryDrawing geometry)
                yield return geometry;
            else if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var item in Flatten(child))
                        yield return item;
        }

        public void Dispose()
        {
            PromptTextStyling.SetIsEnabled(Editor, false);
            _window.Close();
            RichTextBoxExtensions.DisposeEmbeddedRegions(Editor);
        }
    }
}

using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PromptTextStylingTests
{
    [Test]
    public void Styling_DrawsMarkersAndRgbChips_WithoutChangingDocumentOrSelection()
    {
        using var host = new Host("## Heading **bold** #aBc #123456");
        var editor = host.Editor;
        editor.SelectRange(3, 7);
        var originalText = editor.GetPlainText();
        var blocks = editor.Document.Blocks.ToArray();
        var runs = editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).ToArray();
        var caret = editor.GetCaretOffset();
        var changes = 0;
        editor.TextChanged += (_, _) => changes++;

        host.Enable();

        var drawings = host.Drawings();
        Assert.Multiple(() =>
        {
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0xAA, 0xBB, 0xCC)), Is.EqualTo(1));
            Assert.That(drawings.Count(d => d.Brush is SolidColorBrush b && b.Color == Color.FromRgb(0x12, 0x34, 0x56)), Is.EqualTo(1));
            Assert.That(drawings.Count(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.EqualTo(4),
                "Only heading markers get a tint and underline; bold content is emboldened directly.");
            Assert.That(drawings.Count(d => d.Pen?.Thickness == editor.FontSize / 24), Is.EqualTo(4));
            Assert.That(drawings.All(d => d.Bounds.Width > 0 && d.Bounds.Height > 0), Is.True);
            Assert.That(editor.GetPlainText(), Is.EqualTo(originalText));
            Assert.That(editor.Document.Blocks.ToArray(), Is.EqualTo(blocks));
            Assert.That(editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).ToArray(), Is.EqualTo(runs));
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
            Assert.That(host.Drawings().Count(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.EqualTo(4));
            Assert.That(host.Drawings().Count(d => d.Pen?.Thickness == host.Editor.FontSize / 24), Is.EqualTo(4));
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
            Assert.That(drawings.Count(d => ReferenceEquals(d.Brush, host.MarkerBrush)), Is.EqualTo(6));
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
        host.Enable();
        host.Editor.FontFamily = new FontFamily("Consolas");
        host.Drain();

        var start = host.Editor.GetTextPointerAt(12).GetCharacterRect(LogicalDirection.Forward);
        var marker = host.Drawings().First(d => ReferenceEquals(d.Brush, host.MarkerBrush));
        Assert.That(marker.Geometry.Bounds.Left, Is.EqualTo(start.Left).Within(0.01));
        Assert.That(marker.Geometry.Bounds.Top, Is.EqualTo(start.Top).Within(0.01));
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
        internal Brush MarkerBrush { get; } = Brushes.CornflowerBlue;
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
            _window.Resources["DocumentLinkText"] = MarkerBrush;
            _window.Resources["LabelText"] = Brushes.Black;
            _window.Show();
            Drain();
        }

        internal void Enable()
        {
            PromptTextStyling.SetIsEnabled(Editor, true);
            Drain();
        }

        internal void SetMarkerBrush(Brush brush) => _window.Resources["DocumentLinkText"] = brush;

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

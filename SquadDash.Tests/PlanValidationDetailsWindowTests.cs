using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PlanValidationDetailsWindowTests
{
    [Test]
    public void FontZoom_MultipliesLiveEnvironmentSizeAcrossTheWindowAndPersists() =>
        WithWindow("Light", (window, store, fonts) =>
        {
            AssertFonts(window, 13);
            Assert.That(window.TryZoomFont(120, ModifierKeys.Control), Is.True);
            AssertFonts(window, 14.3);
            Assert.That(store.Load().ValidationDetailsFontMultiplier, Is.EqualTo(1.1));
            Assert.That(fonts["FontSizeBody"], Is.EqualTo(13.0), "Zoom must not change the environment.");

            fonts["FontSizeBody"] = 20.0;
            AssertFonts(window, 22);
            Assert.That(window.TryZoomFont(-120, ModifierKeys.Control), Is.True);
            AssertFonts(window, 20);

            window.TryZoomFont(120, ModifierKeys.Control);
            var reopened = new PlanValidationDetailsWindow(Presentation(), store);
            try
            {
                Assert.That(reopened.FontSize, Is.EqualTo(22).Within(0.001));
            }
            finally
            {
                reopened.Close();
            }
        });

    [Test]
    public void FontZoom_LeavesOrdinaryScrollingAloneAndClampsBothEnds() =>
        WithWindow("Dark", (window, store, _) =>
        {
            foreach (var modifiers in new[] { ModifierKeys.None, ModifierKeys.Shift, ModifierKeys.Control | ModifierKeys.Shift })
                Assert.That(window.TryZoomFont(120, modifiers), Is.False);
            Assert.That(window.TryZoomFont(0, ModifierKeys.Control), Is.False);
            Assert.That(window.FontSize, Is.EqualTo(13));

            for (var i = 0; i < 40; i++)
                window.TryZoomFont(120, ModifierKeys.Control);
            AssertFonts(window, 39);
            Assert.That(store.Load().ValidationDetailsFontMultiplier, Is.EqualTo(3));

            for (var i = 0; i < 40; i++)
                window.TryZoomFont(-120, ModifierKeys.Control);
            AssertFonts(window, 6.5);
            Assert.That(store.Load().ValidationDetailsFontMultiplier, Is.EqualTo(0.5));
        });

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Selection_UsesImportantTextAndIndependentHalfOpacityLayers(string theme) =>
        WithWindow(theme, (window, _, _) =>
        {
            var list = Descendants(window).OfType<ListBox>().Single();
            var passed = (ListBoxItem)list.Items[0];
            var failed = (ListBoxItem)list.Items[1];
            Assert.That(list.SelectedItem, Is.SameAs(failed));
            AssertSelection(window, failed, true);
            AssertSelection(window, passed, false);

            var evidence = Descendants(window).OfType<TextBox>().Single();
            Assert.That(evidence.Foreground, Is.SameAs(window.FindResource("ImportantText")));
            Assert.That(evidence.Text, Is.EqualTo("Failure evidence"));
            Assert.That(Descendants(window).OfType<TextBlock>().Single(t => t.Text == "Fail assertion").Foreground,
                Is.SameAs(window.FindResource("ImportantText")));

            list.SelectedItem = passed;
            Layout(window);
            AssertSelection(window, failed, false);
            AssertSelection(window, passed, true);
            Assert.That(evidence.Text, Is.EqualTo("Pass evidence"));

            var important = new SolidColorBrush(Colors.Magenta);
            window.Resources["ImportantText"] = important;
            Assert.That(AssertionText(passed).Foreground, Is.SameAs(important), "Selected text follows theme changes.");
            Assert.That(evidence.Foreground, Is.SameAs(important));
            Assert.That(Descendants(window).OfType<TextBlock>().Single(t => t.Text == "Pass assertion").Foreground,
                Is.SameAs(important));
            Assert.That(window.Resources.Contains(typeof(ListBoxItem)), Is.False, "Style belongs only to this list.");
        });

    private static void AssertSelection(PlanValidationDetailsWindow window, ListBoxItem item, bool selected)
    {
        item.ApplyTemplate();
        var fill = (Border)item.Template.FindName("SelectionFill", item);
        var border = (Border)item.Template.FindName("SelectionBorder", item);
        var glyph = ((Grid)item.Content).Children.OfType<TextBlock>().First();
        Assert.Multiple(() =>
        {
            Assert.That(AssertionText(item).Foreground,
                Is.SameAs(window.FindResource(selected ? "ImportantText" : "BodyText")));
            Assert.That(fill.Opacity, Is.EqualTo(0.5));
            Assert.That(border.Opacity, Is.EqualTo(0.5));
            Assert.That(fill.Visibility, Is.EqualTo(selected ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(border.Visibility, Is.EqualTo(fill.Visibility));
            Assert.That(fill.Background, Is.SameAs(window.FindResource("ChipSelectedSurface")));
            Assert.That(border.BorderBrush, Is.SameAs(window.FindResource("ChipSelectedBorder")));
            Assert.That(item.Opacity, Is.EqualTo(1));
            Assert.That(AssertionText(item).Opacity, Is.EqualTo(1));
            Assert.That(glyph.Foreground, Is.SameAs(window.FindResource(
                ((PlanValidationAssertionDetails)item.Tag).Passed == true ? "TaskSuccessText" : "PriorityCritical")));
        });
    }

    private static TextBlock AssertionText(ListBoxItem item) =>
        ((Grid)item.Content).Children.OfType<TextBlock>().Last();

    private static void AssertFonts(PlanValidationDetailsWindow window, double expected)
    {
        Layout(window);
        Assert.That(window.FontSize, Is.EqualTo(expected).Within(0.001));
        foreach (var text in Descendants(window).OfType<TextBlock>())
        {
            Assert.That(text.FontSize, Is.EqualTo(expected).Within(0.001), text.Text);
            Assert.That(text.FontFamily, Is.EqualTo(window.FontFamily), text.Text);
        }
        var evidence = Descendants(window).OfType<TextBox>().Single();
        Assert.That(evidence.FontSize, Is.EqualTo(expected).Within(0.001));
        Assert.That(evidence.FontFamily, Is.EqualTo(window.FontFamily));
    }

    private static void WithWindow(string theme,
        Action<PlanValidationDetailsWindow, ApplicationSettingsStore, ResourceDictionary> assertion) =>
        WpfTestContext.Run(() =>
        {
            using var workspace = new TestWorkspace();
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary { ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0 };
            app.Resources.MergedDictionaries.Add(fonts);
            PlanValidationDetailsWindow? window = null;
            try
            {
                var store = new ApplicationSettingsStore(workspace.GetPath("settings.json"));
                window = new PlanValidationDetailsWindow(Presentation(), store);
                var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash", "Themes")))
                    directory = directory.Parent;
                Assert.That(directory, Is.Not.Null);
                using var stream = File.OpenRead(Path.Combine(directory!.FullName, "SquadDash", "Themes", $"{theme}.xaml"));
                window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
                Layout(window);
                assertion(window, store, fonts);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    private static void Layout(Window window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(980, 720));
        content.Arrange(new Rect(0, 0, 980, 720));
        content.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
        if (parent is Window window)
        {
            yield return (DependencyObject)window.Content;
            foreach (var child in Descendants((DependencyObject)window.Content))
                yield return child;
        }
    }

    private static PlanValidationDetailsPresentation Presentation() => new(
        "plan", "Plan", "validation", "Validation", "failed", "Summary", "0123456789", null,
        [new(1, "Pass assertion", true, "Pass evidence"), new(2, "Fail assertion", false, "Failure evidence")]);
}

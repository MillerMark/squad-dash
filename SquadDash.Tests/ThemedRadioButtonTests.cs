using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class ThemedRadioButtonTests {
    [TestCase("Dark", 12.0)]
    [TestCase("Light", 12.0)]
    [TestCase("Dark", 24.0)]
    [TestCase("Light", 24.0)]
    public void Click_ShowsFilledDotOnlyForSelectedRadio(string theme, double fontSize) {
        var panel = CreateGroup(theme, fontSize);
        var radios = panel.Children.Cast<ClickableRadioButton>().ToArray();
        Layout(panel);
        AssertSelected(radios, radios[0]);

        foreach (var radio in radios.Reverse()) {
            radio.PerformClick();
            Layout(panel);
            AssertSelected(radios, radio);
            radio.PerformClick();
            Layout(panel);
            AssertSelected(radios, radio);
        }
    }

    [Test]
    public void GapBetweenGlyphAndLabel_IsHitTestable() {
        var panel = CreateGroup("Dark", 16);
        var window = new Window {
            Content = panel, Width = 720, Height = 100,
            ShowActivated = false, ShowInTaskbar = false
        };
        try {
            window.Show();
            window.UpdateLayout();
            var radio = (RadioButton)panel.Children[0];
            var hit = radio.InputHitTest(new Point(radio.FontSize + 3, radio.ActualHeight / 2));
            Assert.That(hit, Is.Not.Null, "The gap between the ring and caption must still accept clicks.");
        }
        finally {
            window.Close();
        }
    }

    private static void AssertSelected(ClickableRadioButton[] radios, RadioButton selected) {
        Assert.That(radios.Count(radio => radio.IsChecked == true), Is.EqualTo(1));
        foreach (var radio in radios) {
            var dot = (Ellipse)radio.Template.FindName("InnerDot", radio);
            Assert.That(radio.IsChecked, Is.EqualTo(radio == selected));
            Assert.That(dot.Visibility, Is.EqualTo(radio == selected ? Visibility.Visible : Visibility.Collapsed));
            if (radio != selected) continue;
            Assert.Multiple(() => {
                Assert.That(dot.ActualWidth, Is.GreaterThan(0));
                Assert.That(dot.ActualHeight, Is.GreaterThan(0));
                Assert.That(dot.RenderedGeometry.Bounds.Width, Is.GreaterThan(0), "A visible element with zero geometry still draws no selection.");
                Assert.That(dot.RenderedGeometry.Bounds.Height, Is.GreaterThan(0));
                Assert.That(dot.Fill, Is.EqualTo(radio.FindResource("LabelText")));
            });
        }
    }

    private static WrapPanel CreateGroup(string theme, double fontSize) {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
            directory = directory.Parent;
        var source = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found."), "SquadDash");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var element = new XElement(XDocument.Load(Path.Combine(source, "App.xaml"))
            .Descendants(presentation + "Style").Single(style => (string?)style.Attribute(x + "Key") == "ThemedRadioButtonStyle"));
        element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        var panel = new WrapPanel();
        using var themeStream = File.OpenRead(Path.Combine(source, "Themes", theme + ".xaml"));
        panel.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(themeStream));
        panel.Resources["ThemedRadioButtonStyle"] = (Style)XamlReader.Parse(element.ToString());
        foreach (var type in Enum.GetValues<ButtonCommandType>()) {
            var radio = new ClickableRadioButton {
                Content = type.ToString(), Tag = type, GroupName = "CommandType",
                FontSize = fontSize, IsChecked = type == ButtonCommandType.PowerShell
            };
            radio.SetResourceReference(FrameworkElement.StyleProperty, "ThemedRadioButtonStyle");
            panel.Children.Add(radio);
        }
        return panel;
    }

    private static void Layout(FrameworkElement element) {
        element.Measure(new Size(720, 100));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }

    private sealed class ClickableRadioButton : RadioButton {
        internal void PerformClick() => OnClick();
    }
}

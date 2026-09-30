using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class AgentCardRoleTitleTests
{
    [TestCase(false, 11.0, 2)]
    [TestCase(true, 11.0, 1)]
    [TestCase(false, 22.0, 2)]
    [TestCase(true, 22.0, 1)]
    public void LongRole_RendersCappedLinesWithEllipsis(bool active, double fontSize, int lines)
    {
        var role = CreateRole(new RoleModel { IsInActivePanel = active }, fontSize);
        Layout(role);

        Assert.Multiple(() =>
        {
            Assert.That(role.ActualHeight, Is.EqualTo(role.LineHeight * lines).Within(0.01));
            Assert.That(HasEllipsis(role), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShortRole_DoesNotReserveExtraLinesOrRenderEllipsis(bool active)
    {
        var role = CreateRole(new RoleModel { RoleText = "Developer", IsInActivePanel = active });
        Layout(role);

        Assert.Multiple(() =>
        {
            Assert.That(role.ActualHeight, Is.EqualTo(role.LineHeight).Within(0.01));
            Assert.That(HasEllipsis(role), Is.False);
        });
    }

    [Test]
    public void PanelAndFontChanges_UpdateLineCap()
    {
        var model = new RoleModel();
        var role = CreateRole(model);
        Layout(role);
        Assert.That(role.ActualHeight, Is.EqualTo(role.LineHeight * 2).Within(0.01));

        model.IsInActivePanel = true;
        Layout(role);
        Assert.That(role.ActualHeight, Is.EqualTo(role.LineHeight).Within(0.01));

        role.Resources["FontSizeSmall"] = 22.0;
        Layout(role);
        Assert.That(role.ActualHeight, Is.EqualTo(22 * 1.3).Within(0.01));

        model.IsInActivePanel = false;
        Layout(role);
        Assert.That(role.ActualHeight, Is.EqualTo(22 * 1.3 * 2).Within(0.01));
    }

    [TestCase("Dark", "#D4B880")]
    [TestCase("Light", "#6E5035")]
    public void Tooltip_ShowsFullRoleWithThemedTypography(string theme, string expectedColor)
    {
        var model = new RoleModel();
        var role = CreateRole(model);
        using var themeStream = File.OpenRead(Path.Combine(SourceDirectory(), "Themes", theme + ".xaml"));
        role.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(themeStream));
        role.Resources.Remove("AgentRoleText");
        Layout(role);
        var window = new Window
        {
            Content = role.Parent,
            Width = 170,
            Height = 100,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var tip = (ToolTip)role.ToolTip;
        try
        {
            window.Show();
            tip.PlacementTarget = role;
            tip.IsOpen = true;
            tip.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var content = (TextBlock)tip.Content;
            Assert.Multiple(() =>
            {
                Assert.That(content.Text, Is.EqualTo(model.RoleText));
                Assert.That(content.FontSize, Is.EqualTo(role.FontSize));
                Assert.That(content.Foreground, Is.EqualTo(role.Foreground));
                Assert.That(((SolidColorBrush)content.Foreground).Color.ToString(), Is.EqualTo("#FF" + expectedColor[1..]));
                Assert.That(content.TextTrimming, Is.EqualTo(TextTrimming.None));
                Assert.That(tip.Background, Is.EqualTo(role.FindResource("InputSurface")));
                Assert.That(tip.BorderBrush, Is.EqualTo(role.FindResource("InputBorder")));
            });

            role.Resources["FontSizeSmall"] = 22.0;
            role.Resources["AgentRoleText"] = Brushes.Coral;
            tip.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(content.FontSize, Is.EqualTo(22));
            Assert.That(content.Foreground, Is.EqualTo(Brushes.Coral));
        }
        finally
        {
            tip.IsOpen = false;
            window.Close();
        }
    }

    private static TextBlock CreateRole(RoleModel model, double fontSize = 11)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(SourceDirectory(), "MainWindow.xaml"));
        var element = new XElement(source.Descendants(presentation + "TextBlock")
            .Single(e => (string?)e.Attribute(x + "Name") == "RoleTextBlock"));
        element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        XNamespace tests = $"clr-namespace:SquadDash.Tests;assembly={typeof(AgentCardRoleTitleTests).Assembly.GetName().Name}";
        var wrapper = new XElement(presentation + "Border",
            new XAttribute(XNamespace.Xmlns + "tests", tests.NamespaceName),
            new XElement(presentation + "Border.Resources", new XElement(tests + nameof(AgentCardRoleResources))),
            element);
        var role = (TextBlock)((Border)XamlReader.Parse(wrapper.ToString())).Child;
        role.Resources["FontSizeSmall"] = fontSize;
        role.Resources["AgentRoleText"] = Brushes.Tan;
        var app = XDocument.Load(Path.Combine(SourceDirectory(), "App.xaml"));
        var tooltipStyle = app.Descendants(presentation + "Style")
            .Single(e => (string?)e.Attribute("TargetType") == "ToolTip");
        role.Resources[typeof(ToolTip)] = (Style)XamlReader.Parse(tooltipStyle.ToString());
        role.DataContext = model;
        return role;
    }

    private static void Layout(TextBlock role)
    {
        role.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        role.Measure(new Size(124, double.PositiveInfinity));
        role.Arrange(new Rect(new Point(), role.DesiredSize));
        role.UpdateLayout();
    }

    private static bool HasEllipsis(Visual visual)
    {
        return HasEllipsisDrawing(VisualTreeHelper.GetDrawing(visual));
    }

    private static bool HasEllipsisDrawing(Drawing drawing) => drawing switch
    {
        GlyphRunDrawing glyph => glyph.GlyphRun.GlyphIndices.Contains(
            glyph.GlyphRun.GlyphTypeface.CharacterToGlyphMap[0x2026]),
        DrawingGroup group => group.Children.Any(HasEllipsisDrawing),
        _ => false
    };

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found."), "SquadDash");
    }

    private sealed class RoleModel : INotifyPropertyChanged
    {
        private bool _isInActivePanel;
        public string RoleText { get; init; } = "WPF and user interface specialist responsible for agent cards and all application windows";
        public bool IsInActivePanel
        {
            get => _isInActivePanel;
            set
            {
                _isInActivePanel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInActivePanel)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

public sealed class AgentCardRoleResources : ResourceDictionary
{
    public AgentCardRoleResources()
    {
        Add("FractionConverter", new FractionConverter());
    }
}

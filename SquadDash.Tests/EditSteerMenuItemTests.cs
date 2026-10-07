using System.IO;
using System.Xml.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class EditSteerMenuItemTests
{
    [Test]
    public void Steer_IsNamedAndWiredImmediatelyBetweenSeparatorAndNewQueueToFront()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(SourcePath("MainWindow.xaml"));
        var steer = document.Descendants(presentation + "MenuItem")
            .Single(item => (string?)item.Attribute(x + "Name") == "EditSteerMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That((string?)steer.Attribute("Header"), Is.EqualTo("Steer"));
            Assert.That((string?)steer.Attribute("InputGestureText"), Is.EqualTo("Ctrl+Enter"));
            Assert.That((string?)steer.Attribute("Click"), Is.EqualTo("EditSteerMenuItem_Click"));
            Assert.That((string?)steer.Parent?.Attribute("Header"), Is.EqualTo("_Edit"));
            Assert.That(steer.ElementsBeforeSelf().Last().Name, Is.EqualTo(presentation + "Separator"));
            Assert.That((string?)steer.ElementsAfterSelf().First().Attribute(x + "Name"),
                Is.EqualTo("EditNewQueueToFrontMenuItem"));
            Assert.That(steer.Attribute("Command"), Is.Null, "The click handler alone should invoke steering.");
        });
    }

    [Test]
    public void Click_AwaitsExistingSteeringAndHandlesCallbackFailures()
    {
        var source = File.ReadAllText(SourcePath("MainWindow.xaml.cs"));
        var start = source.IndexOf("private async void EditSteerMenuItem_Click(", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var end = source.IndexOf("private void EditNewQueueToFrontMenuItem_Click(", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        var handler = source[start..end];

        Assert.That(handler, Does.Contain("await SteerActiveWorkAsync();")
            .And.Contain("catch (Exception ex)")
            .And.Contain("HandleUiCallbackException(nameof(EditSteerMenuItem_Click), ex);"));
    }

    private static string SourcePath(string fileName)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
            directory = directory.Parent;

        return Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found."),
            "SquadDash", fileName);
    }
}

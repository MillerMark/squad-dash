using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class WorkspaceInstallDiagnosticsWindowTests
{
    [Test]
    public void CustomReport_UsesRequestedTitleContentAndStandardCopyButton()
    {
        _ = Application.Current ?? new Application();
        var window = new WorkspaceInstallDiagnosticsWindow(
            "Plan Recovery Diagnostics",
            "Plan: Example\nStep 14a: Repair camera state");

        var textBox = FindDescendant<TextBox>(window);
        var copyButton = FindDescendant<CopyButton>(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.Title, Is.EqualTo("Plan Recovery Diagnostics"));
            Assert.That(textBox, Is.Not.Null);
            Assert.That(textBox!.Text, Does.Contain("Step 14a: Repair camera state"));
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(copyButton, Is.Not.Null,
                "diagnostic reports must use the application's standard copy control");
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } descendant)
                return descendant;
        }

        return null;
    }
}

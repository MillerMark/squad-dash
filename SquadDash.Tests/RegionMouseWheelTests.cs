using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash.Tests;

[TestFixture]
public sealed class RegionMouseWheelTests
{
    [TestCase(-120, 400, 500, 100, true)]
    [TestCase(-120, 200, 500, 100, false)]
    [TestCase(-120, 0, 500, 100, false)]
    [TestCase(120, 0, 500, 100, true)]
    [TestCase(120, 200, 500, 100, false)]
    [TestCase(120, 400, 500, 100, false)]
    [TestCase(-120, 0, 100, 100, true)]
    [TestCase(120, 0, 100, 100, true)]
    [TestCase(-120, 0, 50, 100, true)]
    [TestCase(120, 0, 50, 100, true)]
    [TestCase(-120, 0, 0, 0, true)]
    [TestCase(120, 0, 0, 0, true)]
    [TestCase(-120, 399.5, 500, 100, true)]
    [TestCase(-120, 399.4, 500, 100, false)]
    [TestCase(120, 0.5, 500, 100, true)]
    [TestCase(120, 0.6, 500, 100, false)]
    [TestCase(0, 0, 500, 100, false)]
    [TestCase(0, 0, 0, 0, false)]
    public void ShouldBubbleMouseWheel_OnlyAtDirectionalEdge(
        int delta, double verticalOffset, double extentHeight, double viewportHeight, bool expected)
    {
        Assert.That(AvalonEditRegionEditor.ShouldBubbleMouseWheel(
            delta, verticalOffset, extentHeight, viewportHeight), Is.EqualTo(expected));
    }

    [TestCase(-120)]
    [TestCase(120)]
    [Apartment(ApartmentState.STA)]
    public void NonScrollableEditor_RaisesEquivalentWheelOnVisualParent(int delta)
    {
        using var editor = new AvalonEditRegionEditor(string.Empty);
        var parent = new Border { Child = editor.Control };
        var ancestor = new Border { Child = parent };
        MouseWheelEventArgs? forwarded = null;
        var receivedCount = 0;
        ancestor.MouseWheel += (_, e) =>
        {
            forwarded = e;
            receivedCount++;
        };
        var original = new MouseWheelEventArgs(Mouse.PrimaryDevice, 123, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };

        editor.Control.RaiseEvent(original);

        Assert.Multiple(() =>
        {
            Assert.That(original.Handled, Is.True);
            Assert.That(receivedCount, Is.EqualTo(1));
            Assert.That(forwarded, Is.Not.Null);
            Assert.That(forwarded?.RoutedEvent, Is.EqualTo(UIElement.MouseWheelEvent));
            Assert.That(forwarded?.Source, Is.SameAs(editor.Control));
            Assert.That(forwarded?.MouseDevice, Is.SameAs(original.MouseDevice));
            Assert.That(forwarded?.Timestamp, Is.EqualTo(original.Timestamp));
            Assert.That(forwarded?.Delta, Is.EqualTo(delta));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Dispose_UnhooksPreviewMouseWheel()
    {
        using var editor = new AvalonEditRegionEditor(string.Empty);
        var parent = new Border { Child = editor.Control };
        var receivedCount = 0;
        parent.MouseWheel += (_, _) => receivedCount++;
        editor.Dispose();
        var original = new MouseWheelEventArgs(Mouse.PrimaryDevice, 123, -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };

        editor.Control.RaiseEvent(original);

        Assert.Multiple(() =>
        {
            Assert.That(original.Handled, Is.False);
            Assert.That(receivedCount, Is.Zero);
        });
    }
}

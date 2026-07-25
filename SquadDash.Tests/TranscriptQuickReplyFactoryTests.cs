using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TranscriptQuickReplyFactoryTests
{
    [Test]
    public void CreateButton_UsesTranscriptFontAndSharedGeometry()
    {
        _ = Application.Current ?? new Application();

        var button = TranscriptQuickReplyFactory.CreateButton("Run plan", 19, tag: "payload");

        Assert.Multiple(() =>
        {
            Assert.That(button.FontSize, Is.EqualTo(19));
            Assert.That(button.Padding, Is.EqualTo(new Thickness(10, 4, 10, 4)));
            Assert.That(button.Margin, Is.EqualTo(new Thickness(0, 0, 8, 8)));
            Assert.That(button.MinHeight, Is.EqualTo(28));
            Assert.That(button.Tag, Is.EqualTo("payload"));
        });
    }

    [Test]
    public void CreateContainer_MarksPlanButtonsForLaterTranscriptZoomUpdates()
    {
        _ = Application.Current ?? new Application();
        var panel = new WrapPanel();
        var button = TranscriptQuickReplyFactory.CreateButton("Run plan", 14);
        panel.Children.Add(button);

        var container = TranscriptQuickReplyFactory.CreateContainer(panel);
        var buttons = TranscriptQuickReplyFactory.EnumerateButtons(container.Child).ToArray();

        Assert.That(TranscriptQuickReplyFactory.IsQuickReplyContainer(container), Is.True);
        Assert.That(buttons, Is.EqualTo(new[] { button }));
    }
}

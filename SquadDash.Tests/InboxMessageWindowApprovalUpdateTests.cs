using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxMessageWindowApprovalUpdateTests
{
    [Test]
    public void AttachApprovalUpdatingOverlay_UsesMessageLayoutRootAndIsIdempotent() =>
        WpfTestContext.Run(() =>
        {
            var chromeRoot = new Grid();
            var messageLayoutRoot = new Grid();
            messageLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            messageLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            messageLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            messageLayoutRoot.RowDefinitions.Add(new RowDefinition());
            var contentBorder = new Border { Child = messageLayoutRoot };
            chromeRoot.Children.Add(contentBorder);
            var overlay = new Border();

            InboxMessageWindow.AttachApprovalUpdatingOverlay(messageLayoutRoot, overlay);
            InboxMessageWindow.AttachApprovalUpdatingOverlay(messageLayoutRoot, overlay);

            Assert.Multiple(() =>
            {
                Assert.That(messageLayoutRoot.Children.Contains(overlay), Is.True);
                Assert.That(Grid.GetRow(overlay), Is.EqualTo(2));
                Assert.That(messageLayoutRoot.Children, Has.Count.EqualTo(1));
                Assert.That(chromeRoot.Children.Contains(overlay), Is.False,
                    "The update overlay belongs to the inner message grid, not the chrome grid.");
            });
        });
}

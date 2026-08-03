using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxMessageWindowApprovalUpdateTests
{
    [Test]
    public void ApplyInteractiveFontSize_UsesBodySizeForActionsAndAttachments() =>
        WpfTestContext.Run(() =>
        {
            var actions = new WrapPanel();
            var action = new Button { FontSize = 9 };
            actions.Children.Add(action);
            var attachments = new WrapPanel();
            var label = new TextBlock { FontSize = 9 };
            attachments.Children.Add(new Border { Child = label });

            InboxMessageWindow.ApplyInteractiveFontSize(actions, attachments, 19);

            Assert.Multiple(() =>
            {
                Assert.That(action.FontSize, Is.EqualTo(19));
                Assert.That(label.FontSize, Is.EqualTo(19));
            });
        });

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

    [Test]
    public void CompleteApprovalUpdate_ClearsVisibleProgressState() =>
        WpfTestContext.Run(() =>
        {
            var before = new InboxMessage
            {
                Id = "approval-1",
                Subject = "Approval needed",
                Body = "Review this work.",
                Actions = [],
            };
            var after = before with
            {
                Subject = "Approved",
                Body = "Approval complete.",
                Actions = [],
            };
            var window = new InboxMessageWindow(before, (_, _) => { });

            window.BeginApprovalUpdate();
            Assert.That(window.IsApprovalUpdateInProgress, Is.True);

            window.CompleteApprovalUpdate(after, (_, _) => { });
            Assert.That(window.IsApprovalUpdateInProgress, Is.False);
        });
}

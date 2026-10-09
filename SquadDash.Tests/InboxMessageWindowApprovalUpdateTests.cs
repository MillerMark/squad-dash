using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxMessageWindowApprovalUpdateTests
{
    [Test]
    public void BuildInboxDetailsClipboardText_CopiesExactlyTwoLabeledLines()
    {
        Assert.That(
            InboxMessageWindow.BuildInboxDetailsClipboardText("X", @"C:\Workspace\.squad\inbox\Y.json"),
            Is.EqualTo($"Subject: X{Environment.NewLine}File: C:\\Workspace\\.squad\\inbox\\Y.json"));
    }

    [Test]
    public void SubjectContextMenu_HasOnlyCopyMessageDetailsAndPreservesCaptionGestures() =>
        WpfTestContext.Run(() =>
        {
            var message = new InboxMessage { Id = "copy-details", Subject = "Subject", Body = "Body", Actions = [] };
            var resolverCalled = false;
            var window = new InboxMessageWindow(message, (_, _) => Task.FromResult(true),
                resolveMessageFilePath: id =>
                {
                    resolverCalled = true;
                    return $@"C:\Workspace\.squad\inbox\{id}.json";
                });
            try
            {
                var chrome = (Grid)window.Content;
                var root = (Grid)((Border)chrome.Children[0]).Child;
                var header = root.Children.OfType<DockPanel>().Single();
                var subject = header.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First();
                var menu = subject.ContextMenu!;
                var item = menu.Items.OfType<MenuItem>().Single();
                var menuStyle = new Style(typeof(ContextMenu));
                var itemStyle = new Style(typeof(MenuItem));
                menu.Resources["ThemedContextMenuStyle"] = menuStyle;
                item.Resources["ThemedMenuItemStyle"] = itemStyle;

                Assert.Multiple(() =>
                {
                    Assert.That(subject.Text, Is.EqualTo(message.Subject));
                    Assert.That(subject.FontWeight, Is.EqualTo(FontWeights.Bold));
                    Assert.That(menu.Items, Has.Count.EqualTo(1));
                    Assert.That(item.Header, Is.EqualTo("Copy Message Details"));
                    Assert.That(menu.Style, Is.SameAs(menuStyle));
                    Assert.That(item.Style, Is.SameAs(itemStyle));
                    Assert.That(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(subject), Is.False);
                    Assert.That(resolverCalled, Is.False, "Resolve the path only when the copy action is invoked.");
                });
            }
            finally
            {
                window.Close();
            }
        });

    [Test]
    public void BuildMessageClipboardText_CopiesRenderedBody() =>
        WpfTestContext.Run(() =>
    {
        var message = new InboxMessage
        {
            Id = "copy-1",
            Subject = "Subject",
            Body = "Message body.  \r\n",
            Actions = [],
        };

        var document = MarkdownFlowDocumentBuilder.BuildInbox(message.Body);
        Assert.That(InboxMessageWindow.BuildMessageClipboardText(message, document), Is.EqualTo("Message body." + Environment.NewLine));
    });

    [Test]
    public void AttachBodyLinkCopyMenus_LabelsExistingFileAndTargetsItsAbsolutePath() =>
        WpfTestContext.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "SquadDash-InboxMenu", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var file = Path.Combine(root, "runbook.md");
                File.WriteAllText(file, "test");
                var document = MarkdownFlowDocumentBuilder.BuildInbox(
                    "[Open runbook](app://open-path:runbook.md)",
                    13,
                    _ => { });

                InboxMessageWindow.AttachBodyLinkCopyMenus(document, root);

                var link = document.Blocks.OfType<Paragraph>()
                    .Single()
                    .Inlines.OfType<Hyperlink>()
                    .Single();
                var item = link.ContextMenu!.Items.OfType<MenuItem>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(item.Header, Is.EqualTo("Copy File Path"));
                    Assert.That(InboxLinkCopyTargetResolver.Resolve((string)link.Tag, root)?.ClipboardText,
                        Is.EqualTo(file));
                });
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });

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
    public void InlineApprovalRunbookLink_OpensTheBoundKitMessage() =>
        WpfTestContext.Run(() =>
        {
            var openedMessageId = string.Empty;
            var message = new InboxMessage
            {
                Id = "approval-with-runbook",
                Subject = "Approval needed",
                Body = $"[Open the validation instructions]({DurableApprovalRequestManager.ApprovalRunbookLink})",
                Actions = [],
                Attachments =
                [
                    new InboxAttachment
                    {
                        Type = DurableApprovalRequestManager.RunbookAttachmentType,
                        Label = DurableApprovalRequestManager.RunbookAttachmentLabel,
                        InboxMessageId = "validation-kit",
                    },
                ],
            };
            var window = new InboxMessageWindow(
                message,
                (_, _) => Task.FromResult(true),
                openInboxMessage: id => openedMessageId = id);
            try
            {
                window.OpenInlineBodyLink(DurableApprovalRequestManager.ApprovalRunbookLink);

                Assert.That(openedMessageId, Is.EqualTo("validation-kit"));
            }
            finally
            {
                window.Close();
            }
        });

    [Test]
    public void OpenApprovalReview_RemainsEnabledAfterOpening() =>
        WpfTestContext.Run(() =>
        {
            _ = Application.Current ?? new Application();
            var invocationCount = 0;
            var action = new InboxAction
            {
                Label = "Open approval review",
                RouteMode = DurableApprovalRequestManager.ApprovalReviewRouteMode,
            };
            var message = new InboxMessage
            {
                Id = "approval-navigation",
                Subject = "Approval needed",
                Body = "Review this checkpoint.",
                Actions = [action],
            };
            var window = new InboxMessageWindow(message, (_, _) => Task.FromResult(++invocationCount > 0));
            try
            {
                var chrome = (Grid)window.Content;
                var root = (Grid)((Border)chrome.Children[0]).Child;
                var actionRegion = root.Children.OfType<StackPanel>()
                    .Single(panel => Grid.GetRow(panel) == 2);
                var navigation = actionRegion.Children.OfType<WrapPanel>().First();
                var button = navigation.Children.OfType<Button>().Single();

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Multiple(() =>
                {
                    Assert.That(invocationCount, Is.EqualTo(1));
                    Assert.That(button.IsEnabled, Is.True);
                });
            }
            finally
            {
                window.Close();
            }
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
            var window = new InboxMessageWindow(before, (_, _) => Task.FromResult(true));

            window.BeginApprovalUpdate();
            Assert.That(window.IsApprovalUpdateInProgress, Is.True);

            window.CompleteApprovalUpdate(after, (_, _) => Task.FromResult(true));
            Assert.That(window.IsApprovalUpdateInProgress, Is.False);
        });

    [Test]
    public void SetDecisionActionsEnabled_LocksEveryRecoveryChoiceAsOneGroup() =>
        WpfTestContext.Run(() =>
        {
            _ = Application.Current ?? new Application();
            var message = new InboxMessage
            {
                Id = "recovery-1",
                Subject = "Plan recovery",
                Body = "Choose one recovery action.",
                Actions =
                [
                    new InboxAction { Label = "Assess with AI" },
                    new InboxAction { Label = "Accept Step 4 as Complete" },
                    new InboxAction { Label = "Continue Working" },
                ],
            };
            var window = new InboxMessageWindow(message, (_, _) => Task.FromResult(true));

            window.SetDecisionActionsEnabled(false);
            Assert.That(window.AreDecisionActionsEnabled, Is.False);

            window.SetDecisionActionsEnabled(true);
            Assert.That(window.AreDecisionActionsEnabled, Is.True);
        });
}

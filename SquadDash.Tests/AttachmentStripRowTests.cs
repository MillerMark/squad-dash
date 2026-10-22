using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PathShape = System.Windows.Shapes.Path;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class AttachmentStripRowTests
{
    [TestCase("whitespace")]
    [TestCase("label")]
    [TestCase("run")]
    public void RowClick_OpensViewerOnceWithoutNotesFocus_AndStopsStripNavigation(string target)
    {
        var run = new Run("Attachment");
        var label = new TextBlock(run);
        var row = new DockPanel();
        var opens = new List<bool>();
        AttachmentStripRow.Populate(row, label, new("", "Attachment", null), opens.Add);
        var strip = new StackPanel();
        strip.Children.Add(row);
        var stripClicks = 0;
        strip.MouseLeftButtonUp += (_, _) => stripClicks++;
        var window = Host(strip);
        try
        {
            switch (target)
            {
                case "whitespace": row.RaiseEvent(MouseUpEvent()); break;
                case "label": label.RaiseEvent(MouseUpEvent()); break;
                case "run": run.RaiseEvent(MouseUpEvent()); break;
            }
            Assert.Multiple(() =>
            {
                Assert.That(row.Background, Is.SameAs(Brushes.Transparent));
                Assert.That(row.Cursor, Is.SameAs(Cursors.Hand));
                Assert.That(opens, Is.EqualTo(new[] { false }));
                Assert.That(stripClicks, Is.Zero);
                Assert.That(row.Children.OfType<TextBox>(), Is.Empty);
            });
        }
        finally { window.Close(); }
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void KeyboardActivation_OpensViewer(Key key)
    {
        var row = new DockPanel();
        var opens = new List<bool>();
        AttachmentStripRow.Populate(row, new TextBlock(), new("", "Attachment", null), opens.Add);
        var window = Host(row);
        try
        {
            Keyboard.Focus(row);
            Assert.That(row.IsKeyboardFocused, Is.True);
            row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!,
                Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.That(opens, Is.EqualTo(new[] { false }));
        }
        finally { window.Close(); }
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", true)]
    [TestCase("saved notes", true)]
    public void NotesBubble_UsesLiveThemeResources_AndOpensWithFocus(string? notes, bool filled)
    {
        var row = new DockPanel();
        row.Resources["FontSizeBody"] = 14.0;
        row.Resources["SubtleText"] = Brushes.Gray;
        row.Resources["ImportantText"] = Brushes.Black;
        var opens = new List<bool>();
        AttachmentStripRow.Populate(row, new TextBlock(), new("", "Attachment", null, Notes: notes), opens.Add);
        var button = row.Children.OfType<Button>().Single();
        var bubble = (PathShape)button.Content;
        var window = Host(row);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(DockPanel.GetDock(button), Is.EqualTo(Dock.Right));
                Assert.That(bubble.Stroke, Is.SameAs(filled ? Brushes.Black : Brushes.Gray));
                Assert.That(bubble.Fill, Is.SameAs(filled ? Brushes.Black : null));
                Assert.That(bubble.Width, Is.EqualTo(14));
            });
            row.Resources["SubtleText"] = Brushes.Blue;
            row.Resources["ImportantText"] = Brushes.Red;
            row.Resources["FontSizeBody"] = 22.0;
            window.UpdateLayout();
            Assert.Multiple(() =>
            {
                Assert.That(bubble.Stroke, Is.SameAs(filled ? Brushes.Red : Brushes.Blue));
                Assert.That(bubble.Fill, Is.SameAs(filled ? Brushes.Red : null));
                Assert.That(bubble.Width, Is.EqualTo(22));
                Assert.That(bubble.Height, Is.EqualTo(22));
            });
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(MouseUpEvent());
            Assert.That(opens, Is.EqualTo(new[] { true }));
        }
        finally { window.Close(); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Images_HaveNoNotesIcon_ButRowStillOpensViewer(bool imagePath)
    {
        var attachment = imagePath
            ? new FollowUpAttachment("", "image", null, ImagePath: @"C:\photo.png", Notes: "excluded")
            : new FollowUpAttachment("", "image", null, FileReferencePath: @"C:\photo.png", Notes: "excluded");
        var row = new DockPanel();
        var opens = new List<bool>();
        AttachmentStripRow.Populate(row, new TextBlock(), attachment, opens.Add);
        row.RaiseEvent(MouseUpEvent());
        Assert.Multiple(() =>
        {
            Assert.That(row.Children.OfType<Button>(), Is.Empty);
            Assert.That(opens, Is.EqualTo(new[] { false }));
        });
    }

    [Test]
    public void DismissButton_DoesNotOpenViewer()
    {
        var row = new DockPanel();
        var dismiss = new Button { Content = "Remove" };
        var dismissed = 0;
        dismiss.Click += (_, e) => { dismissed++; e.Handled = true; };
        DockPanel.SetDock(dismiss, Dock.Right);
        row.Children.Add(dismiss);
        var opens = new List<bool>();
        AttachmentStripRow.Populate(row, new TextBlock(), new("", "Attachment", null), opens.Add);
        var window = Host(row);
        try
        {
            dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dismiss.RaiseEvent(MouseUpEvent());
            Assert.Multiple(() =>
            {
                Assert.That(dismissed, Is.EqualTo(1));
                Assert.That(opens, Is.Empty);
            });
        }
        finally { window.Close(); }
    }

    private static MouseButtonEventArgs MouseUpEvent() =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent };

    private static Window Host(UIElement content)
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Content = content, Width = 400, Height = 160, ShowInTaskbar = false };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        return window;
    }
}

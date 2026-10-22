using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class AttachmentNotesEditorTests
{
    [TestCase(@"C:\photo.png")]
    [TestCase(@"C:\photo.JPG")]
    [TestCase(@"C:\photo.webp")]
    [TestCase(@"C:\photo.svg")]
    public void ImagesHaveNoNotesEditorOrOutboundNotes(string path)
    {
        foreach (var attachment in new[]
        {
            new FollowUpAttachment("", "image", null, ImagePath: path, Notes: "excluded"),
            new FollowUpAttachment("", "file", null, FileReferencePath: path, Notes: "excluded"),
        })
        {
            Assert.That(AttachmentNotesEditor.Create(attachment, _ => Assert.Fail()), Is.Null);
            Assert.That(FollowUpPromptBuilder.Build("message", [attachment]), Does.Not.Contain("excluded"));
        }
    }

    [Test]
    public void TextEditor_HasNotesLabelAndImmediatelyUpdatesNotes()
    {
        string? updated = null;
        var attachment = new FollowUpAttachment("", "text", null, ContentBlock: "source", Notes: "saved");
        var panel = AttachmentNotesEditor.Create(attachment, text => updated = text)!;
        var editor = panel.Children.OfType<TextBox>().Single();
        var label = panel.Children.OfType<Label>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(label.Content, Is.EqualTo("Notes"));
            Assert.That(label.Target, Is.SameAs(editor));
            Assert.That(editor.Text, Is.EqualTo("saved"));
            Assert.That(editor.MinLines, Is.EqualTo(1));
            Assert.That(editor.MaxLines, Is.EqualTo(8));
            Assert.That(editor.AcceptsReturn, Is.True);
            Assert.That(editor.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
        });
        editor.Text = "</attachment>\nchanged";
        Assert.That(updated, Is.EqualTo("</attachment>\nchanged"));
    }

    [Test]
    public void Editor_GrowsFromOneToEightLinesThenScrolls_AndRemeasuresOnFontChange()
    {
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var panel = AttachmentNotesEditor.Create(new FollowUpAttachment("", "text", null, ContentBlock: "source"), _ => { })!;
        var editor = panel.Children.OfType<TextBox>().Single();
        var label = panel.Children.OfType<Label>().Single();
        var host = new StackPanel();
        host.Resources["FontSizeBody"] = 14.0;
        host.Resources["FontSizeSmall"] = 11.0;
        host.Children.Add(panel);
        var window = new Window
        {
            Content = host, Width = 400, Height = 600,
            ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            Layout(window);
            var small = AssertLineSizing(editor, window);
            var smallLabel = label.FontSize;
            host.Resources["FontSizeBody"] = 24.0;
            host.Resources["FontSizeSmall"] = 18.0;
            Layout(window);
            Assert.That(editor.FontSize, Is.EqualTo(24));
            Assert.That(label.FontSize, Is.EqualTo(18));
            var large = AssertLineSizing(editor, window);
            Assert.Multiple(() =>
            {
                Assert.That(smallLabel, Is.EqualTo(11));
                Assert.That(large.Min, Is.GreaterThan(small.Min));
                Assert.That(large.Max, Is.GreaterThan(small.Max));
                Assert.That((large.Max - large.Min) / (small.Max - small.Min),
                    Is.EqualTo(24.0 / 14.0).Within(0.05));
                Assert.That(label.FontSize / smallLabel, Is.EqualTo(18.0 / 11.0).Within(0.05));
                Assert.That(label.FontSize, Is.LessThan(editor.FontSize));
            });
            editor.Text = string.Join(" ", Enumerable.Repeat("wrapping", 300));
            Layout(window);
            Assert.That(editor.ActualHeight, Is.EqualTo(large.Max).Within(1));
            Assert.That(FindScrollViewer(editor)!.ScrollableHeight, Is.GreaterThan(0));
        }
        finally
        {
            window.Close();
            GC.KeepAlive(application);
        }
    }

    private static (double Min, double Max) AssertLineSizing(TextBox editor, Window window)
    {
        editor.Text = "";
        Layout(window);
        var min = editor.ActualHeight;
        var chrome = editor.Padding.Top + editor.Padding.Bottom + editor.BorderThickness.Top + editor.BorderThickness.Bottom;
        Assert.That(min - chrome, Is.EqualTo(editor.FontFamily.LineSpacing * editor.FontSize).Within(2));
        editor.Text = "one\ntwo";
        Layout(window);
        var lineHeight = editor.ActualHeight - min;
        Assert.That(lineHeight, Is.GreaterThan(0));
        editor.Text = string.Join("\n", Enumerable.Repeat("line", 8));
        Layout(window);
        var max = editor.ActualHeight;
        Assert.That(max, Is.EqualTo(min + 7 * lineHeight).Within(2));
        Assert.That(FindScrollViewer(editor)!.ScrollableHeight, Is.EqualTo(0).Within(1));
        editor.Text += "\nninth\ntenth";
        Layout(window);
        Assert.Multiple(() =>
        {
            Assert.That(editor.ActualHeight, Is.EqualTo(max).Within(1));
            Assert.That(FindScrollViewer(editor)!.ScrollableHeight, Is.GreaterThan(0));
        });
        editor.ScrollToEnd();
        Layout(window);
        Assert.That(editor.VerticalOffset, Is.GreaterThan(0));
        return (min, max);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer)
            return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child)
                return child;
        return null;
    }
}

using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
internal sealed class PromptClipboardPasteTests
{
    [TestCase("\r\n", "UnicodeText", false)]
    [TestCase("\n", "UnicodeText", false)]
    [TestCase("\r\n", "Text", false)]
    [TestCase("\r\n", "Rich Text Format", false)]
    [TestCase("\r\n", "HTML Format", false)]
    [TestCase("\r\n", "UnicodeText", true)]
    [TestCase("\n", "UnicodeText", true)]
    [TestCase("\r\n", "UnicodeText", false, true)]
    [TestCase("\n", "UnicodeText", false, true)]
    public void NativePaste_MarkdownPayload_MatchesReloadedTextAndParagraphSpacing(
        string newline, string clipboardFormat, bool replaceSelection, bool enableStyling = false)
    {
        using var host = new PasteHost(enableStyling);
        var editor = host.Editor;
        var text = string.Join("\n", Enumerable.Range(1, 12).SelectMany(section => new[]
        {
            $"## Section {section}: prompt editor",
            "- Preserve **bold**, `code`, and [links](https://example.test).",
            "1. Keep \"quotes\", apostrophes, <tags> & punctuation: [] {} !?",
            "",
            "> Keep this intentional blank line above.",
            "",
            "",
            "Next paragraph; do not collapse either blank line.",
        })) + "\n";
        var expected = replaceSelection ? "before " + text + " after" : text;
        if (replaceSelection)
        {
            editor.SetPlainText("before selected after");
            editor.SelectRange(7, 8);
        }

        host.Paste(text.Replace("\n", newline), clipboardFormat);

        Assert.That(editor.GetPlainText(), Is.EqualTo(expected));
        Assert.That(editor.GetLineCount(), Is.EqualTo(expected.Split('\n').Length));
        var paragraphs = editor.Document.Blocks.OfType<Paragraph>().ToArray();
        var pastedHeight = editor.ExtentHeight;
        var pastedLineTops = paragraphs.Take(8)
            .Select(p => p.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top).ToArray();
        // Queue navigation saves plain text and reconstructs the draft with SetPlainText.
        var queue = new PromptQueue();
        queue.Enqueue(editor.GetPlainText(), 1);
        editor.SetPlainText("another queued prompt");
        editor.SetPlainText(queue.Items[0].Text);
        host.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(editor.GetPlainText(), Is.EqualTo(expected));
            Assert.That(queue.DequeueFirstReady()!.Text, Is.EqualTo(expected));
            Assert.That(pastedHeight, Is.EqualTo(editor.ExtentHeight).Within(0.1),
                "Pasting must not add paragraph spacing that disappears after switching drafts.");
            var reloaded = editor.Document.Blocks.OfType<Paragraph>().Take(8).ToArray();
            for (var i = 0; i < reloaded.Length; i++)
                Assert.That(pastedLineTops[i],
                    Is.EqualTo(reloaded[i].ContentStart.GetCharacterRect(LogicalDirection.Forward).Top).Within(0.1),
                    $"Paragraph {i} must occupy the same position before and after draft reload.");
        });
    }

    [Test]
    public void NativePaste_ThenTextAndParagraphOrSoftBreakInsertion_PreservesTextAndSpacing()
    {
        using var host = new PasteHost();
        var editor = host.Editor;
        host.Paste("first\r\nsecond", DataFormats.UnicodeText);
        editor.SetCaretOffset(editor.GetTextLength());
        editor.CaretPosition = editor.CaretPosition.InsertParagraphBreak();
        editor.CaretPosition.InsertTextInRun("typed");
        editor.SetCaretOffset(editor.GetTextLength());
        editor.CaretPosition = editor.CaretPosition.InsertLineBreak();
        editor.CaretPosition.InsertTextInRun("soft");
        host.Drain();

        const string expected = "first\nsecond\ntyped\nsoft";
        Assert.That(editor.GetPlainText(), Is.EqualTo(expected));
        Assert.That(editor.GetLineCount(), Is.EqualTo(4));
        Assert.That(editor.Document.Blocks.OfType<Paragraph>().Select(p => p.Margin),
            Is.All.EqualTo(new Thickness(0)));
    }

    [Test]
    public void NativePaste_SharedUndoRedo_PreservesTextSelectionAndSpacing()
    {
        using var host = new PasteHost(enableStyling: true);
        var editor = host.Editor;
        var history = new PromptUndoHistory(editor);
        history.CaptureBaseline();
        editor.TextChanged += (_, _) =>
        {
            if (!history.IsApplying && !PromptTextStyling.IsApplyingFormatting(editor))
                history.PushSnapshot();
        };
        host.Paste("## Heading\r\n\r\nBody **bold**\r\n", DataFormats.UnicodeText);
        var pasted = PromptUndoManager.CaptureSnapshot(editor);
        var height = editor.ExtentHeight;

        Assert.That(history.Undo(), Is.True);
        host.Drain();
        Assert.That(editor.GetPlainText(), Is.Empty);
        Assert.That(history.Redo(), Is.True);
        host.Drain();
        Assert.That(PromptUndoManager.CaptureSnapshot(editor), Is.EqualTo(pasted));
        Assert.That(editor.ExtentHeight, Is.EqualTo(height).Within(0.1));
    }

    private sealed class PasteHost : IDisposable
    {
        private readonly IDataObject? _clipboard = Clipboard.GetDataObject();
        private readonly Window _window;
        private int _pasteEvents;
        internal RichTextBox Editor { get; } = new()
        {
            FontSize = 16,
            Padding = new Thickness(10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsUndoEnabled = false,
        };

        internal PasteHost(bool enableStyling = false)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
                directory = directory.Parent;
            var source = Path.Combine(directory?.FullName ??
                throw new DirectoryNotFoundException("SquadDash source not found."), "SquadDash", "MainWindow.xaml");
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var prompt = XDocument.Load(source).Descendants(presentation + "RichTextBox")
                .Single(e => (string?)e.Attribute(x + "Name") == "PromptTextBox");
            // Load the actual prompt-scoped resources, so wiring is covered as well as WPF layout.
            var resources = prompt.Element(presentation + "RichTextBox.Resources");
            if (resources is not null)
            {
                var dictionary = new XElement(presentation + "ResourceDictionary",
                    new XAttribute(XNamespace.Xmlns + "x", x), resources.Elements());
                Editor.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            }
            DataObject.AddPastingHandler(Editor, (_, e) =>
            {
                _pasteEvents++;
                RichTextBoxExtensions.PreparePlainTextPaste(e, Clipboard.ContainsText());
            });
            _window = new Window
            {
                Content = new AdornerDecorator { Child = Editor }, Width = 900, Height = 600,
                ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000,
            };
            PromptTextStyling.SetIsEnabled(Editor, enableStyling);
            _window.Show();
            Drain();
        }

        internal void Paste(string text, string format)
        {
            var data = new DataObject();
            if (format == DataFormats.Text || format == DataFormats.UnicodeText)
                data.SetData(format, text);
            else
            {
                data.SetData(DataFormats.UnicodeText, text);
                data.SetData(format, format == DataFormats.Rtf
                    ? @"{\rtf1\ansi Rich representation must not be pasted.\par}"
                    : "<html><body><p>Rich representation must not be pasted.</p></body></html>");
            }
            Clipboard.SetDataObject(data, false);
            Assert.That(ApplicationCommands.Paste.CanExecute(null, Editor), Is.True);
            ApplicationCommands.Paste.Execute(null, Editor);
            Drain();
            Assert.That(_pasteEvents, Is.EqualTo(1), "Must exercise WPF's native clipboard paste pipeline.");
            Assert.That(Clipboard.GetData(format == DataFormats.Text ? DataFormats.Text : DataFormats.UnicodeText),
                Is.EqualTo(text), "Paste must not rewrite the source clipboard.");
        }

        internal void Drain()
        {
            _window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            _window.UpdateLayout();
            AdornerLayer.GetAdornerLayer(Editor)?.GetAdorners(Editor)?.OfType<PromptTextAdorner>()
                .SingleOrDefault()?.FlushPendingInkRefreshForTesting();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        public void Dispose()
        {
            PromptTextStyling.SetIsEnabled(Editor, false);
            _window.Close();
            if (_clipboard is null)
                Clipboard.Clear();
            else
                Clipboard.SetDataObject(_clipboard, true);
        }
    }
}

using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class AttachmentReadOnlyViewerTests
{
    [SetUp]
    public void SetUp()
    {
        var application = Application.Current;
        if (application is null)
        {
            application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            application.Resources["FontSizeNormal"] = 14.0;
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            application.Resources["FontSizeNormal"] = 14.0;
        }
    }

    [TestCase("cs", "C#")]
    [TestCase("unrecognized", null)]
    [TestCase("text", null)]
    public void WholeFence_UsesOneProtectedEditorWithLabelAndCopyButton(string token, string? grammar)
    {
        var classification = AttachmentTextClassifier.Classify($"```{token}\nConsole.WriteLine(1);\n```");
        var host = ReadOnlyHighlightedEditorFactory.Build(
            classification.Content, classification.Grammar, classification.LanguageLabel!, 14);
        var editor = host.Children.OfType<TextEditor>().Single();
        var label = host.Children.OfType<TextBlock>().Single();
        var copyHost = host.Children.OfType<Grid>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("Console.WriteLine(1);"));
            Assert.That(editor.SyntaxHighlighting?.Name, Is.EqualTo(grammar));
            Assert.That(editor.IsReadOnly, Is.True);
            Assert.That(editor.FontFamily.Source, Is.EqualTo("Consolas"));
            Assert.That(editor.Options.EnableHyperlinks, Is.False);
            Assert.That(editor.Options.EnableEmailHyperlinks, Is.False);
            Assert.That(editor.Document.UndoStack.SizeLimit, Is.Zero);
            Assert.That(InputMethod.GetIsInputMethodEnabled(editor), Is.False);
            Assert.That(editor.WordWrap, Is.False);
            Assert.That(editor.ShowLineNumbers, Is.False);
            Assert.That(label.Text, Is.EqualTo(classification.LanguageLabel));
            Assert.That(label.FontFamily, Is.EqualTo(SystemFonts.MessageFontFamily));
            Assert.That(label.FontSize, Is.EqualTo(14 * 0.7));
            Assert.That(copyHost.Children.OfType<CopyButton>().Count(), Is.EqualTo(1));
            Assert.That(editor.ContextMenu!.Items.OfType<MenuItem>().Select(item => item.Header),
                Is.EqualTo(new[] { "Copy", "Select All" }));
        });
        ((MenuItem)editor.ContextMenu!.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(editor.SelectedText, Is.EqualTo(classification.Content));
    }

    [Test]
    public void WholeCode_UsesPooledGrammarButIndependentDocuments()
    {
        var first = ReadOnlyHighlightedEditorFactory.Create("using System;", "C#", 14, new Thickness(0));
        var second = ReadOnlyHighlightedEditorFactory.Create("class C {}", "C#", 14, new Thickness(0));
        Assert.Multiple(() =>
        {
            Assert.That(first.SyntaxHighlighting, Is.SameAs(second.SyntaxHighlighting));
            Assert.That(first.Document, Is.Not.SameAs(second.Document));
        });
    }

    [Test]
    public void MixedDocument_KeepsProseAndPerFenceLanguageAndLazilyLoadsReadOnlyEditors()
    {
        const string text = "Before\n```cs\nConsole.WriteLine(1);\n```\nBetween\n```json\n{\"ok\":true}\n```\nAfter";
        var document = MarkdownDocumentRenderer.BuildReadOnlyDocument(text, 14);
        var previews = document.Blocks.OfType<BlockUIContainer>()
            .Select(block => (TranscriptCodeBlockPreview)block.Tag).ToArray();
        var host = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false };
        var window = new Window
        {
            Content = host, Width = 500, Height = 600,
            ShowActivated = false, ShowInTaskbar = false,
        };
        Assert.That(previews, Has.Length.EqualTo(2));
        Assert.That(previews.All(preview => preview.Surface is TextBox), Is.True);
        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Multiple(() =>
            {
                Assert.That(previews.All(preview => preview.Surface is TextEditor { IsReadOnly: true }), Is.True);
                Assert.That(previews.Select(preview => ((TextEditor)preview.Surface).SyntaxHighlighting.Name),
                    Is.EqualTo(new[] { "C#", "Json" }));
                Assert.That(MarkdownVisibleContent.GetText(document),
                    Does.Contain("Before").And.Contain("Console.WriteLine(1);")
                        .And.Contain("Between").And.Contain("{\"ok\":true}").And.Contain("After"));
            });
            host.Selection.Select(document.ContentStart, document.ContentEnd);
            Assert.That(MarkdownVisibleContent.GetSelectedText(host),
                Does.Contain("Console.WriteLine(1);").And.Contain("{\"ok\":true}"));
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        Assert.That(previews.All(preview => preview.Surface is TextBox), Is.True);
    }

    [Test]
    public void MixedDocument_DoesNotStripQuickReplyMarkupOrEnableNavigation()
    {
        const string text = "Before [example](https://example.com)\n```text\nliteral\n```\n<quick-replies>\nContinue\n</quick-replies>";
        var document = MarkdownDocumentRenderer.BuildReadOnlyDocument(text, 14);
        var link = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines)
            .OfType<Hyperlink>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(link.IsEnabled, Is.False);
            Assert.That(MarkdownVisibleContent.GetText(document), Does.Contain("<quick-replies>").And.Contain("Continue"));
        });
    }
}

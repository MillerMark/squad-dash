using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AvalonEditMarkdownEditorMigrationArchitectureTests
{
    [Test]
    public void RepoDocumentation_UsesSharedHighlightedEditorAndKeepsDocumentLifecycle()
    {
        var xaml = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml"));
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("<local:AvalonEditMarkdownEditor x:Name=\"DocSourceEditor\"")
                .And.Contain("ShowImageButton=\"True\"")
                .And.Contain("ShowHrButton=\"False\"")
                .And.Contain("ImageInsertRequested=\"DocSourceEditor_ImageInsertRequested\"")
                .And.Contain("TextChanged=\"DocSourceEditor_TextChanged\""));
            Assert.That(xaml, Does.Not.Contain("DocSourceTextBox").And.Not.Contain("DocEditorToolbar"));
            Assert.That(source, Does.Not.Contain("DocSourceTextBox.")
                .And.Not.Contain("_docSourceFind")
                .And.Not.Contain("_docSourceHover")
                .And.Not.Contain("_docSourceOverlayCanvas")
                .And.Not.Contain("ApplyDocSourceFontSize"));
            Assert.That(source, Does.Contain("StripDocFrontMatter(raw, out var frontMatter)")
                .And.Contain("DocSourceEditor.SetText(stripped);")
                .And.Contain("DocSourceEditor.SetText(string.Empty);")
                .And.Contain("var bodyText = DocSourceEditor.GetText();")
                .And.Contain("if (string.IsNullOrWhiteSpace(bodyText))")
                .And.Contain("File.WriteAllText(_currentDocPath, _currentDocFrontMatter + bodyText);"));
            Assert.That(source, Does.Contain("if (_suppressDocSourceTextChanged || _isClosing) return;")
                .And.Contain("_docSourceSaveTimer.Start();")
                .And.Contain("_docPreviewRefreshTimer.Start();")
                .And.Contain("DocSourceEditor?.Dispose();"));
            Assert.That(source, Does.Contain("DocSourceEditor.HighlightSourceLine(lineHint);")
                .And.Contain("DocSourceEditor.ClearSourceHighlight();")
                .And.Contain("ApplicationCommands.Find.Execute(null, textArea);")
                .And.Contain("SetDocSourceLayout(topBottom: false)")
                .And.Contain("SetDocSourceLayout(topBottom: true)"));
        });
    }

    [Test]
    public void RepoDocumentation_KeepsDocActionsAndDefersInputToAvalon()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DocSourceEditor?.Editor?.TextArea?.ContextMenu")
                .And.Contain("Paste image from clipboard")
                .And.Contain("Add to New Chat")
                .And.Contain("Add to Notes")
                .And.Contain("![Screenshot: brief description](images/screenshot-{id}.png)")
                .And.Contain("new ClipboardImageEditorWindow(this, clipImg)"));
            Assert.That(source, Does.Contain("DocSourceEditor.ReviseWithAiCallback")
                .And.Contain("DocSourceEditor.StartPttCallback")
                .And.Contain("DocSourceEditor.StopPttCallback")
                .And.Contain("if (IsDocSourceEditorFocused())")
                .And.Contain("private bool LastFocusedIsDocSourceTextBox()")
                .And.Contain("DocSourceEditor.CycleCase(false);")
                .And.Contain("DocSourceEditor?.RefreshTheme();"));
            Assert.That(source, Does.Contain("MapRawOffsetToNormalizedOffset(beforeRawText, selectionStart)")
                .And.Contain("MapNormalizedOffsetToRawOffset(beforeRawText, offset)")
                .And.Contain("if (!CanMutateDocSourceRange(editor, rawOffset, rawRemoveLength))")
                .And.Contain("using (editor.Document.RunUpdate())")
                .And.Contain("editor.Document.Replace(rawOffset, rawRemoveLength, insertText);"));
        });
    }

    [Test]
    public void CodeHealthTaskEditorWindow_UsesAvalonEditorForInstructionsHoverAndLifecycle()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "CodeHealthTaskEditorWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private readonly AvalonEditMarkdownEditor _instructionsPanel;")
                .And.Contain("var panel = new AvalonEditMarkdownEditor { ShowHrButton = false };")
                .And.Contain("panel.SetText(_task.Instructions ?? \"\");")
                .And.Contain("var instructionsText = _instructionsPanel.GetText().TrimEnd('\\r', '\\n');"),
                "Instructions should be seeded and saved through AvalonEdit with the HR button hidden.");
            Assert.That(source, Does.Contain("panel.PreviewUpdateRequested += (_, _)    => UpdateMarkdownPreview();")
                .And.Contain("panel.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => ClearInstructionsHoverHighlight();")
                .And.Contain("var editor = _instructionsPanel.Editor;")
                .And.Contain("BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList();")
                .And.Contain("FindInlineAtPoint"),
                "Preview hover and highlight should use AvalonEdit text-area geometry and inline hit-testing.");
            Assert.That(source, Does.Contain("if (e.Key == Key.Escape && !e.Handled)")
                .And.Contain("_instructionsPanel.Dispose();")
                .And.Contain("ClearInstructionsHoverHighlight();"),
                "Window key handling must allow child handlers to consume Escape and dispose the editor lifecycle correctly.");
            Assert.That(source, Does.Not.Contain("_pttInstructions"),
                "Dictation/search ownership for markdown should stay with the Avalon editor, not a RichTextBox attachment.");
            Assert.That(source, Does.Contain("configureMarkdownEditor?.Invoke(_instructionsPanel);")
                .And.Contain("panel.DocumentPath = _loopMdPath ?? _task.SourceFilePath ?? string.Empty;")
                .And.Contain("panel.CleanupPrompt = _settingsProvider().CleanupPrompt;"));
        });
    }

    [Test]
    public void CodeHealthTaskEditorWindow_ReceivesRevisionAndPopupDictationCallbacksFromLauncher()
    {
        var controller = File.ReadAllText(FindRepoFile("SquadDash", "CodeHealthPanelController.cs"));
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = main.IndexOf("_codeHealthPanel = new CodeHealthPanelController(", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var end = main.IndexOf("_codeHealthPanel.ClearFilterAction", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        var launch = main[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(controller.Split("configureMarkdownEditor: _configureMarkdownEditor").Length - 1,
                Is.EqualTo(2), "Both New Task and Edit Task must configure revision services.");
            Assert.That(launch, Does.Contain("editor.ReviseWithAiCallback = context.ReviseWithAiCallback;")
                .And.Contain("editor.StartPttCallback = context.StartPttCallback;")
                .And.Contain("editor.StopPttCallback = context.StopPttCallback;"));
        });
    }

    [Test]
    public void HintAuthoringWindow_UsesAvalonEditorForSeedPreviewFocusAndDisposal()
    {
        var xaml = File.ReadAllText(FindRepoFile("SquadDash", "Hints", "UI", "HintAuthoringWindow.xaml"));
        var source = File.ReadAllText(FindRepoFile("SquadDash", "Hints", "UI", "HintAuthoringWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("<local:AvalonEditMarkdownEditor x:Name=\"MarkdownEditor\"")
                .And.Contain("ShowHrButton=\"False\""),
                "Hint authoring should host the Avalon markdown editor in XAML with HR insertion hidden.");
            Assert.That(source, Does.Contain("private const string DefaultMarkdown")
                .And.Contain("MarkdownEditor.SetText(existing.MarkdownText);")
                .And.Contain("MarkdownEditor.SetText(DefaultMarkdown);")
                .And.Contain("MarkdownPreview.Markdown = MarkdownEditor.GetText();")
                .And.Contain("hint.MarkdownText    = MarkdownEditor.GetText();"),
                "Seed, preview, and save paths should all read/write markdown through AvalonEdit APIs.");
            Assert.That(source, Does.Contain("if (MarkdownEditor.IsKeyboardFocusWithin)")
                .And.Contain("return null;")
                .And.Contain("MarkdownEditor.TextChanged += MarkdownEditor_TextChanged;")
                .And.Contain("private void MarkdownEditor_TextChanged(object? sender, string text)")
                .And.Contain("private void DebounceTimer_Tick(object? sender, EventArgs e)")
                .And.Contain("MarkdownEditor.Dispose();"),
                "Focus routing and lifecycle hooks must keep markdown dictation ownership in-editor and dispose on close.");
        });
    }

    [Test]
    public void PlanViewerWindow_PopulateTaskEditorMigratesOnlyDescriptionToAvalonAndDisposesAcrossRebuild()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var editorStart = source.IndexOf("private bool PopulateTaskEditor", StringComparison.Ordinal);
        var editorEnd = source.IndexOf("private List<ContractExampleEditorRow>", editorStart, StringComparison.Ordinal);

        Assert.That(editorStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(editorEnd, Is.GreaterThan(editorStart));
        var editor = source[editorStart..editorEnd];

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private static bool IsGraphTextInput(IInputElement? element) =>")
                .And.Contain("or ICSharpCode.AvalonEdit.Editing.TextArea;"),
                "Space-to-pan should treat Avalon text areas as text inputs.");
            Assert.That(editor, Does.Contain("var title = AddEditorField(panel, \"Title\", task.Title, 34);")
                .And.Contain("var description = new AvalonEditMarkdownEditor { MinHeight = 170 };")
                .And.Contain("description.SetText(task.Description);")
                .And.Contain("_taskDescriptionEditor = description;")
                .And.Contain("var descriptionText = description.GetText();"),
                "Task editing should keep Title on the shared text field path while moving only Description to AvalonEdit.");
            Assert.That(source, Does.Contain("var title = AddEditorField(panel, \"Title\", validation.Title, 34);")
                .And.Contain("var message = AddEditorField(panel, \"Message\", gate.Message, 75);"),
                "Shared AddEditorField-based editors outside task description should remain unchanged.");
            Assert.That(source, Does.Contain("_taskDescriptionEditor?.Dispose();")
                .And.Contain("_taskDescriptionEditor = null;")
                .And.Contain("private void DetachDescriptionDocuments()"),
                "Description editor instances should be disposed during refresh and document detachment.");
        });
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail($"Could not find {Path.Combine(pathParts)} from {TestContext.CurrentContext.TestDirectory}.");
        return string.Empty;
    }
}

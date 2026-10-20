using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class AvalonEditMarkdownEditorTests
{
    private Dictionary<object, object> _resources = [];

    [SetUp]
    public void SaveResources()
    {
        EnsureApplication();
        _resources = Application.Current.Resources.Cast<DictionaryEntry>()
            .ToDictionary(entry => entry.Key, entry => entry.Value!);
    }

    [TearDown]
    public void RestoreResources()
    {
        Application.Current.Resources.Clear();
        foreach (var entry in _resources)
            Application.Current.Resources[entry.Key] = entry.Value;
    }

    [Test]
    public void CharterAndHistory_SaveTheirOwnFiles_PreserveFrontMatter_AndUndoAcrossSave()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var charterPath = Path.Combine(directory, "charter.md");
        var historyPath = Path.Combine(directory, "history.md");
        const string frontMatter = "---\r\nname: agent\r\n---\r\n";
        File.WriteAllText(charterPath, frontMatter + "# Charter\r\noriginal\r\n");
        File.WriteAllText(historyPath, "# History\r\noriginal\r\n");
        var charter = MarkdownDocumentTabState.Load("charter", charterPath, useAvalonEdit: true);
        var history = MarkdownDocumentTabState.Load("history", historyPath, useAvalonEdit: true);
        try
        {
            charter.AvalonEditor!.TextChanged += (_, text) => charter.UpdateWorkingText(text);
            history.AvalonEditor!.TextChanged += (_, text) => history.UpdateWorkingText(text);
            Assert.That(charter.GetEditorText(), Is.EqualTo(charter.SavedText));
            Assert.That(charter.IsDirty, Is.False);

            charter.AvalonEditor.Editor.Document.Insert(0, "charter edit\n");
            history.AvalonEditor.Editor.Document.Insert(0, "history edit\n");
            Assert.That(charter.IsDirty && history.IsDirty, Is.True);
            charter.SaveEditorChanges();
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(charterPath), Is.EqualTo(frontMatter + "charter edit\n# Charter\noriginal\n"));
                Assert.That(File.ReadAllText(historyPath), Is.EqualTo("# History\r\noriginal\r\n"));
                Assert.That(charter.IsDirty, Is.False);
                Assert.That(history.IsDirty, Is.True);
            });
            history.SaveEditorChanges();
            Assert.That(File.ReadAllText(historyPath), Is.EqualTo("history edit\n# History\noriginal\n"));

            charter.AvalonEditor.Editor.Undo();
            Assert.That(charter.IsDirty, Is.True);
            charter.AvalonEditor.Editor.Redo();
            Assert.That(charter.IsDirty, Is.False);

            File.Delete(charterPath);
            Directory.CreateDirectory(charterPath);
            charter.AvalonEditor.Editor.Document.Insert(0, "unsaved\n");
            var savedText = charter.SavedText;
            Assert.Throws<UnauthorizedAccessException>(() => charter.SaveEditorChanges());
            Assert.That(charter.IsDirty, Is.True);
            Assert.That(charter.SavedText, Is.EqualTo(savedText));
        }
        finally
        {
            charter.AvalonEditor?.Dispose();
            history.AvalonEditor?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DefaultDocumentLoading_DoesNotOptOtherCallersIntoAvalonEdit()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# Existing editor");
            var document = MarkdownDocumentTabState.Load("other", path);
            Assert.That(document.AvalonEditor, Is.Null);
            Assert.That(document.EditorControl, Is.SameAs(document.EditorTextBox));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void EveryFormattingButton_UsesExistingMarkdownTransformation_AndIsUndoable()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        using var editor = new AvalonEditMarkdownEditor();
        using var host = HostLoaded(editor);
        var cases = new (string Tooltip, Action<TextBox> Apply)[]
        {
            ("Bold selection (Ctrl+B)", MarkdownEditorCommands.ApplyBold),
            ("Italic selection (Ctrl+I)", MarkdownEditorCommands.ApplyItalic),
            ("Insert link", MarkdownEditorCommands.InsertLink),
            ("Insert table", MarkdownEditorCommands.InsertTable),
            ("Smart code formatting (inline or block)", box => MarkdownEditorCommands.ApplyInlineCodeOrFence(box)),
            ("Insert horizontal rule (---)", MarkdownEditorCommands.InsertHorizontalRule),
            ("Convert selection to bullet list", MarkdownEditorCommands.ApplyBulletList),
            ("Convert selection to numbered list", MarkdownEditorCommands.ApplyNumberedList),
        };
        foreach (var item in cases)
        {
            const string text = "alpha\nbeta";
            editor.SetText(text);
            editor.Editor.Select(0, text.Length);
            var expected = new TextBox { Text = text };
            expected.Select(0, text.Length);
            item.Apply(expected);
            var button = ((StackPanel)FindToolbar(editor).Content).Children.OfType<Button>()
                .Single(button => Equals(button.ToolTip, item.Tooltip));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            Assert.That(editor.GetText(), Is.EqualTo(expected.Text.ReplaceLineEndings("\n")), item.Tooltip);
            editor.Editor.Undo();
            Assert.That(editor.GetText(), Is.EqualTo(text), item.Tooltip);
        }
    }

    [Test]
    public void FormattingAfterRawCrLfEdit_UsesCorrectOffsets_AndUndoRestoresRawDocument()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        using var editor = new AvalonEditMarkdownEditor();
        using var host = HostLoaded(editor);
        const string raw = "first\r\nsecond";
        editor.Editor.Document.Text = raw;
        editor.Editor.Select(7, 6);
        var bold = (Button)FindToolbar(editor).FindName("BoldButton");
        bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, bold));
        Assert.That(editor.GetText(), Is.EqualTo("first\n**second**"));
        editor.Editor.Undo();
        Assert.That(editor.Editor.Document.Text, Is.EqualTo(raw));
    }

    [TestCase("`", "`word`")]
    [TestCase("'", "`word`")]
    [TestCase("\"", "\"word\"")]
    [TestCase("(", "(word)")]
    public void SelectionWrapping_HandlesTextInputWithoutSwallowingNextCharacter(string input, string expected)
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        using var editor = new AvalonEditMarkdownEditor();
        using var host = HostLoaded(editor);
        editor.SetText("word");
        editor.Editor.Select(0, 4);
        var composition = new TextComposition(InputManager.Current, editor.Editor.TextArea, input);
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent
        };
        editor.Editor.TextArea.RaiseEvent(args);
        Assert.That(args.Handled, Is.True);
        Assert.That(editor.GetText(), Is.EqualTo(expected));
        editor.Editor.Select(editor.Editor.Document.TextLength, 0);
        var next = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, editor.Editor.TextArea, "x"))
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent
        };
        editor.Editor.TextArea.RaiseEvent(next);
        Assert.That(next.Handled, Is.False);
    }

    [Test]
    public void EnterAndPaste_UseLfLineEndings_AndEnterKeepsNativeUndo()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        using var editor = new AvalonEditMarkdownEditor();
        using var host = HostLoaded(editor);
        editor.SetText("first");
        editor.Editor.CaretOffset = 5;
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.Return)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        editor.Editor.TextArea.RaiseEvent(enter);
        Assert.That(editor.Editor.Document.Text, Is.EqualTo("first\n"));
        Assert.That(editor.Editor.CaretOffset, Is.EqualTo(6));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("first"));

        var paste = new DataObjectPastingEventArgs(
            new DataObject(DataFormats.UnicodeText, "one\r\ntwo\rthree"), false, DataFormats.UnicodeText);
        editor.Editor.RaiseEvent(paste);
        Assert.That(paste.DataObject.GetData(DataFormats.UnicodeText), Is.EqualTo("one\ntwo\nthree"));
    }

    [Test]
    public void SetText_NormalizesLineEndings_AndDoesNotRaiseChangeEvents()
    {
        EnsureApplication();
        SeedThemeResources(
            Color.FromRgb(0xFA, 0xFA, 0xF8),
            Color.FromRgb(0x2A, 0x1E, 0x12),
            Color.FromRgb(0x53, 0x37, 0x1E),
            14d);

        using var editor = new AvalonEditMarkdownEditor();
        var textChangedCount = 0;
        var previewCount = 0;
        editor.TextChanged += (_, _) => textChangedCount++;
        editor.PreviewUpdateRequested += (_, _) => previewCount++;

        editor.SetText("one\r\ntwo\rthree\nfour");

        Assert.Multiple(() =>
        {
            Assert.That(editor.GetText(), Is.EqualTo("one\ntwo\nthree\nfour"));
            Assert.That(textChangedCount, Is.EqualTo(0));
            Assert.That(previewCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void TextEdits_RaiseImmediateTextChanged_AndDebouncedPreview_ThenStopAfterDispose()
    {
        EnsureApplication();
        SeedThemeResources(
            Color.FromRgb(0xFA, 0xFA, 0xF8),
            Color.FromRgb(0x2A, 0x1E, 0x12),
            Color.FromRgb(0x53, 0x37, 0x1E),
            14d);

        var editor = new AvalonEditMarkdownEditor();
        using var host = HostLoaded(editor);

        var textChangedCount = 0;
        var previewCount = 0;
        string? latestText = null;
        string? latestPreview = null;
        editor.TextChanged += (_, text) =>
        {
            textChangedCount++;
            latestText = text;
        };
        editor.PreviewUpdateRequested += (_, text) =>
        {
            previewCount++;
            latestPreview = text;
        };

        editor.Editor.Document.Insert(0, "A");
        Assert.Multiple(() =>
        {
            Assert.That(textChangedCount, Is.EqualTo(1));
            Assert.That(latestText, Is.EqualTo("A"));
        });

        WaitUntil(() => previewCount == 1, TimeSpan.FromSeconds(2));
        Assert.That(latestPreview, Is.EqualTo("A"));

        editor.Dispose();
        editor.Editor.Document.Insert(1, "B");
        PumpDispatcher(TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(textChangedCount, Is.EqualTo(1));
            Assert.That(previewCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ToolbarFormatting_AndTypingParticipateInNativeUndoRedo()
    {
        EnsureApplication();
        SeedThemeResources(
            Color.FromRgb(0xFA, 0xFA, 0xF8),
            Color.FromRgb(0x2A, 0x1E, 0x12),
            Color.FromRgb(0x53, 0x37, 0x1E),
            14d);

        using var editor = new AvalonEditMarkdownEditor();
        using var host = HostLoaded(editor);
        editor.SetText("hello world");
        editor.Editor.Select(6, 5);

        var toolbar = FindToolbar(editor);
        var bold = (Button?)toolbar.FindName("BoldButton");
        Assert.That(bold, Is.Not.Null);
        bold!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, bold));
        Assert.That(editor.GetText(), Is.EqualTo("hello **world**"));

        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("hello world"));
        editor.Editor.Redo();
        Assert.That(editor.GetText(), Is.EqualTo("hello **world**"));

        editor.Editor.CaretOffset = editor.Editor.Document.TextLength;
        editor.Editor.Document.Insert(editor.Editor.CaretOffset, "!");
        Assert.That(editor.GetText(), Is.EqualTo("hello **world**!"));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("hello **world**"));
        editor.Editor.Redo();
        Assert.That(editor.GetText(), Is.EqualTo("hello **world**!"));
    }

    [Test]
    public void UsesMarkdownGrammar_ThemePalette_FontAndLayoutConventions()
    {
        EnsureApplication();
        var lightSurface = Color.FromRgb(0xFB, 0xFA, 0xF7);
        var lightCodeText = Color.FromRgb(0x2A, 0x1E, 0x12);
        var lightImportant = Color.FromRgb(0x53, 0x37, 0x1E);
        SeedThemeResources(lightSurface, lightCodeText, lightImportant, 19d);

        using var workspace = new TestWorkspace();
        using var editor = new AvalonEditMarkdownEditor(new ApplicationSettingsStore(workspace.GetPath("settings.json")));
        using var host = HostLoaded(editor);
        var toolbar = FindToolbar(editor);
        var image = (Button?)toolbar.FindName("ImageButton");
        var hr = (Button?)toolbar.FindName("HrButton");

        Assert.Multiple(() =>
        {
            Assert.That(editor.Editor.SyntaxHighlighting?.Name, Is.EqualTo("Markdown"));
            Assert.That(editor.Editor.WordWrap, Is.True);
            Assert.That(editor.Editor.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto));
            Assert.That(editor.Editor.HorizontalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Disabled));
            Assert.That(editor.Editor.FontFamily.Source, Is.EqualTo(SystemFonts.MessageFontFamily.Source));
            Assert.That(editor.Editor.FontSize, Is.EqualTo(19d).Within(0.01));
            Assert.That(editor.LastChildFill, Is.True);
            Assert.That(DockPanel.GetDock(toolbar), Is.EqualTo(Dock.Top));
            Assert.That(image?.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(hr?.Visibility, Is.EqualTo(Visibility.Visible));
        });

        editor.RefreshTheme();
        Assert.Multiple(() =>
        {
            Assert.That(((SolidColorBrush)editor.Editor.Background).Color, Is.EqualTo(lightSurface));
            Assert.That(((SolidColorBrush)editor.Editor.Foreground).Color, Is.EqualTo(lightImportant));
        });

        var darkSurface = Color.FromRgb(0x14, 0x11, 0x0D);
        var darkCodeText = Color.FromRgb(0xD8, 0xCC, 0xBA);
        var darkImportant = Color.FromRgb(0xE5, 0xD5, 0xC0);
        SeedThemeResources(darkSurface, darkCodeText, darkImportant, 19d);
        editor.RefreshTheme();

        Assert.Multiple(() =>
        {
            Assert.That(((SolidColorBrush)editor.Editor.Background).Color, Is.EqualTo(darkSurface));
            Assert.That(((SolidColorBrush)editor.Editor.Foreground).Color, Is.EqualTo(darkImportant));
            Assert.That(editor.Editor.SyntaxHighlighting?.Name, Is.EqualTo("Markdown"));
        });
    }

    private static MarkdownEditorToolbar FindToolbar(AvalonEditMarkdownEditor editor) =>
        editor.Children.OfType<MarkdownEditorToolbar>().Single();

    [Test]
    public void CaseCycling_ReusesVariants_AndOneUndoRestoresOriginalWithoutErasingEarlierHistory()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        using var editor = new AvalonEditMarkdownEditor();
        editor.SetText("some words");
        editor.Editor.Document.Insert(0, "prefix ");
        editor.Editor.Select(7, 10);
        var variants = TextCaseHelper.ComputeVariants("some words");
        var first = TextCaseHelper.GetFirstVariantIndex("some words");
        editor.CycleCase(false);
        Assert.That(editor.Editor.SelectedText, Is.EqualTo(variants[first]));
        editor.CycleCase(false);
        Assert.That(editor.Editor.SelectedText, Is.EqualTo(variants[(first + 1) % variants.Count]));
        editor.CycleCase(true);
        Assert.That(editor.Editor.SelectedText, Is.EqualTo(variants[first]));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("prefix some words"));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("some words"));
    }

    [Test]
    public void ReverseCaseCycling_StartsAtLastVariant_AndResetsOnReselection()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 14);
        using var editor = new AvalonEditMarkdownEditor();
        editor.SetText("some words");
        editor.Editor.Select(0, 10);
        editor.CycleCase(true);
        Assert.That(editor.Editor.SelectedText, Is.EqualTo(TextCaseHelper.ComputeVariants("some words")[^1]));
        editor.Editor.Select(0, 4);
        var selected = editor.Editor.SelectedText;
        editor.CycleCase(false);
        Assert.That(editor.Editor.SelectedText, Is.EqualTo(
            TextCaseHelper.ComputeVariants(selected)[TextCaseHelper.GetFirstVariantIndex(selected)]));
    }

    [Test]
    public void Zoom_ComposesWithLiveEnvironmentFont_AndPersistsAcrossControls()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 20);
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("settings.json");
        var store = new ApplicationSettingsStore(path);
        store.SaveMarkdownEditorZoomMultiplier(1.5);
        using var editor = new AvalonEditMarkdownEditor(store);
        using var second = new AvalonEditMarkdownEditor(new ApplicationSettingsStore(path));
        using var host = HostLoaded(editor);
        Assert.That(editor.Editor.FontSize, Is.EqualTo(30).Within(.001));
        editor.ZoomByWheelDelta(120);
        PumpDispatcher(TimeSpan.FromMilliseconds(1));
        Assert.That(editor.Editor.FontSize, Is.EqualTo(33).Within(.001));
        Assert.That(second.Editor.FontSize, Is.EqualTo(33).Within(.001));
        // HwndSource is not an Application window, so invalidate its resource scope directly.
        editor.Resources["FontSizeBody"] = 10d;
        PumpDispatcher(TimeSpan.FromMilliseconds(1));
        Assert.That(editor.Editor.FontSize, Is.EqualTo(16.5).Within(.001));
        Assert.That(new ApplicationSettingsStore(path).Load().MarkdownEditorZoomMultiplier, Is.EqualTo(1.65).Within(.001));
        editor.ZoomByWheelDelta(-120);
        Assert.That(editor.Editor.FontSize, Is.EqualTo(15).Within(.001));
        Assert.That(editor.Editor.BorderThickness, Is.EqualTo(new Thickness(0)));
        Assert.That(editor.Children.OfType<TextBlock>(), Is.Empty);
    }

    [Test]
    public void Zoom_ClampsAndRestoresOnLoad_WithoutMutatingTextOrUndo()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 20);
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings.json"));
        using var editor = new AvalonEditMarkdownEditor(store);
        editor.SetText("original");
        editor.Editor.Document.Insert(8, " edit");
        editor.ZoomByWheelDelta(int.MaxValue);
        Assert.That(editor.Editor.FontSize, Is.EqualTo(60));
        editor.ZoomByWheelDelta(int.MinValue);
        Assert.That(editor.Editor.FontSize, Is.EqualTo(10));
        store.SaveMarkdownEditorZoomMultiplier(2);
        using var host = HostLoaded(editor);
        Assert.That(editor.Editor.FontSize, Is.EqualTo(40));
        Assert.That(editor.GetText(), Is.EqualTo("original edit"));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("original"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Dictation_TracksTargetAcrossAdjacentEdits_AndPreservesUndo(bool replaceSelection)
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 20);
        using var workspace = new TestWorkspace();
        using var editor = new AvalonEditMarkdownEditor(new ApplicationSettingsStore(workspace.GetPath("settings.json")));
        editor.SetText("before selected after");
        editor.Editor.Select(7, replaceSelection ? 8 : 0);
        editor.CaptureDictationSelection();
        editor.Editor.Document.Insert(0, "prefix ");
        var before = editor.GetText();
        var plan = VoiceHeuristics.VoiceInsertionHeuristics.PlanDictationInsertion(
            before, 14, replaceSelection ? 8 : 0, "New text", []);
        editor.InsertDictationPhrase("New text");
        Assert.That(editor.GetText(), Is.EqualTo(plan.TextAfterInsert));
        Assert.That(editor.Editor.CaretOffset, Is.EqualTo(plan.CaretIndexAfterInsert));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo(before));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("before selected after"));
    }

    [Test]
    public void Dictation_ReplacementRulesNormalizeLf_AndUndoSeparatelyFromRecognition()
    {
        SeedThemeResources(Colors.White, Colors.Black, Colors.Black, 20);
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings.json"));
        store.SaveVoiceReplacementRules([new VoiceHeuristics.VoiceReplacementRule("Hello", "First\r\nSecond")]);
        using var editor = new AvalonEditMarkdownEditor(store);
        editor.SetText("");
        editor.CaptureDictationSelection();
        editor.InsertDictationPhrase("Hello");
        Assert.That(editor.Editor.Text, Is.EqualTo("First\nSecond"));
        Assert.That(editor.Editor.CaretOffset, Is.EqualTo(12));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.EqualTo("Hello"));
        editor.Editor.Undo();
        Assert.That(editor.GetText(), Is.Empty);
    }

    private static HwndSource HostLoaded(FrameworkElement element)
    {
        var source = new HwndSource(new HwndSourceParameters("avalon-markdown-editor-test", 0, 0)
        {
            WindowStyle = 0,
        })
        {
            RootVisual = element,
        };
        PumpDispatcher(TimeSpan.FromMilliseconds(1));
        return source;
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = new DispatcherTimer { Interval = timeout };

        timer.Tick += (_, _) =>
        {
            if (predicate())
                frame.Continue = false;
        };
        deadline.Tick += (_, _) => frame.Continue = false;

        timer.Start();
        deadline.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        deadline.Stop();

        Assert.That(predicate(), Is.True);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
            _ = new Application();
    }

    private static void SeedThemeResources(Color codeSurface, Color codeText, Color importantText, double fontSizeBody)
    {
        var resources = Application.Current!.Resources;
        resources["ThemedButtonStyle"] = new Style(typeof(Button));
        resources["ThemedMenuItemStyle"] = new Style(typeof(MenuItem));
        resources["ThemedContextMenuStyle"] = new Style(typeof(ContextMenu));
        resources["ThemedMenuSeparatorStyle"] = new Style(typeof(Separator));
        resources["SubtleBorder"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        resources["PanelBorder"] = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        resources["LabelText"] = new SolidColorBrush(codeText);
        resources["FontSizeTiny"] = 10d;
        resources["FontSizeNormal"] = 14d;
        resources["FontSizeMedium"] = 15d;
        resources["FontSizeLarge"] = 16d;
        resources["FontSizeHeading"] = 18d;
        resources["CodeSurface"] = new SolidColorBrush(codeSurface);
        resources["CodeText"] = new SolidColorBrush(codeText);
        resources["ImportantText"] = new SolidColorBrush(importantText);
        resources["FontSizeBody"] = fontSizeBody;
    }
}

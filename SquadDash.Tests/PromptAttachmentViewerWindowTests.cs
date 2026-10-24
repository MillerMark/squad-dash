using System.Threading;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class PromptAttachmentViewerWindowTests
{
    private readonly List<PromptAttachmentViewerWindow> _ownedViewers = [];
    private SynchronizationContext? _previousContext;

    [SetUp]
    public void SetUp()
    {
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        EnsureResources();
        CloseViewerWindows();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            CloseViewerWindows();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(_previousContext);
        }
    }

    [TestCase("user", "2026-10-22T12:00:00Z", "From user \u2022 2m ago")]
    [TestCase("assistant", "2026-10-22T08:02:00-04:00", "From assistant \u2022 0s ago")]
    [TestCase("system", null, "From system")]
    [TestCase("user", "not a timestamp", "From user")]
    [TestCase("user", "", "From user")]
    [TestCase(null, "2026-10-22T12:00:00Z", "From transcript \u2022 2m ago")]
    [TestCase(" ", null, "From transcript")]
    [TestCase("user", "2026-10-22T12:05:00Z", "From user \u2022 0s ago")]
    public void TranscriptOrigin_FormatsCompactTimeAndGracefulFallbacks(string? role, string? timestamp, string expected)
    {
        var source = new TranscriptAttachmentSource(Role: role, Timestamp: timestamp);
        Assert.That(PromptAttachmentViewerWindow.FormatTranscriptOrigin(source,
            new DateTimeOffset(2026, 10, 22, 12, 2, 0, TimeSpan.Zero)), Is.EqualTo(expected));
    }

    [Test]
    public void TranscriptOrigin_LegacyAttachmentHasNoInventedTime()
    {
        Assert.That(PromptAttachmentViewerWindow.FormatTranscriptOrigin(null, DateTimeOffset.Now),
            Is.EqualTo("From transcript"));
    }

    [Test]
    public void TranscriptOrigin_MinuteTickRefreshesEveryTab_AndCloseDetachesTimer()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 2, 0, TimeSpan.Zero);
        var source = new TranscriptAttachmentSource("user", 7, now.AddMinutes(-2).ToString("O"),
            "Hidden conversation", "hidden-message-id");
        var first = new FollowUpAttachment("", "first", null, TranscriptQuote: "selected body",
            Notes: "keep notes", TranscriptSource: source);
        var second = first with { Description = "second", TranscriptSource = source with { Role = "assistant" } };
        var window = new PromptAttachmentViewerWindow([first, second], null,
            notesChanged: (current, notes) => current with { Notes = notes }, nowProvider: () => now);
        var timer = (DispatcherTimer)typeof(PromptAttachmentViewerWindow)
            .GetField("_originRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.That(timer.IsEnabled, Is.False);
        window.Show();
        Pump();
        var tabs = FindVisualDescendants<TabControl>(window).Single();
        var firstLine = FindVisualDescendants<TextBlock>(window).Single(block => block.Text.StartsWith("From "));
        Assert.Multiple(() =>
        {
            Assert.That(firstLine.Text, Is.EqualTo("From user \u2022 2m ago"));
            Assert.That(timer.Interval, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(timer.IsEnabled, Is.True);
            Assert.That(FindVisualDescendants<TextBox>(window).Select(box => box.Text),
                Is.EquivalentTo(new[] { "selected body", "keep notes" }));
            Assert.That(FindVisualDescendants<TextBlock>(window).Any(block =>
                block.Text.Contains("Hidden conversation") || block.Text.Contains("hidden-message-id")), Is.False);
        });

        now = now.AddMinutes(1);
        Tick(timer);
        Assert.That(firstLine.Text, Is.EqualTo("From user \u2022 3m ago"));
        tabs.SelectedIndex = 1;
        Pump();
        var secondLine = FindVisualDescendants<TextBlock>(window).Single(block => block.Text.StartsWith("From "));
        Assert.That(secondLine.Text, Is.EqualTo("From assistant \u2022 3m ago"));
        window.Close();
        Assert.That(timer.IsEnabled, Is.False);
        now = now.AddMinutes(1);
        Tick(timer);
        Assert.That(secondLine.Text, Is.EqualTo("From assistant \u2022 3m ago"));
    }

    [TestCase(null)]
    [TestCase("invalid")]
    public void TranscriptOrigin_WithoutValidTime_DoesNotStartTimer(string? timestamp)
    {
        var attachment = new FollowUpAttachment("", "quote", null, TranscriptQuote: "body",
            TranscriptSource: new TranscriptAttachmentSource(Role: "user", Timestamp: timestamp));
        var window = new PromptAttachmentViewerWindow([attachment], null);
        window.Show();
        Pump();
        var timer = (DispatcherTimer)typeof(PromptAttachmentViewerWindow)
            .GetField("_originRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Multiple(() =>
        {
            Assert.That(timer.IsEnabled, Is.False);
            Assert.That(FindVisualDescendants<TextBlock>(window).Single(block => block.Text.StartsWith("From ")).Text,
                Is.EqualTo("From user"));
        });
    }

    private static void Tick(DispatcherTimer timer)
    {
        var handlers = (EventHandler?)typeof(DispatcherTimer)
            .GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timer);
        handlers?.Invoke(timer, EventArgs.Empty);
    }

    [Test]
    public void EditableNotes_RenderBelowContent_AndReuseWindowAfterReplacement()
    {
        var attachment = new FollowUpAttachment("", "first", null, ContentBlock: "body", Notes: null);
        var callbackCurrents = new List<FollowUpAttachment>();
        var callbackReturns = new List<FollowUpAttachment>();
        FollowUpAttachment UpdateNotes(FollowUpAttachment current, string? notes)
        {
            callbackCurrents.Add(current);
            var updated = current with { Notes = notes };
            callbackReturns.Add(updated);
            return updated;
        }

        PromptAttachmentViewerWindow.Show([attachment], owner: null, notesChanged: UpdateNotes, focusNotes: true);
        Pump();

        var firstWindow = GetOpenViewerWindows().Single();
        var notesLabel = FindVisualDescendants<Label>(firstWindow).Single(label => Equals(label.Content, "Notes"));
        var notesEditor = FindVisualDescendants<TextBox>(firstWindow).Single(textBox =>
            AutomationProperties.GetName(textBox) == "Notes for first");
        var notesPanel = (DockPanel)notesLabel.Parent;
        var contentPanel = (DockPanel)notesPanel.Parent;
        var body = contentPanel.Children.OfType<ScrollViewer>().Single();
        firstWindow.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.Title, Is.EqualTo("Text Attachment"));
            Assert.That(notesLabel.FontSize, Is.EqualTo((double)Application.Current.Resources["FontSizeSmall"]));
            Assert.That(notesEditor.FontSize, Is.EqualTo((double)Application.Current.Resources["FontSizeBody"]));
            Assert.That(notesEditor.MinLines, Is.EqualTo(1));
            Assert.That(notesEditor.MaxLines, Is.EqualTo(8));
            Assert.That(notesEditor.IsReadOnly, Is.False);
            Assert.That(notesEditor.Text, Is.Empty);
            Assert.That(notesEditor.IsKeyboardFocused, Is.True);
            Assert.That(DockPanel.GetDock(notesPanel), Is.EqualTo(Dock.Bottom));
            Assert.That(DockPanel.GetDock(notesLabel), Is.EqualTo(Dock.Left));
            Assert.That(notesLabel.Target, Is.SameAs(notesEditor));
            Assert.That(notesPanel.Children[^1], Is.SameAs(notesEditor));
            Assert.That(contentPanel.Children[^1], Is.SameAs(body));
            Assert.That(((TextBox)body.Content).Text, Is.EqualTo("body"));
            Assert.That(notesLabel.FontSize, Is.LessThan(notesEditor.FontSize));
            Assert.That(notesPanel.TranslatePoint(new Point(), contentPanel).Y,
                Is.GreaterThanOrEqualTo(body.TranslatePoint(new Point(0, body.ActualHeight), contentPanel).Y));
        });

        notesEditor.Text = "first edit";
        notesEditor.Text = "second edit";
        Assert.Multiple(() =>
        {
            Assert.That(firstWindow.HasNoteChanges, Is.True);
            Assert.That(callbackCurrents, Has.Count.EqualTo(2));
            Assert.That(ReferenceEquals(callbackCurrents[0], attachment), Is.True);
            Assert.That(ReferenceEquals(callbackCurrents[1], callbackReturns[0]), Is.True);
        });

        PromptAttachmentViewerWindow.Show([callbackReturns[^1]], owner: null, notesChanged: UpdateNotes, focusNotes: true);
        Pump();
        Assert.Multiple(() =>
        {
            Assert.That(GetOpenViewerWindows().Single(), Is.SameAs(firstWindow));
            Assert.That(firstWindow.HasNoteChanges, Is.False);
            Assert.That(FindNoteActions(firstWindow).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(notesEditor.IsKeyboardFocused, Is.True);
            Assert.That(notesEditor.SelectionLength, Is.EqualTo(0));
            Assert.That(notesEditor.CaretIndex, Is.EqualTo(notesEditor.Text.Length));
        });
        notesEditor.Text = "";
        PromptAttachmentViewerWindow.Show([callbackReturns[^1]], owner: null, notesChanged: UpdateNotes, focusNotes: true);
        Pump();
        Assert.Multiple(() =>
        {
            Assert.That(GetOpenViewerWindows().Single(), Is.SameAs(firstWindow));
            Assert.That(notesEditor.Text, Is.Empty);
            Assert.That(notesEditor.IsKeyboardFocused, Is.True);
        });
    }

    [Test]
    public void FreshNotesIconOpening_FocusesNotesWithCaretAtEnd()
    {
        var attachment = new FollowUpAttachment("", "saved", null, ContentBlock: "body", Notes: "saved notes");
        PromptAttachmentViewerWindow.Show([attachment], null,
            notesChanged: (current, notes) => current with { Notes = notes }, focusNotes: true);
        Pump();
        var window = GetOpenViewerWindows().Single();
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == "Notes for saved");
        Assert.Multiple(() =>
        {
            Assert.That(editor.IsKeyboardFocused, Is.True);
            Assert.That(FocusManager.GetFocusedElement(window), Is.SameAs(editor));
            Assert.That(editor.SelectionLength, Is.EqualTo(0));
            Assert.That(editor.CaretIndex, Is.EqualTo(editor.Text.Length));
        });
    }

    [Test]
    public void ExistingWindow_ReusedForSameIdentity_AndFocusNotesUsesSelectedTab()
    {
        var first = new FollowUpAttachment("", "first tab", null, ContentBlock: "first", Notes: "a");
        var second = new FollowUpAttachment("", "second tab", null, ContentBlock: "second", Notes: "b");
        FollowUpAttachment Update(FollowUpAttachment current, string? notes) => current with { Notes = notes };

        PromptAttachmentViewerWindow.Show([first, second], owner: null, notesChanged: Update, focusNotes: false);
        Pump();
        var window = GetOpenViewerWindows().Single();
        var tabs = FindVisualDescendants<TabControl>(window).Single();
        tabs.SelectedIndex = 1;
        Pump();

        PromptAttachmentViewerWindow.Show([first, second], owner: null, notesChanged: Update, focusNotes: true);
        Pump();

        var afterReuse = GetOpenViewerWindows().Single();
        var focusedEditor = Keyboard.FocusedElement as TextBox;
        Assert.Multiple(() =>
        {
            Assert.That(afterReuse, Is.SameAs(window));
            Assert.That(focusedEditor, Is.Not.Null);
            Assert.That(AutomationProperties.GetName(focusedEditor!), Is.EqualTo("Notes for second tab"));
            Assert.That(FocusManager.GetFocusedElement(afterReuse), Is.SameAs(focusedEditor));
            Assert.That(focusedEditor!.SelectionLength, Is.EqualTo(0));
            Assert.That(focusedEditor.CaretIndex, Is.EqualTo(focusedEditor.Text.Length));
        });
    }

    [Test]
    public void ShowWithOwner_FocusNotesTransfersFromOwner_OnNewReuseAndMinimizedViewer()
    {
        var attachment = new FollowUpAttachment("", "first", null, ContentBlock: "body", Notes: "saved notes");
        var ownerEditor = new TextBox { Text = "owner input" };
        var owner = new Window
        {
            Width = 320,
            Height = 220,
            ShowInTaskbar = false,
            Content = ownerEditor
        };

        try
        {
            owner.Show();
            owner.Activate();
            ownerEditor.Focus();
            Keyboard.Focus(ownerEditor);
            Pump();

            PromptAttachmentViewerWindow.Show([attachment], owner,
                notesChanged: (current, notes) => current with { Notes = notes }, focusNotes: true);
            // Finish the originating click with focus back on the owner before deferred focus runs.
            owner.Activate();
            ownerEditor.Focus();
            Pump();
            var viewer = GetOpenViewerWindows().Single();
            AssertFocusedNotesEditor(viewer, "Notes for first");

            owner.Activate();
            ownerEditor.Focus();
            Keyboard.Focus(ownerEditor);
            Pump();

            PromptAttachmentViewerWindow.Show([attachment], owner,
                notesChanged: (current, notes) => current with { Notes = notes }, focusNotes: true);
            owner.Activate();
            ownerEditor.Focus();
            Pump();
            var reusedViewer = GetOpenViewerWindows().Single();
            Assert.That(reusedViewer, Is.SameAs(viewer));
            AssertFocusedNotesEditor(reusedViewer, "Notes for first");

            owner.Activate();
            ownerEditor.Focus();
            Keyboard.Focus(ownerEditor);
            viewer.WindowState = WindowState.Minimized;
            Pump();

            PromptAttachmentViewerWindow.Show([attachment], owner,
                notesChanged: (current, notes) => current with { Notes = notes }, focusNotes: true);
            owner.Activate();
            ownerEditor.Focus();
            Pump();
            var restoredViewer = GetOpenViewerWindows().Single();
            Assert.Multiple(() =>
            {
                Assert.That(restoredViewer, Is.SameAs(viewer));
                Assert.That(restoredViewer.WindowState, Is.EqualTo(WindowState.Normal));
            });
            AssertFocusedNotesEditor(restoredViewer, "Notes for first");
        }
        finally
        {
            foreach (var window in GetOpenViewerWindows().ToArray())
                window.Close();
            owner.Close();
            Pump();
        }
    }

    [Test]
    public void FocusNotesFalse_DoesNotForceEditorSelection()
    {
        var attachment = new FollowUpAttachment("", "first", null, ContentBlock: "body", Notes: "existing");
        PromptAttachmentViewerWindow.Show([attachment], owner: null, notesChanged: (current, notes) => current with { Notes = notes }, focusNotes: false);
        Pump();

        var window = GetOpenViewerWindows().Single();
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == "Notes for first");

        Assert.That(editor.SelectionLength, Is.EqualTo(0));
    }

    [Test]
    public void ReadOnlyHistoricalNotes_ShowWithoutEditableCallback()
    {
        var attachment = new FollowUpAttachment("", "history", null, ContentBlock: "body", Notes: "preserved");

        PromptAttachmentViewerWindow.Show([attachment], owner: null, notesChanged: null, focusNotes: false);
        Pump();

        var window = GetOpenViewerWindows().Single();
        var notesEditor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == "Notes for history");

        Assert.Multiple(() =>
        {
            Assert.That(notesEditor.IsReadOnly, Is.True);
            Assert.That(notesEditor.Text, Is.EqualTo("preserved"));
        });
    }

    [Test]
    public void EnterWhileEditingNotes_AcceptsPersistedEditsAndClosesWindow()
    {
        var attachment = new FollowUpAttachment("", "editable", null, ContentBlock: "body", Notes: "line1");
        var persisted = attachment;
        PromptAttachmentViewerWindow.Show([attachment], owner: null,
            notesChanged: (current, notes) => persisted = current with { Notes = notes }, focusNotes: true);
        Pump();

        var window = GetOpenViewerWindows().Single();
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == "Notes for editable");
        editor.Focus();
        Keyboard.Focus(editor);
        Pump();

        editor.Text = "accepted edits";
        var key = RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.Enter);
        Pump();

        Assert.Multiple(() =>
        {
            Assert.That(key.Handled, Is.True);
            Assert.That(WaitFor(() => !window.IsVisible), Is.True);
            Assert.That(persisted.Notes, Is.EqualTo("accepted edits"));
        });
    }

    [Test]
    public void Images_DoNotRenderNotesEditor()
    {
        var attachment = new FollowUpAttachment("", "image", null, ImagePath: @"C:\missing-image.png", Notes: "ignored");

        PromptAttachmentViewerWindow.Show([attachment], owner: null, notesChanged: (current, notes) => current with { Notes = notes }, focusNotes: true);
        Pump();

        var window = GetOpenViewerWindows().Single();
        Assert.Multiple(() =>
        {
            Assert.That(window.Title, Is.EqualTo("Image Attachment"));
            Assert.That(FindVisualDescendants<Label>(window).Any(label => Equals(label.Content, "Notes")), Is.False);
            Assert.That(FindVisualDescendants<TextBox>(window).Any(textBox =>
                (AutomationProperties.GetName(textBox) ?? string.Empty).StartsWith("Notes for ", StringComparison.Ordinal)), Is.False);
        });
    }

    [TestCase(Key.LeftCtrl, false)]
    [TestCase(Key.RightCtrl, false)]
    [TestCase(Key.LeftCtrl, true)]
    [TestCase(Key.RightCtrl, true)]
    public void DoubleCtrlInEditableNotes_InvokesSharedDictation(Key ctrl, bool multipleTabs)
    {
        var starts = 0;
        var window = ShowVoiceViewer(() => starts++, editable: true, multipleTabs);
        if (multipleTabs)
        {
            FindVisualDescendants<TabControl>(window).Single().SelectedIndex = 1;
            Pump();
        }
        window.FocusNotes();
        Pump();
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == (multipleTabs ? "Notes for second" : "Notes for first"));
        Assert.Multiple(() =>
        {
            Assert.That(editor.IsKeyboardFocused, Is.True);
            Assert.That(FocusManager.GetFocusedElement(window), Is.SameAs(editor));
            Assert.That(editor.SelectionLength, Is.EqualTo(0));
            Assert.That(editor.CaretIndex, Is.EqualTo(editor.Text.Length));
        });

        var firstDown = RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, ctrl);
        var firstUp = RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, ctrl);
        var secondDown = RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, ctrl);
        RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, ctrl);

        Assert.Multiple(() =>
        {
            Assert.That(firstDown.Handled, Is.False);
            Assert.That(firstUp.Handled, Is.False);
            Assert.That(secondDown.Handled, Is.True);
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(editor.Text, Is.EqualTo("saved notes"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DoubleCtrlOutsideEditableNotes_DoesNotStartDictation(bool focusBody)
    {
        var starts = 0;
        var window = ShowVoiceViewer(() => starts++, editable: focusBody);
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox => focusBody
            ? textBox.Text == "body"
            : AutomationProperties.GetName(textBox) == "Notes for first");
        Keyboard.Focus(editor);
        Assert.That(editor.IsKeyboardFocusWithin, Is.True);

        RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
        RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);
        var secondDown = RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
        RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);

        Assert.Multiple(() =>
        {
            Assert.That(secondDown.Handled, Is.False);
            Assert.That(starts, Is.Zero);
        });
    }

    [TestCase(Key.A)]
    [TestCase(Key.C)]
    [TestCase(Key.V)]
    [TestCase(Key.X)]
    [TestCase(Key.Z)]
    [TestCase(Key.Y)]
    public void CtrlEditingShortcut_IsNotConsumedOrCountedAsFirstTap(Key shortcut)
    {
        var starts = 0;
        var window = ShowVoiceViewer(() => starts++, editable: true);
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == "Notes for first");
        Keyboard.Focus(editor);
        Assert.That(editor.IsKeyboardFocusWithin, Is.True);

        RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
        var shortcutDown = RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, shortcut);
        RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, shortcut);
        RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);
        var nextCtrl = RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
        RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);

        Assert.Multiple(() =>
        {
            Assert.That(shortcutDown.Handled, Is.False);
            Assert.That(nextCtrl.Handled, Is.False);
            Assert.That(starts, Is.Zero);
        });
    }

    private PromptAttachmentViewerWindow ShowVoiceViewer(Action onDictationStart, bool editable, bool multipleTabs = false)
    {
        var first = new FollowUpAttachment("", "first", null, ContentBlock: "body", Notes: "saved notes");
        var second = new FollowUpAttachment("", "second", null, ContentBlock: "second body", Notes: "saved notes");
        var window = new PromptAttachmentViewerWindow(multipleTabs ? [first, second] : [first], owner: null,
            notesChanged: editable ? (current, notes) => current with { Notes = notes } : null,
            settingsProvider: () =>
            {
                onDictationStart();
                // Exercise the real gesture/start path without credentials, audio capture, or network access.
                return ApplicationSettingsSnapshot.Empty with { SpeechProvider = SpeechProvider.OpenAI, OpenAiSpeechApiKey = null };
            });
        _ownedViewers.Add(window);
        window.Show();
        Pump();
        return window;
    }

    private static KeyEventArgs RaisePreviewKey(TextBox editor, RoutedEvent routedEvent, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, Environment.TickCount, key)
        {
            RoutedEvent = routedEvent,
        };
        editor.RaiseEvent(args);
        return args;
    }

    [TestCase("a")]
    [TestCase("already typed")]
    [TestCase("\u00e9\u4e2d")]
    [TestCase(" ")]
    public void TranscriptAnnotation_KeyboardPreservesSeedAndContinuesTyping(string seed)
    {
        var transcript = CreateSelectedTranscript();
        var owner = ShowTranscript(transcript);
        var attachments = new List<FollowUpAttachment>();
        try
        {
            owner.PreviewTextInput += (_, e) =>
            {
                if (TranscriptAnnotationShortcut.TryStart(transcript, e.Text, false,
                    box => EnsureQuote(box, attachments),
                    (attachment, text, dictate) => PromptAttachmentViewerWindow.ShowForAnnotation(
                        attachment, owner, (current, notes) => SaveNotes(attachments, current, notes), text, dictate)))
                    e.Handled = true;
            };
            Assert.That(TextCompositionManager.StartComposition(
                new TextComposition(InputManager.Current, transcript, seed)), Is.True);

            // The first composition must already be inserted before dispatcher focus repair.
            var viewer = GetOpenViewerWindows().Single();
            var notes = FindNotesEditor(viewer);
            Assert.That(notes.Text, Is.EqualTo(seed));
            Pump();
            AssertFocusedNotesEditor(viewer, "Notes for quote");
            notes.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                new TextComposition(InputManager.Current, notes, " next"))
            {
                RoutedEvent = TextCompositionManager.TextInputEvent
            });
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(attachments, Has.Count.EqualTo(1));
                Assert.That(attachments[0].TranscriptQuote, Is.EqualTo("selected quote"));
                Assert.That(attachments[0].Notes, Is.EqualTo(seed + " next"));
                Assert.That(notes.CaretIndex, Is.EqualTo(notes.Text.Length));
                Assert.That(transcript.Selection.Text, Is.EqualTo("selected quote"));
            });
        }
        finally
        {
            CloseViewerWindows();
            owner.Close();
        }
    }

    [Test]
    public void TranscriptAnnotation_ExistingQuoteAndViewerAreReusedAfterNotesReplacement()
    {
        var transcript = CreateSelectedTranscript();
        var owner = ShowTranscript(transcript);
        var original = new FollowUpAttachment("", "quote", null, TranscriptQuote: "selected quote", Notes: "saved ");
        var attachments = new List<FollowUpAttachment> { original };
        // Dedupe must take precedence over the attachment limit.
        attachments.AddRange(Enumerable.Range(1, 14).Select(i =>
            new FollowUpAttachment("", $"other {i}", null, TranscriptQuote: $"other {i}")));
        try
        {
            void Open(FollowUpAttachment attachment, string? text, bool dictate) =>
                PromptAttachmentViewerWindow.ShowForAnnotation(attachment, owner,
                    (current, notes) => SaveNotes(attachments, current, notes), text, dictate);
            Assert.That(TranscriptAnnotationShortcut.EnsureAttachment(attachments, original with { Notes = null }),
                Is.SameAs(original));
            Assert.That(TranscriptAnnotationShortcut.TryStart(transcript, "first", false,
                box => EnsureQuote(box, attachments), Open), Is.True);
            Pump();
            var viewer = GetOpenViewerWindows().Single();
            FindNotesEditor(viewer).SelectAll();
            owner.Activate();
            Keyboard.Focus(transcript);
            Assert.That(TranscriptAnnotationShortcut.TryStart(transcript, " second", false,
                box => EnsureQuote(box, attachments), Open), Is.True);
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(GetOpenViewerWindows().Single(), Is.SameAs(viewer));
                Assert.That(attachments, Has.Count.EqualTo(15));
                Assert.That(attachments[0].Notes, Is.EqualTo("saved first second"));
                Assert.That(FindNotesEditor(viewer).CaretIndex, Is.EqualTo("saved first second".Length));
            });
        }
        finally
        {
            CloseViewerWindows();
            owner.Close();
        }
    }

    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(false, false, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(false, false, true)]
    public void TranscriptAnnotation_RequiresFocusAndSelection(bool focused, bool selected, bool voice)
    {
        var transcript = CreateSelectedTranscript();
        var owner = ShowTranscript(transcript);
        try
        {
            if (!selected)
                transcript.Selection.Select(transcript.Document.ContentStart, transcript.Document.ContentStart);
            if (!focused)
                Keyboard.ClearFocus();
            var ensured = 0;
            var opened = 0;
            Assert.That(TranscriptAnnotationShortcut.TryStart(transcript, voice ? null : "typed", voice,
                _ => { ensured++; return new FollowUpAttachment("", "quote", null); },
                (_, _, _) => opened++), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(ensured, Is.Zero);
                Assert.That(opened, Is.Zero);
            });
        }
        finally { owner.Close(); }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("\u0003")]
    [TestCase("\r")]
    public void TranscriptAnnotation_NonTextKeyboardInputDoesNotAttach(string? text)
    {
        var transcript = CreateSelectedTranscript();
        var owner = ShowTranscript(transcript);
        try
        {
            Assert.That(TranscriptAnnotationShortcut.TryStart(transcript, text, false,
                _ => throw new AssertionException("Should not ensure attachment"),
                (_, _, _) => Assert.Fail("Should not open viewer")), Is.False);
        }
        finally { owner.Close(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TranscriptAnnotation_FullContextWithoutMatchingQuoteDoesNotOpenViewer(bool voice)
    {
        var transcript = CreateSelectedTranscript();
        var owner = ShowTranscript(transcript);
        var attachments = Enumerable.Range(0, 15).Select(i =>
            new FollowUpAttachment("", $"other {i}", null, TranscriptQuote: $"other {i}")).ToList();
        try
        {
            Assert.That(TranscriptAnnotationShortcut.TryStart(transcript, voice ? null : "typed", voice,
                box => EnsureQuote(box, attachments),
                (_, _, _) => Assert.Fail("Should not open unattached editor")), Is.False);
            Assert.That(attachments, Has.Count.EqualTo(15));
        }
        finally { owner.Close(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TranscriptAnnotation_VoiceUsesExistingDictationAndStopsOnCtrlRelease(bool existing)
    {
        var transcript = CreateSelectedTranscript();
        var owner = ShowTranscript(transcript);
        var attachments = new List<FollowUpAttachment>();
        if (existing)
            attachments.Add(new FollowUpAttachment("", "quote", null, TranscriptQuote: "selected quote", Notes: "Existing."));
        var speech = new AnnotationSpeechService();
        PromptAttachmentViewerWindow? viewer = null;
        try
        {
            Assert.That(TranscriptAnnotationShortcut.TryStart(transcript, null, true,
                box => EnsureQuote(box, attachments),
                (attachment, text, dictate) =>
                {
                    viewer = new PromptAttachmentViewerWindow([attachment], owner,
                        notesChanged: (current, notes) => SaveNotes(attachments, current, notes),
                        settingsProvider: () => ApplicationSettingsSnapshot.Empty with
                        {
                            SpeechProvider = SpeechProvider.OpenAI,
                            OpenAiSpeechApiKey = "test-only-not-a-credential"
                        },
                        speechServiceFactory: () => speech);
                    viewer.Owner = owner;
                    viewer.Show();
                    viewer.BeginAnnotation(text, dictate);
                }), Is.True);
            Pump();
            var notes = FindNotesEditor(viewer!);
            Assert.That(speech.Starts, Is.EqualTo(1));
            viewer!.BeginAnnotation(null, startDictation: true);
            Assert.That(speech.Starts, Is.EqualTo(1), "Reusing an active viewer must not start a second recognizer.");
            speech.Emit("hello there");
            Pump();
            var expected = existing ? "Existing. hello there" : "hello there";
            Assert.Multiple(() =>
            {
                Assert.That(attachments, Has.Count.EqualTo(1));
                Assert.That(attachments[0].Notes, Is.EqualTo(expected));
                Assert.That(notes.Text, Is.EqualTo(expected));
                Assert.That(notes.CaretIndex, Is.EqualTo(expected.Length));
                Assert.That(notes.IsKeyboardFocused, Is.True);
            });
            var release = RaisePreviewKey(notes, Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);
            Pump();
            Assert.That(WaitFor(() => speech.Stops == 1), Is.True, "Expected Ctrl release to stop dictation.");
            Assert.Multiple(() =>
            {
                Assert.That(release.Handled, Is.True);
                Assert.That(speech.Stops, Is.EqualTo(1));
                Assert.That(notes.Text, Is.EqualTo(expected + " final phrase"));
                Assert.That(attachments[0].Notes, Is.EqualTo(notes.Text));
                Assert.That(notes.CaretIndex, Is.EqualTo(notes.Text.Length));
            });
        }
        finally
        {
            if (viewer is not null)
                AcceptAndClose(viewer);
            owner.Close();
            Pump();
        }
    }

    private static RichTextBox CreateSelectedTranscript()
    {
        var run = new Run("selected quote");
        var transcript = new ThrottledScrollRichTextBox
        {
            Document = new FlowDocument(new Paragraph(run)),
            IsReadOnly = true
        };
        transcript.Selection.Select(run.ContentStart, run.ContentEnd);
        return transcript;
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  \r\n\t")]
    [TestCase("saved notes")]
    public void NoteActions_CompareAgainstOpeningSnapshot_AndHideWhenEditsAreUndone(string? baseline)
    {
        var attachments = QuoteAttachments(baseline);
        var viewer = ShowEditableViewer(attachments);
        var editor = FindNotesEditor(viewer);
        var actions = FindNoteActions(viewer);
        Assert.Multiple(() =>
        {
            Assert.That(viewer.HasNoteChanges, Is.False);
            Assert.That(actions.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(actions.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Right));
            Assert.That(DockPanel.GetDock(actions), Is.EqualTo(Dock.Bottom));
            Assert.That(actions.Children.OfType<Button>().Select(button => button.Content),
                Is.EqualTo(new[] { "OK", "Cancel" }));
        });

        editor.Text = (baseline ?? "") + " changed";
        Assert.Multiple(() =>
        {
            Assert.That(viewer.HasNoteChanges, Is.True);
            Assert.That(actions.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(attachments[0].Notes, Is.EqualTo(editor.Text));
        });
        editor.Text = baseline ?? "";
        Assert.Multiple(() =>
        {
            Assert.That(viewer.HasNoteChanges, Is.False);
            Assert.That(actions.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase("cancel")]
    [TestCase("escape")]
    [TestCase("close")]
    public void DiscardNo_KeepsEditsOpen_ThenYesRestoresExactPersistedBaseline(string action)
    {
        var attachments = QuoteAttachments("original \r\n notes ");
        var original = attachments[0];
        var confirmed = false;
        var prompts = 0;
        var viewer = ShowEditableViewer(attachments, () => { prompts++; return confirmed; });
        var editor = FindNotesEditor(viewer);
        editor.Text = "first replacement";
        editor.Text = "second replacement";

        void Cancel()
        {
            if (action == "close")
                viewer.Close();
            else if (action == "escape")
                Assert.That(RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.Escape).Handled, Is.True);
            else
                FindNoteActions(viewer).Children.OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }

        Cancel();
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(viewer.IsVisible, Is.True);
            Assert.That(editor.Text, Is.EqualTo("second replacement"));
            Assert.That(attachments.Single().Notes, Is.EqualTo(editor.Text));
            Assert.That(viewer.HasNoteChanges, Is.True);
        });
        confirmed = true;
        Cancel();
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(2), "Each attempt should prompt once, not again in Closing.");
            Assert.That(viewer.IsVisible, Is.False);
            Assert.That(attachments.Single(), Is.EqualTo(original));
            Assert.That(attachments.Single(), Is.Not.SameAs(original), "Rollback must use the latest replacement identity.");
        });
        editor.Text = "late edit on closed editor";
        Assert.That(attachments.Single().Notes, Is.EqualTo(original.Notes));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \r\n\t ")]
    public void DiscardYes_RestoresNullEmptyAndWhitespaceWithoutNormalization(string? baseline)
    {
        var attachments = QuoteAttachments(baseline);
        var viewer = ShowEditableViewer(attachments);
        FindNotesEditor(viewer).Text = "new notes";
        viewer.CancelNotes();
        Assert.Multiple(() =>
        {
            Assert.That(attachments.Single().Notes, Is.EqualTo(baseline));
            Assert.That(viewer.IsVisible, Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Accept_KeepsNewAttachmentAndNotes_WithoutDiscardConfirmation(bool clickOk)
    {
        var attachments = QuoteAttachments(null);
        var viewer = ShowEditableViewer(attachments,
            () => throw new AssertionException("Acceptance must not ask to discard."),
            _ => Assert.Fail("Acceptance must never remove the newly attached quote."));
        FindNotesEditor(viewer).Text = "accepted";
        if (clickOk)
            FindNoteActions(viewer).Children.OfType<Button>().Single(button => Equals(button.Content, "OK"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else
            RaisePreviewKey(FindNotesEditor(viewer), Keyboard.PreviewKeyDownEvent, Key.Enter);
        Assert.That(WaitFor(() => !viewer.IsVisible), Is.True);
        Assert.That(attachments.Single().Notes, Is.EqualTo("accepted"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PristineCancel_PromptsEvenWithoutVisibleActions_AndNoKeepsProvisionalAttachment(bool provisional)
    {
        var attachments = QuoteAttachments(null);
        var prompts = 0;
        var viewer = ShowEditableViewer(attachments, () => { prompts++; return false; },
            provisional ? current => attachments.Remove(current) : null);
        viewer.CancelNotes();
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(viewer.IsVisible, Is.True);
            Assert.That(attachments, Has.Count.EqualTo(1));
            Assert.That(FindNoteActions(viewer).Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void PristineClose_PromptsOnlyForCurrentProvisionalAttachment(bool provisional, int expectedPrompts)
    {
        var attachments = QuoteAttachments(null);
        var prompts = 0;
        var viewer = ShowEditableViewer(attachments, () => { prompts++; return true; },
            provisional ? current => attachments.Remove(current) : null);
        viewer.Close();
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(expectedPrompts));
            Assert.That(viewer.IsVisible, Is.False);
            Assert.That(attachments, Has.Count.EqualTo(provisional ? 0 : 1));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TranscriptProvenance_CancelRemovesOnlyQuoteAddedByThisInvocation(bool existing, bool voice)
    {
        var attachments = existing ? QuoteAttachments("saved") : new List<FollowUpAttachment>();
        var untouched = new FollowUpAttachment("", "other", null, TranscriptQuote: "other quote");
        attachments.Add(untouched);
        var quote = TranscriptAnnotationShortcut.EnsureAttachment(attachments, QuoteAttachments(null)[0], out var wasAdded)!;
        Assert.That(wasAdded, Is.EqualTo(!existing));
        var speech = new AnnotationSpeechService();
        FollowUpAttachment? removed = null;
        var viewer = ShowEditableViewer(attachments, discard: wasAdded ? current =>
        {
            removed = current;
            var index = attachments.FindIndex(item => ReferenceEquals(item, current));
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            attachments.RemoveAt(index);
        } : null, speech: speech, displayed: [quote]);
        viewer.BeginAnnotation(voice ? null : "typed", voice);
        if (voice)
            speech.Emit("spoken");
        Pump();
        viewer.CancelNotes();
        Assert.Multiple(() =>
        {
            Assert.That(viewer.IsVisible, Is.False);
            Assert.That(attachments.Any(item => ReferenceEquals(item, untouched)), Is.True);
            Assert.That(attachments, Has.Count.EqualTo(existing ? 2 : 1));
            if (existing)
                Assert.That(attachments[0].Notes, Is.EqualTo("saved"));
            else
            {
                Assert.That(removed, Is.Not.Null);
                Assert.That(removed!.Notes, Is.Null, "Rollback must precede removal of the final replacement.");
                Assert.That(removed, Is.Not.SameAs(quote));
            }
        });
    }

    [Test]
    public void TranscriptProvenance_DedupeAtCapacityIsNotNew_AndCapacityFailureResetsOutFlag()
    {
        var attachments = QuoteAttachments("saved");
        attachments.AddRange(Enumerable.Range(1, 14).Select(index =>
            new FollowUpAttachment("", $"quote {index}", null, TranscriptQuote: $"quote {index}")));
        var reused = TranscriptAnnotationShortcut.EnsureAttachment(attachments, QuoteAttachments(null)[0], out var wasAdded);
        Assert.Multiple(() =>
        {
            Assert.That(reused, Is.SameAs(attachments[0]));
            Assert.That(wasAdded, Is.False);
        });
        wasAdded = true;
        var rejected = TranscriptAnnotationShortcut.EnsureAttachment(attachments,
            new FollowUpAttachment("", "overflow", null, TranscriptQuote: "overflow"), out wasAdded);
        Assert.Multiple(() =>
        {
            Assert.That(rejected, Is.Null);
            Assert.That(wasAdded, Is.False);
            Assert.That(attachments, Has.Count.EqualTo(15));
        });
    }

    [Test]
    public void Reinvocation_ResetsSnapshotAndClearsOriginalShortcutRemovalProvenance()
    {
        var attachments = QuoteAttachments(null);
        var viewer = ShowEditableViewer(attachments,
            discard: _ => Assert.Fail("The old invocation must not retain removal authority."));
        var editor = FindNotesEditor(viewer);
        editor.Text = "previous invocation";
        viewer.BeginEditingInvocation();
        Assert.Multiple(() =>
        {
            Assert.That(viewer.HasNoteChanges, Is.False);
            Assert.That(FindNoteActions(viewer).Visibility, Is.EqualTo(Visibility.Collapsed));
        });
        editor.Text = "new invocation";
        viewer.CancelNotes();
        Assert.That(attachments.Single().Notes, Is.EqualTo("previous invocation"));

        var reopened = ShowEditableViewer(attachments);
        Assert.That(reopened.HasNoteChanges, Is.False);
        FindNotesEditor(reopened).Text = "reopened edit";
        reopened.CancelNotes();
        Assert.That(attachments.Single().Notes, Is.EqualTo("previous invocation"));
    }

    [Test]
    public void DiscardAcrossTabs_RestoresEverySnapshot_AndIgnoresImageNotes()
    {
        var attachments = QuoteAttachments("first");
        attachments.Add(new FollowUpAttachment("", "image", null, ImagePath: @"C:\missing.png", Notes: "image notes"));
        attachments.Add(new FollowUpAttachment("", "second", null, ContentBlock: "second", Notes: null));
        var original = attachments.ToArray();
        var viewer = ShowEditableViewer(attachments);
        FindNotesEditor(viewer).Text = "first changed";
        var tabs = FindVisualDescendants<TabControl>(viewer).Single();
        tabs.SelectedIndex = 2;
        Pump();
        var second = FindVisualDescendants<TextBox>(viewer).Single(box => AutomationProperties.GetName(box) == "Notes for second");
        second.Text = "second changed";
        tabs.SelectedIndex = 1;
        Pump();
        Assert.That(FindNoteActions(viewer).Visibility, Is.EqualTo(Visibility.Visible));
        viewer.CancelNotes();
        Assert.That(attachments, Is.EqualTo(original));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HistoricalViewer_CloseAndEscapeNeverPromptOrEnableNoteActions(bool escape)
    {
        var viewer = new PromptAttachmentViewerWindow(QuoteAttachments("historical notes"), null,
            confirmDiscardNotes: () => throw new AssertionException("Read-only notes must not ask to discard."));
        _ownedViewers.Add(viewer);
        viewer.Show();
        Pump();
        var editor = FindNotesEditor(viewer);
        Assert.Multiple(() =>
        {
            Assert.That(editor.IsReadOnly, Is.True);
            Assert.That(viewer.HasNoteChanges, Is.False);
            Assert.That(FindNoteActions(viewer).Visibility, Is.EqualTo(Visibility.Collapsed));
        });
        if (escape)
            RaisePreviewKey(editor, Keyboard.PreviewKeyDownEvent, Key.Escape);
        else
            viewer.Close();
        Assert.That(viewer.IsVisible, Is.False);
    }

    [Test]
    public void Deactivation_RetainsLiveEditsAndCurrentInvocationRemovalProvenance()
    {
        var attachments = QuoteAttachments(null);
        var prompts = 0;
        var removed = 0;
        var viewer = ShowEditableViewer(attachments, () => { prompts++; return true; }, _ => removed++);
        FindNotesEditor(viewer).Text = "keep until explicit decision";
        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewer, [EventArgs.Empty]);
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.Zero);
            Assert.That(removed, Is.Zero);
            Assert.That(viewer.IsVisible, Is.True);
            Assert.That(viewer.HasNoteChanges, Is.True);
            Assert.That(attachments.Single().Notes, Is.EqualTo("keep until explicit decision"));
        });
        viewer.CancelNotes();
        Assert.That(removed, Is.EqualTo(1));
    }

    [Test]
    public void DiscardConfirmation_NestedDispatcherSpeechAndRepeatedCancelStillRollBackOnce()
    {
        var attachments = QuoteAttachments("saved");
        var speech = new AnnotationSpeechService();
        PromptAttachmentViewerWindow? viewer = null;
        var prompts = 0;
        viewer = ShowEditableViewer(attachments, () =>
        {
            prompts++;
            speech.Emit("during confirmation");
            Pump();
            viewer!.CancelNotes();
            viewer.Close();
            Assert.That(viewer.IsVisible, Is.True);
            Assert.That(attachments[0].Notes, Does.Contain("during confirmation"));
            return true;
        }, speech: speech);
        viewer.BeginAnnotation(null, startDictation: true);
        viewer.CancelNotes();
        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(viewer.IsVisible, Is.False);
            Assert.That(attachments.Single().Notes, Is.EqualTo("saved"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Discard_BlocksQueuedAndLateSpeech_EvenDuringPendingServiceOperation(bool stopping)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speech = new AnnotationSpeechService
        {
            StartCompletion = stopping ? Task.CompletedTask : gate.Task,
            StopCompletion = stopping ? gate.Task : Task.CompletedTask
        };
        var attachments = QuoteAttachments("saved");
        var viewer = ShowEditableViewer(attachments, speech: speech);
        var editor = FindNotesEditor(viewer);
        try
        {
            viewer.BeginAnnotation(null, startDictation: true);
            speech.Emit("accepted before cancel");
            Pump();
            Assert.That(attachments[0].Notes, Does.Contain("accepted before cancel"));
            if (stopping)
            {
                RaisePreviewKey(editor, Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);
                Assert.That(WaitFor(() => speech.Stops == 1), Is.True);
            }
            speech.Emit("queued before cancel");
            viewer.CancelNotes();
            speech.Emit("late after cancel");
            gate.SetResult();
            Pump();
            Assert.That(WaitFor(() => !stopping || speech.Disposals > 0), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(viewer.IsVisible, Is.False);
                Assert.That(attachments.Single().Notes, Is.EqualTo("saved"));
                Assert.That(editor.Text, Does.Not.Contain("queued before cancel").And.Not.Contain("late after cancel"));
            });
        }
        finally
        {
            gate.TrySetResult();
            Pump();
        }
    }

    [Test]
    public void DiscardNo_DoesNotDisposeSpeech_AndLaterAcceptanceKeepsFinalPhrase()
    {
        var attachments = QuoteAttachments("saved");
        var speech = new AnnotationSpeechService();
        var viewer = ShowEditableViewer(attachments, () => false, speech: speech);
        viewer.BeginAnnotation(null, startDictation: true);
        viewer.CancelNotes();
        Assert.That(speech.Disposals, Is.Zero);
        speech.Emit("kept speech");
        Pump();
        AcceptAndClose(viewer);
        Assert.Multiple(() =>
        {
            Assert.That(attachments.Single().Notes, Is.EqualTo("saved kept speech final phrase"));
            Assert.That(speech.Stops, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Acceptance_DrainsPendingDictation_AndJoinsAnAlreadyStoppingSession(bool releaseFirst)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speech = new AnnotationSpeechService { StopCompletion = gate.Task };
        var attachments = QuoteAttachments(null);
        var viewer = ShowEditableViewer(attachments,
            () => throw new AssertionException("Do not prompt while acceptance drains dictation."), speech: speech);
        try
        {
            viewer.BeginAnnotation(null, startDictation: true);
            if (releaseFirst)
            {
                RaisePreviewKey(FindNotesEditor(viewer), Keyboard.PreviewKeyUpEvent, Key.LeftCtrl);
                Assert.That(WaitFor(() => speech.Stops == 1), Is.True);
            }
            var accepted = viewer.AcceptNotesAsync();
            Assert.That(WaitFor(() => speech.Stops == 1), Is.True);
            viewer.Close();
            viewer.CancelNotes();
            Assert.Multiple(() =>
            {
                Assert.That(viewer.IsVisible, Is.True);
                Assert.That(accepted.IsCompleted, Is.False);
                Assert.That(FindNoteActions(viewer).IsEnabled, Is.False);
                Assert.That(speech.Stops, Is.EqualTo(1));
            });
            gate.SetResult();
            Assert.That(WaitFor(() => accepted.IsCompleted), Is.True);
            accepted.GetAwaiter().GetResult();
            speech.Emit("too late");
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(viewer.IsVisible, Is.False);
                Assert.That(attachments.Single().Notes, Is.EqualTo("final phrase"));
                Assert.That(speech.Stops, Is.EqualTo(1));
            });
        }
        finally
        {
            gate.TrySetResult();
            Pump();
        }
    }

    private static List<FollowUpAttachment> QuoteAttachments(string? notes) =>
        [new FollowUpAttachment("", "quote", null, TranscriptQuote: "selected quote", Notes: notes)];

    private PromptAttachmentViewerWindow ShowEditableViewer(
        List<FollowUpAttachment> attachments, Func<bool>? confirm = null,
        Action<FollowUpAttachment>? discard = null, AnnotationSpeechService? speech = null,
        IReadOnlyList<FollowUpAttachment>? displayed = null)
    {
        var viewer = new PromptAttachmentViewerWindow(displayed ?? attachments, null,
            notesChanged: (current, notes) => SaveNotes(attachments, current, notes),
            confirmDiscardNotes: confirm ?? (() => true), discardNewAttachment: discard,
            settingsProvider: () => ApplicationSettingsSnapshot.Empty with
            {
                SpeechProvider = SpeechProvider.OpenAI,
                OpenAiSpeechApiKey = "test-only-not-a-credential"
            }, speechServiceFactory: () => speech ?? throw new AssertionException("Unexpected dictation."));
        _ownedViewers.Add(viewer);
        viewer.Show();
        Pump();
        return viewer;
    }

    private static StackPanel FindNoteActions(PromptAttachmentViewerWindow viewer) =>
        (StackPanel)FindVisualDescendants<Button>(viewer).Single(button => Equals(button.Content, "OK")).Parent;

    private static Window ShowTranscript(RichTextBox transcript)
    {
        var owner = new Window { Content = transcript, Width = 400, Height = 200, ShowInTaskbar = false };
        owner.Show();
        owner.Activate();
        Keyboard.Focus(transcript);
        Pump();
        Assert.That(transcript.IsKeyboardFocusWithin, Is.True);
        return owner;
    }

    private static FollowUpAttachment? EnsureQuote(RichTextBox transcript, List<FollowUpAttachment> attachments) =>
        TranscriptAnnotationShortcut.EnsureAttachment(attachments,
            new FollowUpAttachment("", "quote", null, TranscriptQuote: TranscriptCopyService.BuildSelectionText(transcript)));

    private static FollowUpAttachment SaveNotes(List<FollowUpAttachment> attachments, FollowUpAttachment current, string? notes)
    {
        var index = attachments.FindIndex(item => ReferenceEquals(item, current));
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        return attachments[index] = current with { Notes = notes };
    }

    private static TextBox FindNotesEditor(PromptAttachmentViewerWindow viewer) =>
        FindVisualDescendants<TextBox>(viewer).Single(box => AutomationProperties.GetName(box) == "Notes for quote");

    private sealed class AnnotationSpeechService : ISpeechRecognitionService
    {
        public event EventHandler<string>? PhraseRecognized;
        public event EventHandler<double>? VolumeChanged { add { } remove { } }
        public event EventHandler<string>? RecognitionError { add { } remove { } }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public Task StartCompletion { get; init; } = Task.CompletedTask;
        public Task StopCompletion { get; init; } = Task.CompletedTask;
        public Task StartAsync(string key, string regionOrEndpoint, IEnumerable<string>? phraseHints = null, string? language = null)
        {
            Starts++;
            return StartCompletion;
        }
        public async Task StopAsync()
        {
            Stops++;
            await StopCompletion;
            Emit("final phrase");
        }
        public void Emit(string text) => PhraseRecognized?.Invoke(this, text);
        public void WriteAudioData(byte[] buffer, int count) { }
        public void Dispose() => Disposals++;
    }

    private static IReadOnlyList<PromptAttachmentViewerWindow> GetOpenViewerWindows() =>
        GetTrackedViewerWindows().Where(IsWindowVisible).ToArray();

    private static void AssertFocusedNotesEditor(PromptAttachmentViewerWindow window, string automationName)
    {
        var editor = FindVisualDescendants<TextBox>(window).Single(textBox =>
            AutomationProperties.GetName(textBox) == automationName);
        Assert.Multiple(() =>
        {
            Assert.That(window.IsActive, Is.True);
            Assert.That(editor.IsKeyboardFocused, Is.True);
            Assert.That(FocusManager.GetFocusedElement(window), Is.SameAs(editor));
            Assert.That(editor.SelectionLength, Is.EqualTo(0));
            Assert.That(editor.CaretIndex, Is.EqualTo(editor.Text.Length));
        });
    }

    private void CloseViewerWindows()
    {
        foreach (var window in GetTrackedViewerWindows().Concat(_ownedViewers).Distinct().ToArray())
        {
            if (window.Dispatcher.CheckAccess())
                AcceptAndClose(window);
            else
                window.Dispatcher.Invoke(() => AcceptAndClose(window));
        }
        _ownedViewers.Clear();
        Pump();
    }

    private static void AcceptAndClose(PromptAttachmentViewerWindow window)
    {
        var accepted = window.AcceptNotesAsync();
        Assert.That(WaitFor(() => accepted.IsCompleted), Is.True, "Viewer cleanup must not open a discard dialog.");
        accepted.GetAwaiter().GetResult();
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var child in FindVisualDescendants<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        void ApplyResources()
        {
            app.Resources["FontSizeBody"] = 14.0;
            app.Resources["FontSizeSmall"] = 11.0;
            app.Resources["FontSizeNormal"] = 14.0;
            app.Resources["ImportantText"] = new SolidColorBrush(Colors.OrangeRed);
            app.Resources["SubtleText"] = new SolidColorBrush(Colors.Gray);
            app.Resources["LabelText"] = new SolidColorBrush(Colors.White);
            app.Resources["InputSurface"] = new SolidColorBrush(Colors.Black);
            app.Resources["InputBorder"] = new SolidColorBrush(Colors.DimGray);
            app.Resources["PanelBorder"] = new SolidColorBrush(Colors.DimGray);
            app.Resources["WindowBorderGlow"] = new SolidColorBrush(Colors.CornflowerBlue);
            app.Resources["AppSurface"] = new SolidColorBrush(Colors.Black);
            app.Resources["CaptionCloseButtonStyle"] = new Style(typeof(Button));
            app.Resources["ThemedIconButtonStyle"] = new Style(typeof(Button));
            app.Resources["ThemedButtonStyle"] = new Style(typeof(Button));
            app.Resources["ThemedTabControlStyle"] = new Style(typeof(TabControl));
            app.Resources["ThemedTabItemStyle"] = new Style(typeof(TabItem));
            app.Resources["RosterScrollViewerStyle"] = new Style(typeof(ScrollViewer));
        }

        if (app.Dispatcher.CheckAccess())
            ApplyResources();
        else
            app.Dispatcher.Invoke(ApplyResources);
    }

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Pump();
            if (condition())
                return true;
            Thread.Sleep(10);
        }

        Pump();
        return condition();
    }

    private static IReadOnlyList<PromptAttachmentViewerWindow> GetTrackedViewerWindows()
    {
        var field = typeof(PromptAttachmentViewerWindow)
            .GetField("s_openViewers", BindingFlags.Static | BindingFlags.NonPublic);
        if (field?.GetValue(null) is not IEnumerable<WeakReference<PromptAttachmentViewerWindow>> weakRefs)
            return [];

        var windows = new List<PromptAttachmentViewerWindow>();
        foreach (var weakRef in weakRefs)
            if (weakRef.TryGetTarget(out var window))
                windows.Add(window);
        return windows;
    }

    private static bool IsWindowVisible(Window window)
    {
        if (window.Dispatcher.CheckAccess())
            return window.IsVisible;
        return window.Dispatcher.Invoke(() => window.IsVisible);
    }
}

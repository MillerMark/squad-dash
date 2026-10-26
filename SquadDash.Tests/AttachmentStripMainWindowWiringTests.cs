using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AttachmentStripMainWindowWiringTests
{
    [Test]
    public void OrdinaryLiveAndReplayTurns_UseSharedDisplayProjectionWithoutReplacingPersistedPrompt()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var creation = SourceRegion(source, "private TranscriptTurnView CreateTranscriptTurnView(",
            "private void HandleTranscriptLinkClick(").Replace("\r\n", "\n");
        var replay = SourceRegion(source, "private void RenderPersistedTurn(",
            "private bool RenderStructuredPersistedNarrative(").Replace("\r\n", "\n");

        Assert.Multiple(() =>
        {
            Assert.That(creation, Does.Contain("var pendingAttachments = _pendingTranscriptAttachments;"));
            Assert.That(creation, Does.Contain("_pendingTranscriptAttachments = null;"));
            Assert.That(creation, Does.Contain(
                "SteeringTranscriptEntryFactory.ParsePromptForDisplay(displayPrompt, pendingAttachments)"));
            Assert.That(creation, Does.Contain("displayPrompt = attachmentDisplay.Prompt;"));
            Assert.That(creation, Does.Contain("var attachmentsForViewer = attachmentDisplay.Attachments;"));
            Assert.That(creation, Does.Contain("var hasAttachments = attachmentDisplay.AttachmentCount > 0;"));
            Assert.That(creation, Does.Contain("if (!string.IsNullOrEmpty(promptBody) || hasAttachments || hasDictation)"));
            Assert.That(creation, Does.Contain("SteeringTranscriptEntryFactory.AppendPromptSupplements("));
            Assert.That(creation, Does.Contain("new TranscriptTurnView(\n            thread,\n            prompt,"));
            Assert.That(creation, Does.Not.Contain("StripTypedAttachmentHeaders(")
                .And.Not.Contain("ExtractAttachmentBlocks("));
            Assert.That(replay, Does.Contain("CreateTranscriptTurnView(\n            thread,\n            turn.Prompt,"));
        });
    }

    [Test]
    public void PersistedSteering_UsesProjectedBodyCountAndViewerAttachments()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var replay = SourceRegion(source, "private void RenderPersistedHostNotice(",
            "private void FinalizeCurrentTurnResponse()");
        Assert.Multiple(() =>
        {
            Assert.That(replay, Does.Contain("SteeringTranscriptEntryFactory.ParsePersistedPrompt(providerPrompt)"));
            Assert.That(replay, Does.Contain("SteeringTranscriptEntryFactory.CreateParagraph("));
            Assert.That(replay, Does.Contain("display.Prompt,"));
            Assert.That(replay, Does.Contain("display.Attachments,"));
            Assert.That(replay, Does.Contain("display.AttachmentCount > 0 ? display.AttachmentCount : null"));
            Assert.That(replay, Does.Contain("OpenPromptAttachments,"));
        });
    }

    [Test]
    public void LiveSteering_ProjectsDisplayWithoutReplacingStoredProviderPrompt()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var live = SourceRegion(source, "private void TryAppendAcceptedSteeringTranscriptEntry(",
            "private void EvaluateSystemSteeringNotices(").Replace("\r\n", "\n");
        Assert.Multiple(() =>
        {
            Assert.That(live, Does.Contain(
                "SteeringTranscriptEntryFactory.ParsePromptForDisplay(displayPrompt, attachments)"));
            Assert.That(live, Does.Contain("displayPrompt = display.Prompt;"));
            Assert.That(live, Does.Contain("display.Attachments,"));
            Assert.That(live, Does.Contain("historicalAttachmentCount: display.AttachmentCount,"));
            Assert.That(live, Does.Contain(
                "SteeringTranscriptEntryFactory.Append(\n                targetTurn,\n                providerPrompt,"));
        });
    }

    [Test]
    public void AgentTurnStartMarker_IsBuiltOnlyAfterAttachmentTransportProjection()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var creation = SourceRegion(source, "private TranscriptTurnView CreateTranscriptTurnView(",
            "private void HandleTranscriptLinkClick(");
        var projection = creation.IndexOf("SteeringTranscriptEntryFactory.ParsePromptForDisplay(", StringComparison.Ordinal);
        var marker = creation.IndexOf("ToolTranscriptFormatter.BuildAgentTurnStartMarker(", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(projection, Is.GreaterThanOrEqualTo(0));
            Assert.That(marker, Is.GreaterThan(projection),
                "The visible agent start marker must not format raw attachment transport before display projection.");
        });
    }

    [Test]
    public void TranscriptAttachmentActions_CaptureSourceBeforeNewChatChangesSelection()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var currentChat = SourceRegion(source, "private void AttachTranscriptFollowUp(", "private FollowUpAttachment? TryBuildTranscriptFollowUpAttachment(");
        var creation = SourceRegion(source, "private FollowUpAttachment? TryBuildTranscriptFollowUpAttachment(", "private void AddTranscriptFollowUp(");
        var newChat = SourceRegion(source, "private void AddTranscriptSelectionToNewChat(", "private void PreserveVisibleTranscriptViewportsThroughLayout(");
        Assert.Multiple(() =>
        {
            Assert.That(currentChat, Does.Contain("TryBuildTranscriptFollowUpAttachment(rtb)"));
            Assert.That(currentChat, Does.Contain("AddTranscriptFollowUp(attachment)"));
            Assert.That(creation, Does.Contain("TranscriptAttachmentSourceFactory.FromSelection("));
            Assert.That(creation, Does.Contain("TranscriptQuote: quote"));
            Assert.That(creation, Does.Contain("TranscriptSource: transcriptSource"));
            Assert.That(newChat.IndexOf("TryBuildTranscriptFollowUpAttachment(rtb)", StringComparison.Ordinal),
                Is.LessThan(newChat.IndexOf("AddEmptyQueueSlot()", StringComparison.Ordinal)));
            Assert.That(newChat, Does.Contain("AddTranscriptFollowUp(precomputedAttachment)"));
            Assert.That(newChat, Does.Contain("PreserveVisibleTranscriptViewportsThroughLayout("));
        });
    }

    [Test]
    public void UpdateFollowUpStripCore_UsesPopulateAndRemovesInlineNotesEditor()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var method = SourceRegion(source, "private void UpdateFollowUpStripCore()", "private void ReopenImageForEditing(");
        var persistence = SourceRegion(source, "private FollowUpAttachment UpdateAttachmentNotes(", "private void ReopenImageForEditing(");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Not.Contain("AttachmentNotesEditor.Create("));
            Assert.That(method, Does.Not.Contain("label.MouseLeftButtonUp +="));
            Assert.That(method, Does.Contain("AttachmentStripRow.Populate(row, label, att, _ =>"));
            Assert.That(method, Does.Contain("PromptAttachmentViewerWindow.Show("));
            Assert.That(method, Does.Contain("new[] { capturedAtt }, Window.GetWindow(row) ?? (CanShowOwnedWindow() ? this : null)"));
            Assert.That(method, Does.Contain("_followUpAttachments.FirstOrDefault(entry => ReferenceEquals(entry.Value, list))"));
            Assert.That(method, Does.Contain("if (owner.Key.Length == 0)"));
            Assert.That(method, Does.Contain("PersistDraftFollowUp();"));
            Assert.That(method, Does.Contain("SyncQueuePanel();"));
            Assert.That(method, Does.Contain("notesChanged: (current, notes) =>"));
            Assert.That(method, Does.Contain("capturedAtt = UpdateAttachmentNotes(list, current, notes)"));
            Assert.That(persistence, Does.Contain("var updated = current with { Notes = notes };"));
            Assert.That(persistence, Does.Contain("owner.Value![index] = updated;"));
            Assert.That(method, Does.Contain("focusNotes: true"));
            Assert.That(method, Does.Not.Contain("focusNotes: focusNotes"));
        });
    }

    [Test]
    public void UpdateFollowUpStripCore_DismissIsAddedBeforePopulateAndHandlesClick()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var method = SourceRegion(source, "private void UpdateFollowUpStripCore()", "private void ReopenImageForEditing(");
        var dismissIndex = method.IndexOf("row.Children.Add(dismissBtn);", StringComparison.Ordinal);
        var populateIndex = method.IndexOf("AttachmentStripRow.Populate(row, label, att, _ =>", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("dismissBtn.Click += (_, e) =>"));
            Assert.That(method, Does.Contain("e.Handled = true;"));
            Assert.That(dismissIndex, Is.GreaterThan(-1));
            Assert.That(populateIndex, Is.GreaterThan(dismissIndex));
        });
    }

    [Test]
    public void PromptAttachmentViewerWindow_Source_UsesFocusNotesContractForShowAndFocusRoutine()
    {
        var source = ReadSource("PromptAttachmentViewerWindow.cs");
        var showMethod = SourceRegion(source, "internal static void Show(", "private static bool SameAttachments");
        var focusMethod = SourceRegion(source, "internal void FocusNotes()", "private static UIElement BuildAttachmentContentCore(");

        Assert.Multiple(() =>
        {
            Assert.That(showMethod, Does.Contain("if (existing.WindowState == WindowState.Minimized)"));
            Assert.That(showMethod, Does.Contain("existing.WindowState = WindowState.Normal;"));
            Assert.That(showMethod, Does.Contain("existing.Activate();"));
            Assert.That(showMethod, Does.Contain("if (focusNotes)"));
            Assert.That(showMethod, Does.Contain("var win = GetOrShow(attachments, owner, openInboxMessage, notesChanged, restorePromptFocus: restorePromptFocus);"));
            Assert.That(showMethod, Does.Contain("win?.FocusNotes();"));
            Assert.That(showMethod, Does.Contain("return existing;"));

            Assert.That(focusMethod, Does.Contain("Dispatcher.BeginInvoke(DispatcherPriority.Input"));
            Assert.That(focusMethod, Does.Contain("if (!IsVisible || _confirmingDiscard || _completingNotes || _notesCompleted)"));
            Assert.That(focusMethod, Does.Contain("Activate();"));
            Assert.That(focusMethod, Does.Contain("FocusManager.SetFocusedElement(this, editor);"));
            Assert.That(focusMethod, Does.Contain("editor.Focus();"));
            Assert.That(focusMethod, Does.Contain("Keyboard.Focus(editor);"));
            Assert.That(focusMethod, Does.Contain("editor.Select(editor.Text.Length, 0);"));
            Assert.That(focusMethod, Does.Contain("editor.ScrollToEnd();"));
            Assert.That(source, Does.Contain("ShowActivated = true;"));
        });
    }

    [Test]
    public void TranscriptAnnotation_WiresTextCompositionAndExistingVoiceStartBeforePromptFallback()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var keyboard = SourceRegion(source, "private void Window_PreviewTextInput(", "private bool TryStartTranscriptAnnotation(");
        var shortcut = SourceRegion(source, "private bool TryStartTranscriptAnnotation(", "private void AttachTranscriptFollowUp(");
        var voice = SourceRegion(source, "private void TryStartPushToTalkFromFocusedTarget(", "private void Window_Deactivated(");
        var ensure = SourceRegion(source, "private FollowUpAttachment? EnsureTranscriptFollowUp(", "private void AddTranscriptSelectionToNewChat(");
        Assert.Multiple(() =>
        {
            Assert.That(ReadSource("MainWindow.xaml"), Does.Contain("PreviewTextInput=\"Window_PreviewTextInput\""));
            Assert.That(keyboard, Does.Contain("!e.Handled && TryStartTranscriptAnnotation(e.Text, startDictation: false)"));
            Assert.That(keyboard, Does.Contain("e.Handled = true;"));
            Assert.That(shortcut, Does.Contain("Keyboard.FocusedElement as RichTextBox"));
            Assert.That(shortcut, Does.Contain("ReferenceEquals(transcript, OutputTextBox)"));
            Assert.That(shortcut, Does.Contain("_primaryAgentTranscriptHosts.Values.Any("));
            Assert.That(shortcut, Does.Contain("_secondaryTranscripts.Any("));
            Assert.That(shortcut, Does.Contain("TranscriptAnnotationShortcut.TryStart("));
            Assert.That(shortcut, Does.Contain("TryBuildTranscriptFollowUpAttachment(box)"));
            Assert.That(shortcut, Does.Contain("GetOrCreateFollowUpList(_activeTabId ?? \"\")"));
            Assert.That(shortcut, Does.Contain("EnsureTranscriptFollowUp(attachment, out wasAdded)"));
            Assert.That(shortcut, Does.Contain("PromptAttachmentViewerWindow.ShowForAnnotation("));
            Assert.That(shortcut, Does.Contain("attachment, Window.GetWindow(transcript) ?? (CanShowOwnedWindow() ? this : null)"));
            Assert.That(shortcut, Does.Contain("UpdateAttachmentNotes(list!, current, notes)"));
            Assert.That(shortcut, Does.Contain("discardNewAttachment: wasAdded ? current => RemoveCancelledTranscriptAttachment(list!, current) : null"));
            Assert.That(ensure, Does.Contain("TranscriptAnnotationShortcut.EnsureAttachment(list, attachment, out wasAdded)"));
            Assert.That(ensure, Does.Contain("PersistDraftFollowUp()"));
            Assert.That(ensure, Does.Contain("SyncQueuePanel()"));
            Assert.That(voice.IndexOf("TryStartTranscriptAnnotation(null, startDictation: true)", StringComparison.Ordinal),
                Is.LessThan(voice.IndexOf("focusedRichTextBox == ActiveTranscriptBox", StringComparison.Ordinal)));
            Assert.That(voice, Does.Contain("ResetPttState();\r\n            return;").Or.Contain("ResetPttState();\n            return;"));
        });
    }

    [Test]
    public void ViewerCloseHandoff_SourceRunsAfterClosingHandlers_WithForegroundAndOwnerSafetyGuards()
    {
        var source = ReadSource("PromptAttachmentViewerWindow.cs");
        var closing = SourceRegion(source, "protected override void OnClosing(CancelEventArgs e)",
            "private void OnClosing(object? sender, CancelEventArgs e)");
        var baseCall = closing.IndexOf("base.OnClosing(e);", StringComparison.Ordinal);
        var guard = closing.IndexOf("if (e.Cancel", StringComparison.Ordinal);
        var activation = closing.IndexOf("owner.Activate()", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(baseCall, Is.GreaterThanOrEqualTo(0));
            Assert.That(guard, Is.GreaterThan(baseCall));
            Assert.That(activation, Is.GreaterThan(guard));
            Assert.That(closing, Does.Contain("Dispatcher.HasShutdownStarted").And.Contain("Dispatcher.HasShutdownFinished"));
            Assert.That(closing, Does.Contain("!IsActive || !WindowHelper.IsForegroundWindow(this)"));
            Assert.That(closing, Does.Contain("Owner is not { IsVisible: true, IsEnabled: true } owner"));
            Assert.That(closing, Does.Contain("owner.WindowState == WindowState.Minimized"));
            Assert.That(closing[guard..activation], Does.Contain("return;"));
            Assert.That(closing, Does.Not.Contain("BeginInvoke").And.Not.Contain("SetForegroundWindow")
                .And.Not.Contain("Topmost").And.Not.Contain("Keyboard.Focus"));
            Assert.That(closing, Does.Contain("if (!owner.Activate())").And.Contain("SquadDashTrace.Write"));
        });
    }

    [Test]
    public void CancelledTranscriptRemoval_FollowsListAndReplacementIdentity_WithoutRecreatingMissingAttachments()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var removal = SourceRegion(source, "private void RemoveCancelledTranscriptAttachment(", "private FollowUpAttachment UpdateAttachmentNotes(");
        var update = SourceRegion(source, "private FollowUpAttachment UpdateAttachmentNotes(", "private void ReopenImageForEditing(");
        var click = SourceRegion(source, "private void AttachTranscriptFollowUp(", "private FollowUpAttachment? TryBuildTranscriptFollowUpAttachment(");
        var strip = SourceRegion(source, "private void UpdateFollowUpStripCore()", "private void RemoveCancelledTranscriptAttachment(");
        Assert.Multiple(() =>
        {
            foreach (var method in new[] { removal, update })
            {
                Assert.That(method, Does.Contain("_followUpAttachments.FirstOrDefault(entry => ReferenceEquals(entry.Value, list))"));
                Assert.That(method, Does.Contain("FindIndex(candidate => ReferenceEquals(candidate, current))"));
                Assert.That(method, Does.Contain("if (index < 0)"));
                Assert.That(method, Does.Contain("SquadDashTrace.Write("));
                Assert.That(method, Does.Contain("if (owner.Key.Length == 0)"));
                Assert.That(method, Does.Contain("PersistDraftFollowUp();"));
                Assert.That(method, Does.Contain("SyncQueuePanel();"));
                Assert.That(method, Does.Contain("UpdateFollowUpStrip();"));
                Assert.That(method, Does.Not.Contain("GetOrCreateFollowUpList("));
                Assert.That(method, Does.Not.Contain(".Add("));
            }
            Assert.That(removal, Does.Contain("owner.Value!.RemoveAt(index);"));
            Assert.That(removal, Does.Contain("_guidedTourCoordinator.RaiseAllAttachmentsRemoved();"));
            Assert.That(click, Does.Not.Contain("discardNewAttachment"));
            Assert.That(strip, Does.Not.Contain("discardNewAttachment"));
        });
    }

    [Test]
    public void EnqueueAndPrioritize_KeepTheListIdentityCapturedByTheOpenNotesViewer()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var enqueue = SourceRegion(source, "private void EnqueueCurrentPrompt(", "private void EnqueueRcPrompt(");
        var prioritize = SourceRegion(source, "private void PrioritizeActiveTabToFront()", "private void ShowPriorityFeedback(");
        Assert.Multiple(() =>
        {
            foreach (var method in new[] { enqueue, prioritize })
            {
                Assert.That(method, Does.Contain("_followUpAttachments.Remove(\"\");"));
                Assert.That(method, Does.Contain("_followUpAttachments[item.Id] = draftList;"));
                Assert.That(method, Does.Contain("PersistDraftFollowUp();"));
                Assert.That(method, Does.Contain("SyncQueuePanel();"));
            }
        });
    }

    [Test]
    public void NotesConfirmationAndKeyboard_SourceUsesExactPromptSafeDefaultAndShiftEnterPassThrough()
    {
        var source = ReadSource("PromptAttachmentViewerWindow.cs");
        var confirmation = SourceRegion(source, "private bool ConfirmDiscardNotes()", "internal Window CreateDiscardNotesConfirmation()");
        Assert.Multiple(() =>
        {
            Assert.That(PromptAttachmentViewerWindow.DiscardNotesPrompt,
                Is.EqualTo("Are you sure you want to lose your note edits?"));
            Assert.That(source, Does.Contain("if (HasNoteChanges && !_confirmDiscardNotes())"));
            Assert.That(source, Does.Contain("IsDefault = true, IsCancel = true"));
            Assert.That(confirmation, Does.Contain("CreateDiscardNotesConfirmation().ShowDialog() == true"));
            Assert.That(confirmation, Does.Contain("!confirmed && IsActive"));
            Assert.That(confirmation, Does.Not.Contain("Activate()").And.Not.Contain("Keyboard.Focus("));
            Assert.That(source, Does.Contain("e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None"));
            Assert.That(source, Does.Contain("Closing += OnClosing;"));
            Assert.That(source, Does.Not.Contain("Deactivated +="));
            Assert.That(source, Does.Contain("existing.BeginEditingInvocation(discardNewAttachment, restorePromptFocus);"));
        });
    }

    [Test]
    public void PromptFocusReturn_SourceCapturesInvokingPromptAndContext_ForRowTypingAndVoiceRoutes()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var capture = SourceRegion(source, "private Action<Window> CreateAttachmentPromptFocusReturn()",
            "private void AttachTranscriptFollowUp(");
        var annotation = SourceRegion(source, "private bool TryStartTranscriptAnnotation(",
            "private Action<Window> CreateAttachmentPromptFocusReturn()");
        var strip = SourceRegion(source, "private void UpdateFollowUpStripCore()", "private void RemoveCancelledTranscriptAttachment(");
        Assert.Multiple(() =>
        {
            foreach (var route in new[] { annotation, strip })
                Assert.That(route, Does.Contain("restorePromptFocus: CreateAttachmentPromptFocusReturn()"));
            Assert.That(capture, Does.Contain("var prompt = PromptTextBox;"));
            Assert.That(capture, Does.Contain("var tabId = _activeTabId;"));
            Assert.That(capture, Does.Contain("var workspace = _currentWorkspace;"));
            Assert.That(capture, Does.Contain("AttachmentPromptFocusReturn.Queue(owner, prompt, () =>"));
            Assert.That(capture, Does.Contain("!_isClosing && !_mainWindowClosingInProgress"));
            Assert.That(capture, Does.Contain("ReferenceEquals(PromptTextBox, prompt)"));
            Assert.That(capture, Does.Contain("ReferenceEquals(_currentWorkspace, workspace)"));
            Assert.That(capture, Does.Contain("string.Equals(_activeTabId, tabId, StringComparison.Ordinal)"));
            Assert.That(annotation, Does.Contain("(attachment, text, dictate) => PromptAttachmentViewerWindow.ShowForAnnotation("));
            Assert.That(annotation, Does.Contain("notes), text, dictate,"));
        });
    }

    [Test]
    public void PromptFocusReturn_SourceRecordsOnlyAcceptedClose_AndInvokesAfterAllClosedSubscribers()
    {
        var source = ReadSource("PromptAttachmentViewerWindow.cs");
        var closing = SourceRegion(source, "protected override void OnClosing(CancelEventArgs e)",
            "protected override void OnClosed(EventArgs e)");
        var closed = SourceRegion(source, "protected override void OnClosed(EventArgs e)",
            "private void OnClosing(object? sender, CancelEventArgs e)");
        var accept = SourceRegion(source, "internal async Task AcceptNotesAsync()", "internal void CancelNotes()");
        Assert.Multiple(() =>
        {
            Assert.That(closing.IndexOf("_acceptedCloseOwner = null;", StringComparison.Ordinal),
                Is.LessThan(closing.IndexOf("base.OnClosing(e);", StringComparison.Ordinal)));
            Assert.That(closing, Does.Contain("else if (_notesCloseIntent == NotesCloseIntent.Accept && _notesChanged is not null)"));
            Assert.That(closing, Does.Contain("_acceptedCloseOwner = owner;"));
            Assert.That(closing, Does.Not.Contain("_restorePromptFocus?.Invoke"));
            Assert.That(closed.IndexOf("base.OnClosed(e);", StringComparison.Ordinal),
                Is.LessThan(closed.IndexOf("restorePromptFocus?.Invoke(owner);", StringComparison.Ordinal)));
            Assert.That(closed, Does.Contain("_acceptedCloseOwner = null;"));
            Assert.That(closed, Does.Contain("_restorePromptFocus = null;"));
            Assert.That(closed, Does.Contain("if (owner is not null)"));
            Assert.That(accept.IndexOf("if (_notesCompleted)", StringComparison.Ordinal),
                Is.GreaterThan(accept.IndexOf("await _notesDictation.StopAsync();", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void PromptFocusQueue_SourceDefersUntilInputAndGuardsContextForegroundHostAndInterveningInput()
    {
        var source = ReadSource("AttachmentPromptFocusReturn.cs");
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("dispatcher.BeginInvoke(DispatcherPriority.Input"));
            Assert.That(source, Does.Contain("!ReferenceEquals(Keyboard.FocusedElement, previousFocus)"));
            Assert.That(source, Does.Contain("!ReferenceEquals(Window.GetWindow(prompt), promptHost)"));
            Assert.That(source, Does.Contain("!prompt.IsLoaded || !prompt.IsVisible || !prompt.IsEnabled || !prompt.Focusable"));
            Assert.That(source, Does.Contain("!canReturnFocus()"));
            Assert.That(source, Does.Contain("!WindowHelper.IsForegroundWindow(invokingOwner)"));
            Assert.That(source, Does.Contain("!WindowHelper.IsForegroundWindow(promptHost!)"));
            foreach (var input in new[] { "Keyboard.PreviewKeyDownEvent", "Mouse.PreviewMouseDownEvent",
                         "Mouse.PreviewMouseWheelEvent", "TextCompositionManager.PreviewTextInputEvent" })
                Assert.That(source, Does.Contain(input));
            foreach (var subscription in new[] { "invokingOwner.Deactivated", "invokingOwner.Closing",
                         "dispatcher.ShutdownStarted", "inputManager.PreProcessInput" })
            {
                Assert.That(source, Does.Contain(subscription + " +="));
                Assert.That(source, Does.Contain(subscription + " -="));
            }
            Assert.That(source, Does.Contain("Keyboard.Focus(prompt)"));
            Assert.That(source, Does.Not.Contain("Select(").And.Not.Contain("CaretPosition =")
                .And.Not.Contain("SetPlainText(").And.Not.Contain("Topmost"));
        });
    }

    private static string ReadSource(string fileName)
    {
        var root = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(root))
        {
            var candidate = Path.Combine(root, "SquadDash", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            root = Path.GetDirectoryName(root)!;
        }

        throw new FileNotFoundException($"Could not locate {fileName} under repository root.");
    }

    private static string SourceRegion(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}

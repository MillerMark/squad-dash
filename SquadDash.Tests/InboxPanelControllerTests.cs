using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace SquadDash.Tests;

/// <summary>
/// Tests for the state-management additions to <see cref="InboxPanelController"/>:
/// panel visibility (Show/Hide/Toggle), agent suggestions, and filter/unread persistence
/// delegates. All tests run on a dedicated STA thread via <see cref="WpfTestContext"/>
/// because the controller constructor requires WPF elements.
/// </summary>
[TestFixture]
internal sealed class InboxPanelControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a minimal <see cref="InboxPanelController"/> with no-op WPF elements.</summary>
    private static InboxPanelController BuildMinimal(
        bool              initialPanelVisible            = false,
        Action<bool>?     syncBorderVisibility           = null,
        Action?           flashPanel                     = null,
        Action<bool>?     setMenuChecked                 = null,
        Action?           persistVisibility              = null,
        Action<string>?   onFilterPersisted              = null,
        Action<bool>?     onUnreadOnlyPersisted          = null,
        Action?           onFilterChangedForIntelliSense = null,
        string[]?         agentSuggestions               = null)
    {
        var listPanel         = new StackPanel();
        var scrollContainer   = new ScrollViewer { Content = listPanel };
        var viewerBorder      = new Border();
        var subjectLabel      = new TextBlock();
        var metaLabel         = new TextBlock();
        var attachmentsPanel  = new WrapPanel();
        var actionsPanel      = new WrapPanel();
        var viewerBody        = new FlowDocumentScrollViewer();

        return new InboxPanelController(
            listPanel:              listPanel,
            listScrollContainer:    scrollContainer,
            viewerBorder:           viewerBorder,
            viewerSubjectLabel:     subjectLabel,
            viewerMetaLabel:        metaLabel,
            viewerAttachmentsPanel: attachmentsPanel,
            viewerBody:             viewerBody,
            markRead:               _ => { },
            markUnread:             _ => { },
            archive:                _ => { },
            delete:                 _ => { },
            viewerActionsPanel:     actionsPanel,
            onActionClicked:        (_, _) => { },
            openMessageWindow:      (_, _) => { },
            initialPanelVisible:            initialPanelVisible,
            syncBorderVisibility:           syncBorderVisibility,
            flashPanel:                     flashPanel,
            setMenuChecked:                 setMenuChecked,
            persistVisibility:              persistVisibility,
            onFilterPersisted:              onFilterPersisted,
            onUnreadOnlyPersisted:          onUnreadOnlyPersisted,
            onFilterChangedForIntelliSense: onFilterChangedForIntelliSense,
            agentSuggestions:               agentSuggestions);
    }

    // ── PanelVisible initial state ────────────────────────────────────────────

    [Test]
    public void PanelVisible_DefaultsFalse() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            Assert.That(ctrl.PanelVisible, Is.False);
        });

    [Test]
    public void PanelVisible_InitialTrue_ReturnsTrue() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal(initialPanelVisible: true);
            Assert.That(ctrl.PanelVisible, Is.True);
        });

    // ── Show ──────────────────────────────────────────────────────────────────

    [Test]
    public void Show_SetsPanelVisibleTrue() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            ctrl.Show();
            Assert.That(ctrl.PanelVisible, Is.True);
        });

    [Test]
    public void Show_CallsSyncBorderVisibilityWithTrue() =>
        WpfTestContext.Run(() =>
        {
            bool? received = null;
            var ctrl = BuildMinimal(syncBorderVisibility: v => received = v);
            ctrl.Show();
            Assert.That(received, Is.True);
        });

    [Test]
    public void Show_CallsSetMenuCheckedWithTrue() =>
        WpfTestContext.Run(() =>
        {
            bool? received = null;
            var ctrl = BuildMinimal(setMenuChecked: v => received = v);
            ctrl.Show();
            Assert.That(received, Is.True);
        });

    [Test]
    public void Show_CallsPersistVisibility() =>
        WpfTestContext.Run(() =>
        {
            int calls = 0;
            var ctrl = BuildMinimal(persistVisibility: () => calls++);
            ctrl.Show();
            Assert.That(calls, Is.EqualTo(1));
        });

    [Test]
    public void Show_WithFlashTrue_CallsFlashPanel() =>
        WpfTestContext.Run(() =>
        {
            int calls = 0;
            var ctrl = BuildMinimal(flashPanel: () => calls++);
            ctrl.Show(flash: true);
            Assert.That(calls, Is.EqualTo(1));
        });

    [Test]
    public void Show_WithFlashFalse_DoesNotCallFlashPanel() =>
        WpfTestContext.Run(() =>
        {
            int calls = 0;
            var ctrl = BuildMinimal(flashPanel: () => calls++);
            ctrl.Show(flash: false);
            Assert.That(calls, Is.EqualTo(0));
        });

    // ── Hide ──────────────────────────────────────────────────────────────────

    [Test]
    public void Hide_SetsPanelVisibleFalse() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal(initialPanelVisible: true);
            ctrl.Hide();
            Assert.That(ctrl.PanelVisible, Is.False);
        });

    [Test]
    public void Hide_CallsSyncBorderVisibilityWithFalse() =>
        WpfTestContext.Run(() =>
        {
            bool? received = null;
            var ctrl = BuildMinimal(initialPanelVisible: true, syncBorderVisibility: v => received = v);
            ctrl.Hide();
            Assert.That(received, Is.False);
        });

    [Test]
    public void Hide_CallsSetMenuCheckedWithFalse() =>
        WpfTestContext.Run(() =>
        {
            bool? received = null;
            var ctrl = BuildMinimal(initialPanelVisible: true, setMenuChecked: v => received = v);
            ctrl.Hide();
            Assert.That(received, Is.False);
        });

    [Test]
    public void Hide_CallsPersistVisibility() =>
        WpfTestContext.Run(() =>
        {
            int calls = 0;
            var ctrl = BuildMinimal(initialPanelVisible: true, persistVisibility: () => calls++);
            ctrl.Hide();
            Assert.That(calls, Is.EqualTo(1));
        });

    // ── Toggle ────────────────────────────────────────────────────────────────

    [Test]
    public void Toggle_WhenHidden_ShowsPanel() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            ctrl.Toggle();
            Assert.That(ctrl.PanelVisible, Is.True);
        });

    [Test]
    public void Toggle_WhenVisible_HidesPanel() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal(initialPanelVisible: true);
            ctrl.Toggle();
            Assert.That(ctrl.PanelVisible, Is.False);
        });

    [Test]
    public void Toggle_WhenHidden_DoesNotCallFlash() =>
        WpfTestContext.Run(() =>
        {
            int calls = 0;
            var ctrl = BuildMinimal(flashPanel: () => calls++);
            ctrl.Toggle();
            Assert.That(calls, Is.EqualTo(0));
        });

    // ── AgentSuggestions ──────────────────────────────────────────────────────

    [Test]
    public void AgentSuggestions_DefaultsEmpty() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            Assert.That(ctrl.AgentSuggestions, Is.Empty);
        });

    [Test]
    public void AgentSuggestions_PassedViaConstructor_ReturnsValue() =>
        WpfTestContext.Run(() =>
        {
            var suggestions = new[] { "alice", "bob" };
            var ctrl = BuildMinimal(agentSuggestions: suggestions);
            Assert.That(ctrl.AgentSuggestions, Is.EquivalentTo(suggestions));
        });

    [Test]
    public void SetAgentSuggestions_UpdatesProperty() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            ctrl.SetAgentSuggestions(["alice", "bob"]);
            Assert.That(ctrl.AgentSuggestions, Is.EquivalentTo(new[] { "alice", "bob" }));
        });

    // ── HandleFilterTextChanged ───────────────────────────────────────────────

    [Test]
    public void HandleFilterTextChanged_CallsOnFilterPersisted() =>
        WpfTestContext.Run(() =>
        {
            string? persisted = null;
            var ctrl = BuildMinimal(onFilterPersisted: t => persisted = t);
            ctrl.HandleFilterTextChanged("hello");
            Assert.That(persisted, Is.EqualTo("hello"));
        });

    [Test]
    public void HandleFilterTextChanged_CallsOnFilterChangedForIntelliSense() =>
        WpfTestContext.Run(() =>
        {
            int calls = 0;
            var ctrl = BuildMinimal(onFilterChangedForIntelliSense: () => calls++);
            ctrl.HandleFilterTextChanged("test");
            Assert.That(calls, Is.EqualTo(1));
        });

    [Test]
    public void HandleFilterTextChanged_NullDelegates_DoesNotThrow() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            Assert.DoesNotThrow(() => ctrl.HandleFilterTextChanged("any"));
        });

    // ── HandleUnreadOnlyChanged ───────────────────────────────────────────────

    [Test]
    public void HandleUnreadOnlyChanged_True_CallsOnUnreadOnlyPersistedWithTrue() =>
        WpfTestContext.Run(() =>
        {
            bool? received = null;
            var ctrl = BuildMinimal(onUnreadOnlyPersisted: v => received = v);
            ctrl.HandleUnreadOnlyChanged(true);
            Assert.That(received, Is.True);
        });

    [Test]
    public void HandleUnreadOnlyChanged_False_CallsOnUnreadOnlyPersistedWithFalse() =>
        WpfTestContext.Run(() =>
        {
            bool? received = null;
            var ctrl = BuildMinimal(onUnreadOnlyPersisted: v => received = v);
            ctrl.HandleUnreadOnlyChanged(false);
            Assert.That(received, Is.False);
        });

    [Test]
    public void HandleUnreadOnlyChanged_NullDelegate_DoesNotThrow() =>
        WpfTestContext.Run(() =>
        {
            var ctrl = BuildMinimal();
            Assert.DoesNotThrow(() => ctrl.HandleUnreadOnlyChanged(true));
        });
}

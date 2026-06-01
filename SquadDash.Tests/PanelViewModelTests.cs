using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PanelViewModelTests
{
    // ── InboxPanelViewModel ──────────────────────────────────────────────────

    [Test]
    public void InboxPanelViewModel_DefaultValues_AreCorrect()
    {
        var vm = new InboxPanelViewModel();

        Assert.That(vm.Messages,         Is.Not.Null);
        Assert.That(vm.Messages,         Is.Empty);
        Assert.That(vm.FilterText,        Is.EqualTo(string.Empty));
        Assert.That(vm.UnreadOnly,        Is.False);
        Assert.That(vm.SelectedMessage,   Is.Null);
    }

    [Test]
    public void InboxPanelViewModel_UnreadOnly_CanBeToggled()
    {
        var vm = new InboxPanelViewModel();
        vm.UnreadOnly = true;
        Assert.That(vm.UnreadOnly, Is.True);
    }

    [Test]
    public void InboxPanelViewModel_SelectedMessage_CanBeSetAndCleared()
    {
        var vm  = new InboxPanelViewModel();
        var msg = new InboxMessage { Id = "m1", Subject = "Hello" };
        vm.SelectedMessage = msg;
        Assert.That(vm.SelectedMessage, Is.SameAs(msg));
        vm.SelectedMessage = null;
        Assert.That(vm.SelectedMessage, Is.Null);
    }

    // ── TasksPanelViewModel ──────────────────────────────────────────────────

    [Test]
    public void TasksPanelViewModel_DefaultValues_AreCorrect()
    {
        var vm = new TasksPanelViewModel();

        Assert.That(vm.ShowCompleted, Is.False);
        Assert.That(vm.FilterText,    Is.EqualTo(string.Empty));
    }

    [Test]
    public void TasksPanelViewModel_ShowCompleted_CanBeToggled()
    {
        var vm = new TasksPanelViewModel();
        vm.ShowCompleted = true;
        Assert.That(vm.ShowCompleted, Is.True);
    }

    // ── NotesPanelViewModel ──────────────────────────────────────────────────

    [Test]
    public void NotesPanelViewModel_DefaultValues_AreCorrect()
    {
        var vm = new NotesPanelViewModel();

        Assert.That(vm.Notes,      Is.Not.Null);
        Assert.That(vm.Notes,      Is.Empty);
        Assert.That(vm.FilterText, Is.EqualTo(string.Empty));
        Assert.That(vm.SortOrder,  Is.EqualTo(NotesSortOrder.MostRecentOnTop));
    }

    [Test]
    public void NotesPanelViewModel_SortOrder_CanBeChangedFromDefault()
    {
        var vm = new NotesPanelViewModel();
        vm.SortOrder = NotesSortOrder.Alphabetical;
        Assert.That(vm.SortOrder, Is.EqualTo(NotesSortOrder.Alphabetical));
    }

    [Test]
    public void NotesPanelViewModel_Notes_CanBeReplaced()
    {
        var vm = new NotesPanelViewModel();
        vm.Notes = new List<NoteItem>
        {
            new NoteItem(Guid.NewGuid(), "First",  0),
            new NoteItem(Guid.NewGuid(), "Second", 1),
        };
        Assert.That(vm.Notes.Count, Is.EqualTo(2));
    }

    // ── MaintenancePanelViewModel ────────────────────────────────────────────

    [Test]
    public void MaintenancePanelViewModel_DefaultValues_AreCorrect()
    {
        var vm = new MaintenancePanelViewModel();

        Assert.That(vm.Config,             Is.Null);
        Assert.That(vm.RunnerActive,        Is.False);
        Assert.That(vm.NextMaintenanceAt,   Is.EqualTo(DateTimeOffset.MaxValue));
    }

    [Test]
    public void MaintenancePanelViewModel_RunnerActive_CanBeSetToTrue()
    {
        var vm = new MaintenancePanelViewModel();
        vm.RunnerActive = true;
        Assert.That(vm.RunnerActive, Is.True);
    }

    [Test]
    public void MaintenancePanelViewModel_TaskOptionsPanels_IsInitializedEmpty()
    {
        var vm = new MaintenancePanelViewModel();
        Assert.That(vm.TaskOptionsPanels, Is.Not.Null);
        Assert.That(vm.TaskOptionsPanels.Count, Is.EqualTo(0));
    }

    // ── PromptExecutionController.BuildQueuedQuestionInboxHint ───────────────

    [Test]
    public void BuildQueuedQuestionInboxHint_ReturnsExpectedKeyPhrases()
    {
        var hint = PromptExecutionController.BuildQueuedQuestionInboxHint();

        Assert.That(hint, Does.Contain("stepped away"));
        Assert.That(hint, Does.Contain("inbox message"));
        Assert.That(hint, Does.Contain("brief or trivial"));
    }
}

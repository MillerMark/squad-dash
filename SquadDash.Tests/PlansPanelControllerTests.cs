using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="PlansPanelController"/> — panel visibility, plan insertion,
/// deduplication, watcher refresh, and the RevealCollectedPlan convenience method.
/// All tests run on a dedicated STA thread via <see cref="WpfTestContext"/> because
/// the controller creates WPF elements.
/// </summary>
[TestFixture]
internal sealed class PlansPanelControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Plan MakePlan(
        string planId   = "test-plan-1",
        string title    = "Test Plan",
        string status   = "approved",
        string branch   = "feature/test",
        string revision = "rev1") =>
        new(
            PlanId:          planId,
            Revision:        revision,
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: status,
            Title:           title,
            Branch:          branch,
            Summary:         "A test plan.",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(0, 0),
            Timestamps:      new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));

    private static (PlansPanelController controller,
                    StackPanel activePanel,
                    StackPanel completedPanel,
                    List<bool> visibilityCalls)
        BuildController(Func<bool>? isPromptRunning = null)
    {
        var activePanel    = new StackPanel();
        var completedPanel = new StackPanel();
        var completedSection = new Border();
        var archivedPanel = new StackPanel();
        var archivedSection = new Border();
        var visibilityCalls  = new List<bool>();

        var controller = new PlansPanelController(
            activePanel:          activePanel,
            completedPanel:       completedPanel,
            completedSection:     completedSection,
            archivedPanel:        archivedPanel,
            archivedSection:      archivedSection,
            openPlan:             _ => { },
            syncBorderVisibility: v => visibilityCalls.Add(v),
            setMenuChecked:       _ => { },
            persistVisibility:    () => { },
            isPromptRunning:      isPromptRunning);

        return (controller, activePanel, completedPanel, visibilityCalls);
    }

    // ── Hidden panel is revealed ──────────────────────────────────────────────

    [Test]
    public void RevealCollectedPlan_HiddenPanel_BecomesVisible() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, _, _) = BuildController();
            Assert.That(ctrl.PanelVisible, Is.False);

            ctrl.RevealCollectedPlan(MakePlan());

            Assert.That(ctrl.PanelVisible, Is.True);
        });

    // ── Visible panel stays visible ───────────────────────────────────────────

    [Test]
    public void RevealCollectedPlan_AlreadyVisible_StaysVisible() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, _, _) = BuildController();
            ctrl.Show();
            Assert.That(ctrl.PanelVisible, Is.True);

            ctrl.RevealCollectedPlan(MakePlan());

            Assert.That(ctrl.PanelVisible, Is.True);
        });

    // ── No row duplication ────────────────────────────────────────────────────

    [Test]
    public void RevealCollectedPlan_CalledTwiceSamePlan_NoDuplicateRows() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan();

            ctrl.RevealCollectedPlan(plan);
            ctrl.RevealCollectedPlan(plan);

            var borders = activePanel.Children.OfType<Border>()
                .Where(b => b.Tag is string s && s == plan.PlanId)
                .ToList();
            Assert.That(borders, Has.Count.EqualTo(1));
        });

    // ── Watcher refresh replaces stale rows ───────────────────────────────────

    [Test]
    public void OnPlanChanged_UpdatedPlan_ReplacesExistingEntry() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan(title: "Original");
            ctrl.RevealCollectedPlan(plan);

            var updated = plan with { Title = "Updated" };
            ctrl.OnPlanChanged(updated);

            var borders = activePanel.Children.OfType<Border>()
                .Where(b => b.Tag is string s && s == plan.PlanId)
                .ToList();
            Assert.That(borders, Has.Count.EqualTo(1));
        });

    [Test]
    public void OnPlanChanged_NewRevision_ShowsUpdatedBadgeUntilPlanIsOpened() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan();
            ctrl.Refresh([plan]);

            ctrl.OnPlanChanged(plan with { Revision = "rev2", RevisionNumber = 2 });

            var updatedRow = activePanel.Children.OfType<Border>().Single();
            var updatedTitleRow = (StackPanel)((StackPanel)updatedRow.Child).Children[0];
            Assert.That(updatedTitleRow.Children.OfType<TextBlock>().Any(text => text.Text == "Updated"), Is.True);
            Assert.That(updatedRow.Effect, Is.Not.Null);

            var openItem = updatedRow.ContextMenu!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Open Plan"));
            openItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var acknowledgedRow = activePanel.Children.OfType<Border>().Single();
            var acknowledgedTitleRow = (StackPanel)((StackPanel)acknowledgedRow.Child).Children[0];
            Assert.That(acknowledgedTitleRow.Children.OfType<TextBlock>().Any(text => text.Text == "Updated"), Is.False);
        });

    [Test]
    public void OnPlanChanged_NewerRevisionWithReopenedStep_UpdatesVisibleProgressBackward() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan() with
            {
                Progress = new PlanProgress(7, 8),
                RevisionNumber = 1,
            };
            ctrl.Show();
            ctrl.Refresh([plan]);

            ctrl.OnPlanChanged(plan with
            {
                Revision = "rev2",
                RevisionNumber = 2,
                LifecycleStatus = PlanLifecycleStatus.Interrupted,
                Progress = new PlanProgress(6, 8),
            });

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var progressRow = rowStack.Children.OfType<StackPanel>()
                .Single(panel => panel.Children.OfType<ProgressBar>().Any());
            Assert.Multiple(() =>
            {
                Assert.That(progressRow.Children.OfType<ProgressBar>().Single().Value, Is.EqualTo(6));
                Assert.That(progressRow.Children.OfType<TextBlock>().Single().Text, Is.EqualTo("6/8 complete"));
            });
        });

    [Test]
    public void OnPlanChanged_OlderRevisionCannotOverwriteReopenedProgress() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var current = MakePlan(revision: "rev2") with
            {
                RevisionNumber = 2,
                Progress = new PlanProgress(6, 8),
            };
            ctrl.Refresh([current]);

            ctrl.OnPlanChanged(current with
            {
                Revision = "rev1",
                RevisionNumber = 1,
                Progress = new PlanProgress(7, 8),
            });

            var rowStack = (StackPanel)activePanel.Children.OfType<Border>().Single().Child;
            var progressRow = rowStack.Children.OfType<StackPanel>()
                .Single(panel => panel.Children.OfType<ProgressBar>().Any());
            Assert.That(progressRow.Children.OfType<TextBlock>().Single().Text, Is.EqualTo("6/8 complete"));
        });

    [Test]
    public void ContextMenu_Revise_InvokesRevisionDraftCallback() =>
        WpfTestContext.Run(() =>
        {
            Plan? revised = null;
            var active = new StackPanel();
            var controller = new PlansPanelController(
                active,
                new StackPanel(),
                new Border(),
                new StackPanel(),
                new Border(),
                _ => { },
                revisePlan: plan => revised = plan);
            var plan = MakePlan();
            controller.Refresh([plan]);

            var row = active.Children.OfType<Border>().Single();
            var reviseItem = row.ContextMenu!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Revise"));
            reviseItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.That(revised, Is.SameAs(plan));
        });

    // ── Different plans create separate rows ──────────────────────────────────

    [Test]
    public void RevealCollectedPlan_DifferentPlans_CreatesSeparateRows() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var planA = MakePlan(planId: "plan-a", title: "Plan A");
            var planB = MakePlan(planId: "plan-b", title: "Plan B");

            ctrl.RevealCollectedPlan(planA);
            ctrl.RevealCollectedPlan(planB);

            var tags = activePanel.Children.OfType<Border>()
                .Select(b => b.Tag as string)
                .Where(t => t is not null)
                .ToList();
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That(tags, Does.Contain("plan-a"));
            Assert.That(tags, Does.Contain("plan-b"));
        });

    // ── SyncBorderVisibility called on reveal ─────────────────────────────────

    [Test]
    public void RevealCollectedPlan_TriggersSyncBorderVisibility() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, _, visibilityCalls) = BuildController();

            ctrl.RevealCollectedPlan(MakePlan());

            Assert.That(visibilityCalls, Does.Contain(true));
        });

    // ── Row Tag matches PlanId ────────────────────────────────────────────────

    [Test]
    public void BuildRow_SetsTagToPlanId() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan(planId: "my-unique-id");

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>()
                .FirstOrDefault(b => b.Tag is string s && s == "my-unique-id");
            Assert.That(row, Is.Not.Null);
        });

    [Test]
    public void ExecutingPlan_UsesPortraitProgressAndActivityRows() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController(isPromptRunning: () => true);
            var tasks = Enumerable.Range(1, 8)
                .Select(index => new PlanTask(
                    $"task-{index}", $"Task {index}", "Work", [], "normal",
                    index <= 3 ? PlanTaskStatus.Complete :
                    index == 5 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending))
                .ToArray();
            var plan = MakePlan(status: PlanLifecycleStatus.Executing) with
            {
                Tasks = tasks,
                Progress = new PlanProgress(3, 8, ExecutingTaskId: "task-5"),
            };

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var progressRow = (StackPanel)rowStack.Children[1];
            var progressText = progressRow.Children.OfType<TextBlock>()
                .Single(block => block.Text == "3/8 complete");
            var spinner = progressRow.Children.OfType<TextBlock>()
                .Single(block => block.Text == UiTimingConstants.ToolSpinnerFrames[0]);
            var activityText = (TextBlock)rowStack.Children[2];

            Assert.Multiple(() =>
            {
                Assert.That(progressText.Text, Is.EqualTo("3/8 complete"));
                Assert.That(activityText.Text, Is.EqualTo("Step 5 running"));
                Assert.That(spinner.Text, Is.EqualTo(UiTimingConstants.ToolSpinnerFrames[0]));
            });
        });

    [Test]
    public void ExecutingPlan_NotRunning_ShowsNoIcon() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController(isPromptRunning: () => false);
            var plan = MakePlan(status: PlanLifecycleStatus.Executing) with
            {
                Tasks = [new PlanTask("t1", "Task 1", "Work", [], "normal", PlanTaskStatus.Executing)],
                Progress = new PlanProgress(0, 1, ExecutingTaskId: "t1"),
            };

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var titleRow = (StackPanel)rowStack.Children[0];
            var statusIcon = (TextBlock)titleRow.Children[0];

            Assert.That(statusIcon.Text, Is.EqualTo("⟳"));
        });

    [Test]
    public void ExecutingPlan_AdvanceFrame_UpdatesSpinnerText() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController(isPromptRunning: () => true);
            var plan = MakePlan(status: PlanLifecycleStatus.Executing) with
            {
                Tasks = [new PlanTask("t1", "Task 1", "Work", [], "normal", PlanTaskStatus.Executing)],
                Progress = new PlanProgress(0, 1, ExecutingTaskId: "t1"),
            };

            ctrl.Refresh([plan]);
            ctrl.AdvancePlanActivityFrame(3);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var progressRow = (StackPanel)rowStack.Children[1];
            var statusIcon = progressRow.Children.OfType<TextBlock>()
                .Single(block => block.Text == UiTimingConstants.ToolSpinnerFrames[3]);

            Assert.That(statusIcon.Text, Is.EqualTo(UiTimingConstants.ToolSpinnerFrames[3]));
        });

    [Test]
    public void ArchivedPlan_IsHiddenUntilShowArchivedIsEnabled() =>
        WpfTestContext.Run(() =>
        {
            var active = new StackPanel();
            var completed = new StackPanel();
            var completedSection = new Border();
            var archived = new StackPanel();
            var archivedSection = new Border();
            var controller = new PlansPanelController(
                active, completed, completedSection, archived, archivedSection, _ => { });
            var plan = MakePlan(status: PlanLifecycleStatus.Archived);

            controller.Refresh([plan]);
            Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Collapsed));

            controller.SetShowArchived(true);
            Assert.Multiple(() =>
            {
                Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(archived.Children.OfType<Border>().Single().Tag, Is.EqualTo(plan.PlanId));
            });
        });

    [Test]
    public void Refresh_OrdersPlansByMostRecentRunAcrossCompletionStates() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, completedPanel, _) = BuildController();
            var now = DateTimeOffset.UtcNow;
            var olderActive = MakePlan("older-active", "Older Active") with
            {
                Timestamps = new PlanTimestamps(now.AddDays(-4), LastRunAt: now.AddDays(-2)),
            };
            var newerActive = MakePlan("newer-active", "Newer Active") with
            {
                Timestamps = new PlanTimestamps(now.AddDays(-5), LastRunAt: now),
            };
            var completed = MakePlan("completed", "Completed", PlanLifecycleStatus.Completed) with
            {
                Timestamps = new PlanTimestamps(now.AddDays(-3), CompletedAt: now.AddDays(-1), LastRunAt: now.AddDays(-1)),
            };

            ctrl.Refresh([olderActive, completed, newerActive]);

            Assert.Multiple(() =>
            {
                Assert.That(activePanel.Children.OfType<Border>().Select(row => row.Tag),
                    Is.EqualTo(new object[] { "newer-active", "older-active" }));
                Assert.That(completedPanel.Children.OfType<Border>().Select(row => row.Tag),
                    Is.EqualTo(new object[] { "completed" }));
            });
        });
}

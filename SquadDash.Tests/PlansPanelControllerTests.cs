using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        BuildController(
            Func<bool>? isPromptRunning = null,
            Action<Plan>? archivePlan = null,
            Action<Plan>? startPlan = null,
            Action<Plan>? resumePlan = null,
            Func<string, bool>? isSimulationOverlayPlanId = null,
            Action<Plan>? restorePlan = null,
            StackPanel? archivedPanel = null)
    {
        var activePanel    = new StackPanel();
        var completedPanel = new StackPanel();
        var completedSection = new Border();
        archivedPanel ??= new StackPanel();
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
            isPromptRunning:      isPromptRunning,
            archivePlan:          archivePlan,
            startPlan:            startPlan,
            resumePlan:           resumePlan,
            restorePlan:          restorePlan,
            isSimulationOverlayPlanId: isSimulationOverlayPlanId);

        return (controller, activePanel, completedPanel, visibilityCalls);
    }

    [TestCase(PlanLifecycleStatus.Archived, false, true, true)]
    [TestCase(PlanLifecycleStatus.Archived, true, true, false)]
    [TestCase(PlanLifecycleStatus.Archived, false, false, false)]
    [TestCase(PlanLifecycleStatus.Approved, false, true, false)]
    [TestCase(PlanLifecycleStatus.Interrupted, false, true, false)]
    [TestCase(PlanLifecycleStatus.Completed, false, true, false)]
    public void ReactivateMenu_OnlyAvailableForDurableArchivedPlans(
        string status, bool simulation, bool canRestore, bool expected) =>
        WpfTestContext.Run(() =>
        {
            var archived = new StackPanel();
            var (controller, active, completed, _) = BuildController(
                restorePlan: canRestore ? _ => { } : null,
                archivedPanel: archived,
                isSimulationOverlayPlanId: _ => simulation);
            controller.Refresh([MakePlan(status: status)]);

            var row = active.Children.OfType<Border>()
                .Concat(completed.Children.OfType<Border>())
                .Concat(archived.Children.OfType<Border>()).Single();
            Assert.That(row.ContextMenu!.Items.OfType<MenuItem>()
                .Any(item => Equals(item.Header, "Reactivate")), Is.EqualTo(expected));
        });

    [TestCase(PlanLifecycleStatus.Staged, false)]
    [TestCase(PlanLifecycleStatus.Approved, false)]
    [TestCase(PlanLifecycleStatus.Interrupted, false)]
    [TestCase(PlanLifecycleStatus.WaitingForCleanWorktree, false)]
    [TestCase(PlanLifecycleStatus.Blocked, false)]
    [TestCase(PlanLifecycleStatus.Stopped, true)]
    [TestCase(PlanLifecycleStatus.Completed, true)]
    public void ReactivateMenu_RestoresPersistedLifecycleAndGroupingWithoutExecution(
        string status, bool terminal) =>
        WpfTestContext.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "SquadDashReactivateTests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new PlanStore(folder);
                var original = MakePlan(status: status) with
                {
                    Tasks =
                    [
                        new PlanTask("t1", "Task 1", "Accepted work", [], "normal", PlanTaskStatus.Complete),
                        new PlanTask("t2", "Task 2", "Remaining work", [], "normal",
                            terminal ? PlanTaskStatus.Complete : PlanTaskStatus.Pending),
                    ],
                    Progress = new PlanProgress(terminal ? 2 : 1, 2),
                    IsPaused = status == PlanLifecycleStatus.Interrupted,
                };
                store.Save(PlanStoreUpdater.ApplyArchived(original));
                var archivedPlan = store.Load(original.PlanId)!;
                var archived = new StackPanel();
                var restoreCalls = 0;
                Plan? restored = null;
                var (controller, active, completed, _) = BuildController(
                    archivedPanel: archived,
                    archivePlan: _ => Assert.Fail("Reactivate must not archive."),
                    startPlan: _ => Assert.Fail("Reactivate must not start execution."),
                    resumePlan: _ => Assert.Fail("Reactivate must not resume execution."),
                    restorePlan: plan =>
                    {
                        restoreCalls++;
                        Assert.That(plan, Is.SameAs(archivedPlan));
                        var current = store.Load(plan.PlanId)!;
                        restored = store.Save(PlanStoreUpdater.ApplyRestoredForRevision(current));
                    });
                controller.Refresh([archivedPlan]);
                controller.SetShowArchived(true);

                var menu = archived.Children.OfType<Border>().Single().ContextMenu!;
                var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray();
                Assert.That(headers, Does.Not.Contain("Start Plan"));
                Assert.That(headers, Does.Not.Contain("Resume Plan"));
                Assert.That(headers, Does.Not.Contain("Assess && Continue"));
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Reactivate"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.That(restoreCalls, Is.EqualTo(1));
                Assert.That(restored, Is.Not.Null);
                var reloaded = store.Load(original.PlanId)!;
                Assert.That(reloaded.LifecycleStatus, Is.EqualTo(status));
                Assert.That(reloaded.LifecycleStatus, Is.Not.EqualTo(PlanLifecycleStatus.Executing));
                Assert.That(reloaded.Progress, Is.EqualTo(original.Progress));
                Assert.That(JsonSerializer.Serialize(reloaded.Tasks), Is.EqualTo(JsonSerializer.Serialize(original.Tasks)));
                Assert.That(reloaded.IsPaused, Is.EqualTo(original.IsPaused));
                Assert.That(reloaded.Timestamps, Is.EqualTo(original.Timestamps));
                Assert.That(reloaded.PreArchiveLifecycleStatus, Is.Null);

                controller.OnPlanChanged(reloaded);
                Assert.That(archived.Children.OfType<Border>(), Is.Empty);
                Assert.That((terminal ? completed : active).Children.OfType<Border>().Single().Tag,
                    Is.EqualTo(original.PlanId));
                controller.Refresh(store.LoadAll());
                Assert.That(archived.Children.OfType<Border>(), Is.Empty);
                Assert.That((terminal ? completed : active).Children.OfType<Border>().Single().Tag,
                    Is.EqualTo(original.PlanId));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
        });

    [TestCase(PlanLifecycleStatus.Staged, "Archive…")]
    [TestCase(PlanLifecycleStatus.Approved, "Archive…")]
    [TestCase(PlanLifecycleStatus.Interrupted, "Archive…")]
    [TestCase(PlanLifecycleStatus.WaitingForCleanWorktree, "Archive…")]
    [TestCase(PlanLifecycleStatus.Blocked, "Archive…")]
    [TestCase(PlanLifecycleStatus.Stopped, "Archive…")]
    [TestCase(PlanLifecycleStatus.Completed, "Archive Plan")]
    [TestCase(PlanLifecycleStatus.Executing, null)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval, null)]
    public void ArchiveMenu_VisibilityDoesNotDependOnResume(string status, string? expectedHeader) =>
        WpfTestContext.Run(() =>
        {
            Plan? archived = null;
            var (controller, active, completed, _) = BuildController(archivePlan: plan => archived = plan);
            var plan = MakePlan(status: status);
            controller.Refresh([plan]);

            var row = active.Children.OfType<Border>().Concat(completed.Children.OfType<Border>()).Single();
            var archiveItem = row.ContextMenu!.Items.OfType<MenuItem>()
                .SingleOrDefault(item => item.Header is string header && header.StartsWith("Archive"));
            Assert.That(archiveItem?.Header, Is.EqualTo(expectedHeader));
            if (archiveItem is not null)
            {
                archiveItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.That(archived, Is.SameAs(plan));
            }
        });

    [TestCase(PlanLifecycleStatus.Interrupted, false, "Assess && Continue")]
    [TestCase(PlanLifecycleStatus.Interrupted, true, "Resume Plan")]
    [TestCase(PlanLifecycleStatus.WaitingForCleanWorktree, false, "Resume Plan")]
    public void ArchiveMenu_CoexistsWithUnchangedResumeAction(string status, bool safeRework, string resumeHeader) =>
        WpfTestContext.Run(() =>
        {
            Plan? resumed = null;
            Plan? assessed = null;
            var (controller, active, _, _) = BuildController(
                archivePlan: _ => { },
                startPlan: plan => resumed = plan,
                resumePlan: plan => assessed = plan);
            var plan = MakePlan(status: status) with
            {
                InterruptionData = safeRework
                    ? new PlanInterruptionData(PlanRecoveryResumePolicy.BuildReworkPreflightReason("Blocked"),
                        PlanRecoveryState.PendingRecovery, 1)
                    : null,
            };
            controller.Refresh([plan]);

            var menu = active.Children.OfType<Border>().Single().ContextMenu!;
            Assert.That(menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Archive…")), Is.True);
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, resumeHeader))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(resumeHeader == "Resume Plan" ? resumed : assessed, Is.SameAs(plan));
            Assert.That(resumeHeader == "Resume Plan" ? assessed : resumed, Is.Null);
        });

    [Test]
    public void ArchiveMenu_SimulationOverlayIsNotArchivable() =>
        WpfTestContext.Run(() =>
        {
            var (controller, active, _, _) = BuildController(
                archivePlan: _ => Assert.Fail("Simulation must not archive durable plans."),
                isSimulationOverlayPlanId: _ => true);
            controller.Refresh([MakePlan(status: PlanLifecycleStatus.Interrupted)]);

            var menu = active.Children.OfType<Border>().Single().ContextMenu!;
            Assert.That(menu.Items.OfType<MenuItem>()
                .Any(item => item.Header is string header && header.StartsWith("Archive")), Is.False);
        });

    [TestCase(PlanLifecycleStatus.Staged)]
    [TestCase(PlanLifecycleStatus.Approved)]
    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.WaitingForCleanWorktree)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    [TestCase(PlanLifecycleStatus.Stopped)]
    public void ArchiveConfirmation_IncompletePlanRequiresExplicitConsent(string status)
    {
        var plan = MakePlan(status: status);
        foreach (var consent in new[] { false, true })
        {
            var confirmationCount = 0;
            var allowed = PlanArchivePolicy.ConfirmArchive(plan, () =>
            {
                confirmationCount++;
                return consent;
            });
            Assert.That(confirmationCount, Is.EqualTo(1));
            Assert.That(allowed, Is.EqualTo(consent));
        }
    }

    [TestCase(PlanLifecycleStatus.Completed, true)]
    [TestCase(PlanLifecycleStatus.Executing, false)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval, false)]
    [TestCase(PlanLifecycleStatus.Archived, false)]
    public void ArchiveConfirmation_CompletedOrUnavailablePlanDoesNotPrompt(string status, bool expected)
    {
        var allowed = PlanArchivePolicy.ConfirmArchive(MakePlan(status: status), () =>
        {
            Assert.Fail("This plan must not show an incomplete-plan confirmation.");
            return false;
        });
        Assert.That(allowed, Is.EqualTo(expected));
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
            var updatedTitleRow = (Grid)((StackPanel)updatedRow.Child).Children[0];
            Assert.That(updatedTitleRow.Children.OfType<TextBlock>().Any(text => text.Text == "Updated"), Is.True);
            Assert.That(updatedRow.Effect, Is.Not.Null);

            var openItem = updatedRow.ContextMenu!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Open Plan"));
            openItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var acknowledgedRow = activePanel.Children.OfType<Border>().Single();
            var acknowledgedTitleRow = (Grid)((StackPanel)acknowledgedRow.Child).Children[0];
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
                LifecycleStatus = PlanLifecycleStatus.Interrupted,
                RevisionNumber = 2,
                Progress = new PlanProgress(6, 8),
            };
            ctrl.Refresh([current]);

            ctrl.OnPlanChanged(current with
            {
                Revision = "rev1",
                RevisionNumber = 1,
                LifecycleStatus = PlanLifecycleStatus.Interrupted,
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

    [Test]
    public void ContextMenu_Rename_PreservesEditorAcrossLiveUpdateAndShowsSavedAlias() =>
        WpfTestContext.Run(() =>
        {
            var active = new StackPanel();
            var plan = MakePlan(title: "Original") with
            {
                Tasks = [new PlanTask("t1", "Task 1", "Work", [], "normal", PlanTaskStatus.Executing)],
                Progress = new PlanProgress(0, 1, "t1"),
                LifecycleStatus = PlanLifecycleStatus.Executing,
            };
            var latest = plan with { Progress = new PlanProgress(1, 1, null) };
            var controller = new PlansPanelController(
                active, new StackPanel(), new Border(), new StackPanel(), new Border(), _ => { },
                renamePlan: (_, title) => latest with { DisplayTitle = title });
            controller.Refresh([plan]);

            var row = active.Children.OfType<Border>().Single();
            var renameItem = row.ContextMenu!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Rename"));
            renameItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var editor = ((Grid)((StackPanel)row.Child).Children[0]).Children.OfType<TextBox>().Single();
            editor.Text = "Renamed while running";

            controller.OnPlanChanged(latest);
            Assert.That(((Grid)((StackPanel)row.Child).Children[0]).Children.OfType<TextBox>().Single(),
                Is.SameAs(editor), "A live progress refresh must not tear down the active editor.");

            editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            var rebuiltRow = active.Children.OfType<Border>().Single();
            var rebuiltStack = (StackPanel)rebuiltRow.Child;
            var title = ((Grid)rebuiltStack.Children[0]).Children.OfType<TextBlock>()
                .Single(block => block.Text == "Renamed while running");
            var progress = rebuiltStack.Children.OfType<StackPanel>()
                .Single(panel => panel.Children.OfType<ProgressBar>().Any())
                .Children.OfType<TextBlock>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(title.Text, Is.EqualTo("Renamed while running"));
                Assert.That(progress.Text, Is.EqualTo("1/1 complete"));
            });
        });

    [Test]
    public void ContextMenu_Rename_SaveFailureKeepsEnteredTextAvailable() =>
        WpfTestContext.Run(() =>
        {
            var active = new StackPanel();
            var controller = new PlansPanelController(
                active, new StackPanel(), new Border(), new StackPanel(), new Border(), _ => { },
                renamePlan: (_, _) => throw new IOException("locked"));
            controller.Refresh([MakePlan(title: "Original")]);

            var row = active.Children.OfType<Border>().Single();
            row.ContextMenu!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Rename"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var editor = ((Grid)((StackPanel)row.Child).Children[0]).Children.OfType<TextBox>().Single();
            editor.Text = "Do not lose this";
            editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            Assert.Multiple(() =>
            {
                Assert.That(editor.Text, Is.EqualTo("Do not lose this"));
                Assert.That(editor.Parent, Is.Not.Null);
                Assert.That(editor.ToolTip, Is.Not.Null);
            });
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
            var progressRow = (StackPanel)rowStack.Children[1];
            var statusIcon = progressRow.Children.OfType<TextBlock>()
                .FirstOrDefault(block => UiTimingConstants.ToolSpinnerFrames.Contains(block.Text));

            Assert.That(statusIcon, Is.Null);
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

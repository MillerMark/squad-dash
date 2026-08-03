using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Focused lifecycle control tests verifying plan-owned pause-after-step and abort
/// actions, resume semantics (skip evidence assessment for user-paused plans),
/// abort work preservation, archive persistence, and restart round-trip safety.
/// </summary>
[TestFixture]
internal sealed class PlanLifecycleControlValidationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Plan MakeExecutingPlan(string planId = "CTRL-001", int completed = 2, int total = 5) =>
        new(
            PlanId:          planId,
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Control Validation Plan",
            Branch:          "feature/control-test",
            Summary:         "Tests for plan lifecycle controls.",
            Tasks:           Enumerable.Range(1, total).Select(i => new PlanTask(
                $"{planId}-{i:D3}",
                $"Step {i}",
                $"Task {i} description",
                i == 1 ? [] : [$"{planId}-{(i - 1):D3}"],
                "mid",
                i <= completed ? PlanTaskStatus.Complete :
                i == completed + 1 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending)).ToArray(),
            ApprovalGates:   [],
            Progress:        new PlanProgress(completed, total, $"{planId}-{(completed + 1):D3}"),
            Timestamps:      new PlanTimestamps(
                CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
                StartedAt: DateTimeOffset.UtcNow.AddMinutes(-30)));

    private static Plan MakeUserPausedPlan(string planId = "CTRL-001") =>
        MakeExecutingPlan(planId) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Progress = new PlanProgress(3, 5, null),
            InterruptionData = new PlanInterruptionData(
                Reason:        "Paused by user after the previous task was accepted.",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 3),
        };

    private static Plan MakeAbortedPlan(string planId = "CTRL-001") =>
        MakeExecutingPlan(planId) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Progress = new PlanProgress(2, 5, null),
            InterruptionData = new PlanInterruptionData(
                Reason:            "Current plan work was aborted by the user. Repository work was preserved for assessment.",
                RecoveryState:     PlanRecoveryState.PendingRecovery,
                LoopIteration:     2,
                InterruptedTaskId: "CTRL-001-003",
                LastCommit:        "abc1234"),
        };

    private record ActionLog(string Action, Plan Plan);

    private static (PlansPanelController controller, StackPanel activePanel,
        StackPanel archivedPanel, UIElement archivedSection, List<ActionLog> log)
        BuildControllerWithActions()
    {
        var activePanel     = new StackPanel();
        var completedPanel  = new StackPanel();
        var completedSection = new Border();
        var archivedPanel   = new StackPanel();
        var archivedSection = new Border();
        var log             = new List<ActionLog>();

        var controller = new PlansPanelController(
            activePanel:          activePanel,
            completedPanel:       completedPanel,
            completedSection:     completedSection,
            archivedPanel:        archivedPanel,
            archivedSection:      archivedSection,
            openPlan:             p => log.Add(new("open", p)),
            syncBorderVisibility: _ => { },
            setMenuChecked:       _ => { },
            persistVisibility:    () => { },
            startPlan:            p => log.Add(new("start", p)),
            resumePlan:           p => log.Add(new("resume", p)),
            endPlan:              p => log.Add(new("end", p)),
            archivePlan:          p => log.Add(new("archive", p)),
            pausePlan:            p => log.Add(new("pause", p)),
            abortPlan:            p => log.Add(new("abort", p)),
            isPromptRunning:      () => true);

        return (controller, activePanel, archivedPanel, archivedSection, log);
    }

    // ── 1. Executing plan exposes pause button with accessibility name ────────

    [Test]
    public void ExecutingPlan_ShowsPauseButton_WithAccessibilityName() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, _) = BuildControllerWithActions();
            var plan = MakeExecutingPlan();

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var titleRow = (StackPanel)rowStack.Children[0];
            var pauseButton = titleRow.Children.OfType<Button>().FirstOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(pauseButton, Is.Not.Null, "Executing plan must show a pause button.");
                Assert.That(pauseButton!.Content, Is.EqualTo("Ⅱ"));
                Assert.That(AutomationProperties.GetName(pauseButton),
                    Is.EqualTo("Pause after current step"),
                    "Pause button must have an accessible name for screen readers.");
            });
        });

    // ── 2. Pause button invokes the pausePlan action ──────────────────────────

    [Test]
    public void ExecutingPlan_PauseButtonClick_InvokesPauseAction() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, log) = BuildControllerWithActions();
            var plan = MakeExecutingPlan();

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var titleRow = (StackPanel)rowStack.Children[0];
            var pauseButton = titleRow.Children.OfType<Button>().First();

            pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.That(log, Has.Count.EqualTo(1));
            Assert.That(log[0].Action, Is.EqualTo("pause"));
            Assert.That(log[0].Plan.PlanId, Is.EqualTo(plan.PlanId));
        });

    // ── 3. User-paused plan shows resume button with accessibility name ───────

    [Test]
    public void UserPausedPlan_ShowsResumeButton_WithAccessibilityName() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, _) = BuildControllerWithActions();
            var plan = MakeUserPausedPlan();

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var titleRow = (StackPanel)rowStack.Children[0];
            var resumeButton = titleRow.Children.OfType<Button>().FirstOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(resumeButton, Is.Not.Null, "User-paused plan must show a resume button.");
                Assert.That(resumeButton!.Content, Is.EqualTo("▶"));
                Assert.That(AutomationProperties.GetName(resumeButton),
                    Is.EqualTo("Resume plan"),
                    "Resume button must have an accessible name.");
            });
        });

    // ── 4. Resume routes to startPlan (not resumePlan) — skips assessment ─────

    [Test]
    public void UserPausedPlan_ResumeClick_RoutesToStartNotAssess() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, log) = BuildControllerWithActions();
            var plan = MakeUserPausedPlan();

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var titleRow = (StackPanel)rowStack.Children[0];
            var resumeButton = titleRow.Children.OfType<Button>().First();

            resumeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.That(log, Has.Count.EqualTo(1));
            Assert.That(log[0].Action, Is.EqualTo("start"),
                "User-paused resume must route to 'start' (next runnable task) not 'resume' (evidence assessment).");
        });

    // ── 5. Non-user-paused interrupted plan does NOT show inline resume ───────

    [Test]
    public void CrashInterruptedPlan_NoInlineResumeButton() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, _) = BuildControllerWithActions();
            var plan = MakeExecutingPlan() with
            {
                LifecycleStatus = PlanLifecycleStatus.Interrupted,
                InterruptionData = new PlanInterruptionData(
                    Reason: "Plan execution stopped before the current task was accepted.",
                    RecoveryState: PlanRecoveryState.PendingRecovery,
                    LoopIteration: 2),
            };

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var titleRow = (StackPanel)rowStack.Children[0];
            var buttons = titleRow.Children.OfType<Button>().ToList();

            Assert.That(buttons, Has.Count.EqualTo(0),
                "Non-user-paused interrupted plan must not show an inline button — only context menu resume.");
        });

    // ── 6. Abort preserves completed work in interruption data ────────────────

    [Test]
    public void AbortTransition_PreservesCompletedTasksAndCommitEvidence()
    {
        var executing = MakeExecutingPlan();
        var aborted = PlanStoreUpdater.ApplyInterrupted(
            executing,
            reason: "Current plan work was aborted by the user. Repository work was preserved for assessment.",
            loopIteration: 2,
            interruptedTaskId: "CTRL-001-003",
            lastCommit: "abc1234");

        Assert.Multiple(() =>
        {
            Assert.That(aborted.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(aborted.InterruptionData, Is.Not.Null);
            Assert.That(aborted.InterruptionData!.Reason, Does.Contain("aborted by the user"));
            Assert.That(aborted.InterruptionData.LastCommit, Is.EqualTo("abc1234"));
            Assert.That(aborted.InterruptionData.InterruptedTaskId, Is.EqualTo("CTRL-001-003"));
            Assert.That(aborted.Tasks.Count(t => t.Status == PlanTaskStatus.Complete), Is.EqualTo(2),
                "Abort must preserve all previously completed tasks.");
            Assert.That(aborted.Progress.ExecutingTaskId, Is.Null,
                "ExecutingTaskId must be cleared on abort.");
        });
    }

    // ── 7. Archive preserves full plan history durably ─────────────────────────

    [Test]
    public void ArchiveTransition_PreservesFullHistory()
    {
        var completed = MakeExecutingPlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            Progress = new PlanProgress(5, 5, null),
            Timestamps = new PlanTimestamps(
                CreatedAt:   DateTimeOffset.UtcNow.AddHours(-2),
                StartedAt:   DateTimeOffset.UtcNow.AddHours(-2),
                CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
        };

        var archived = PlanStoreUpdater.ApplyArchived(completed);

        Assert.Multiple(() =>
        {
            Assert.That(archived.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Archived));
            Assert.That(archived.Tasks, Has.Count.EqualTo(5), "Archive must preserve all tasks.");
            Assert.That(archived.Timestamps.CreatedAt, Is.EqualTo(completed.Timestamps.CreatedAt));
            Assert.That(archived.Timestamps.CompletedAt, Is.EqualTo(completed.Timestamps.CompletedAt));
            Assert.That(archived.Timestamps.ArchivedAt, Is.Not.Null);
            Assert.That(archived.Progress.TotalCount, Is.EqualTo(5));
            Assert.That(archived.Progress.CompletedCount, Is.EqualTo(5));
        });
    }

    // ── 8. Archived plan shown only when ShowArchived is enabled ───────────────

    [Test]
    public void ArchivedPlan_HiddenByDefault_VisibleAfterShowArchived() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, archivedPanel, archivedSection, _) = BuildControllerWithActions();
            var plan = MakeExecutingPlan() with
            {
                LifecycleStatus = PlanLifecycleStatus.Archived,
                Timestamps = new PlanTimestamps(
                    CreatedAt:  DateTimeOffset.UtcNow.AddHours(-2),
                    ArchivedAt: DateTimeOffset.UtcNow),
            };

            ctrl.Refresh([plan]);
            Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Collapsed),
                "Archived plans must be hidden by default.");

            ctrl.SetShowArchived(true);
            Assert.Multiple(() =>
            {
                Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(archivedPanel.Children.OfType<Border>().Any(b => (string?)b.Tag == plan.PlanId),
                    Is.True, "Archived plan row must exist in the archived panel.");
            });
        });

    // ── 9. Executing plan cannot be archived ──────────────────────────────────

    [Test]
    public void ApplyArchived_ExecutingPlan_IsRejected()
    {
        var executing = MakeExecutingPlan();

        var result = PlanStoreUpdater.ApplyArchived(executing);

        Assert.That(ReferenceEquals(result, executing), Is.True,
            "Archiving an executing plan must be a no-op (returns same reference).");
    }

    // ── 10. Restart round-trip: plan survives serialize/deserialize ────────────

    [Test]
    public void PlanState_SurvivesJsonRoundTrip()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);

            var plan = MakeUserPausedPlan("RESTART-001");
            store.Save(plan);

            var loaded = store.Load("RESTART-001");

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.PlanId, Is.EqualTo("RESTART-001"));
                Assert.That(loaded.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
                Assert.That(loaded.InterruptionData, Is.Not.Null);
                Assert.That(loaded.InterruptionData!.Reason,
                    Does.StartWith("Paused by user"));
                Assert.That(loaded.InterruptionData.RecoveryState,
                    Is.EqualTo(PlanRecoveryState.PendingRecovery));
                Assert.That(loaded.InterruptionData.LoopIteration, Is.EqualTo(3));
                Assert.That(loaded.Progress.CompletedCount, Is.EqualTo(3));
                Assert.That(loaded.Tasks, Has.Count.EqualTo(5));
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 11. Abort round-trip preserves commit evidence after restart ───────────

    [Test]
    public void AbortedPlan_SurvivesJsonRoundTrip_WithCommitEvidence()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);

            var plan = MakeAbortedPlan("RESTART-002");
            store.Save(plan);

            var loaded = store.Load("RESTART-002");

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.InterruptionData!.LastCommit, Is.EqualTo("abc1234"));
                Assert.That(loaded.InterruptionData.InterruptedTaskId, Is.EqualTo("CTRL-001-003"));
                Assert.That(loaded.InterruptionData.Reason, Does.Contain("aborted"));
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 12. Resume from user-pause does NOT require evidence assessment ────────

    [Test]
    public void ResumeAfterUserPause_ClearsInterruptionData_PreservesStartedAt()
    {
        var paused = MakeUserPausedPlan();
        var originalStart = paused.Timestamps.StartedAt;

        var group = new DecomposedTaskGroup(
            GroupId:    paused.PlanId,
            GroupTitle: paused.Title,
            Branch:     paused.Branch,
            Summary:    paused.Summary,
            Tasks:      paused.Tasks.Select(t => new DecomposedSubTask(
                Id:          t.TaskId,
                Description: t.Description,
                DependsOn:   t.DependsOn.ToList(),
                Priority:    t.Priority,
                Title:       t.Title ?? t.TaskId)).ToList());

        // Tasks 1-3 completed (matching the paused plan's 3 completed count)
        var items = paused.Tasks.Select((t, idx) => new TaskItem(
            Text:             t.TaskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        idx < 3,
            Emoji:            "🟡",
            RawLine:          $"- [{(idx < 3 ? "x" : " ")}] **[{t.TaskId}]**",
            DecomposeGroupId: paused.PlanId,
            TaskId:           t.TaskId)).ToList();

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            paused, group, "rev2", items, "CTRL-001-004");

        Assert.Multiple(() =>
        {
            Assert.That(resumed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(resumed.InterruptionData, Is.Null,
                "Resume must clear InterruptionData — no evidence assessment needed.");
            Assert.That(resumed.Timestamps.StartedAt, Is.EqualTo(originalStart),
                "Resume must preserve the original StartedAt timestamp.");
            Assert.That(resumed.Progress.ExecutingTaskId, Is.EqualTo("CTRL-001-004"),
                "Resume must pick up at the next runnable task.");
            Assert.That(resumed.Progress.CompletedCount, Is.EqualTo(3),
                "Previously completed steps must remain counted.");
        });
    }

    // ── 13. PlanExecutionTransitionService.Resume clears recovery state ────────

    [Test]
    public void TransitionService_Resume_SetsRecoveredState()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);
            var service = new PlanExecutionTransitionService(store);

            var plan = MakeAbortedPlan("TRANS-001");
            store.Save(plan);

            var result = service.Resume(plan, DateTimeOffset.UtcNow);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
                Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
                Assert.That(result.Plan.InterruptionData!.RecoveryState,
                    Is.EqualTo(PlanRecoveryState.Recovered));
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 14. Activity label shows "Paused after accepted step" for user-paused ──

    [Test]
    public void UserPausedPlan_ActivityLabel_ShowsPausedText() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, _) = BuildControllerWithActions();
            var plan = MakeUserPausedPlan();

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>().Single();
            var rowStack = (StackPanel)row.Child;
            var textBlocks = rowStack.Children.OfType<TextBlock>().ToList();
            var activityBlock = textBlocks.FirstOrDefault(tb =>
                tb.Text.Contains("Paused", StringComparison.OrdinalIgnoreCase));

            Assert.That(activityBlock, Is.Not.Null,
                "User-paused plan must show 'Paused after accepted step' activity label.");
            Assert.That(activityBlock!.Text, Is.EqualTo("Paused after accepted step"));
        });
}

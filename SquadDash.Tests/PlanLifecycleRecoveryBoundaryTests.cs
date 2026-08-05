using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Adversarial host-controlled integration tests that exercise the FULL plan lifecycle
/// recovery matrix: pause after accepted task, direct resume without repetition, immediate
/// abort with preserved evidence, restart from every state, archive of stale plans,
/// show-archived filtering, isolation from ordinary filtered loops, approval identity
/// persistence, and verification that no control silently converts pause into failure
/// or abort into blind retry.
/// </summary>
[TestFixture]
internal sealed class PlanLifecycleRecoveryBoundaryTests
{
    // ── Shared helpers ─────────────────────────────────────────────────────────

    private static Plan MakeExecutingPlan(string planId = "REC-001", int completed = 2, int total = 5) =>
        new(
            PlanId:          planId,
            Revision:        "rev-boundary-1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Recovery Boundary Plan",
            Branch:          "feature/recovery-boundary",
            Summary:         "Tests the adversarial lifecycle recovery boundaries.",
            Tasks:           Enumerable.Range(1, total).Select(i => new PlanTask(
                $"{planId}-{i:D3}",
                $"Step {i}",
                $"Task {i} — exercises boundary condition {i}",
                i == 1 ? [] : [$"{planId}-{(i - 1):D3}"],
                "mid",
                i <= completed ? PlanTaskStatus.Complete :
                i == completed + 1 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending)).ToArray(),
            ApprovalGates:   [],
            Progress:        new PlanProgress(completed, total, $"{planId}-{(completed + 1):D3}"),
            Timestamps:      new PlanTimestamps(
                CreatedAt: DateTimeOffset.UtcNow.AddHours(-2),
                StartedAt: DateTimeOffset.UtcNow.AddMinutes(-45)));

    private static Plan MakeApprovedPlan(string planId = "REC-002") =>
        new(
            PlanId:          planId,
            Revision:        "rev-boundary-2",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Approved,
            Title:           "Never-Started Plan",
            Branch:          "feature/never-started",
            Summary:         "Collected but never executed — candidate for stale archive.",
            Tasks:           Enumerable.Range(1, 3).Select(i => new PlanTask(
                $"{planId}-{i:D3}",
                $"Step {i}",
                $"Task {i} — never started",
                i == 1 ? [] : [$"{planId}-{(i - 1):D3}"],
                "mid",
                PlanTaskStatus.Pending)).ToArray(),
            ApprovalGates:   [],
            Progress:        new PlanProgress(0, 3, null),
            Timestamps:      new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddDays(-7)));

    private static Plan MakeUserPausedPlan(string planId = "REC-001")
    {
        var executing = MakeExecutingPlan(planId);
        return PlanStoreUpdater.ApplyInterrupted(
            executing,
            reason: "Paused by user after the previous task was accepted.",
            loopIteration: 2,
            lastCompletedTaskId: $"{planId}-002");
    }

    private static Plan MakeAbortedPlan(string planId = "REC-001")
    {
        var executing = MakeExecutingPlan(planId);
        return PlanStoreUpdater.ApplyInterrupted(
            executing,
            reason: "Current plan work was aborted by the user. Repository work was preserved for assessment.",
            loopIteration: 2,
            interruptedTaskId: $"{planId}-003",
            lastCommit: "deadbeef");
    }

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

    // ── 1. Pause after accepted task ──────────────────────────────────────────

    [Test]
    public void PauseAfterAcceptedTask_SetsInterruptedWithUserPauseFlag_CompletedTasksPreserved()
    {
        var executing = MakeExecutingPlan();
        var paused = PlanStoreUpdater.ApplyInterrupted(
            executing,
            reason: "Paused by user after the previous task was accepted.",
            loopIteration: 2,
            lastCompletedTaskId: "REC-001-002");

        Assert.Multiple(() =>
        {
            Assert.That(paused.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(paused.InterruptionData, Is.Not.Null);
            Assert.That(paused.InterruptionData!.Reason, Does.Contain("Paused by user"));
            Assert.That(paused.InterruptionData.LastCompletedTaskId, Is.EqualTo("REC-001-002"));
            Assert.That(paused.Tasks.Count(t => t.Status == PlanTaskStatus.Complete), Is.EqualTo(2),
                "Completed tasks must remain intact after pause.");
            Assert.That(paused.Progress.ExecutingTaskId, Is.Null,
                "No task should be executing while paused.");
        });
    }

    // ── 2. Direct resume without repeating completed work ─────────────────────

    [Test]
    public void DirectResumeAfterUserPause_PicksUpAtNextTask_DoesNotRepeatCompleted()
    {
        var paused = MakeUserPausedPlan("RESUME-001");

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

        var items = paused.Tasks.Select((t, idx) => new TaskItem(
            Text:             t.TaskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        idx < 2,
            Emoji:            "🟡",
            RawLine:          $"- [{(idx < 2 ? "x" : " ")}] **[{t.TaskId}]**",
            DecomposeGroupId: paused.PlanId,
            TaskId:           t.TaskId)).ToList();

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            paused, group, "rev-resumed", items, "RESUME-001-003");

        Assert.Multiple(() =>
        {
            Assert.That(resumed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(resumed.InterruptionData, Is.Null,
                "Resume must clear InterruptionData — no evidence assessment needed for user-pause.");
            Assert.That(resumed.Progress.ExecutingTaskId, Is.EqualTo("RESUME-001-003"),
                "Resume must advance to next runnable task, not repeat completed work.");
            Assert.That(resumed.Progress.CompletedCount, Is.EqualTo(2),
                "Previously completed tasks must remain counted.");
        });
    }

    // ── 3. Immediate abort with preserved repository evidence ─────────────────

    [Test]
    public void ReworkPreflightPause_OffersDirectResumeInsteadOfAssessment()
    {
        WpfTestContext.Run(() =>
        {
            var executing = MakeExecutingPlan("REWORK-RESUME-001");
            var paused = PlanStoreUpdater.ApplyInterrupted(
                executing,
                PlanRecoveryResumePolicy.BuildReworkPreflightReason("Two files are dirty."),
                loopIteration: 0,
                interruptedTaskId: "REWORK-RESUME-001-003");
            var (controller, activePanel, _, _, log) = BuildControllerWithActions();

            controller.Refresh([paused]);

            var row = activePanel.Children.OfType<Border>().Single(border =>
                string.Equals(border.Tag as string, paused.PlanId, StringComparison.Ordinal));
            var actions = row.ContextMenu!.Items.OfType<MenuItem>().ToArray();
            var resume = actions.Single(item =>
                string.Equals(item.Header as string, "Resume Plan", StringComparison.Ordinal));
            Assert.That(actions.Any(item =>
                    string.Equals(item.Header as string, "Assess & Continue", StringComparison.Ordinal)),
                Is.False);

            resume.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.That(log.Any(entry =>
                entry.Action == "start" && entry.Plan.PlanId == paused.PlanId), Is.True);
        });
    }

    [Test]
    public void ImmediateAbort_PreservesCommitEvidence_CompletedTasksSurvive()
    {
        var executing = MakeExecutingPlan("ABORT-001", completed: 3, total: 6);
        var aborted = PlanStoreUpdater.ApplyInterrupted(
            executing,
            reason: "Current plan work was aborted by the user. Repository work was preserved for assessment.",
            loopIteration: 3,
            interruptedTaskId: "ABORT-001-004",
            lastCommit: "c0ffee42");

        Assert.Multiple(() =>
        {
            Assert.That(aborted.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(aborted.InterruptionData!.LastCommit, Is.EqualTo("c0ffee42"),
                "Abort must preserve the last commit for evidence assessment.");
            Assert.That(aborted.InterruptionData.InterruptedTaskId, Is.EqualTo("ABORT-001-004"));
            Assert.That(aborted.InterruptionData.Reason, Does.Contain("aborted by the user"));
            Assert.That(aborted.Tasks.Count(t => t.Status == PlanTaskStatus.Complete), Is.EqualTo(3),
                "All previously completed tasks must survive abort.");
            Assert.That(aborted.Progress.ExecutingTaskId, Is.Null);
        });
    }

    // ── 4. Restart round-trip from each lifecycle state ───────────────────────

    [TestCase("executing")]
    [TestCase("interrupted-pause")]
    [TestCase("interrupted-abort")]
    [TestCase("archived")]
    public void RestartRoundTrip_AllFieldsSurviveSerializeDeserialize(string stateLabel)
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);

            var planId = $"RESTART-{stateLabel}";
            var plan = stateLabel switch
            {
                "executing"        => MakeExecutingPlan(planId),
                "interrupted-pause" => MakeUserPausedPlan(planId),
                "interrupted-abort" => MakeAbortedPlan(planId),
                "archived"         => PlanStoreUpdater.ApplyArchived(
                    PlanStoreUpdater.ApplyCompleted(MakeExecutingPlan(planId) with
                    {
                        Progress = new PlanProgress(5, 5, null),
                    })),
                _ => throw new ArgumentException($"Unknown state: {stateLabel}"),
            };

            store.Save(plan);
            var loaded = store.Load(planId);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null, $"Plan in state '{stateLabel}' must survive round-trip.");
                Assert.That(loaded!.PlanId, Is.EqualTo(planId));
                Assert.That(loaded.LifecycleStatus, Is.EqualTo(plan.LifecycleStatus));
                Assert.That(loaded.Tasks, Has.Count.EqualTo(plan.Tasks.Count));
                Assert.That(loaded.Progress.CompletedCount, Is.EqualTo(plan.Progress.CompletedCount));
                Assert.That(loaded.Progress.TotalCount, Is.EqualTo(plan.Progress.TotalCount));
                Assert.That(loaded.Timestamps.CreatedAt, Is.EqualTo(plan.Timestamps.CreatedAt));

                if (plan.InterruptionData is not null)
                {
                    Assert.That(loaded.InterruptionData, Is.Not.Null);
                    Assert.That(loaded.InterruptionData!.Reason, Is.EqualTo(plan.InterruptionData.Reason));
                    Assert.That(loaded.InterruptionData.RecoveryState, Is.EqualTo(plan.InterruptionData.RecoveryState));
                    Assert.That(loaded.InterruptionData.LastCommit, Is.EqualTo(plan.InterruptionData.LastCommit));
                }
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 5. Archive of stale never-started plan ────────────────────────────────

    [Test]
    public void ArchiveNeverStartedPlan_MovesToArchived_AllMetadataPreserved()
    {
        var neverStarted = MakeApprovedPlan("STALE-001");
        var archived = PlanStoreUpdater.ApplyArchived(neverStarted);

        Assert.Multiple(() =>
        {
            Assert.That(archived.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Archived));
            Assert.That(archived.Tasks, Has.Count.EqualTo(3));
            Assert.That(archived.Tasks.All(t => t.Status == PlanTaskStatus.Pending), Is.True,
                "All tasks must remain in Pending state — nothing was ever started.");
            Assert.That(archived.Timestamps.CreatedAt, Is.EqualTo(neverStarted.Timestamps.CreatedAt));
            Assert.That(archived.Timestamps.ArchivedAt, Is.Not.Null);
            Assert.That(archived.Title, Is.EqualTo(neverStarted.Title));
            Assert.That(archived.Branch, Is.EqualTo(neverStarted.Branch));
            Assert.That(archived.Summary, Is.EqualTo(neverStarted.Summary));
        });
    }

    // ── 6. Show archived filtering ────────────────────────────────────────────

    [Test]
    public void ArchivedPlans_ExcludedFromActiveList_VisibleWhenShowArchivedTrue() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, archivedPanel, archivedSection, _) = BuildControllerWithActions();
            var activePlan = MakeExecutingPlan("ACTIVE-001");
            var archivedPlan = PlanStoreUpdater.ApplyArchived(
                PlanStoreUpdater.ApplyCompleted(MakeExecutingPlan("ARCHIVED-001") with
                {
                    Progress = new PlanProgress(5, 5, null),
                }));

            ctrl.Refresh([activePlan, archivedPlan]);

            Assert.Multiple(() =>
            {
                Assert.That(activePanel.Children.OfType<Border>().Count(), Is.EqualTo(1),
                    "Only the active plan should appear in the active panel.");
                Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Collapsed),
                    "Archived section must be hidden by default.");
            });

            ctrl.SetShowArchived(true);

            Assert.Multiple(() =>
            {
                Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(archivedPanel.Children.OfType<Border>().Any(), Is.True,
                    "Archived plan must be visible after ShowArchived is enabled.");
            });
        });

    // ── 7. Isolation from ordinary filtered loop ──────────────────────────────

    [Test]
    public void PlanOwnedControls_DoNotLeakIntoNonPlanLoopIterations() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _, log) = BuildControllerWithActions();
            var plan = MakeExecutingPlan("ISO-001");

            ctrl.Refresh([plan]);

            // Verify the plan row is present
            var planRows = activePanel.Children.OfType<Border>()
                .Where(b => (string?)b.Tag == "ISO-001").ToList();
            Assert.That(planRows, Has.Count.EqualTo(1), "Plan row must exist after refresh.");

            // Now refresh with an empty list — plan-specific rows should vanish
            ctrl.Refresh([]);

            var leakedRows = activePanel.Children.OfType<Border>()
                .Where(b => (string?)b.Tag == "ISO-001").ToList();

            Assert.Multiple(() =>
            {
                Assert.That(leakedRows, Has.Count.EqualTo(0),
                    "Plan-owned controls must not persist when the plan is no longer in the active list.");
                Assert.That(log, Has.Count.EqualTo(0),
                    "No action callbacks should fire during a clear-refresh.");
            });
        });

    // ── 8. Approval identity survives restart via JSON round-trip ─────────────

    [Test]
    public void ApprovalGate_WithResolvedIdentity_SurvivesJsonRoundTrip()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);

            var resolvedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var plan = MakeExecutingPlan("GATE-001") with
            {
                ApprovalGates = new[]
                {
                    new PlanApprovalGate(
                        GateId:      "gate-1",
                        Message:     "Approve deployment to staging",
                        AfterTaskIds:  new[] { "GATE-001-002" },
                        BeforeTaskIds: new[] { "GATE-001-003" },
                        Status:      PlanGateStatus.Approved,
                        RequestedAt: resolvedAt.AddMinutes(-2),
                        ResolvedAt:  resolvedAt,
                        ResolutionNote: "LGTM — approved by lead",
                        ResolvedBy:  "Alice Smith (@alicesmith)"),
                },
            };

            store.Save(plan);
            var loaded = store.Load("GATE-001");

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.ApprovalGates, Has.Count.EqualTo(1));
                var gate = loaded.ApprovalGates[0];
                Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Approved));
                Assert.That(gate.ResolvedBy, Is.EqualTo("Alice Smith (@alicesmith)"),
                    "Resolved identity must survive restart.");
                Assert.That(gate.ResolvedAt, Is.EqualTo(resolvedAt),
                    "Resolution timestamp must survive restart.");
                Assert.That(gate.ResolutionNote, Is.EqualTo("LGTM — approved by lead"));
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 9a. No silent conversion: pause must NOT become "failed" ──────────────

    [Test]
    public void PauseDoesNotSilentlyBecomeFailed()
    {
        var executing = MakeExecutingPlan("NOSIL-001");
        var paused = PlanStoreUpdater.ApplyInterrupted(
            executing,
            reason: "Paused by user after the previous task was accepted.",
            loopIteration: 2);

        Assert.Multiple(() =>
        {
            Assert.That(paused.LifecycleStatus, Is.Not.EqualTo(PlanLifecycleStatus.Blocked),
                "Pause must never silently become Blocked (failed).");
            Assert.That(paused.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(paused.InterruptionData!.RecoveryState,
                Is.EqualTo(PlanRecoveryState.PendingRecovery),
                "Pause must set PendingRecovery, not any failure state.");
            Assert.That(paused.Tasks.Any(t => t.Status == PlanTaskStatus.Failed), Is.False,
                "No task should be marked Failed due to a user-initiated pause.");
        });
    }

    // ── 9b. No silent conversion: abort must NOT trigger blind retry ──────────

    [Test]
    public void AbortDoesNotTriggerBlindRetry()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);
            var service = new PlanExecutionTransitionService(store);

            var aborted = MakeAbortedPlan("NOSIL-002");
            store.Save(aborted);

            // Resume the aborted plan through the transition service
            var result = service.Resume(aborted, DateTimeOffset.UtcNow);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
                // The resumed plan must retain its interruption data with Recovered state
                // This means the host can detect it was an abort-resume, not a blind retry
                Assert.That(result.Plan!.InterruptionData, Is.Not.Null,
                    "Resume after abort must retain InterruptionData so the host knows it's a recovery, not a blind retry.");
                Assert.That(result.Plan.InterruptionData!.RecoveryState,
                    Is.EqualTo(PlanRecoveryState.Recovered),
                    "Recovery state must be 'recovered', not erased — blind retry would erase evidence.");
                Assert.That(result.Plan.InterruptionData.LastCommit, Is.EqualTo("deadbeef"),
                    "Abort evidence (commit hash) must survive resume for assessment.");
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 10. Authoritative projection consistency ──────────────────────────────

    [Test]
    public void AuthoritativeProjection_AllSurfacesReadSamePlan()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);

            var plan = MakeExecutingPlan("PROJ-001");
            store.Save(plan);

            // Load via single-plan and all-plan APIs
            var single = store.Load("PROJ-001");
            var all = store.LoadAll();
            var fromAll = all.FirstOrDefault(p => p.PlanId == "PROJ-001");

            Assert.Multiple(() =>
            {
                Assert.That(single, Is.Not.Null);
                Assert.That(fromAll, Is.Not.Null);
                Assert.That(single!.PlanId, Is.EqualTo(fromAll!.PlanId));
                Assert.That(single.LifecycleStatus, Is.EqualTo(fromAll.LifecycleStatus));
                Assert.That(single.Progress.CompletedCount, Is.EqualTo(fromAll.Progress.CompletedCount));
                Assert.That(single.Revision, Is.EqualTo(fromAll.Revision));
                Assert.That(single.Tasks.Count, Is.EqualTo(fromAll.Tasks.Count));
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    // ── 11. PlanExecutionTransitionService rejects terminal plans ─────────────

    [Test]
    public void TransitionService_RejectsResumeOnArchivedPlan()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);
            var service = new PlanExecutionTransitionService(store);

            var archived = PlanStoreUpdater.ApplyArchived(
                PlanStoreUpdater.ApplyCompleted(MakeExecutingPlan("TERM-001") with
                {
                    Progress = new PlanProgress(5, 5, null),
                }));
            store.Save(archived);

            var result = service.Resume(archived, DateTimeOffset.UtcNow);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
                Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Archived),
                    "Archived plan must not be altered by a rejected resume.");
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }
}

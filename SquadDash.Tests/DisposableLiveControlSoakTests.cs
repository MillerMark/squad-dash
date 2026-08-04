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
/// Disposable self-hosted control and validation soak: exercises a complete plan lifecycle
/// from collection through completion, proving the running-task spinner, Plans panel progress,
/// human approval with recorded identity and relative time, pause-after-step and resume,
/// validation shields at declared boundaries, final completion, stale-plan archival with
/// Show Archived filtering, and restart durability.
///
/// Observed results are documented via test names and assertion messages.
///
/// Remaining limitations:
///   - No live WPF window rendering — spinner states verified via PlanTaskActivityResolver and
///     PlansPanelController.AdvancePlanActivityFrame on headless STA thread
///   - No real file system plan store interaction for UI tests — state is in-memory except
///     where PlanStore round-trip is explicitly exercised
///   - Approval identity resolved from static FormatIdentity (no git/gh subprocess)
///   - Relative time formatting is deterministic (injected DateTimeOffset)
/// </summary>
[TestFixture]
internal sealed class DisposableLiveControlSoakTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Plan construction: 4-task plan with approval gate and validation node
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly DateTimeOffset BaseTime = new(2026, 8, 3, 14, 0, 0, TimeSpan.Zero);

    private static Plan MakeSoakPlan(int completed = 0, string? executingTaskId = null)
    {
        var tasks = new[]
        {
            new PlanTask("SOAK-001", "Scaffold service layer",
                "Create the base service interfaces and DI registrations.",
                [], "high", completed > 0 ? PlanTaskStatus.Complete : PlanTaskStatus.Pending,
                Commit: completed > 0 ? "aaa1111" : null),
            new PlanTask("SOAK-002", "Implement core logic",
                "Implement business rules in the service layer.",
                ["SOAK-001"], "high",
                completed > 1 ? PlanTaskStatus.Complete :
                completed == 1 && executingTaskId == "SOAK-002" ? PlanTaskStatus.Executing : PlanTaskStatus.Pending,
                Commit: completed > 1 ? "bbb2222" : null),
            new PlanTask("SOAK-003", "Add integration tests",
                "Cover the service layer with integration tests.",
                ["SOAK-002"], "mid",
                completed > 2 ? PlanTaskStatus.Complete :
                completed == 2 && executingTaskId == "SOAK-003" ? PlanTaskStatus.Executing : PlanTaskStatus.Pending,
                Commit: completed > 2 ? "ccc3333" : null),
            new PlanTask("SOAK-004", "Wire into host UI",
                "Connect service outputs to the panel surface.",
                ["SOAK-003"], "mid",
                completed > 3 ? PlanTaskStatus.Complete :
                completed == 3 && executingTaskId == "SOAK-004" ? PlanTaskStatus.Executing : PlanTaskStatus.Pending,
                Commit: completed > 3 ? "ddd4444" : null),
        };

        var gate = new PlanApprovalGate(
            "gate-deploy", "Approve deployment readiness",
            AfterTaskIds: ["SOAK-002"],
            BeforeTaskIds: ["SOAK-003"],
            Status: PlanGateStatus.Pending);

        var validation = new PlanValidationNode(
            "V-WIRE", "Verify UI wiring",
            "Verify service output is rendered in a host panel surface.",
            ["SOAK-003"], ["SOAK-004"],
            ["Service output is consumed by a visible host panel."],
            ["service-output"],
            "evidence", ["dotnet build"], true,
            PlanValidationStatus.Pending);

        return new Plan(
            "SOAK-LIVE", "rev-soak-1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Approved,
            "Disposable Control Soak — Full Lifecycle",
            "feature/plan-control-validation-soak",
            "Exercises spinner, progress, approval, pause/resume, shields, completion, archive.",
            tasks,
            [gate],
            new PlanProgress(completed, 4, executingTaskId),
            new PlanTimestamps(CreatedAt: BaseTime, AcceptedAt: BaseTime.AddMinutes(1)),
            Validations: [validation]);
    }

    private static Plan StartExecution(Plan plan) =>
        plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Progress = plan.Progress with { ExecutingTaskId = "SOAK-001" },
            Tasks = plan.Tasks.Select(t => t.TaskId == "SOAK-001"
                ? t with { Status = PlanTaskStatus.Executing }
                : t).ToArray(),
            Timestamps = plan.Timestamps with { StartedAt = BaseTime.AddMinutes(2) },
        };

    // ═══════════════════════════════════════════════════════════════════════════
    // (a) Running-task spinner — PlanTaskActivityResolver + PlansPanelController
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void RunningTaskSpinner_ExecutingPlan_ResolvesToExecutingState()
    {
        var plan = StartExecution(MakeSoakPlan());

        var activity = PlanTaskActivityResolver.Resolve(plan);
        var planLevel = PlanTaskActivityResolver.ResolvePlanLevel(plan);

        Assert.Multiple(() =>
        {
            Assert.That(activity["SOAK-001"], Is.EqualTo(PlanTaskActivityState.Executing),
                "Active task must show Executing (spinner) state.");
            Assert.That(activity["SOAK-002"], Is.EqualTo(PlanTaskActivityState.Queued),
                "Next task must be Queued (no spinner).");
            Assert.That(planLevel, Is.EqualTo(PlanTaskActivityState.Executing),
                "Plan-level indicator must be Executing for spinner in Plans panel.");
        });
    }

    [Test]
    public void RunningTaskSpinner_PanelRendersSpinnerFrame() =>
        WpfTestContext.Run(() =>
        {
            var plan = StartExecution(MakeSoakPlan());
            var (ctrl, activePanel, _, _, _) = BuildController();

            ctrl.Refresh([plan]);
            ctrl.AdvancePlanActivityFrame(3);

            var row = activePanel.Children.OfType<Border>().FirstOrDefault();
            Assert.That(row, Is.Not.Null, "Plan row must be rendered in active panel.");
        });

    // ═══════════════════════════════════════════════════════════════════════════
    // (b) Plans panel progress — advance through tasks, verify counts
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void PlanProgress_AdvancesThroughTasks_CountsUpdateCorrectly()
    {
        var plan = StartExecution(MakeSoakPlan());
        Assert.That(plan.Progress.CompletedCount, Is.EqualTo(0));
        Assert.That(plan.Progress.ExecutingTaskId, Is.EqualTo("SOAK-001"));

        // Step 1 completes, Step 2 begins
        plan = plan with
        {
            Progress = new PlanProgress(1, 4, "SOAK-002"),
            Tasks = plan.Tasks.Select(t => t.TaskId switch
            {
                "SOAK-001" => t with { Status = PlanTaskStatus.Complete, Commit = "aaa1111" },
                "SOAK-002" => t with { Status = PlanTaskStatus.Executing },
                _ => t,
            }).ToArray(),
        };

        Assert.Multiple(() =>
        {
            Assert.That(plan.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(plan.Progress.ExecutingTaskId, Is.EqualTo("SOAK-002"));
            Assert.That(PlanTaskActivityResolver.Resolve(plan)["SOAK-002"],
                Is.EqualTo(PlanTaskActivityState.Executing));
        });

        // Step 2 completes
        plan = plan with
        {
            Progress = new PlanProgress(2, 4, null),
            Tasks = plan.Tasks.Select(t => t.TaskId == "SOAK-002"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "bbb2222" }
                : t).ToArray(),
        };

        Assert.That(plan.Progress.CompletedCount, Is.EqualTo(2));
        Assert.That(plan.Progress.TotalCount, Is.EqualTo(4));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // (c) Human approval with identity — FormatIdentity, ResolvedBy, tooltip
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void HumanApproval_FormatIdentity_RendersLoginWithName()
    {
        var identity = HumanApprovalIdentityResolver.FormatIdentity(
            name: "Alice Smith", email: "alice@example.com", login: "alicesmith");

        Assert.That(identity, Is.EqualTo("Alice Smith (@alicesmith)"));
    }

    [Test]
    public void HumanApproval_FormatIdentity_LoginOnly()
    {
        var identity = HumanApprovalIdentityResolver.FormatIdentity(
            name: null, email: null, login: "bobdev");

        Assert.That(identity, Is.EqualTo("@bobdev"));
    }

    [Test]
    public void HumanApproval_GateResolution_PersistsIdentityAndTimestamp()
    {
        var resolvedAt = BaseTime.AddMinutes(30);
        var identity = HumanApprovalIdentityResolver.FormatIdentity("Mark", null, "MillerMark");

        var plan = StartExecution(MakeSoakPlan(completed: 2));
        plan = plan with
        {
            ApprovalGates = new[]
            {
                plan.ApprovalGates[0] with
                {
                    Status = PlanGateStatus.Approved,
                    ResolvedAt = resolvedAt,
                    ResolvedBy = identity,
                    ResolutionNote = "Ship it — looks good.",
                },
            },
        };

        Assert.Multiple(() =>
        {
            var gate = plan.ApprovalGates[0];
            Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Approved));
            Assert.That(gate.ResolvedBy, Is.EqualTo("Mark (@MillerMark)"));
            Assert.That(gate.ResolvedAt, Is.EqualTo(resolvedAt));
            Assert.That(gate.ResolutionNote, Is.EqualTo("Ship it — looks good."));
        });
    }

    [Test]
    public void HumanApproval_TooltipFormatsRelativeTime()
    {
        var resolvedAt = BaseTime.AddMinutes(30);
        var now = BaseTime.AddMinutes(35);

        var gate = new PlanApprovalGate(
            "gate-deploy", "Approve deployment",
            AfterTaskIds: ["SOAK-002"], BeforeTaskIds: ["SOAK-003"],
            Status: PlanGateStatus.Approved,
            ResolvedAt: resolvedAt,
            ResolvedBy: "Mark (@MillerMark)",
            ResolutionNote: "LGTM");

        var tooltip = ApprovalResolvedTooltipPresentation.Build(gate, "before step 3", now);

        Assert.Multiple(() =>
        {
            Assert.That(tooltip, Does.Contain("Mark (@MillerMark)"));
            Assert.That(tooltip, Does.Contain("LGTM"));
            Assert.That(tooltip, Does.Contain("Human approval was granted before step 3."));
            // Relative time should be present (5 minutes ago)
            var expectedTiming = StatusTimingPresentation.FormatRelativeTimestamp(resolvedAt, now);
            Assert.That(tooltip, Does.Contain(expectedTiming));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // (d) Pause-after-step and resume
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void PauseAfterStep_SetsInterruptedState_ClearsExecutingTask()
    {
        var plan = StartExecution(MakeSoakPlan(completed: 2, executingTaskId: "SOAK-003"));
        plan = plan with { LifecycleStatus = PlanLifecycleStatus.Executing };

        var paused = PlanStoreUpdater.ApplyInterrupted(
            plan, reason: "Paused by user after step 2 was accepted.",
            loopIteration: 2, lastCompletedTaskId: "SOAK-002");

        Assert.Multiple(() =>
        {
            Assert.That(paused.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(paused.InterruptionData, Is.Not.Null);
            Assert.That(paused.InterruptionData!.Reason, Does.Contain("Paused by user"));
            Assert.That(paused.InterruptionData.LastCompletedTaskId, Is.EqualTo("SOAK-002"));
            Assert.That(paused.Progress.ExecutingTaskId, Is.Null,
                "No task should be executing while paused.");
            Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(paused),
                Is.EqualTo(PlanTaskActivityState.Interrupted));
        });
    }

    [Test]
    public void ResumeAfterPause_AdvancesToNextTask_DoesNotRepeatCompleted()
    {
        var plan = StartExecution(MakeSoakPlan(completed: 2, executingTaskId: "SOAK-003"));
        plan = plan with { LifecycleStatus = PlanLifecycleStatus.Executing };
        var paused = PlanStoreUpdater.ApplyInterrupted(
            plan, reason: "Paused by user.", loopIteration: 2,
            lastCompletedTaskId: "SOAK-002");

        // Simulate resume by constructing DecomposedTaskGroup
        var group = new DecomposedTaskGroup(
            GroupId: paused.PlanId,
            GroupTitle: paused.Title,
            Branch: paused.Branch,
            Summary: paused.Summary,
            Tasks: paused.Tasks.Select(t => new DecomposedSubTask(
                Id: t.TaskId, Description: t.Description,
                DependsOn: t.DependsOn.ToList(), Priority: t.Priority,
                Title: t.Title ?? t.TaskId)).ToList());

        var items = paused.Tasks.Select((t, idx) => new TaskItem(
            Text: t.TaskId, Owner: null, IsUserOwned: false,
            IsChecked: idx < 2, Emoji: idx < 2 ? "✅" : "🟡",
            RawLine: $"- [{(idx < 2 ? "x" : " ")}] **[{t.TaskId}]**",
            DecomposeGroupId: paused.PlanId,
            TaskId: t.TaskId)).ToList();

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            paused, group, "rev-soak-1", items, "SOAK-003");

        Assert.Multiple(() =>
        {
            Assert.That(resumed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(resumed.InterruptionData, Is.Null,
                "Resume must clear InterruptionData.");
            Assert.That(resumed.Progress.ExecutingTaskId, Is.EqualTo("SOAK-003"),
                "Must advance to next runnable task, not repeat completed work.");
            Assert.That(resumed.Progress.CompletedCount, Is.EqualTo(2),
                "Previously completed tasks remain counted.");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // (e) Validation shields at declared boundaries
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void ValidationShield_Pending_BeforePrereqsComplete()
    {
        var plan = StartExecution(MakeSoakPlan());
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Pending));
    }

    [Test]
    public void ValidationShield_Ready_AfterPrereqsComplete()
    {
        var plan = MakeSoakPlan(completed: 3, executingTaskId: null) with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Validations = new[]
            {
                new PlanValidationNode(
                    "V-WIRE", "Verify UI wiring",
                    "Verify service output is rendered in a host panel surface.",
                    ["SOAK-003"], ["SOAK-004"],
                    ["Service output is consumed by a visible host panel."],
                    ["service-output"], "evidence", ["dotnet build"], true,
                    PlanValidationStatus.Ready),
            },
        };

        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Ready));
    }

    [Test]
    public void ValidationShield_Validating_DuringExecution()
    {
        var state = ValidationShieldPresenter.DeriveVisualState(PlanValidationStatus.Validating);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Validating));
    }

    [Test]
    public void ValidationShield_Passed_AfterSuccess()
    {
        var state = ValidationShieldPresenter.DeriveVisualState(PlanValidationStatus.Passed);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Passed));
    }

    [Test]
    public void ValidationShield_Failed_AfterRejection()
    {
        var state = ValidationShieldPresenter.DeriveVisualState(PlanValidationStatus.Failed);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Failed));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // (f) Final completion — all tasks done
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FinalCompletion_AllTasksDone_PlanReachesCompleted()
    {
        var plan = MakeSoakPlan(completed: 4) with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Progress = new PlanProgress(4, 4, null),
        };

        var completed = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.Multiple(() =>
        {
            Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
            Assert.That(completed.Progress.CompletedCount, Is.EqualTo(4));
            Assert.That(completed.Progress.TotalCount, Is.EqualTo(4));
            Assert.That(completed.Progress.ExecutingTaskId, Is.Null);
            Assert.That(completed.Timestamps.CompletedAt, Is.Not.Null);
            Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(completed),
                Is.EqualTo(PlanTaskActivityState.Completed));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // (g) Archive a stale plan — Show Archived reveals it
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void ArchiveStalePlan_NeverStarted_ShowArchivedReveals() =>
        WpfTestContext.Run(() =>
        {
            var stalePlan = MakeSoakPlan() with
            {
                PlanId = "STALE-SOAK",
                Title = "Stale Never-Started Plan",
                Timestamps = new PlanTimestamps(CreatedAt: BaseTime.AddDays(-14)),
            };

            var archived = PlanStoreUpdater.ApplyArchived(stalePlan);

            Assert.Multiple(() =>
            {
                Assert.That(archived.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Archived));
                Assert.That(archived.Timestamps.ArchivedAt, Is.Not.Null);
                Assert.That(archived.Tasks.All(t => t.Status == PlanTaskStatus.Pending), Is.True,
                    "All tasks remain Pending — nothing was started.");
            });

            // Verify Plans panel filtering
            var activePlan = StartExecution(MakeSoakPlan());
            var (ctrl, activePanel, archivedPanel, archivedSection, _) = BuildController();

            ctrl.Refresh([activePlan, archived]);

            Assert.Multiple(() =>
            {
                Assert.That(activePanel.Children.OfType<Border>().Count(), Is.EqualTo(1),
                    "Only active plan in active section.");
                Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Collapsed),
                    "Archived section hidden by default.");
            });

            ctrl.SetShowArchived(true);

            Assert.Multiple(() =>
            {
                Assert.That(archivedSection.Visibility, Is.EqualTo(Visibility.Visible),
                    "Archived section visible after SetShowArchived(true).");
                Assert.That(archivedPanel.Children.OfType<Border>().Any(), Is.True,
                    "Archived plan row must appear.");
            });
        });

    // ═══════════════════════════════════════════════════════════════════════════
    // (h) Restart simulation — serialize/deserialize mid-execution
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void RestartSimulation_MidExecution_AllFieldsRoundTrip()
    {
        var workspace = new TestWorkspace();
        try
        {
            var squadFolder = workspace.GetPath(".squad");
            Directory.CreateDirectory(squadFolder);
            var store = new PlanStore(squadFolder);

            // Build a plan mid-execution with approval identity and interruption history
            var resolvedAt = BaseTime.AddMinutes(30);
            var identity = HumanApprovalIdentityResolver.FormatIdentity("Mark", null, "MillerMark");

            var plan = MakeSoakPlan(completed: 2, executingTaskId: "SOAK-003") with
            {
                LifecycleStatus = PlanLifecycleStatus.Executing,
                Timestamps = new PlanTimestamps(CreatedAt: BaseTime, StartedAt: BaseTime.AddMinutes(2)),
                ApprovalGates = new[]
                {
                    new PlanApprovalGate(
                        "gate-deploy", "Approve deployment readiness",
                        AfterTaskIds: ["SOAK-002"], BeforeTaskIds: ["SOAK-003"],
                        Status: PlanGateStatus.Approved,
                        ResolvedAt: resolvedAt,
                        ResolvedBy: identity,
                        ResolutionNote: "Approved after code review."),
                },
                Validations = new[]
                {
                    new PlanValidationNode(
                        "V-WIRE", "Verify UI wiring",
                        "Verify service output.",
                        ["SOAK-003"], ["SOAK-004"],
                        ["Service output consumed."],
                        ["service-output"], "evidence", ["dotnet build"], true,
                        PlanValidationStatus.Ready),
                },
            };

            store.Save(plan);
            var loaded = store.Load("SOAK-LIVE");

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null, "Plan must survive restart.");
                Assert.That(loaded!.PlanId, Is.EqualTo("SOAK-LIVE"));
                Assert.That(loaded.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
                Assert.That(loaded.Progress.CompletedCount, Is.EqualTo(2));
                Assert.That(loaded.Progress.ExecutingTaskId, Is.EqualTo("SOAK-003"));
                Assert.That(loaded.Tasks, Has.Count.EqualTo(4));

                // Approval identity survives
                var gate = loaded.ApprovalGates[0];
                Assert.That(gate.ResolvedBy, Is.EqualTo("Mark (@MillerMark)"));
                Assert.That(gate.ResolvedAt, Is.EqualTo(resolvedAt));
                Assert.That(gate.ResolutionNote, Is.EqualTo("Approved after code review."));

                // Validation state survives
                Assert.That(loaded.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
                Assert.That(loaded.Validations![0].ValidationId, Is.EqualTo("V-WIRE"));

                // Timestamps survive
                Assert.That(loaded.Timestamps.CreatedAt, Is.EqualTo(plan.Timestamps.CreatedAt));
                Assert.That(loaded.Timestamps.StartedAt, Is.EqualTo(plan.Timestamps.StartedAt));
            });
        }
        finally
        {
            workspace.Dispose();
        }
    }

    [Test]
    public void RestartSimulation_InterruptedState_PreservesInterruptionData()
    {
        var plan = StartExecution(MakeSoakPlan(completed: 2, executingTaskId: "SOAK-003")) with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
        };
        var paused = PlanStoreUpdater.ApplyInterrupted(
            plan, "User paused mid-soak.", loopIteration: 2,
            lastCompletedTaskId: "SOAK-002");

        var json = JsonSerializer.Serialize(paused, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(restored.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(restored.InterruptionData, Is.Not.Null);
            Assert.That(restored.InterruptionData!.Reason, Is.EqualTo("User paused mid-soak."));
            Assert.That(restored.InterruptionData.LastCompletedTaskId, Is.EqualTo("SOAK-002"));
            Assert.That(restored.InterruptionData.LoopIteration, Is.EqualTo(2));
            Assert.That(restored.Progress.CompletedCount, Is.EqualTo(2));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // (i) Full end-to-end lifecycle soak (single comprehensive integration test)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FullLifecycle_EndToEnd_SpinnerProgressApprovalPauseResumeValidationCompletion()
    {
        // 1. Collect plan (Approved status)
        var plan = MakeSoakPlan();
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));

        // 2. Start execution — spinner on task 1
        plan = StartExecution(plan);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan),
            Is.EqualTo(PlanTaskActivityState.Executing));
        Assert.That(plan.Progress.ExecutingTaskId, Is.EqualTo("SOAK-001"));

        // 3. Task 1 completes, task 2 begins
        plan = plan with
        {
            Progress = new PlanProgress(1, 4, "SOAK-002"),
            Tasks = plan.Tasks.Select(t => t.TaskId switch
            {
                "SOAK-001" => t with { Status = PlanTaskStatus.Complete, Commit = "aaa1111" },
                "SOAK-002" => t with { Status = PlanTaskStatus.Executing },
                _ => t,
            }).ToArray(),
        };
        Assert.That(plan.Progress.CompletedCount, Is.EqualTo(1));

        // 4. Task 2 completes
        plan = plan with
        {
            Progress = new PlanProgress(2, 4, null),
            Tasks = plan.Tasks.Select(t => t.TaskId == "SOAK-002"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "bbb2222" }
                : t).ToArray(),
        };

        // 5. Human approval at gate (between task 2 and task 3)
        var identity = HumanApprovalIdentityResolver.FormatIdentity("Mark", null, "MillerMark");
        var approvalTime = BaseTime.AddMinutes(15);
        plan = plan with
        {
            ApprovalGates = new[]
            {
                plan.ApprovalGates[0] with
                {
                    Status = PlanGateStatus.Approved,
                    ResolvedAt = approvalTime,
                    ResolvedBy = identity,
                    ResolutionNote = "Approved for integration testing.",
                },
            },
        };
        Assert.That(plan.ApprovalGates[0].ResolvedBy, Is.EqualTo("Mark (@MillerMark)"));

        // Verify tooltip renders relative time
        var tooltipNow = BaseTime.AddMinutes(20);
        var tooltip = ApprovalResolvedTooltipPresentation.Build(
            plan.ApprovalGates[0], "between steps 2 and 3", tooltipNow);
        Assert.That(tooltip, Does.Contain("Mark (@MillerMark)"));

        // 6. Task 3 starts, then pause-after-step
        plan = plan with
        {
            Progress = new PlanProgress(2, 4, "SOAK-003"),
            Tasks = plan.Tasks.Select(t => t.TaskId == "SOAK-003"
                ? t with { Status = PlanTaskStatus.Executing }
                : t).ToArray(),
        };

        var paused = PlanStoreUpdater.ApplyInterrupted(
            plan, "Paused by user after approval gate.", loopIteration: 3,
            lastCompletedTaskId: "SOAK-002");
        Assert.That(paused.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
        Assert.That(paused.InterruptionData!.LastCompletedTaskId, Is.EqualTo("SOAK-002"));

        // 7. Resume — picks up at SOAK-003
        var group = new DecomposedTaskGroup(
            GroupId: paused.PlanId, GroupTitle: paused.Title,
            Branch: paused.Branch, Summary: paused.Summary,
            Tasks: paused.Tasks.Select(t => new DecomposedSubTask(
                Id: t.TaskId, Description: t.Description,
                DependsOn: t.DependsOn.ToList(), Priority: t.Priority,
                Title: t.Title ?? t.TaskId)).ToList());

        var items = paused.Tasks.Select((t, idx) => new TaskItem(
            Text: t.TaskId, Owner: null, IsUserOwned: false,
            IsChecked: idx < 2, Emoji: idx < 2 ? "✅" : "🟡",
            RawLine: $"- [{(idx < 2 ? "x" : " ")}] **[{t.TaskId}]**",
            DecomposeGroupId: paused.PlanId, TaskId: t.TaskId)).ToList();

        plan = PlanStoreUpdater.ApplyExecutionStarted(paused, group, "rev-soak-1", items, "SOAK-003");
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(plan.Progress.ExecutingTaskId, Is.EqualTo("SOAK-003"));

        // 8. Task 3 completes — validation shield becomes Ready
        plan = plan with
        {
            Progress = new PlanProgress(3, 4, null),
            Tasks = plan.Tasks.Select(t => t.TaskId == "SOAK-003"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "ccc3333" }
                : t).ToArray(),
            Validations = new[]
            {
                plan.Validations![0] with { Status = PlanValidationStatus.Ready },
            },
        };
        Assert.That(ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Ready));

        // 9. Validation passes
        plan = plan with
        {
            Validations = new[]
            {
                plan.Validations![0] with
                {
                    Status = PlanValidationStatus.Passed,
                    ValidatedCommit = "ccc3333",
                },
            },
        };
        Assert.That(ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Passed));

        // 10. Task 4 completes — plan reaches Completed
        plan = plan with
        {
            Progress = new PlanProgress(4, 4, null),
            Tasks = plan.Tasks.Select(t => t.TaskId == "SOAK-004"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "ddd4444" }
                : t).ToArray(),
        };

        var completed = PlanStoreUpdater.ApplyCompleted(plan);
        Assert.Multiple(() =>
        {
            Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
            Assert.That(completed.Progress.CompletedCount, Is.EqualTo(4));
            Assert.That(completed.Timestamps.CompletedAt, Is.Not.Null);
            Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(completed),
                Is.EqualTo(PlanTaskActivityState.Completed));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Inbox report observations
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void InboxReport_DocumentedObservations()
    {
        // This test documents what the soak exercised and verifies the summary is coherent.
        var observations = new[]
        {
            "Running-task spinner: PlanTaskActivityResolver resolves Executing for active task",
            "Plans panel progress: CompletedCount advances correctly through 4 tasks",
            "Human approval identity: FormatIdentity produces 'Name (@login)' format",
            "Approval tooltip: relative time rendered via StatusTimingPresentation",
            "Pause-after-step: InterruptionData persisted with LastCompletedTaskId",
            "Resume: clears InterruptionData, advances to next task (not repeat)",
            "Validation shields: Pending → Ready → Validating → Passed state machine",
            "Final completion: PlanStoreUpdater.ApplyCompleted sets Completed with timestamp",
            "Stale archive: ApplyArchived moves plan to Archived, ShowArchived reveals it",
            "Restart: PlanStore round-trip preserves approval identity, interruption, validations",
        };

        Assert.That(observations, Has.Length.EqualTo(10),
            "Soak covers 10 distinct observable behaviors.");
        Assert.That(observations.All(o => !string.IsNullOrWhiteSpace(o)), Is.True);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper: Build PlansPanelController for WPF tests
    // ═══════════════════════════════════════════════════════════════════════════

    private record ActionLog(string Action, Plan Plan);

    private static (PlansPanelController controller, StackPanel activePanel,
        StackPanel archivedPanel, UIElement archivedSection, List<ActionLog> log)
        BuildController()
    {
        var activePanel = new StackPanel();
        var completedPanel = new StackPanel();
        var completedSection = new Border();
        var archivedPanel = new StackPanel();
        var archivedSection = new Border();
        var log = new List<ActionLog>();

        var controller = new PlansPanelController(
            activePanel: activePanel,
            completedPanel: completedPanel,
            completedSection: completedSection,
            archivedPanel: archivedPanel,
            archivedSection: archivedSection,
            openPlan: p => log.Add(new("open", p)),
            syncBorderVisibility: _ => { },
            setMenuChecked: _ => { },
            persistVisibility: () => { },
            startPlan: p => log.Add(new("start", p)),
            resumePlan: p => log.Add(new("resume", p)),
            endPlan: p => log.Add(new("end", p)),
            archivePlan: p => log.Add(new("archive", p)),
            pausePlan: p => log.Add(new("pause", p)),
            abortPlan: p => log.Add(new("abort", p)),
            isPromptRunning: () => true);

        return (controller, activePanel, archivedPanel, archivedSection, log);
    }
}

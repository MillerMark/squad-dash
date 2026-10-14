using System;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Exhaustive tests for <see cref="PlanPauseMenuPolicy"/> — the pure, WPF-free decisions behind the
/// Plans-panel-row Pause/Resume toggle and the single paused-plan approval-card suppression gate.
/// Every <see cref="PlanLifecycleStatus"/> constant is covered for visibility; the label and the
/// suppression predicate are covered for both IsPaused states.
/// </summary>
[TestFixture]
internal sealed class PlanPauseMenuPolicyTests
{
    private static Plan MakePlan(string lifecycleStatus, bool isPaused = false) =>
        new(
            PlanId:          "plan-1",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: lifecycleStatus,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan.",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(0, 0),
            Timestamps:      new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow),
            IsPaused:        isPaused);

    // ── Visibility: SHOW when started but not terminal ────────────────────────

    [TestCase(PlanLifecycleStatus.Executing)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval)]
    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    [TestCase(PlanLifecycleStatus.WaitingForCleanWorktree)]
    public void IsPauseResumeAvailable_True_WhenStartedAndNotTerminal(string status) =>
        Assert.That(PlanPauseMenuPolicy.IsPauseResumeAvailable(MakePlan(status)), Is.True);

    // ── Visibility: HIDE when not started or terminal ─────────────────────────

    [TestCase(PlanLifecycleStatus.Staged)]
    [TestCase(PlanLifecycleStatus.Approved)]
    [TestCase(PlanLifecycleStatus.Stopped)]
    [TestCase(PlanLifecycleStatus.Completed)]
    [TestCase(PlanLifecycleStatus.Archived)]
    public void IsPauseResumeAvailable_False_WhenNotStartedOrTerminal(string status) =>
        Assert.That(PlanPauseMenuPolicy.IsPauseResumeAvailable(MakePlan(status)), Is.False);

    [Test]
    public void IsPauseResumeAvailable_CoversEveryLifecycleStatusConstant()
    {
        // Guards against a new lifecycle status silently defaulting to "hidden" without a decision.
        foreach (var status in PlanLifecycleStatus.All)
        {
            var expectedVisible = status is
                PlanLifecycleStatus.Executing or
                PlanLifecycleStatus.AwaitingApproval or
                PlanLifecycleStatus.Interrupted or
                PlanLifecycleStatus.Blocked or
                PlanLifecycleStatus.WaitingForCleanWorktree;
            Assert.That(
                PlanPauseMenuPolicy.IsPauseResumeAvailable(MakePlan(status)),
                Is.EqualTo(expectedVisible),
                $"Lifecycle status '{status}' visibility decision is undefined.");
        }
    }

    [Test]
    public void IsPauseResumeAvailable_IndependentOfPausedState()
    {
        Assert.That(
            PlanPauseMenuPolicy.IsPauseResumeAvailable(MakePlan(PlanLifecycleStatus.Executing, isPaused: true)),
            Is.True);
        Assert.That(
            PlanPauseMenuPolicy.IsPauseResumeAvailable(MakePlan(PlanLifecycleStatus.Staged, isPaused: true)),
            Is.False);
    }

    [Test]
    public void IsPauseResumeAvailable_NullPlan_Throws() =>
        Assert.That(() => PlanPauseMenuPolicy.IsPauseResumeAvailable(null!), Throws.ArgumentNullException);

    // ── Toggle label ─────────────────────────────────────────────────────────

    [Test]
    public void ToggleLabel_NotPaused_IsPause() =>
        Assert.That(
            PlanPauseMenuPolicy.ToggleLabel(MakePlan(PlanLifecycleStatus.Executing, isPaused: false)),
            Is.EqualTo("Pause plan"));

    [Test]
    public void ToggleLabel_Paused_IsResume() =>
        Assert.That(
            PlanPauseMenuPolicy.ToggleLabel(MakePlan(PlanLifecycleStatus.Executing, isPaused: true)),
            Is.EqualTo("Resume plan"));

    [Test]
    public void ToggleLabel_MatchesPublishedConstants()
    {
        Assert.That(PlanPauseMenuPolicy.PauseLabel, Is.EqualTo("Pause plan"));
        Assert.That(PlanPauseMenuPolicy.ResumeLabel, Is.EqualTo("Resume plan"));
    }

    [Test]
    public void ToggleLabel_IndependentOfLifecycleStatus()
    {
        foreach (var status in PlanLifecycleStatus.All)
        {
            Assert.That(PlanPauseMenuPolicy.ToggleLabel(MakePlan(status, isPaused: false)), Is.EqualTo("Pause plan"));
            Assert.That(PlanPauseMenuPolicy.ToggleLabel(MakePlan(status, isPaused: true)), Is.EqualTo("Resume plan"));
        }
    }

    [Test]
    public void ToggleLabel_NullPlan_Throws() =>
        Assert.That(() => PlanPauseMenuPolicy.ToggleLabel(null!), Throws.ArgumentNullException);

    // ── Approval-card suppression predicate ───────────────────────────────────

    [Test]
    public void ShouldSuppressApprovalCard_True_WhenPaused() =>
        Assert.That(
            PlanPauseMenuPolicy.ShouldSuppressApprovalCard(MakePlan(PlanLifecycleStatus.AwaitingApproval, isPaused: true)),
            Is.True);

    [Test]
    public void ShouldSuppressApprovalCard_False_WhenNotPaused() =>
        Assert.That(
            PlanPauseMenuPolicy.ShouldSuppressApprovalCard(MakePlan(PlanLifecycleStatus.AwaitingApproval, isPaused: false)),
            Is.False);

    [Test]
    public void ShouldSuppressApprovalCard_False_WhenPlanIsNull() =>
        Assert.That(PlanPauseMenuPolicy.ShouldSuppressApprovalCard(null), Is.False);

    [Test]
    public void ShouldSuppressApprovalCard_DependsOnlyOnPausedFlag_NotLifecycleStatus()
    {
        foreach (var status in PlanLifecycleStatus.All)
        {
            Assert.That(
                PlanPauseMenuPolicy.ShouldSuppressApprovalCard(MakePlan(status, isPaused: true)),
                Is.True,
                $"Paused plan in '{status}' must suppress its approval card.");
            Assert.That(
                PlanPauseMenuPolicy.ShouldSuppressApprovalCard(MakePlan(status, isPaused: false)),
                Is.False,
                $"Unpaused plan in '{status}' must not be suppressed by the pause gate.");
        }
    }
}

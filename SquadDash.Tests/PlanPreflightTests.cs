using NUnit.Framework;
using System.Collections.Generic;

namespace SquadDash.Tests;

/// <summary>
/// Verifies the shape and contract of <see cref="PlanPreflightBlockedException"/>.
/// </summary>
[TestFixture]
internal sealed class PlanPreflightTests
{
    [Test]
    public void RepositoryRequired_OffersInitializationAndAutomaticPlanContinuation()
    {
        var exception = new PlanPreflightBlockedException(
            PlanPreflightBlockedException.RepositoryInitializationRequiredCondition,
            [],
            "feature/calculator");

        var content = PlanPreflightRecoveryContent.From(exception);

        Assert.Multiple(() =>
        {
            Assert.That(exception.RequiresRepositoryInitialization, Is.True);
            Assert.That(content.Title, Is.EqualTo("Git repository required"));
            Assert.That(content.Summary, Does.Contain("does not have an active Git branch"));
            Assert.That(content.Summary, Does.Contain("No plan work was started"));
            Assert.That(content.ChangedFilesSummary, Is.Empty);
            Assert.That(content.RecoveryGuidance, Does.Contain("Initialize repository and start plan"));
            Assert.That(content.RecoveryGuidance, Does.Contain("continue the interrupted plan action automatically"));
            Assert.That(content.TechnicalDetails, Does.Contain("feature/calculator"));
        });
    }

    [Test]
    public void RecoveryContent_ExplainsThatPlanDidNotStartAndListsEveryPath()
    {
        var exception = new PlanPreflightBlockedException(
            "Uncommitted changes", [".squad/tasks.md", "src/App.cs"], "feature/recovery");

        var content = PlanPreflightRecoveryContent.From(exception);

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Plan not started"));
            Assert.That(content.Summary, Does.Contain("No plan work was started"));
            Assert.That(content.Summary, Does.Contain("feature/recovery"));
            Assert.That(content.ChangedFilesSummary, Does.Contain(".squad/tasks.md"));
            Assert.That(content.ChangedFilesSummary, Does.Contain("src/App.cs"));
            Assert.That(content.RecoveryGuidance, Does.Contain("commit or stash"));
            Assert.That(content.RecoveryGuidance, Does.Contain("will not discard"));
            Assert.That(content.TechnicalDetails, Does.Contain("Changed files: 2"));
            Assert.That(content.ClipboardText, Does.Contain("Plan not started"));
            Assert.That(content.ClipboardText, Does.Contain("feature/recovery"));
            Assert.That(content.ClipboardText, Does.Contain("src/App.cs"));
        });
    }

    [Test]
    public void RecoveryContent_CapsVisiblePathsButCopiesEveryPath()
    {
        var paths = new[]
        {
            "src/One.cs",
            "src/Two.cs",
            "src/Three.cs",
            "src/Four.cs",
            "src/Five.cs",
        };
        var exception = new PlanPreflightBlockedException(
            "Uncommitted changes", paths, "feature/recovery");

        var content = PlanPreflightRecoveryContent.FromAmendment(
            exception,
            "Build guided-tour fixtures",
            "Clean up fixtures on every exit");

        Assert.Multiple(() =>
        {
            Assert.That(content.ChangedFilesSummary, Does.Contain("src/One.cs"));
            Assert.That(content.ChangedFilesSummary, Does.Contain("src/Three.cs"));
            Assert.That(content.ChangedFilesSummary, Does.Not.Contain("src/Four.cs"));
            Assert.That(content.ChangedFilesSummary, Does.Contain("+ 2 more files"));
            Assert.That(content.TechnicalDetails, Does.Not.Contain("src/Five.cs"));
            Assert.That(content.ClipboardText, Does.Contain("src/Four.cs"));
            Assert.That(content.ClipboardText, Does.Contain("src/Five.cs"));
        });
    }

    [Test]
    public void ReworkRecoveryContent_ExplainsThatReopenIsPreservedAndOffersResume()
    {
        var exception = new PlanPreflightBlockedException(
            "Uncommitted changes", [".squad/tasks.md", "tour.json"], "feature/rework");

        var content = PlanPreflightRecoveryContent.FromRework(
            exception,
            "Build guided-tour fixtures",
            "Add simulated notes");

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Rework ready — execution paused"));
            Assert.That(content.Summary, Does.Contain("Add simulated notes"));
            Assert.That(content.Summary, Does.Contain("no new task work started"));
            Assert.That(content.RecoveryGuidance, Does.Contain("Resume Rework"));
            Assert.That(content.RecoveryGuidance, Does.Contain("will not submit them again"));
            Assert.That(content.ChangedFilesSummary, Does.Contain("tour.json"));
        });
    }

    [Test]
    public void ReworkPreflightPause_IsSafelyResumableAndRecognizesPendingAttempt()
    {
        var attempted = new PlanTaskAttempt(
            PlanTaskStatus.Complete,
            "abcdef1",
            System.DateTimeOffset.UtcNow,
            "Initial implementation",
            "changes-requested",
            "Adjust the guided tour.");
        var task = new PlanTask(
            "P-1", "Add simulated notes", "Description", [], "mid", PlanTaskStatus.Pending,
            AttemptHistory: [attempted]);
        var executing = new Plan(
            "P", "rev", PlanSource.Manual, PlanLifecycleStatus.Executing,
            "Build guided-tour fixtures", "feature/rework", "Summary",
            [task], [], new PlanProgress(0, 1), new PlanTimestamps(System.DateTimeOffset.UtcNow));
        var paused = PlanStoreUpdater.ApplyInterrupted(
            executing,
            PlanRecoveryResumePolicy.BuildReworkPreflightReason("Two files are dirty."),
            loopIteration: 0,
            interruptedTaskId: task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryResumePolicy.HasPendingRework(executing), Is.True);
            Assert.That(PlanRecoveryResumePolicy.IsReworkPreflightPause(paused), Is.True);
            Assert.That(PlanRecoveryResumePolicy.IsSafelyResumable(paused), Is.True);
            Assert.That(paused.Progress.ExecutingTaskId, Is.Null);
        });
    }

    [Test]
    public void AcceptedWorkContinuation_IsSafelyResumableWithoutRepeatingCompletedTask()
    {
        var completed = new PlanTask(
            "P-1", "Accepted amendment", "Description", [], "high", PlanTaskStatus.Complete,
            Commit: "abcdef1");
        var next = new PlanTask(
            "P-2", "Next task", "Description", [completed.TaskId], "normal", PlanTaskStatus.Pending);
        var interruption = new PlanInterruptionData(
            "Preserved work for P-1 was adopted as abcdef1.",
            PlanRecoveryState.PendingRecovery,
            0,
            InterruptedTaskId: next.TaskId,
            LastCompletedTaskId: completed.TaskId,
            LastCommit: "abcdef1");
        var plan = new Plan(
            "P", "rev", PlanSource.Manual, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/plan", "Summary", [completed, next], [],
            new PlanProgress(1, 2), new PlanTimestamps(System.DateTimeOffset.UtcNow),
            InterruptionData: interruption);

        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryResumePolicy.IsAcceptedWorkContinuation(plan), Is.True);
            Assert.That(PlanRecoveryResumePolicy.IsSafelyResumable(plan), Is.True);
        });
    }

    [Test]
    public void PlanPreflightBlockedException_Properties_StoredCorrectly()
    {
        var paths = new List<string> { "src/Foo.cs", "src/Bar.cs" };
        var ex = new PlanPreflightBlockedException("Uncommitted changes", paths, "feature/my-branch");

        Assert.Multiple(() =>
        {
            Assert.That(ex.Condition,     Is.EqualTo("Uncommitted changes"));
            Assert.That(ex.TargetBranch,  Is.EqualTo("feature/my-branch"));
            Assert.That(ex.ChangedPaths,  Has.Count.EqualTo(2));
            Assert.That(ex.ChangedPaths,  Does.Contain("src/Foo.cs"));
            Assert.That(ex.ChangedPaths,  Does.Contain("src/Bar.cs"));
        });
    }

    [Test]
    public void PlanPreflightBlockedException_IsNotInvalidOperationException()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", [], "main");

        Assert.That(ex, Is.Not.InstanceOf<System.InvalidOperationException>(),
            "PlanPreflightBlockedException must be its own type, not a subclass of InvalidOperationException.");
    }

    [Test]
    public void PlanPreflightBlockedException_Message_IncludesConditionAndBranch()
    {
        var ex = new PlanPreflightBlockedException(
            "Uncommitted changes",
            ["src/A.cs"],
            "feature/xyz");

        Assert.That(ex.Message, Does.Contain("Uncommitted changes"));
        Assert.That(ex.Message, Does.Contain("feature/xyz"));
    }

    [Test]
    public void PlanPreflightBlockedException_NullBranch_MessageOmitsBranchClause()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", ["src/A.cs"], targetBranch: null);

        // No branch text — message must still be well-formed
        Assert.That(ex.Message, Does.Contain("Uncommitted changes"));
        Assert.That(ex.Message, Does.Not.Contain("branch"));
    }

    [Test]
    public void PlanPreflightBlockedException_EmptyPaths_ZeroCount()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", [], "main");

        Assert.That(ex.ChangedPaths, Is.Empty);
        Assert.That(ex.Message,      Does.Contain("0 file(s)"));
    }

    [Test]
    public void PlanPreflightBlockedException_IsException()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", [], null);

        Assert.That(ex, Is.InstanceOf<System.Exception>());
    }
}

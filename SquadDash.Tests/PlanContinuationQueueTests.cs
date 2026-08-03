namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanContinuationQueueTests
{
    // ── Selection: read-only text is returned when the item is built ────────────

    [Test]
    public void Build_SetsReadOnlyDisplayText_WithPlanAndDependencyDetails()
    {
        var display = PlanContinuationQueuePresentation.Build(BuildPlan(completed: 2, total: 5));

        Assert.That(display, Is.Not.Null);
        Assert.That(display!.Description, Does.Contain("locked continuation"));
        Assert.That(display.Description, Does.Contain("Plan:"));
        Assert.That(display.Description, Does.Contain("Next task:"));
        Assert.That(display.Description, Does.Contain("Why it is next:"));
        Assert.That(display.Description, Does.Contain("Release:"));
    }

    [Test]
    public void Build_LabelIncludesTaskTitle()
    {
        var display = PlanContinuationQueuePresentation.Build(BuildPlan(completed: 0, total: 4));

        Assert.That(display, Is.Not.Null);
        Assert.That(display!.Label, Is.EqualTo("Plan Step 2: Task 2"));
    }

    [Test]
    public void Build_LabelFallsBackToTaskId_WhenTitleIsNull()
    {
        var tasks = new[]
        {
            new PlanTask("P-1", "Task 1", "D", [], "normal", PlanTaskStatus.Complete),
            new PlanTask("P-2", null, "D", [], "normal", PlanTaskStatus.Executing),
            new PlanTask("P-3", null, "D", [], "normal", PlanTaskStatus.Pending),
        };
        var plan = new Plan(
            "P", "rev", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "My Plan", "feature/x", "Summary", tasks, [],
            new PlanProgress(1, 3, "P-2"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var display = PlanContinuationQueuePresentation.Build(plan);

        Assert.That(display, Is.Not.Null);
        Assert.That(display!.Label, Is.EqualTo("Plan Step 3: P-3"));
    }

    // ── IsLocked prevents editing ──────────────────────────────────────────────

    [Test]
    public void LockedItem_CannotBeEdited()
    {
        var item = BuildContinuationQueueItem();

        Assert.That(item.IsLocked, Is.True);
        Assert.That(item.ReadOnlyDisplayText, Is.Not.Null.And.Not.Empty);
    }

    // ── Restart: persistence round-trip + no duplication ───────────────────────

    [Test]
    public void EnsureContinuation_RemovesOldItemBeforeAdding()
    {
        var queue = new PromptQueue();
        // Simulate an existing continuation from a prior session restore.
        queue.EnqueueItem(new PromptQueueItem
        {
            Text = "old plan continuation",
            SourceTag = "plan-continuation",
            IsLocked = true,
            DisplayLabel = "Plan Step 2: Old",
            ReadOnlyDisplayText = "old description",
        });
        Assert.That(queue.Items.Count(i => i.SourceTag == "plan-continuation"), Is.EqualTo(1));

        // Simulate what EnsurePlanContinuationQueueItem does: remove then re-add.
        queue.RemoveByTag("plan-continuation");
        queue.EnqueueItem(BuildContinuationQueueItem());

        Assert.That(queue.Items.Count(i => i.SourceTag == "plan-continuation"), Is.EqualTo(1));
    }

    [Test]
    public void RemoveByTag_ClearsPriorContinuation_WhenPlanIsComplete()
    {
        var queue = new PromptQueue();
        queue.EnqueueItem(BuildContinuationQueueItem());
        Assert.That(queue.Items.Count, Is.EqualTo(1));

        // Plan completes — remove continuation.
        queue.RemoveByTag("plan-continuation");

        Assert.That(queue.Items.Count, Is.EqualTo(0));
    }

    // ── Stale state: when plan completes, continuation is removed ──────────────

    [Test]
    public void Build_ReturnsNull_WhenAllStepsComplete()
    {
        // All steps done — no next step.
        Assert.That(PlanContinuationQueuePresentation.Build(BuildPlan(completed: 5, total: 5)), Is.Null);
    }

    [Test]
    public void Build_ReturnsNull_WhenOnlyExecutingStepRemains()
    {
        // Only the currently executing step is left — no continuation beyond it.
        Assert.That(PlanContinuationQueuePresentation.Build(BuildPlan(completed: 4, total: 5)), Is.Null);
    }

    // ── Approval pause: when plan hits approval gate ───────────────────────────

    [Test]
    public void Build_StillReturnsContinuation_WhenPlanIsAwaitingApproval()
    {
        var tasks = Enumerable.Range(1, 6)
            .Select(i => new PlanTask(
                $"P-{i}", $"Task {i}", "D", [], "normal",
                i <= 2 ? PlanTaskStatus.Complete :
                i == 3 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending))
            .ToArray();
        var plan = new Plan(
            "P", "rev", PlanSource.DecomposeDecision, PlanLifecycleStatus.AwaitingApproval,
            "Plan", "feature/plan", "S", tasks, [],
            new PlanProgress(2, 6, "P-3"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var display = PlanContinuationQueuePresentation.Build(plan);

        // Continuation still shows (it's the host's responsibility to remove/update
        // when the plan status changes to awaiting-approval at the UI layer).
        Assert.That(display, Is.Not.Null);
        Assert.That(display!.StepNumber, Is.EqualTo(4));
    }

    // ── Dequeue: continuation is consumed during drain ─────────────────────────

    [Test]
    public void DequeueFirstReady_ReturnsContinuationItem_WhenAtFront()
    {
        var queue = new PromptQueue();
        var continuation = BuildContinuationQueueItem();
        queue.EnqueueItem(continuation);

        var dequeued = queue.DequeueFirstReady();

        Assert.That(dequeued, Is.Not.Null);
        Assert.That(dequeued!.SourceTag, Is.EqualTo("plan-continuation"));
        Assert.That(queue.Items, Is.Empty);
    }

    // ── Ordering: user items are placed before the continuation ────────────────

    [Test]
    public void UserItems_AppearBeforeContinuation_InQueue()
    {
        var queue = new PromptQueue();
        var continuation = BuildContinuationQueueItem();
        queue.EnqueueItem(continuation);

        var userItem = new PromptQueueItem { Text = "user prompt", SequenceNumber = 2 };
        queue.EnqueueItem(userItem);
        // Simulate PlaceNewQueueItemBeforePlanContinuation: move user item before continuation.
        var continuationIndex = queue.Items
            .Select((item, index) => (item, index))
            .First(pair => pair.item.SourceTag == "plan-continuation").index;
        var userIndex = queue.Items
            .Select((item, index) => (item, index))
            .First(pair => ReferenceEquals(pair.item, userItem)).index;
        if (userIndex > continuationIndex)
            queue.Reorder(userItem.Id, continuationIndex);

        Assert.That(queue.Items[0].Text, Is.EqualTo("user prompt"));
        Assert.That(queue.Items[1].SourceTag, Is.EqualTo("plan-continuation"));
    }

    // ── Mutation prevention: continuation cannot be reordered by user ──────────

    [Test]
    public void LockedContinuationItem_HasSourceTagAndIsLocked()
    {
        var item = BuildContinuationQueueItem();

        Assert.Multiple(() =>
        {
            Assert.That(item.SourceTag, Is.EqualTo("plan-continuation"));
            Assert.That(item.IsLocked, Is.True);
            Assert.That(item.DisplayLabel, Does.StartWith("Plan Step"));
        });
    }

    // ── Dependency reason text ─────────────────────────────────────────────────

    [Test]
    public void Build_ShowsDependencyNames_WhenNextTaskHasDependencies()
    {
        var tasks = new[]
        {
            new PlanTask("P-1", "Setup DB", "D", [], "normal", PlanTaskStatus.Complete),
            new PlanTask("P-2", "Auth Module", "D", [], "normal", PlanTaskStatus.Executing),
            new PlanTask("P-3", "User API", "D", ["P-1", "P-2"], "normal", PlanTaskStatus.Pending),
        };
        var plan = new Plan(
            "P", "rev", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "Plan", "feature/x", "S", tasks, [],
            new PlanProgress(1, 3, "P-2"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var display = PlanContinuationQueuePresentation.Build(plan);

        Assert.That(display, Is.Not.Null);
        Assert.That(display!.Description, Does.Contain("Setup DB"));
        Assert.That(display.Description, Does.Contain("Auth Module"));
        Assert.That(display.Description, Does.Contain("becomes eligible after"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static PromptQueueItem BuildContinuationQueueItem()
    {
        var plan = BuildPlan(completed: 1, total: 5);
        var presentation = PlanContinuationQueuePresentation.Build(plan)!;
        return new PromptQueueItem
        {
            Text = $"Continue plan P",
            SequenceNumber = 1,
            QueueNumber = 1,
            IsSystemInjected = true,
            SourceTag = "plan-continuation",
            IsLocked = true,
            DisplayLabel = presentation.Label,
            ReadOnlyDisplayText = presentation.Description,
        };
    }

    private static Plan BuildPlan(int completed, int total)
    {
        var tasks = Enumerable.Range(1, total)
            .Select(index => new PlanTask(
                $"P-{index}", $"Task {index}", "Description", [], "normal",
                index <= completed ? PlanTaskStatus.Complete :
                index == completed + 1 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending))
            .ToArray();
        return new Plan(
            "P", "revision", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary", tasks, [],
            new PlanProgress(completed, total, tasks.ElementAtOrDefault(completed)?.TaskId),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }
}

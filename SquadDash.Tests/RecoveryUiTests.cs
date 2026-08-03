using System;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests covering the plain-language recovery inbox message produced by
/// <see cref="DecomposePlanInbox.BuildRecoveryMessage"/>.
/// </summary>
[TestFixture]
internal sealed class RecoveryUiTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static PendingDecomposePlan BuildPlan(
        string groupId    = "PLAN-20260728",
        string groupTitle = "My Feature Plan",
        string branch     = "feature/my-plan") =>
        new PendingDecomposePlan(
            "rev-abc123",
            new DecomposedTaskGroup(
                groupId,
                groupTitle,
                branch,
                "Implement the feature.",
                [new DecomposedSubTask("PLAN-20260728-001", "First task", [], "high")]));

    private static InboxMessage BuildMessage(
        string taskId = "PLAN-20260728-003",
        string reason = "The AI exceeded the context window.") =>
        DecomposePlanInbox.BuildRecoveryMessage(
            BuildPlan(),
            taskId,
            reason,
            DateTimeOffset.Parse("2026-07-28T10:00:00Z"));

    // ── Body format ──────────────────────────────────────────────────────────

    [Test]
    public void BuildRecoveryMessage_BodyContainsPlanTitle()
    {
        var message = BuildMessage();
        Assert.That(message.Body, Does.Contain("My Feature Plan"),
            "Body should name the plan by title for plain-language clarity.");
    }

    [Test]
    public void BuildRecoveryMessage_BodyContainsTaskId()
    {
        var message = BuildMessage(taskId: "PLAN-20260728-003");
        Assert.That(message.Body, Does.Contain("PLAN-20260728-003"),
            "Body should identify which task blocked the plan.");
    }

    [Test]
    public void BuildRecoveryMessage_BodyContainsReason()
    {
        const string reason = "The AI exceeded the context window.";
        var message = BuildMessage(reason: reason);
        Assert.That(message.Body, Does.Contain(reason),
            "Body should include the raw reason inside a blockquote.");
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    [Test]
    public void BuildRecoveryMessage_HasAssessAndContinueAction()
    {
        var message = BuildMessage();
        Assert.That(
            message.Actions.Select(a => a.Label),
            Has.Member("Assess & Continue"),
            "Recovery message must expose the evidence-driven continuation action.");
    }

    [Test]
    public void BuildRecoveryMessage_DoesNotOfferBlindRetry()
    {
        var message = BuildMessage();
        Assert.That(
            message.Actions.Select(a => a.Label),
            Has.None.Contains("Retry"));
    }

    [Test]
    public void BuildRecoveryMessage_HasReplanAction()
    {
        var message = BuildMessage();
        Assert.That(
            message.Actions.Select(a => a.Label),
            Has.Member("Replan Remaining Work"),
            "Recovery message must expose the replan action.");
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    [Test]
    public void BuildRecoveryMessage_PriorityIsCritical()
    {
        var message = BuildMessage();
        Assert.That(message.Priority, Is.EqualTo("critical"),
            "A blocked plan needs immediate attention and must use critical priority.");
    }

    [Test]
    public void BuildRecoveryMessage_IdIncludesTaskId()
    {
        var message = BuildMessage(taskId: "PLAN-20260728-007");
        Assert.That(message.Id, Does.Contain("PLAN-20260728-007"),
            "Message ID must include the task ID so callers can de-duplicate by task.");
    }
}

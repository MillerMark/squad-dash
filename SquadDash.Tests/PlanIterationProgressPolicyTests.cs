using NUnit.Framework;

namespace SquadDash.Tests;

internal sealed class PlanIterationProgressPolicyTests
{
    [TestCase("tool_start", LoopIterationProgressKind.ToolActivity)]
    [TestCase("tool_complete", LoopIterationProgressKind.ToolActivity)]
    [TestCase("subagent_started", LoopIterationProgressKind.WorkerActivity)]
    [TestCase("subagent_message", LoopIterationProgressKind.WorkerActivity)]
    [TestCase("subagent_completed", LoopIterationProgressKind.WorkerActivity)]
    [TestCase("task_complete", LoopIterationProgressKind.VerificationActivity)]
    public void ExplicitWorkEventsRenewTheLease(string type, LoopIterationProgressKind expected)
    {
        var evt = new SquadSdkEvent { Type = type };

        Assert.That(PlanIterationProgressPolicy.TryResolve(evt, out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("thinking_delta")]
    [TestCase("usage")]
    [TestCase("session_ready")]
    public void TelemetryAndThoughtNoiseDoNotRenewTheLease(string type)
    {
        Assert.That(
            PlanIterationProgressPolicy.TryResolve(new SquadSdkEvent { Type = type }, out _),
            Is.False);
    }

    [Test]
    public void NonEmptyCoordinatorOutputRenewsTheLease()
    {
        Assert.That(
            PlanIterationProgressPolicy.TryResolve(
                new SquadSdkEvent { Type = "response_delta", Chunk = "saved commit" },
                out var kind),
            Is.True);
        Assert.That(kind, Is.EqualTo(LoopIterationProgressKind.CoordinatorOutput));
    }
}

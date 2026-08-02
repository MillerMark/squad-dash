namespace SquadDash.Tests;

[TestFixture]
internal sealed class QueuedLoopJobTests
{
    [Test]
    public void EncodeDecode_PreservesExactLoopPathAndTaskSnapshot()
    {
        var scope = new FilteredTaskScopeSnapshot(
            "parser",
            [new FilteredTaskScopeItem("task-1", "- [ ] Parser", "Parser")]);
        var job = new QueuedLoopJob(
            new ActiveLoopExecutionState(@"D:\repo\.squad\loop-filtered-tasks.md", scope.Encode()),
            "7 filtered tasks",
            7);

        var decoded = QueuedLoopJob.TryDecode(job.Encode(), out var restored);

        Assert.That(decoded, Is.True);
        Assert.That(restored?.Execution.LoopPath, Is.EqualTo(job.Execution.LoopPath));
        Assert.That(restored?.Execution.FilterText, Is.EqualTo(job.Execution.FilterText));
        Assert.That(restored?.TaskCount, Is.EqualTo(7));
    }

    [Test]
    public void TryDecode_InvalidPayload_IsRejected()
    {
        Assert.That(QueuedLoopJob.TryDecode("SQUADDASH_QUEUED_LOOP_V1:not-base64", out _), Is.False);
    }
}

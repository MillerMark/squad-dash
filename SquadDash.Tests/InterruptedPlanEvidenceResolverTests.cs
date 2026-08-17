using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InterruptedPlanEvidenceResolverTests
{
    [Test]
    public void FindTaskResults_PrefersOriginalHandoffBeforeLaterRepairAndFiltersIdentity()
    {
        var turns = new[]
        {
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "aaaaaaa", "attempt-1")),
            Turn(ResultJson("OTHER", "TASK-1", "REV-1", "bbbbbbb", "attempt-1")),
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "ccccccc", "attempt-1")),
        };

        var results = InterruptedPlanEvidenceResolver.FindTaskResults(
            turns, "PLAN-1", "REV-1", "TASK-1");

        Assert.That(results.Select(result => result.Commit),
            Is.EqualTo(new[] { "aaaaaaa", "ccccccc" }));
    }

    [Test]
    public void FindTaskResults_DeduplicatesRepeatedRepairCommit()
    {
        var turns = new[]
        {
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "aaaaaaa", "attempt-1")),
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "aaaaaaa", "attempt-1")),
        };

        var results = InterruptedPlanEvidenceResolver.FindTaskResults(
            turns, "PLAN-1", "REV-1", "TASK-1");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    private static TranscriptTurnRecord Turn(string json) => new(
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "prompt",
        string.Empty,
        string.Empty,
        ThinkingCollapsed: false,
        Tools: Array.Empty<TranscriptToolRecord>(),
        ResponseSegments:
        [
            new TranscriptResponseSegmentRecord(string.Empty)
            {
                ProtocolJsonBlocks =
                [
                    new TranscriptProtocolJsonBlock(
                        DecomposeStepResultParser.Marker.TrimEnd(':'),
                        json),
                ],
            },
        ]);

    private static string ResultJson(
        string planId,
        string taskId,
        string revision,
        string commit,
        string attemptId) =>
        $$"""
        {
          "groupId": "{{planId}}",
          "taskId": "{{taskId}}",
          "revision": "{{revision}}",
          "status": "complete",
          "commit": "{{commit}}",
          "summary": "Completed work.",
          "remainingWork": [],
          "verification": {
            "status": "passed",
            "command": "test",
            "summary": "Tests passed."
          },
          "executionAttemptId": "{{attemptId}}"
        }
        """;
}

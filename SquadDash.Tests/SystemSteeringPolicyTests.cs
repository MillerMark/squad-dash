using System.Threading;
using System.Windows.Documents;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SystemSteeringPolicyTests
{
    [TestCase(14, SystemSteeringDecision.None)]
    [TestCase(15, SystemSteeringDecision.Gentle)]
    [TestCase(19, SystemSteeringDecision.Gentle)]
    public void Decide_BeforeFirstNotice_UsesExactFifteenMinuteBoundary(
        int minutes,
        SystemSteeringDecision expected)
    {
        Assert.That(SystemSteeringPolicy.Decide(minutes, false, 20, 0, -1), Is.EqualTo(expected));
    }

    [Test]
    public void Decide_StrongNoticeRequiresThresholdAndNewToolActivity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SystemSteeringPolicy.Decide(20, true, 20, 9, 9),
                Is.EqualTo(SystemSteeringDecision.None));
            Assert.That(SystemSteeringPolicy.Decide(19, true, 20, 10, 9),
                Is.EqualTo(SystemSteeringDecision.None));
            Assert.That(SystemSteeringPolicy.Decide(20, true, 20, 10, 9),
                Is.EqualTo(SystemSteeringDecision.Strong));
        });
    }

    [TestCase(20, 25)]
    [TestCase(25, 30)]
    [TestCase(27, 30)]
    [TestCase(36, 40)]
    public void NextStrongMinute_AdvancesToNextFiveMinuteBoundary(int elapsed, int expected)
    {
        Assert.That(SystemSteeringPolicy.NextStrongMinute(20, elapsed), Is.EqualTo(expected));
    }

    [TestCase("view", WorkToolCategory.Read)]
    [TestCase("rg", WorkToolCategory.Search)]
    [TestCase("apply_patch", WorkToolCategory.Write)]
    [TestCase("edit", WorkToolCategory.Write)]
    [TestCase("create", WorkToolCategory.Write)]
    [TestCase("create_file", WorkToolCategory.Write)]
    [TestCase("str_replace_editor", WorkToolCategory.Write)]
    [TestCase("delegate_temporary_agent", WorkToolCategory.Delegation)]
    [TestCase("powershell", WorkToolCategory.Shell)]
    [TestCase("new_provider_tool", WorkToolCategory.Other)]
    public void Classifier_UsesOnlyExactRegisteredToolNames(string tool, WorkToolCategory expected)
    {
        Assert.That(WorkToolClassifier.Classify(tool), Is.EqualTo(expected));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SystemSteeringNotice_PersistsAndRendersDistinctMarker()
    {
        var thread = new TranscriptThreadState(
            "system-steering-test",
            TranscriptThreadKind.Agent,
            "Worker",
            DateTimeOffset.UtcNow);
        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        var turn = new TranscriptTurnView(
            thread,
            "prompt",
            DateTimeOffset.UtcNow,
            narrative,
            [narrative]);
        const string message = "No write call has been observed.";
        var paragraph = SteeringTranscriptEntryFactory.CreateParagraph(
            message, [], null, null, null, null, isSystem: true);

        var entry = SteeringTranscriptEntryFactory.Append(
            turn,
            message,
            paragraph,
            DateTimeOffset.UtcNow,
            1,
            isSystem: true);

        Assert.That(
            SteeringTranscriptEntryFactory.TryDecodeHostNotice(
                entry.Text,
                out var decoded,
                out var isSystem),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.EqualTo(message));
            Assert.That(isSystem, Is.True);
            Assert.That(entry.GetCopyText(), Does.StartWith("↪️ System steering:"));
            Assert.That(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text,
                Does.Contain("↪️ System steering:"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void WorkMetrics_HistoricalRunKeepsItsOwnPlanScopeWhenThreadIsReused()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var thread = new TranscriptThreadState(
            "reused-agent",
            TranscriptThreadKind.Agent,
            "Lyra",
            startedAt)
        {
            AgentDisplayName = "Lyra",
            PlanId = "new-plan",
            PlanWorkItemKind = PlanWorkItemKinds.Verification,
            PlanWorkItemId = "new-task",
        };
        thread.SavedTurns.Add(new TranscriptTurnRecord(
            startedAt,
            startedAt.AddMinutes(2),
            "old work",
            string.Empty,
            "done",
            true,
            [])
        {
            PlanId = "old-plan",
            PlanWorkItemKind = PlanWorkItemKinds.Implementation,
            PlanWorkItemId = "old-task",
        });

        var run = WorkMetricsBuilder.BuildRuns([thread], DateTimeOffset.UtcNow).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.PlanId, Is.EqualTo("old-plan"));
            Assert.That(run.WorkItemKind, Is.EqualTo(PlanWorkItemKinds.Implementation));
            Assert.That(run.WorkItemId, Is.EqualTo("old-task"));
        });
    }
}

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanDescriptionMarkdownRoundTripTests
{
    [Test]
    public void DescriptionMarkdown_SurvivesParseEditApprovalProjectionAndDurableStorageExactly()
    {
        const string payload = """
            TASKS_JSON:
            {
              "groupId": "READABLE-20260923",
              "groupTitle": "Readable plan descriptions",
              "branch": "feature/readable-plan-descriptions",
              "summary": "Improve plan readability without changing execution semantics.",
              "tasks": [
                {
                  "id": "READABLE-20260923-001",
                  "title": "Render readable descriptions",
                  "description": "Keep the raw contract.\n\n## Observable outcome\n\nThe **inspector** renders `PlanViewerWindow` clearly.\n\n## Consumers\n\nThe plan author and task executor consume the same text.\n\n```csharp\nRender();\n```",
                  "dependsOn": [],
                  "priority": "high",
                  "executionMode": "implementation",
                  "agentRoutingMode": "generic",
                  "genericAgentReason": "No active roster specialist is required for this fixture."
                }
              ]
            }
            """;
        Assert.That(TasksJsonParser.TryParse(payload, out var parsed), Is.True);
        var expected = parsed!.Tasks.Single().Description;
        var edited = StagedPlanEditor.UpdateTask(
            parsed,
            "READABLE-20260923-001",
            "Render readable descriptions",
            "  " + expected + "  ",
            "high",
            []);

        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        var pending = new PendingDecomposePlanStore(squadFolder).Save(edited);
        var durable = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        var planStore = new PlanStore(squadFolder);
        planStore.Save(durable);
        var loaded = planStore.Load(durable.PlanId);
        var reconstructed = PendingDecomposePlanAdapter.FromPlan(loaded!);

        Assert.Multiple(() =>
        {
            Assert.That(edited.Tasks.Single().Description, Is.EqualTo(expected));
            Assert.That(pending.Group.Tasks.Single().Description, Is.EqualTo(expected));
            Assert.That(durable.Tasks.Single().Description, Is.EqualTo(expected));
            Assert.That(loaded!.Tasks.Single().Description, Is.EqualTo(expected));
            Assert.That(reconstructed.Group.Tasks.Single().Description, Is.EqualTo(expected));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(loaded), Is.True);
        });
    }
}

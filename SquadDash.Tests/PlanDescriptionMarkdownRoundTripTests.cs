using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanDescriptionMarkdownRoundTripTests
{
    [Test]
    public void SummaryAndDescriptionMarkdown_SurviveParseEditApprovalProjectionDurableStorageAndExecutionContextExactly()
    {
        const string payload = """
            TASKS_JSON:
            {
              "groupId": "READABLE-20260923",
              "groupTitle": "Readable plan descriptions",
              "branch": "feature/readable-plan-descriptions",
              "summary": "Improve plan readability without changing execution semantics.\n\n:::detail Rendering notes\nThe summary itself carries collapsible detail sections and must stay untouched.\n\n```json\n{\n  \"scope\": \"step-4-regression\",\n  \"preserve\": true\n}\n```\n:::\n\nThe host and executor must consume this as-authored.",
              "tasks": [
                {
                  "id": "READABLE-20260923-001",
                  "title": "Render readable descriptions",
                  "description": "Keep the raw contract.\n\n## Observable outcome\n\nThe **inspector** renders `PlanViewerWindow` clearly.\n\n:::detail Contract detail block\nThis task includes a detail section with fenced code and body text.\n\n```csharp\nRender();\nValidate(\"detail-preserved\");\n```\n:::\n\n## Consumers\n\nThe plan author and task executor consume the same text.",
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
        using var authored = JsonDocument.Parse(payload[(payload.IndexOf(':') + 1)..]);
        var expectedSummary = authored.RootElement.GetProperty("summary").GetString()!;
        var expectedDescription = authored.RootElement.GetProperty("tasks")[0].GetProperty("description").GetString()!;
        Assert.That(parsed!.Summary, Is.EqualTo(expectedSummary));
        Assert.That(parsed.Tasks.Single().Description, Is.EqualTo(expectedDescription));
        var summaryEdited = StagedPlanEditor.UpdatePlan(
            parsed,
            parsed.GroupTitle,
            parsed.Branch,
            "  " + expectedSummary + "  ",
            parsed.Delivery);
        var edited = StagedPlanEditor.UpdateTask(
            summaryEdited,
            "READABLE-20260923-001",
            "Render readable descriptions",
            "  " + expectedDescription + "  ",
            "high",
            []);

        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        var pending = new PendingDecomposePlanStore(squadFolder).Save(edited);
        var durable = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        var planStore = new PlanStore(squadFolder);
        planStore.Save(durable);
        var loaded = planStore.Load(durable.PlanId) ?? throw new AssertionException("Saved plan was not reloaded.");
        var reconstructed = PendingDecomposePlanAdapter.FromPlan(loaded);
        var executionContext = PlanExecutionContextBuilder.Build(loaded, loaded.Tasks.Single());

        Assert.Multiple(() =>
        {
            Assert.That(edited.Summary, Is.EqualTo(expectedSummary));
            Assert.That(pending.Group.Summary, Is.EqualTo(expectedSummary));
            Assert.That(durable.Summary, Is.EqualTo(expectedSummary));
            Assert.That(loaded!.Summary, Is.EqualTo(expectedSummary));
            Assert.That(reconstructed.Group.Summary, Is.EqualTo(expectedSummary));

            Assert.That(edited.Tasks.Single().Description, Is.EqualTo(expectedDescription));
            Assert.That(pending.Group.Tasks.Single().Description, Is.EqualTo(expectedDescription));
            Assert.That(durable.Tasks.Single().Description, Is.EqualTo(expectedDescription));
            Assert.That(loaded.Tasks.Single().Description, Is.EqualTo(expectedDescription));
            Assert.That(reconstructed.Group.Tasks.Single().Description, Is.EqualTo(expectedDescription));

            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(loaded), Is.True);
            Assert.That(executionContext, Does.Contain($"Guiding intent: {expectedSummary}"));
            Assert.That(executionContext, Does.Contain(expectedDescription));
        });
    }
}

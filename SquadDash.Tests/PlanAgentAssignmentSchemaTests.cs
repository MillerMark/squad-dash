using System;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanAgentAssignmentSchemaTests
{
    [Test]
    public void TasksJsonParser_AcceptsMultipleStructuredAssignments()
    {
        const string text = """
            TASKS_JSON:
            {
              "groupId":"ROUTE-20260728",
              "groupTitle":"Routing",
              "branch":"feature/routing",
              "summary":"Verify routing",
              "tasks":[{
                "id":"ROUTE-20260728-001",
                "title":"Implement routing",
                "description":"Implement and test routing",
                "dependsOn":[],
                "priority":"high",
                "parallelEligible":true,
                "agentAssignments":[
                  {"agentHandle":"talia-rune","role":"SDK implementation","allowGenericChildren":true},
                  {"agentHandle":"arjun-sen","role":"C# integration","allowGenericChildren":false}
                ]
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(text, out var group), Is.True);
        Assert.Multiple(() => {
            Assert.That(group!.Tasks[0].AgentAssignments, Has.Count.EqualTo(2));
            Assert.That(group.Tasks[0].ParallelEligible, Is.True);
        });
    }

    [Test]
    public void TasksWriterAndParser_PreserveAssignmentMetadata()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "tasks.md");
            var group = new DecomposedTaskGroup(
                "ROUTE-20260728", "Routing", "feature/routing", "Verify routing",
                [new DecomposedSubTask(
                    "ROUTE-20260728-001", "Implement", [], "high", "Implement routing",
                    AgentAssignments: [new DecomposedAgentAssignment("talia-rune", "SDK", true)],
                    ParallelEligible: true)]);

            new DecomposedTasksWriter().WriteGroup(path, group);
            var parsed = TasksPanelParser.Parse(File.ReadAllLines(path))
                .DecomposeGroups["ROUTE-20260728"].Tasks.Single();

            Assert.Multiple(() => {
                Assert.That(parsed.AgentAssignments, Has.Count.EqualTo(1));
                Assert.That(parsed.AgentAssignments![0].AgentHandle, Is.EqualTo("talia-rune"));
                Assert.That(parsed.ParallelEligible, Is.True);
            });
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void CatalogValidator_RejectsUnavailableAgentAndAcceptsActiveCharteredAgent()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-roster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "agents", "talia-rune"));
        try
        {
            File.WriteAllText(Path.Combine(temp, "team.md"), """
                | Name | Role | Charter | Status |
                |---|---|---|---|
                | Talia Rune | SDK | agents/talia-rune/charter.md | active |
                """);
            File.WriteAllText(Path.Combine(temp, "agents", "talia-rune", "charter.md"), "charter");
            var group = new DecomposedTaskGroup(
                "ROUTE-20260728", "Routing", "feature/routing", "Verify routing",
                [new DecomposedSubTask(
                    "ROUTE-20260728-001", "Implement", [], "high", "Implement routing",
                    AgentAssignments: [new DecomposedAgentAssignment("talia-rune", "SDK")])]);

            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                group, temp, out var validError), Is.True, validError);

            var invalid = group with {
                Tasks = [group.Tasks[0] with {
                    AgentAssignments = [new DecomposedAgentAssignment("missing-agent", "SDK")]
                }]
            };
            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                invalid, temp, out var error), Is.False);
            Assert.That(error, Does.Contain("missing-agent"));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

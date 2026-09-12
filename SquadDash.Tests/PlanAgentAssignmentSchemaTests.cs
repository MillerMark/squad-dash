using System;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanAgentAssignmentSchemaTests
{
    [Test]
    public void Validator_ProseCharterColumn_FindsCanonicalAgentCharter()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-routing-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "agents", "arjun-sen"));
            File.WriteAllText(Path.Combine(temp, "team.md"), """
                | Name | Role | Charter | Status |
                |---|---|---|---|
                | Arjun Sen | Backend Design | Domain modeling, API design, backend implementation | Active |
                """);
            File.WriteAllText(Path.Combine(temp, "agents", "arjun-sen", "charter.md"), "# Arjun Sen");
            var group = new DecomposedTaskGroup(
                "CALC-20260806", "Calculator", "feature/calculator", "Build calculator",
                [new DecomposedSubTask(
                    "CALC-20260806-001", "Build engine", [], "high", "Build engine",
                    AgentRoutingMode: "assigned",
                    AgentAssignments: [new DecomposedAgentAssignment("arjun-sen", "primary")])]);

            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                group, temp, out var error, requireExplicitRouting: true), Is.True, error);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void TasksJsonParser_RejectsMultiplePrimaryAssignments()
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

        Assert.That(TasksJsonParser.TryParse(text, out _, out var diagnostic), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic?.Code, Is.EqualTo("too-many-agent-assignments"));
            Assert.That(diagnostic?.Message, Does.Contain("split required work into dependent tasks")
                .And.Contain("read-only assistance"));
        });
    }

    [Test]
    public void Validator_AcceptsLegacyDecoratedStatusAndWorkspaceRelativeCharterPath()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-legacy-routing-" + Guid.NewGuid().ToString("N"));
        var squadFolder = Path.Combine(temp, ".squad");
        try
        {
            Directory.CreateDirectory(Path.Combine(squadFolder, "agents", "cassianrook"));
            File.WriteAllText(Path.Combine(squadFolder, "team.md"), """
                | Name | Role | Charter | Status |
                |---|---|---|---|
                | CassianRook | AI Model Integration Expert | `.squad/agents/cassianrook/charter.md` | ✅ Active |
                """);
            File.WriteAllText(
                Path.Combine(squadFolder, "agents", "cassianrook", "charter.md"),
                "# CassianRook");
            var group = new DecomposedTaskGroup(
                "LEGACY-20260817", "Legacy routing", "feature/legacy-routing", "Validate legacy routing",
                [new DecomposedSubTask(
                    "LEGACY-20260817-001", "Build integration", [], "high", "Build integration",
                    AgentRoutingMode: "assigned",
                    AgentAssignments: [new DecomposedAgentAssignment("cassianrook", "primary")])]);

            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                group, squadFolder, out var error, requireExplicitRouting: true), Is.True, error);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void TasksJsonParser_NormalizesUnambiguousAssignmentAndGateAliases()
    {
        const string text = """
            TASKS_JSON:
            {
              "groupId":"ROUTE-20260815",
              "groupTitle":"Routing",
              "branch":"feature/routing",
              "summary":"Verify routing",
              "tasks":[
                {
                  "id":"ROUTE-20260815-001",
                  "title":"Implement routing",
                  "description":"Implement routing. Observable outcome: routing exists. Production consumer: task 002 uses it.",
                  "dependsOn":[],
                  "priority":"high",
                  "executionMode":"implementation",
                  "agentRoutingMode":"assigned",
                  "assignedTo":"talia-rune"
                },
                {
                  "id":"ROUTE-20260815-002",
                  "title":"Use routing",
                  "description":"Use routing. Observable outcome: routing is exercised. Production consumer: the application uses it.",
                  "dependsOn":["ROUTE-20260815-001"],
                  "priority":"mid",
                  "executionMode":"implementation",
                  "agentRoutingMode":"generic",
                  "genericAgentReason":"Fixture"
                }
              ],
              "approvalGates":[{
                "gateId":"ROUTE-20260815-GATE-001",
                "title":"Review routing",
                "question":"Does the routing contract look correct?",
                "afterTaskIds":["ROUTE-20260815-001"],
                "beforeTaskIds":["ROUTE-20260815-002"]
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(text, out var group, out var diagnostic), Is.True, diagnostic?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(group!.Tasks[0].AgentAssignments, Has.Count.EqualTo(1));
            Assert.That(group.Tasks[0].AgentAssignments![0].AgentHandle, Is.EqualTo("talia-rune"));
            Assert.That(group.Tasks[0].AgentAssignments![0].Role, Is.EqualTo("Implement routing"));
            Assert.That(group.ApprovalGates![0].Message, Is.EqualTo("Review routing"));
        });
    }

    [Test]
    public void TasksJsonParser_RejectsConflictingAssignmentShapes()
    {
        const string text = """
            TASKS_JSON:
            {
              "groupId":"ROUTE-20260815",
              "groupTitle":"Routing",
              "branch":"feature/routing",
              "summary":"Verify routing",
              "tasks":[{
                "id":"ROUTE-20260815-001",
                "title":"Implement routing",
                "description":"Implement and test routing",
                "dependsOn":[],
                "priority":"high",
                "executionMode":"implementation",
                "agentRoutingMode":"assigned",
                "assignedTo":"talia-rune",
                "agentAssignments":[{"agentHandle":"arjun-sen","role":"Implement routing","allowGenericChildren":true}]
              }]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(text, out _, out var diagnostic), Is.False);
        Assert.That(diagnostic?.Code, Is.EqualTo("conflicting-agent-assignment-shapes"));
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
                    ParallelEligible: true,
                    AgentRoutingMode: "assigned")]);

            new DecomposedTasksWriter().WriteGroup(path, group);
            var parsed = TasksPanelParser.Parse(File.ReadAllLines(path))
                .DecomposeGroups["ROUTE-20260728"].Tasks.Single();

            Assert.Multiple(() => {
                Assert.That(parsed.AgentAssignments, Has.Count.EqualTo(1));
                Assert.That(parsed.AgentAssignments![0].AgentHandle, Is.EqualTo("talia-rune"));
                Assert.That(parsed.ParallelEligible, Is.True);
                Assert.That(parsed.AgentRoutingMode, Is.EqualTo("assigned"));
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
                    AgentAssignments: [new DecomposedAgentAssignment("talia-rune", "SDK", true)])]);

            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                group, temp, out var validError), Is.True, validError);

            var invalid = group with {
                Tasks = [group.Tasks[0] with {
                    AgentAssignments = [new DecomposedAgentAssignment("missing-agent", "SDK", false)]
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

    [Test]
    public void CatalogValidator_RequiresExplicitRoutingForNewPlansAndRejectsMultiplePrimaries()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-routing-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "agents", "talia-rune"));
        Directory.CreateDirectory(Path.Combine(temp, "agents", "arjun-sen"));
        try
        {
            File.WriteAllText(Path.Combine(temp, "team.md"), """
                | Name | Role | Charter | Status |
                |---|---|---|---|
                | Talia Rune | SDK | agents/talia-rune/charter.md | active |
                | Arjun Sen | C# | agents/arjun-sen/charter.md | active |
                """);
            File.WriteAllText(Path.Combine(temp, "agents", "talia-rune", "charter.md"), "charter");
            File.WriteAllText(Path.Combine(temp, "agents", "arjun-sen", "charter.md"), "charter");
            var legacy = new DecomposedTaskGroup(
                "ROUTE-20260728", "Routing", "feature/routing", "Verify routing",
                [new DecomposedSubTask("ROUTE-20260728-001", "Implement", [], "high", "Implement")]);

            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                legacy, temp, out _, requireExplicitRouting: false), Is.True);
            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                legacy, temp, out var missingMode, requireExplicitRouting: true), Is.False);
            Assert.That(missingMode, Does.Contain("agentRoutingMode"));

            var parallel = legacy with {
                Tasks = [legacy.Tasks[0] with {
                    AgentRoutingMode = "assigned",
                    AgentAssignments = [
                        new DecomposedAgentAssignment("talia-rune", "SDK"),
                        new DecomposedAgentAssignment("arjun-sen", "C#")
                    ]
                }]
            };
            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                parallel, temp, out var parallelError, requireExplicitRouting: true), Is.False);
            Assert.That(parallelError, Does.Contain("dependent tasks"));
            Assert.That(parallelError, Does.Contain("read-only assistance"));
            Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
                parallel,
                temp,
                out var offError,
                requireExplicitRouting: false,
                enforceAssignments: false), Is.True, offError);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

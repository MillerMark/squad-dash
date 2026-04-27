using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadScribeWorkspaceRepairServiceTests {
    [Test]
    public void Repair_CreatesMissingScribeSupportArtifacts_AndRepairsSessionLoggingRoute() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(Path.Combine(".squad", "routing.md"), """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | Documentation & institutional memory | Mira Quill | Docs and durable notes |
            | Session logging | Mira Quill | Session summaries |

            ## Rules

            1. Scribe always runs.
            """);
        workspace.CreateFile(Path.Combine(".squad", "templates", "scribe-charter.md"), "# Scribe Template");

        var result = SquadScribeWorkspaceRepairService.Repair(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.Repaired, Is.True);
            Assert.That(result.CreatedScribeCharter, Is.True);
            Assert.That(result.CreatedScribeHistory, Is.True);
            Assert.That(result.CreatedDecisionLog, Is.True);
            Assert.That(result.CreatedDirectories, Is.GreaterThanOrEqualTo(5));
            Assert.That(result.RoutingRepaired, Is.True);
            Assert.That(File.Exists(workspace.GetPath(".squad", "agents", "scribe", "charter.md")), Is.True);
            Assert.That(File.Exists(workspace.GetPath(".squad", "agents", "scribe", "history.md")), Is.True);
            Assert.That(File.Exists(workspace.GetPath(".squad", "decisions.md")), Is.True);
            Assert.That(Directory.Exists(workspace.GetPath(".squad", "log")), Is.True);
            Assert.That(Directory.Exists(workspace.GetPath(".squad", "orchestration-log")), Is.True);
            Assert.That(Directory.Exists(workspace.GetPath(".squad", "decisions", "inbox")), Is.True);
            Assert.That(
                File.ReadAllText(workspace.GetPath(".squad", "routing.md")),
                Does.Contain("| Session logging | Scribe | Automatic — never needs routing |"));
        });
    }

    [Test]
    public void Repair_DoesNotOverwriteExistingScribeFiles_OrAlreadyCorrectRouting() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(Path.Combine(".squad", "routing.md"), """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | Session logging | Scribe | Automatic — never needs routing |
            """);
        workspace.CreateFile(Path.Combine(".squad", "agents", "scribe", "charter.md"), "# Custom Charter");
        workspace.CreateFile(Path.Combine(".squad", "agents", "scribe", "history.md"), "# Custom History");
        workspace.CreateFile(Path.Combine(".squad", "decisions.md"), "# Existing Decisions");
        Directory.CreateDirectory(workspace.GetPath(".squad", "log"));
        Directory.CreateDirectory(workspace.GetPath(".squad", "orchestration-log"));
        Directory.CreateDirectory(workspace.GetPath(".squad", "decisions", "inbox"));

        var result = SquadScribeWorkspaceRepairService.Repair(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.Repaired, Is.False);
            Assert.That(result.CreatedScribeCharter, Is.False);
            Assert.That(result.CreatedScribeHistory, Is.False);
            Assert.That(result.CreatedDecisionLog, Is.False);
            Assert.That(result.CreatedDirectories, Is.EqualTo(0));
            Assert.That(result.RoutingRepaired, Is.False);
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "agents", "scribe", "charter.md")), Is.EqualTo("# Custom Charter"));
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "agents", "scribe", "history.md")), Is.EqualTo("# Custom History"));
        });
    }
}

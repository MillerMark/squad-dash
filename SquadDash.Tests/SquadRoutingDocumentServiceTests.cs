using System.IO;
using System.Text;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadRoutingDocumentServiceTests {
    [Test]
    public void Assess_ReturnsMissing_WhenRoutingFileIsMissing() {
        using var workspace = new TestWorkspace();
        CreateTeam(
            workspace,
            ("orion-vale", "Orion Vale", "Lead Architect", [
                "Own system architecture",
                "Review architectural changes"
            ]));

        var service = new SquadRoutingDocumentService();

        var assessment = service.Assess(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(assessment.Status, Is.EqualTo(SquadRoutingDocumentStatus.Missing));
            Assert.That(assessment.NeedsRepair, Is.True);
            Assert.That(assessment.IssueFingerprint, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Assess_ReturnsUnfilledSeed_WhenRoutingFileContainsTemplatePlaceholders() {
        using var workspace = new TestWorkspace();
        CreateTeam(
            workspace,
            ("lyra-morn", "Lyra Morn", "UI Specialist", [
                "Design interface flows"
            ]));
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | {domain 1} | {Name} | {example tasks} |
            """);

        var service = new SquadRoutingDocumentService();

        var assessment = service.Assess(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(assessment.Status, Is.EqualTo(SquadRoutingDocumentStatus.UnfilledSeed));
            Assert.That(assessment.NeedsRepair, Is.True);
            Assert.That(assessment.IssueFingerprint, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Assess_TreatsLegacyManagedMarkersAsHealthy_WhenRoutingTableIsStillValid() {
        using var workspace = new TestWorkspace();
        CreateTeam(
            workspace,
            ("talia-rune", "Talia Rune", "SDK Specialist", [
                "Own bridge integration work"
            ]));
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            <!-- SquadDash:BEGIN_MANAGED -->
            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | Tooling | Talia Rune | bridge integration |
            <!-- SquadDash:END_MANAGED -->

            ## Custom Notes

            Preserve this note.
            """);

        var service = new SquadRoutingDocumentService();

        var assessment = service.Assess(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(assessment.Status, Is.EqualTo(SquadRoutingDocumentStatus.HealthyCustom));
            Assert.That(assessment.NeedsRepair, Is.False);
            Assert.That(assessment.IssueFingerprint, Is.Null);
        });
    }

    [Test]
    public void Assess_ReturnsHealthyCustom_WhenRoutingTableIsParseable() {
        using var workspace = new TestWorkspace();
        CreateTeam(
            workspace,
            ("mira-quill", "Mira Quill", "Documentation Specialist", [
                "Maintain docs and release notes"
            ]));
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | Documentation | Mira Quill | onboarding docs, release notes |
            """);

        var service = new SquadRoutingDocumentService();

        var assessment = service.Assess(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(assessment.Status, Is.EqualTo(SquadRoutingDocumentStatus.HealthyCustom));
            Assert.That(assessment.NeedsRepair, Is.False);
            Assert.That(assessment.IssueFingerprint, Is.Null);
        });
    }

    [Test]
    public void Assess_DoesNotTreatIssueRoutingPlaceholdersAsTemplatePlaceholders() {
        using var workspace = new TestWorkspace();
        CreateTeam(
            workspace,
            ("orion-vale", "Orion Vale", "Lead Architect", [
                "Own system architecture",
                "Review architectural changes"
            ]));
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            How to decide who handles what.

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | Architecture | Orion Vale | system design, architecture review |

            ## Issue Routing

            | Label | Action | Who |
            |-------|--------|-----|
            | `squad` | Triage: analyze issue, assign `squad:{member}` label | Orion Vale |
            | `squad:{name}` | Pick up issue and complete the work | Named member |
            """);

        var service = new SquadRoutingDocumentService();

        var assessment = service.Assess(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(assessment.Status, Is.EqualTo(SquadRoutingDocumentStatus.HealthyCustom));
            Assert.That(assessment.NeedsRepair, Is.False);
            Assert.That(assessment.IssueFingerprint, Is.Null);
        });
    }

    [Test]
    public void BackupExistingRoutingFile_WritesBackup_WhenRoutingContentExists() {
        using var workspace = new TestWorkspace();
        CreateTeam(
            workspace,
            ("vesper-knox", "Vesper Knox", "Testing Specialist", [
                "Write regression coverage"
            ]));
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | Testing | Vesper Knox | regression coverage |
            """);

        var service = new SquadRoutingDocumentService();

        var backupPath = service.BackupExistingRoutingFile(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(backupPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(backupPath!), Is.True);
            Assert.That(
                File.ReadAllText(backupPath!),
                Does.Contain("| Testing | Vesper Knox | regression coverage |"));
        });
    }

    private static void CreateTeam(
        TestWorkspace workspace,
        params (string Folder, string Name, string Role, string[] Responsibilities)[] members) {
        var builder = new StringBuilder();
        builder.AppendLine("# Squad Team");
        builder.AppendLine();
        builder.AppendLine("## Members");
        builder.AppendLine();
        builder.AppendLine("| Name | Role | Charter | Status |");
        builder.AppendLine("|------|------|---------|--------|");

        foreach (var member in members) {
            builder.AppendLine($"| {member.Name} | {member.Role} | `.squad/agents/{member.Folder}/charter.md` | active |");
            CreateCharter(workspace, member.Folder, member.Name, member.Role, member.Responsibilities);
        }

        workspace.CreateFile(".squad/team.md", builder.ToString());
    }

    private static void CreateCharter(
        TestWorkspace workspace,
        string folder,
        string name,
        string role,
        string[] responsibilities) {
        var builder = new StringBuilder();
        builder.AppendLine($"# {name} — {role}");
        builder.AppendLine();
        builder.AppendLine("## Responsibilities");
        builder.AppendLine();
        foreach (var responsibility in responsibilities)
            builder.AppendLine($"- {responsibility}");

        workspace.CreateFile(Path.Combine(".squad", "agents", folder, "charter.md"), builder.ToString());
    }
}

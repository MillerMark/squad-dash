using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadTeamRootRepairServiceTests {

    // ── Assess ─────────────────────────────────────────────────────────────────

    [Test]
    public void Assess_NoSquadFolder_ReturnsClean() {
        using var ws = new TestWorkspace();
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.ConfigNeedsRepair, Is.False);
            Assert.That(result.HasPollution, Is.False);
        });
    }

    [Test]
    public void Assess_NoConfigJson_ReturnsClean() {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.GetPath(".squad"));
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.ConfigNeedsRepair, Is.False);
            Assert.That(result.HasPollution, Is.False);
        });
    }

    [Test]
    public void Assess_TeamRootIsDot_FlagsConfigNeedsRepair() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": "." }""");
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.ConfigNeedsRepair, Is.True);
    }

    [Test]
    public void Assess_TeamRootIsSquad_ReturnsNoConfigRepairNeeded() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": ".squad" }""");
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.ConfigNeedsRepair, Is.False);
    }

    [Test]
    public void Assess_TeamRootAbsolutePathMatchingWorkspaceRoot_FlagsConfigNeedsRepair() {
        using var ws = new TestWorkspace();
        var absPath = ws.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Use JsonSerializer to properly escape backslashes in Windows paths.
        var jsonPath = System.Text.Json.JsonSerializer.Serialize(absPath);
        var configContent = "{\"version\": 1, \"teamRoot\": " + jsonPath + "}";
        ws.CreateFile(Path.Combine(".squad", "config.json"), configContent);
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.ConfigNeedsRepair, Is.True);
    }

    [Test]
    public void Assess_DuplicateAgentsFolder_ReportedAsPollution() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": "." }""");
        Directory.CreateDirectory(ws.GetPath(".squad", "agents"));
        Directory.CreateDirectory(ws.GetPath("agents"));
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.PollutionItems, Does.Contain("agents/"));
    }

    [Test]
    public void Assess_RootAgentsFolderWithoutSquadCounterpart_NotReportedAsPollution() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": "." }""");
        // No .squad/agents/, so root agents/ is not Squad pollution
        Directory.CreateDirectory(ws.GetPath("agents"));
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.PollutionItems, Does.Not.Contain("agents/"));
    }

    [Test]
    public void Assess_DuplicateCodeHealthMd_ReportedAsPollution() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": "." }""");
        ws.CreateFile(Path.Combine(".squad", "code-health.md"), "# health");
        ws.CreateFile("code-health.md", "# health");
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.PollutionItems, Does.Contain("code-health.md"));
    }

    [Test]
    public void Assess_LoopMdFileAtRoot_ReportedAsPollution() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": "." }""");
        ws.CreateFile("loop-daily.md", "# loop");
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.PollutionItems, Does.Contain("loop-daily.md"));
    }

    [Test]
    public void Assess_MultipleLoopFiles_AllReportedAsPollution() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """{ "version": 1, "teamRoot": "." }""");
        ws.CreateFile("loop-daily.md", "# loop daily");
        ws.CreateFile("loop-weekly.md", "# loop weekly");
        var result = SquadTeamRootRepairService.Assess(ws.RootPath);
        Assert.That(result.PollutionItems, Does.Contain("loop-daily.md"));
        Assert.That(result.PollutionItems, Does.Contain("loop-weekly.md"));
    }

    // ── RepairConfig ───────────────────────────────────────────────────────────

    [Test]
    public void RepairConfig_SetsDotToSquadDotSquad() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """
            {
              "version": 1,
              "teamRoot": "."
            }
            """);
        var fixed_ = SquadTeamRootRepairService.RepairConfig(ws.RootPath);
        Assert.That(fixed_, Is.True);
        var content = File.ReadAllText(ws.GetPath(".squad", "config.json"));
        Assert.That(content, Does.Contain("\"teamRoot\": \".squad\""));
    }

    [Test]
    public void RepairConfig_PreservesOtherConfigFields() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "config.json"), """
            {
              "version": 1,
              "teamRoot": ".",
              "stateBackend": "git",
              "projectKey": "my-project"
            }
            """);
        SquadTeamRootRepairService.RepairConfig(ws.RootPath);
        var content = File.ReadAllText(ws.GetPath(".squad", "config.json"));
        Assert.Multiple(() => {
            Assert.That(content, Does.Contain("\"teamRoot\": \".squad\""));
            Assert.That(content, Does.Contain("\"stateBackend\": \"git\""));
            Assert.That(content, Does.Contain("\"projectKey\": \"my-project\""));
        });
    }

    [Test]
    public void RepairConfig_NoConfigJson_ReturnsFalse() {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.GetPath(".squad"));
        var fixed_ = SquadTeamRootRepairService.RepairConfig(ws.RootPath);
        Assert.That(fixed_, Is.False);
    }

    // ── CleanRootPollution ─────────────────────────────────────────────────────

    [Test]
    public void CleanRootPollution_RemovesAgentsFolderWhenDuplicated() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "agents", "scribe", "charter.md"), "# charter");
        ws.CreateFile(Path.Combine("agents", "scribe", "charter.md"), "# charter");
        var result = SquadTeamRootRepairService.CleanRootPollution(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.CleanedItems, Does.Contain("agents/"));
            Assert.That(Directory.Exists(ws.GetPath("agents")), Is.False);
            Assert.That(Directory.Exists(ws.GetPath(".squad", "agents")), Is.True);
        });
    }

    [Test]
    public void CleanRootPollution_RemovesCodeHealthMdWhenDuplicated() {
        using var ws = new TestWorkspace();
        ws.CreateFile(Path.Combine(".squad", "code-health.md"), "# health");
        ws.CreateFile("code-health.md", "# health");
        var result = SquadTeamRootRepairService.CleanRootPollution(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.CleanedItems, Does.Contain("code-health.md"));
            Assert.That(File.Exists(ws.GetPath("code-health.md")), Is.False);
            Assert.That(File.Exists(ws.GetPath(".squad", "code-health.md")), Is.True);
        });
    }

    [Test]
    public void CleanRootPollution_RemovesLoopFiles() {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.GetPath(".squad"));
        ws.CreateFile("loop-daily.md", "# loop");
        ws.CreateFile("loop-sprint.md", "# loop");
        var result = SquadTeamRootRepairService.CleanRootPollution(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.CleanedItems, Does.Contain("loop-daily.md"));
            Assert.That(result.CleanedItems, Does.Contain("loop-sprint.md"));
            Assert.That(File.Exists(ws.GetPath("loop-daily.md")), Is.False);
            Assert.That(File.Exists(ws.GetPath("loop-sprint.md")), Is.False);
        });
    }

    [Test]
    public void CleanRootPollution_DoesNotRemoveFolderWithoutSquadCounterpart() {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.GetPath(".squad"));
        // Root-level agents/ has no .squad/agents/ counterpart
        ws.CreateFile(Path.Combine("agents", "readme.md"), "# mine");
        var result = SquadTeamRootRepairService.CleanRootPollution(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.CleanedItems, Does.Not.Contain("agents/"));
            Assert.That(Directory.Exists(ws.GetPath("agents")), Is.True);
        });
    }

    [Test]
    public void CleanRootPollution_NothingToClean_ReturnsEmptyResult() {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.GetPath(".squad"));
        var result = SquadTeamRootRepairService.CleanRootPollution(ws.RootPath);
        Assert.Multiple(() => {
            Assert.That(result.AnySuccess, Is.False);
            Assert.That(result.AnyFailure, Is.False);
        });
    }
}

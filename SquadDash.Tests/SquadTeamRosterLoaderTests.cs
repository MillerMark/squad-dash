namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadTeamRosterLoaderTests {
    [Test]
    public void Load_UsesTeamFileNamesAndAppendsUtilityAgentsAtEnd() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Keaton | MVVM Architect | `.squad/agents/mvvm-architect/charter.md` | Active |
            | Verbal | DevOps/CI Engineer | `.squad/agents/devops-ci/charter.md` | Active |
            """);
        workspace.CreateFile(".squad/agents/mvvm-architect/charter.md", """
            # Keaton — MVVM Architect
            """);
        workspace.CreateFile(".squad/agents/devops-ci/charter.md", """
            # Verbal — DevOps/CI Engineer
            """);
        workspace.CreateFile(".squad/agents/devops-ci/history.md", """
            # Verbal History
            """);
        workspace.CreateFile(".squad/agents/ralph/charter.md", """
            # Ralph — Ralph
            """);
        workspace.CreateFile(".squad/agents/scribe/charter.md", """
            # Scribe — Scribe
            """);

        var loader = new SquadTeamRosterLoader();

        var members = loader.Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members.Select(member => member.Name), Is.EqualTo(new[] {
                "Keaton",
                "Verbal",
                "Ralph",
                "Scribe"
            }));
            Assert.That(members[0].Role, Is.EqualTo("MVVM Architect"));
            Assert.That(members[0].Status, Is.EqualTo("Ready"));
            Assert.That(members[0].IsUtilityAgent, Is.False);
            Assert.That(members[1].HistoryPath, Does.EndWith(".squad\\agents\\devops-ci\\history.md"));
            Assert.That(members[2].IsUtilityAgent, Is.True);
            Assert.That(members[3].IsUtilityAgent, Is.True);
            Assert.That(members[1].AccentKey, Is.EqualTo("devops-ci"));
            Assert.That(members[2].AccentKey, Is.EqualTo("ralph"));
            Assert.That(members[3].AccentKey, Is.EqualTo("scribe"));
        });
    }

    [Test]
    public void Load_FallsBackToAgentFoldersWhenTeamFileIsMissing() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/devops-ci/charter.md", """
            # Verbal — DevOps/CI Engineer

            - **Name:** Verbal
            - **Role:** DevOps/CI Engineer
            """);
        workspace.CreateFile(".squad/agents/ralph/charter.md", """
            # Ralph — Ralph
            """);

        var loader = new SquadTeamRosterLoader();

        var members = loader.Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members, Has.Count.EqualTo(2));
            Assert.That(members[0].Name, Is.EqualTo("Verbal"));
            Assert.That(members[0].IsUtilityAgent, Is.False);
            Assert.That(members[1].Name, Is.EqualTo("Ralph"));
            Assert.That(members[1].IsUtilityAgent, Is.True);
            Assert.That(members[0].FolderPath, Does.EndWith(".squad\\agents\\devops-ci"));
        });
    }

    [Test]
    public void Load_PreservesCamelCaseNameForUiHumanizing() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | ReedRichards | Architect | `.squad/agents/reed-richards/charter.md` | Active |
            """);
        workspace.CreateFile(".squad/agents/reed-richards/charter.md", """
            # ReedRichards — Architect
            """);

        var loader = new SquadTeamRosterLoader();

        var members = loader.Load(workspace.RootPath);

        Assert.That(members[0].Name, Is.EqualTo("ReedRichards"));
    }

    [Test]
    public void HasNonUtilityMembers_ReturnsFalse_ForUtilityOnlyRoster() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/ralph/charter.md", "# Ralph");
        workspace.CreateFile(".squad/agents/scribe/charter.md", "# Scribe");

        var loader = new SquadTeamRosterLoader();
        var members = loader.Load(workspace.RootPath);

        Assert.That(SquadTeamRosterLoader.HasNonUtilityMembers(members), Is.False);
    }

    [Test]
    public void GetMissingUtilityAgentNames_ReturnsMissingBuiltIns() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/scribe/charter.md", "# Scribe");

        var missing = SquadTeamRosterLoader.GetMissingUtilityAgentNames(workspace.RootPath);

        Assert.That(missing, Is.EqualTo(new[] { "Ralph" }));
    }

    [Test]
    public void GetMissingUtilityAgentNames_DoesNotReportUtilityListedInTeamFile() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Ralph | Work Monitor | — | Monitor |
            | Scribe | Session Logger | — | Silent |
            """);

        var missing = SquadTeamRosterLoader.GetMissingUtilityAgentNames(workspace.RootPath);

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void Load_TreatsRalphListedInTeamFileAsUtilityAgent() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Ralph | Work Monitor | — | Monitor |
            """);
        workspace.CreateFile(".squad/agents/ralph/charter.md", "# Ralph");

        var loader = new SquadTeamRosterLoader();

        var members = loader.Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members, Has.Count.EqualTo(1));
            Assert.That(members[0].Name, Is.EqualTo("Ralph"));
            Assert.That(members[0].IsUtilityAgent, Is.True);
        });
    }
}

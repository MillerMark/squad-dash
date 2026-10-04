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
    public void Load_UsesAgentFolders_WhenCharterColumnContainsDescriptions() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Orion Vale | Lead / Architect | Service boundaries, API contracts, technical planning | Active |
            | Arjun Sen | Backend Dev | Domain modeling, internal APIs, backend implementation | Active |
            """);
        workspace.CreateFile(".squad/agents/orion-vale/charter.md", "# Orion Vale — Lead / Architect");
        workspace.CreateFile(".squad/agents/arjun-sen/charter.md", "# Arjun Sen — Backend Dev");
        workspace.CreateFile(".squad/casting/registry.json", """
            {
              "agents": {
                "orion-vale": { "persistent_name": "Orion Vale", "status": "active" },
                "arjun-sen": { "persistent_name": "Arjun Sen", "status": "active" }
              }
            }
            """);

        var members = new SquadTeamRosterLoader().Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members.Select(member => member.AccentKey),
                Is.EqualTo(new[] { "orion-vale", "arjun-sen" }));
            Assert.That(members.Select(member => member.FolderPath),
                Has.All.Not.EqualTo(workspace.RootPath));
            Assert.That(members[0].CharterPath, Does.EndWith(".squad\\agents\\orion-vale\\charter.md"));
            Assert.That(members[1].CharterPath, Does.EndWith(".squad\\agents\\arjun-sen\\charter.md"));
        });
    }

    [Test]
    public void Load_DoesNotTreatCharterDescriptionAsWorkspaceRelativePath_WhenAgentFolderIsAbsent() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Alpha One | Architect | Service boundaries and review | Active |
            | Beta Two | QA | Verification and edge cases | Active |
            """);

        var members = new SquadTeamRosterLoader().Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members.Select(member => member.AccentKey),
                Is.EqualTo(new[] { "Alpha One", "Beta Two" }));
            Assert.That(members.Select(member => member.FolderPath), Has.All.Null);
            Assert.That(members.Select(member => member.CharterPath), Has.All.Null);
        });
    }

    [Test]
    public void HasNonUtilityMembers_ReturnsFalse_ForUtilityOnlyRoster() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/ralph/charter.md", "# Ralph");
        workspace.CreateFile(".squad/agents/scribe/charter.md", "# Scribe");
        workspace.CreateFile(".squad/agents/Rai/charter.md", "# Rai");
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", "# Fact Checker");

        var loader = new SquadTeamRosterLoader();
        var members = loader.Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members, Has.Count.EqualTo(4));
            Assert.That(members.All(member => member.IsUtilityAgent), Is.True);
            Assert.That(SquadTeamRosterLoader.HasNonUtilityMembers(members), Is.False);
        });
    }

    [Test]
    public void Load_CanonicalizesLegacyFactCheckerIdentityFromStableUtilityHandle() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", """
            # Fact Checker

            - **Name:** Fact Checker
            - **Role:** Devil's Advocate & Verification Agent
            """);
        workspace.CreateFile("assets/verity-cross.png", string.Empty);

        var member = new SquadTeamRosterLoader().Load(workspace.RootPath).Single();
        var imagePath = AgentImagePathResolver.ResolveBundledPath(
            workspace.GetPath("assets"),
            member.AccentKey,
            member.Name,
            member.Role);

        Assert.Multiple(() => {
            Assert.That(member.Name, Is.EqualTo("Verity Cross"));
            Assert.That(member.Role, Is.EqualTo("Fact Checker, Devil's Advocate & Verification Agent"));
            Assert.That(member.AccentKey, Is.EqualTo("fact-checker"));
            Assert.That(member.IsUtilityAgent, Is.True);
            Assert.That(imagePath, Is.EqualTo(workspace.GetPath("assets", "verity-cross.png")));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Load_CollapsesDuplicateVerifierRows_PreferringStableFolder(bool canonicalFirst) {
        using var workspace = new TestWorkspace();
        const string legacy = "| Fact Checker | Legacy verifier | agents/legacy-verifier/charter.md | Silent |";
        const string canonical = "| Verity Cross | Fact Checker | agents/fact-checker/charter.md | Active |";
        workspace.CreateFile(".squad/team.md", $"""
            ## Members
            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            {(canonicalFirst ? canonical : legacy)}
            | Arjun Sen | Backend Dev | agents/arjun-sen/charter.md | Active |
            {(canonicalFirst ? legacy : canonical)}
            """);
        workspace.CreateFile(".squad/agents/legacy-verifier/charter.md", "# Fact Checker");
        workspace.CreateFile(".squad/agents/legacy-verifier/history.md", "Legacy history");
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", "# Verity Cross");
        workspace.CreateFile(".squad/agents/fact-checker/history.md", "Canonical history");

        var members = new SquadTeamRosterLoader().Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members.Select(member => member.Name), Is.EqualTo(new[] { "Verity Cross", "Arjun Sen" }));
            Assert.That(members[0].Role, Is.EqualTo(FactCheckerInstructions.Role));
            Assert.That(members[0].Status, Is.EqualTo("Ready"));
            Assert.That(members[0].AccentKey, Is.EqualTo("fact-checker"));
            Assert.That(members[0].IsUtilityAgent, Is.True);
            Assert.That(members[0].FolderPath, Is.EqualTo(workspace.GetPath(".squad", "agents", "fact-checker")));
            Assert.That(members[0].CharterPath, Does.EndWith("fact-checker\\charter.md"));
            Assert.That(members[0].HistoryPath, Does.EndWith("fact-checker\\history.md"));
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "agents", "legacy-verifier", "history.md")),
                Is.EqualTo("Legacy history"));
        });
    }

    [TestCase("Fact Checker", "# Verity Cross")]
    [TestCase("Fact Checker", "# Fact Checker")]
    [TestCase("Verity Cross", "# Verity Cross")]
    public void Load_ReconcilesLegacyRowWithAppendedBuiltIn(string name, string canonicalHeading) {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", $"""
            ## Members
            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | {name} | Fact Checker | agents/legacy-verifier/charter.md | Active |
            """);
        workspace.CreateFile(".squad/agents/legacy-verifier/charter.md", $"# {name}");
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", canonicalHeading);
        workspace.CreateFile(".squad/agents/fact-checker/history.md", "Canonical history");

        var member = new SquadTeamRosterLoader().Load(workspace.RootPath).Single();

        Assert.Multiple(() => {
            Assert.That(member.Name, Is.EqualTo(FactCheckerInstructions.DisplayName));
            Assert.That(member.AccentKey, Is.EqualTo(FactCheckerInstructions.Handle));
            Assert.That(member.CharterPath, Does.EndWith("fact-checker\\charter.md"));
            Assert.That(member.HistoryPath, Does.EndWith("fact-checker\\history.md"));
        });
    }

    [TestCase("Fact Checker")]
    [TestCase("Verity Cross")]
    public void Load_CollapsesFolderOnlyVerifierAliases(string legacyName) {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/legacy-verifier/charter.md", $"# {legacyName}");
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", "# Fact Checker");
        workspace.CreateFile(".squad/agents/fact-checker/history.md", "Canonical history");

        var member = new SquadTeamRosterLoader().Load(workspace.RootPath).Single();

        Assert.Multiple(() => {
            Assert.That(member.Name, Is.EqualTo(FactCheckerInstructions.DisplayName));
            Assert.That(member.AccentKey, Is.EqualTo(FactCheckerInstructions.Handle));
            Assert.That(member.IsUtilityAgent, Is.True);
            Assert.That(member.CharterPath, Does.EndWith("fact-checker\\charter.md"));
            Assert.That(member.HistoryPath, Does.EndWith("fact-checker\\history.md"));
        });
    }

    [TestCase("Fact Checker")]
    [TestCase("Verity Cross")]
    [TestCase("  fAcT cHeCkEr  ")]
    public void Load_CollapsesNameOnlyAliasesWithoutCreatingWorkspaceFiles(string name) {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", $"""
            ## Members
            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | {name} | Verifier | - | Active |
            | Verity Cross | Fact Checker | - | Active |
            """);

        var member = new SquadTeamRosterLoader().Load(workspace.RootPath).Single();

        Assert.Multiple(() => {
            Assert.That(member.Name, Is.EqualTo(FactCheckerInstructions.DisplayName));
            Assert.That(member.AccentKey, Is.EqualTo(FactCheckerInstructions.Handle));
            Assert.That(member.IsUtilityAgent, Is.True);
            Assert.That(member.FolderPath, Is.Null);
            Assert.That(member.CharterPath, Is.Null);
            Assert.That(SquadTeamRosterLoader.GetMissingUtilityAgentNames(workspace.RootPath),
                Does.Not.Contain("Fact Checker"));
            Assert.That(Directory.Exists(workspace.GetPath(".squad", "agents")), Is.False);
        });
    }

    [Test]
    public void Load_DoesNotCollapseAnotherAgentWithFactCheckerRole() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/vesper-knox/charter.md", "# Vesper Knox — Fact Checker");
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", "# Verity Cross");

        var members = new SquadTeamRosterLoader().Load(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(members, Has.Count.EqualTo(2));
            Assert.That(members.Single(member => member.Name == "Vesper Knox").IsUtilityAgent, Is.False);
            Assert.That(members.Count(member => member.AccentKey == FactCheckerInstructions.Handle), Is.EqualTo(1));
        });
    }

    [Test]
    public void GetMissingUtilityAgentNames_ReturnsMissingBuiltIns() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/scribe/charter.md", "# Scribe");

        var missing = SquadTeamRosterLoader.GetMissingUtilityAgentNames(workspace.RootPath);

        Assert.That(missing, Is.EqualTo(new[] { "Ralph", "Rai", "Fact Checker", "Rory" }));
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
            | Rai | RAI Reviewer | — | Silent |
            | Fact Checker | Fact Checker | — | Silent |
            | Rory | Planning Architect | — | Ready |
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

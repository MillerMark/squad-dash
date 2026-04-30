using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApplicationSettingsStoreTests {
    [Test]
    public void SaveAgentAccentColor_PersistsPerWorkspaceAndAgent() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var workspaceOne = workspace.GetPath("repo-one");
        var workspaceTwo = workspace.GetPath("repo-two");

        Directory.CreateDirectory(workspaceOne);
        Directory.CreateDirectory(workspaceTwo);

        store.SaveAgentAccentColor(workspaceOne, "Squad", "#ff4472c4");
        store.SaveAgentAccentColor(workspaceOne, "Planner", "#FF7A4EB5");
        store.SaveAgentAccentColor(workspaceTwo, "Squad", "#FF3E7F97");

        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded.AgentAccentColorsByWorkspace[workspaceOne]["Squad"], Is.EqualTo("#FF4472C4"));
            Assert.That(loaded.AgentAccentColorsByWorkspace[workspaceOne]["Planner"], Is.EqualTo("#FF7A4EB5"));
            Assert.That(loaded.AgentAccentColorsByWorkspace[workspaceTwo]["Squad"], Is.EqualTo("#FF3E7F97"));
        });
    }

    [Test]
    public void Normalize_RemovesBlankEntries_AndNormalizesWorkspacePaths() {
        var snapshot = new ApplicationSettingsSnapshot(
            @"D:\Work\Repo\",
            new[] { @"D:\Work\Repo\", " ", @"D:\Work\Repo" },
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase) {
                [@"D:\Work\Repo\"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["Squad"] = " #ff4472c4 ",
                    [" "] = "#FFFFFFFF"
                }
            },
            new Dictionary<string, WorkspaceWindowPlacement>(StringComparer.OrdinalIgnoreCase) {
                [@"D:\Work\Repo\"] = new(10, 20, 1200, 800, true)
            },
            18,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var normalized = snapshot.Normalize();

        Assert.Multiple(() => {
            Assert.That(normalized.LastOpenedFolder, Is.EqualTo(@"D:\Work\Repo"));
            Assert.That(normalized.RecentFolders, Is.EqualTo(new[] { @"D:\Work\Repo" }));
            Assert.That(normalized.AgentAccentColorsByWorkspace[@"D:\Work\Repo"]["Squad"], Is.EqualTo("#FF4472C4"));
            Assert.That(normalized.AgentAccentColorsByWorkspace[@"D:\Work\Repo"].ContainsKey(" "), Is.False);
            Assert.That(normalized.WindowPlacementByWorkspace[@"D:\Work\Repo"].IsMaximized, Is.True);
            Assert.That(normalized.WindowPlacementByWorkspace[@"D:\Work\Repo"].Width, Is.EqualTo(1200));
            Assert.That(normalized.PromptFontSize, Is.EqualTo(18));
        });
    }

    [Test]
    public void SaveWindowPlacement_PersistsPerWorkspace() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveWindowPlacement(repo, new WorkspaceWindowPlacement(100, 200, 1440, 900, true));

        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded.WindowPlacementByWorkspace[repo].Left, Is.EqualTo(100));
            Assert.That(loaded.WindowPlacementByWorkspace[repo].Top, Is.EqualTo(200));
            Assert.That(loaded.WindowPlacementByWorkspace[repo].Width, Is.EqualTo(1440));
            Assert.That(loaded.WindowPlacementByWorkspace[repo].Height, Is.EqualTo(900));
            Assert.That(loaded.WindowPlacementByWorkspace[repo].IsMaximized, Is.True);
        });
    }

    [Test]
    public void SavePromptFontSize_PersistsGlobally() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SavePromptFontSize(19);

        var loaded = store.Load();

        Assert.That(loaded.PromptFontSize, Is.EqualTo(19));
    }

    [Test]
    public void SaveSpeechRegion_SurvivesSubsequentSaves() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveSpeechRegion("eastus");

        // These all used to wipe init-only properties
        store.RememberFolder(repo);
        store.SavePromptFontSize(16);
        store.SaveWindowPlacement(repo, new WorkspaceWindowPlacement(0, 0, 1280, 800, false));

        var loaded = store.Load();
        Assert.That(loaded.SpeechRegion, Is.EqualTo("eastus"));
    }

    [Test]
    public void SaveUserName_SurvivesSubsequentSaves() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveUserName("Alice");
        store.RememberFolder(repo);

        var loaded = store.Load();
        Assert.That(loaded.UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public void SaveIgnoredRoutingIssueFingerprint_PersistsPerWorkspace() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveIgnoredRoutingIssueFingerprint(repo, "ABC123");

        var loaded = store.Load();
        Assert.That(
            loaded.IgnoredRoutingIssueFingerprintsByWorkspace[repo],
            Is.EqualTo("ABC123"));
    }

    [Test]
    public void SaveLoopActive_True_PersistsLoopActiveOnExit() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopActive(true);

        Assert.That(store.Load().LoopActiveOnExit, Is.True);
    }

    [Test]
    public void SaveLoopActive_FalseAfterTrue_ClearsFlag() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopActive(true);
        store.SaveLoopActive(false);

        Assert.That(store.Load().LoopActiveOnExit, Is.False);
    }

    [Test]
    public void SaveLoopActive_True_SurvivesOtherSaves() {
        // Regression guard: unrelated saves must not reset LoopActiveOnExit.
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveLoopActive(true);
        store.RememberFolder(repo);
        store.SavePromptFontSize(16);
        store.SaveWindowPlacement(repo, new WorkspaceWindowPlacement(0, 0, 1280, 800, false));

        Assert.That(store.Load().LoopActiveOnExit, Is.True);
    }
}

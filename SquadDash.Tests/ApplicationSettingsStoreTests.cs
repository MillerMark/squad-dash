using System.IO;
using System.Text.Json.Nodes;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApplicationSettingsStoreTests {
    [Test]
    public void HomeBranch_UnsetAndClearedSettings_DoNotInventMain() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Assert.That(store.Load().GetHomeBranch(repo), Is.Null);
        store.SaveHomeBranch(repo, " master ");
        Assert.That(store.Load().GetHomeBranch(repo), Is.EqualTo("master"));
        store.SaveHomeBranch(repo, " ");
        Assert.Multiple(() => {
            Assert.That(store.Load().GetHomeBranch(repo), Is.Null);
            Assert.That(store.Load().GetHomeBranch(""), Is.Null);
            Assert.That(store.Load().HomeBranchByWorkspace.ContainsKey(repo), Is.False);
        });
    }

    [Test]
    public void IsHomeBranch_UsesResolvedTargetRatherThanMainMasterHeuristic() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var snapshot = store.Load();
        var resolution = HomeBranchResolver.Resolve(null, "master", ["main", "master"]);
        Assert.Multiple(() => {
            Assert.That(snapshot.IsHomeBranch(resolution, "master"), Is.True);
            Assert.That(snapshot.IsHomeBranch(resolution, "main"), Is.False);
            Assert.That(snapshot.IsHomeBranch(resolution, "MASTER"), Is.False);
            Assert.That(snapshot.IsHomeBranch(HomeBranchResolver.Resolve(null, null, []), "main"), Is.False);
        });
    }

    [Test]
    public void RecordRecentBranch_InsertsAndReturnsSnapshot() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(-4));

        var snapshot = store.RecordRecentBranch(repo, " feature/alpha ", now);
        var fromSnapshot = snapshot.GetRecentBranches(repo, now);
        var fromStore = store.GetRecentBranches(repo, now);

        Assert.Multiple(() => {
            Assert.That(fromSnapshot.Count, Is.EqualTo(1));
            Assert.That(fromSnapshot[0].BranchName, Is.EqualTo("feature/alpha"));
            Assert.That(fromSnapshot[0].LastVisitedUtc, Is.EqualTo(now.ToUniversalTime()));
            Assert.That(fromStore, Is.EqualTo(fromSnapshot));
        });
    }

    [Test]
    public void RecordRecentBranch_BlankBranch_Throws() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        Assert.Throws<ArgumentException>(() =>
            store.RecordRecentBranch(repo, "   ", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void RecordRecentBranch_RefreshesTimestampWithoutDuplicatingEntry() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var first = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-4));
        var second = first.AddMinutes(7);

        store.RecordRecentBranch(repo, "feature/refresh", first);
        var snapshot = store.RecordRecentBranch(repo, "feature/refresh", second);
        var recent = snapshot.GetRecentBranches(repo, second);

        Assert.Multiple(() => {
            Assert.That(recent.Count, Is.EqualTo(1));
            Assert.That(recent[0].BranchName, Is.EqualTo("feature/refresh"));
            Assert.That(recent[0].LastVisitedUtc, Is.EqualTo(second.ToUniversalTime()));
        });
    }

    [Test]
    public void GetRecentBranches_ExpiresEntriesStrictlyOlderThanNinetyDays() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        store.RecordRecentBranch(repo, "old-91", now.AddDays(-91));
        store.RecordRecentBranch(repo, "exact-90", now.AddDays(-90));
        store.RecordRecentBranch(repo, "within-89", now.AddDays(-89));

        var branches = store.GetRecentBranches(repo, now);

        Assert.That(branches.Select(branch => branch.BranchName).ToArray(),
            Is.EqualTo(new[] { "within-89", "exact-90" }));
        Assert.That(store.Load().GetRecentBranches(repo, now.AddTicks(1)).Select(branch => branch.BranchName),
            Is.EqualTo(new[] { "within-89" }));
    }

    [Test]
    public void RecordRecentBranch_PrunesExpiredEntriesPersistentlyAcrossWorkspaces() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        var otherRepo = workspace.GetPath("other-repo");
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        store.RecordRecentBranch(otherRepo, "expired", now.AddDays(-91));
        store.RecordRecentBranch(otherRepo, "retained", now.AddDays(-89));
        store.RecordRecentBranch(repo, "current", now);

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.RecentBranchesByWorkspace[otherRepo].Select(entry => entry.BranchName),
                Is.EqualTo(new[] { "retained" }));
            Assert.That(loaded.GetRecentBranches(repo, now).Select(entry => entry.BranchName),
                Is.EqualTo(new[] { "current" }));
            Assert.That(loaded.GetRecentBranches(workspace.GetPath("unknown"), now), Is.Empty);
        });
    }

    [Test]
    public void RecordRecentBranch_IdenticalObservationDoesNotWriteSettings() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        store.RecordRecentBranch(repo, "current", now);
        File.SetLastWriteTimeUtc(settingsPath, now.UtcDateTime);

        store.RecordRecentBranch(repo, "current", now);

        Assert.That(File.GetLastWriteTimeUtc(settingsPath), Is.EqualTo(now.UtcDateTime));
    }

    [Test]
    public void GetRecentBranches_ReturnsNewestFirst() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var start = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        store.RecordRecentBranch(repo, "one", start);
        store.RecordRecentBranch(repo, "three", start.AddMinutes(2));
        store.RecordRecentBranch(repo, "two", start.AddMinutes(1));

        var branches = store.GetRecentBranches(repo, start.AddMinutes(2));
        Assert.That(branches.Select(branch => branch.BranchName).ToArray(),
            Is.EqualTo(new[] { "three", "two", "one" }));
    }

    [Test]
    public void RecordRecentBranch_NormalizesWorkspaceKey() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var withTrailingSeparator = $"{repo}{Path.DirectorySeparatorChar}";
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var snapshot = store.RecordRecentBranch(withTrailingSeparator, "feature/key", now);
        var normalizedWorkspace = WorkspacePaths.NormalizeFolder(repo);

        Assert.Multiple(() => {
            Assert.That(snapshot.RecentBranchesByWorkspace.ContainsKey(normalizedWorkspace), Is.True);
            Assert.That(store.GetRecentBranches(repo, now).Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecordRecentBranch_TreatsBranchNamesAsCaseSensitive() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        store.RecordRecentBranch(repo, "Feature/Main", now);
        store.RecordRecentBranch(repo, "feature/main", now.AddMinutes(1));

        var branches = store.GetRecentBranches(repo, now.AddMinutes(1));
        Assert.That(branches.Select(branch => branch.BranchName).ToArray(),
            Is.EqualTo(new[] { "feature/main", "Feature/Main" }));
    }

    [Test]
    public void RecentBranches_PersistAcrossIndependentStoresAndUnrelatedSaves() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var storeOne = new ApplicationSettingsStore(settingsPath);
        storeOne.RecordRecentBranch(repo, "feature/persist", now);

        var storeTwo = new ApplicationSettingsStore(settingsPath);
        storeTwo.SavePromptFontSize(18);

        var storeThree = new ApplicationSettingsStore(settingsPath);
        var branches = storeThree.GetRecentBranches(repo, now);

        Assert.Multiple(() => {
            Assert.That(branches.Count, Is.EqualTo(1));
            Assert.That(branches[0].BranchName, Is.EqualTo("feature/persist"));
            Assert.That(branches[0].LastVisitedUtc, Is.EqualTo(now));
        });
    }

    [Test]
    public void Load_WhenRecentBranchesPropertyMissingOrNull_UsesEmptyDictionary() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new ApplicationSettingsStore(settingsPath);

        store.RecordRecentBranch(repo, "feature/remove", now);

        var json = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        json.Remove(nameof(ApplicationSettingsSnapshot.RecentBranchesByWorkspace));
        File.WriteAllText(settingsPath, json.ToJsonString(JsonFileStorage.PrettyPrint));

        Assert.That(store.Load().RecentBranchesByWorkspace, Is.Empty);

        json = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        json[nameof(ApplicationSettingsSnapshot.RecentBranchesByWorkspace)] = null;
        File.WriteAllText(settingsPath, json.ToJsonString(JsonFileStorage.PrettyPrint));

        Assert.That(store.Load().RecentBranchesByWorkspace, Is.Empty);
    }

    [Test]
    public void PlanAgentRoutingPolicy_DefaultsToPlanExecutionAndPersistsAllModes() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        Assert.That(store.Load().PlanAgentRoutingPolicy,
            Is.EqualTo(PlanAgentRoutingPolicy.PlanExecutionOnly));

        store.SavePlanAgentRoutingPolicy(PlanAgentRoutingPolicy.Always);
        Assert.That(store.Load().PlanAgentRoutingPolicy, Is.EqualTo(PlanAgentRoutingPolicy.Always));

        store.SavePlanAgentRoutingPolicy("invalid");
        Assert.That(store.Load().PlanAgentRoutingPolicy,
            Is.EqualTo(PlanAgentRoutingPolicy.PlanExecutionOnly));
    }

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
    public void MarkdownEditorZoom_DefaultsToOneForExistingSettings() {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("settings.json");
        var json = System.Text.Json.JsonSerializer.SerializeToNode(new ApplicationSettingsStore(path).Load())!.AsObject();
        json.Remove("MarkdownEditorZoomMultiplier");
        File.WriteAllText(path, json.ToJsonString());
        Assert.That(new ApplicationSettingsStore(path).Load().MarkdownEditorZoomMultiplier, Is.EqualTo(1.0));
    }

    [TestCase(1.75, 1.75)]
    [TestCase(0.1, 0.5)]
    [TestCase(8.0, 3.0)]
    [TestCase(double.NaN, 1.0)]
    [TestCase(double.PositiveInfinity, 1.0)]
    [TestCase(double.NegativeInfinity, 1.0)]
    public void SaveMarkdownEditorZoom_RoundTripsGloballyAndPreservesOtherFonts(double value, double expected) {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("settings.json");
        var store = new ApplicationSettingsStore(path);
        store.SavePromptFontSize(19);
        Assert.That(store.SaveMarkdownEditorZoomMultiplier(value).MarkdownEditorZoomMultiplier, Is.EqualTo(expected));
        var reloaded = new ApplicationSettingsStore(path);
        reloaded.SavePromptFontSize(20);
        var loaded = reloaded.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.MarkdownEditorZoomMultiplier, Is.EqualTo(expected));
            Assert.That(loaded.PromptFontSize, Is.EqualTo(20));
        });
    }

    [TestCase(-2.0, 0.5)]
    [TestCase(5.0, 3.0)]
    public void MarkdownEditorZoom_NormalizesPersistedOutOfRangeValues(double value, double expected) {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("settings.json");
        var snapshot = new ApplicationSettingsStore(path).Load() with { MarkdownEditorZoomMultiplier = value };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(snapshot));
        Assert.That(new ApplicationSettingsStore(path).Load().MarkdownEditorZoomMultiplier, Is.EqualTo(expected));
    }

    [Test]
    public void SaveValidationDetailsFontMultiplier_PersistsGlobally() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveValidationDetailsFontMultiplier(1.75);

        Assert.That(store.Load().ValidationDetailsFontMultiplier, Is.EqualTo(1.75));
    }

    [Test]
    public void SaveValidationDetailsFontMultiplier_RoundTripsAcrossIndependentStoreInstances() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var storeOne = new ApplicationSettingsStore(settingsPath);
        storeOne.SaveValidationDetailsFontMultiplier(2.25);

        var storeTwo = new ApplicationSettingsStore(settingsPath);

        Assert.That(storeTwo.Load().ValidationDetailsFontMultiplier, Is.EqualTo(2.25));
    }

    [Test]
    public void SaveValidationDetailsFontMultiplier_SurvivesUnrelatedSavesAndWorkspaceSwitches() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repoOne = workspace.GetPath("repo-one");
        var repoTwo = workspace.GetPath("repo-two");
        Directory.CreateDirectory(repoOne);
        Directory.CreateDirectory(repoTwo);

        store.SaveValidationDetailsFontMultiplier(1.6);
        store.RememberFolder(repoOne);
        store.SaveWindowPlacement(repoOne, new WorkspaceWindowPlacement(10, 20, 1200, 900, false));
        store.RememberFolder(repoTwo);
        store.SaveAgentAccentColor(repoTwo, "Squad", "#FF4472C4");

        Assert.That(store.Load().ValidationDetailsFontMultiplier, Is.EqualTo(1.6));
    }

    [Test]
    public void SaveValidationDetailsFontMultiplier_NormalizesInvalidAndClampedValues() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        Assert.Multiple(() => {
            Assert.That(store.SaveValidationDetailsFontMultiplier(double.NaN).ValidationDetailsFontMultiplier, Is.EqualTo(1.0));
            Assert.That(store.SaveValidationDetailsFontMultiplier(0).ValidationDetailsFontMultiplier, Is.EqualTo(1.0));
            Assert.That(store.SaveValidationDetailsFontMultiplier(double.NegativeInfinity).ValidationDetailsFontMultiplier, Is.EqualTo(1.0));
            Assert.That(store.SaveValidationDetailsFontMultiplier(0.25).ValidationDetailsFontMultiplier, Is.EqualTo(0.5));
            Assert.That(store.SaveValidationDetailsFontMultiplier(4.2).ValidationDetailsFontMultiplier, Is.EqualTo(3.0));
            Assert.That(store.SaveValidationDetailsFontMultiplier(2.4).ValidationDetailsFontMultiplier, Is.EqualTo(2.4));
        });
    }

    [Test]
    public void NormalizeValidationDetailsFontMultiplier_AppliesExpectedRules() {
        Assert.Multiple(() => {
            Assert.That(ApplicationSettingsSnapshot.NormalizeValidationDetailsFontMultiplier(double.PositiveInfinity), Is.EqualTo(1.0));
            Assert.That(ApplicationSettingsSnapshot.NormalizeValidationDetailsFontMultiplier(-1), Is.EqualTo(1.0));
            Assert.That(ApplicationSettingsSnapshot.NormalizeValidationDetailsFontMultiplier(0.4), Is.EqualTo(0.5));
            Assert.That(ApplicationSettingsSnapshot.NormalizeValidationDetailsFontMultiplier(3.4), Is.EqualTo(3.0));
            Assert.That(ApplicationSettingsSnapshot.NormalizeValidationDetailsFontMultiplier(1.3), Is.EqualTo(1.3));
        });
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

    // NOTE: Tests that call DecryptSettingValue exercise Windows DPAPI, which encrypts data
    // using a key tied to the current Windows user account on the current machine. These tests
    // will fail if run under a different user identity (e.g., a CI service account) because
    // DPAPI-encrypted blobs cannot be decrypted by a different user or on a different machine.
    [Test]
    public void SaveTunnelSettings_NgrokMode_PersistsModeAndToken() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveTunnelSettings("ngrok", "my-auth-token");

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.TunnelMode, Is.EqualTo("ngrok"));
            Assert.That(ApplicationSettingsStore.DecryptSettingValue(loaded.TunnelToken), Is.EqualTo("my-auth-token"));
        });
    }

    [Test]
    public void SaveTunnelSettings_CloudflareMode_Persists() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveTunnelSettings("cloudflare", "cf-token-abc");

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.TunnelMode, Is.EqualTo("cloudflare"));
            Assert.That(ApplicationSettingsStore.DecryptSettingValue(loaded.TunnelToken), Is.EqualTo("cf-token-abc"));
        });
    }

    [Test]
    public void SaveTunnelSettings_NullMode_ClearsTunnelModeAndToken() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveTunnelSettings("ngrok", "token");
        store.SaveTunnelSettings(null, null);

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.TunnelMode, Is.Null);
            Assert.That(loaded.TunnelToken, Is.Null);
        });
    }

    [Test]
    public void SaveTunnelSettings_InvalidMode_IsNormalisedToNull() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveTunnelSettings("localtunnel", "token");

        Assert.That(store.Load().TunnelMode, Is.Null);
    }

    [Test]
    public void SaveTunnelSettings_BlankToken_IsNormalisedToNull() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveTunnelSettings("ngrok", "   ");

        Assert.That(store.Load().TunnelToken, Is.Null);
    }

    [Test]
    public void SaveTunnelSettings_SurvivesSubsequentSaves() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveTunnelSettings("ngrok", "persistme");
        store.RememberFolder(repo);
        store.SaveSpeechRegion("eastus");
        store.SavePromptFontSize(16);

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.TunnelMode, Is.EqualTo("ngrok"));
            Assert.That(ApplicationSettingsStore.DecryptSettingValue(loaded.TunnelToken), Is.EqualTo("persistme"));
        });
    }

    [Test]
    public void Normalize_TunnelMode_NgrokRoundTrips() {
        var snapshot = ApplicationSettingsSnapshot.Empty with {
            TunnelMode = "ngrok",
            TunnelToken = "  tok  "
        };

        var normalized = snapshot.Normalize();

        Assert.Multiple(() => {
            Assert.That(normalized.TunnelMode, Is.EqualTo("ngrok"));
            Assert.That(normalized.TunnelToken, Is.EqualTo("tok"));
        });
    }

    [Test]
    public void Normalize_TunnelMode_UnknownValueBecomesNull() {
        var snapshot = ApplicationSettingsSnapshot.Empty with { TunnelMode = "zerotier" };

        Assert.That(snapshot.Normalize().TunnelMode, Is.Null);
    }

    [Test]
    public void SaveRcPort_PersistsPort() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveRcPort(51234);

        Assert.That(store.Load().RcPersistentPort, Is.EqualTo(51234));
    }

    [Test]
    public void SaveRcPort_OverwritesPreviousPort() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveRcPort(51234);
        store.SaveRcPort(52000);

        Assert.That(store.Load().RcPersistentPort, Is.EqualTo(52000));
    }

    [Test]
    public void SaveLoopMode_SquadCli_PersistsLoopMode() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopMode(LoopMode.SquadCli);

        Assert.That(store.Load().LoopMode, Is.EqualTo(LoopMode.SquadCli));
    }

    [Test]
    public void SaveLoopMode_NativeAgents_PersistsLoopMode() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopMode(LoopMode.SquadCli);
        store.SaveLoopMode(LoopMode.NativeAgents);

        Assert.That(store.Load().LoopMode, Is.EqualTo(LoopMode.NativeAgents));
    }

    [Test]
    public void SaveLoopMode_SurvivesSubsequentSaves() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveLoopMode(LoopMode.SquadCli);
        store.RememberFolder(repo);
        store.SavePromptFontSize(16);

        Assert.That(store.Load().LoopMode, Is.EqualTo(LoopMode.SquadCli));
    }

    [Test]
    public void SaveLoopContinuousContext_False_Persists() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopContinuousContext(false);

        Assert.That(store.Load().LoopContinuousContext, Is.False);
    }

    [Test]
    public void SaveLoopContinuousContext_FalseAfterTrue_Persists() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopContinuousContext(true);
        store.SaveLoopContinuousContext(false);

        Assert.That(store.Load().LoopContinuousContext, Is.False);
    }

    [Test]
    public void SaveLoopContinuousContext_True_SurvivesSubsequentSaves() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveLoopContinuousContext(false);
        store.RememberFolder(repo);
        store.SavePromptFontSize(16);
        store.SaveLoopActive(true);

        Assert.That(store.Load().LoopContinuousContext, Is.False);
    }

    [Test]
    public void SaveLoopMode_AndLoopContinuousContext_PersistIndependently() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveLoopMode(LoopMode.SquadCli);
        store.SaveLoopContinuousContext(false);

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.LoopMode,             Is.EqualTo(LoopMode.SquadCli));
            Assert.That(loaded.LoopContinuousContext, Is.False);
        });
    }

    [Test]
    public void SaveAgentImagePath_Null_RemovesOnlyRequestedAgentMapping() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveAgentImagePath(repo, "arjun-sen", @"C:\images\arjun.png");
        store.SaveAgentImagePath(repo, "lyra-morn", @"C:\images\lyra.png");
        store.SaveAgentImagePath(repo, "arjun-sen", null);

        var images = store.Load().AgentImagePathsByWorkspace[repo];
        Assert.Multiple(() => {
            Assert.That(images.ContainsKey("arjun-sen"), Is.False);
            Assert.That(images["lyra-morn"], Is.EqualTo(@"C:\images\lyra.png"));
        });
    }

    [Test]
    public void LegacyImageMigration_RemovesWorkspaceKeyOnlyWhenNoAgentOwnsIt() {
        var members = new[] {
            new SquadTeamMember("Arjun Sen", "Backend", "Ready", null, null, null, false, "arjun-sen"),
            new SquadTeamMember("Lyra Morn", "Frontend", "Ready", null, null, null, false, "lyra-morn")
        };
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["NewEmptyProject"] = @"C:\images\arjun.png"
        };

        var obsoleteKey = AgentImageCustomizationMigration.FindObsoleteWorkspaceKey(
            @"D:\Drive\Source\NewEmptyProject",
            members,
            images);
        var ownedKey = AgentImageCustomizationMigration.FindObsoleteWorkspaceKey(
            @"D:\Drive\Source\NewEmptyProject",
            members.Append(new SquadTeamMember(
                "NewEmptyProject", "Specialist", "Ready", null, null, null, false, "NewEmptyProject")).ToArray(),
            images);

        Assert.Multiple(() => {
            Assert.That(obsoleteKey, Is.EqualTo("NewEmptyProject"));
            Assert.That(ownedKey, Is.Null);
        });
    }

    [Test]
    public void SaveBridgeDiagnosticsEnabled_PersistsGlobally() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveBridgeDiagnosticsEnabled(true);

        Assert.That(store.Load().BridgeDiagnosticsEnabled, Is.True);
    }

    [Test]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultSnapshot() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.RecentFolders, Is.Empty);
            Assert.That(loaded.ValidationDetailsFontMultiplier, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void Load_WhenFileContainsMalformedJson_LeavesExistingFileUnchanged() {
        using var workspace = new TestWorkspace();
        var settingsDir = workspace.GetPath("settings");
        Directory.CreateDirectory(settingsDir);
        var settingsPath = Path.Combine(settingsDir, "settings.json");
        const string malformed = "not valid json {{ ]]]";
        File.WriteAllText(settingsPath, malformed);
        var store = new ApplicationSettingsStore(settingsPath);

        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.That(File.ReadAllText(settingsPath), Is.EqualTo(malformed));
    }

    [Test]
    public void Save_WritesCurrentSchemaMarkerAndRecoveryCopy() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveSpeechRegion("eastus");

        var main = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        var recoveryPath = $"{settingsPath}.v{ApplicationSettingsSnapshot.CurrentSchemaVersion}.recovery";
        var recovery = JsonNode.Parse(File.ReadAllText(recoveryPath))!.AsObject();

        Assert.Multiple(() => {
            Assert.That(main[nameof(ApplicationSettingsSnapshot.SettingsSchemaVersion)]!.GetValue<int>(),
                Is.EqualTo(ApplicationSettingsSnapshot.CurrentSchemaVersion));
            Assert.That(recovery[nameof(ApplicationSettingsSnapshot.SettingsSchemaVersion)]!.GetValue<int>(),
                Is.EqualTo(ApplicationSettingsSnapshot.CurrentSchemaVersion));
            Assert.That(recovery[nameof(ApplicationSettingsSnapshot.SpeechRegion)]!.GetValue<string>(),
                Is.EqualTo("eastus"));
        });
    }

    [Test]
    public void Save_AfterOlderBuildStripsFields_RestoresCurrentSchemaSettings() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveSpeechRegion("eastus");
        store.SaveWorkspaceTintStop(repo, 4);
        store.SaveDocsPanelState(repo, new WorkspaceDocsPanelState { TasksPanelVisible = true });

        var legacy = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        legacy.Remove(nameof(ApplicationSettingsSnapshot.SettingsSchemaVersion));
        legacy.Remove(nameof(ApplicationSettingsSnapshot.SpeechRegion));
        legacy.Remove(nameof(ApplicationSettingsSnapshot.TintStopByWorkspace));
        legacy.Remove(nameof(ApplicationSettingsSnapshot.DocsPanelStateByWorkspace));
        File.WriteAllText(settingsPath, legacy.ToJsonString(JsonFileStorage.PrettyPrint));

        store.SaveLoopContinuousContext(false);
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded.SpeechRegion, Is.EqualTo("eastus"));
            Assert.That(loaded.TintStopByWorkspace[repo], Is.EqualTo(4));
            Assert.That(loaded.DocsPanelStateByWorkspace[repo].TasksPanelVisible, Is.True);
            Assert.That(loaded.LoopContinuousContext, Is.False);
            Assert.That(loaded.SettingsSchemaVersion,
                Is.EqualTo(ApplicationSettingsSnapshot.CurrentSchemaVersion));
        });
    }

    [Test]
    public void Load_WhenOlderJsonMissingValidationDetailsFontMultiplier_DefaultsToOne() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveValidationDetailsFontMultiplier(2.2);
        var legacy = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        legacy.Remove(nameof(ApplicationSettingsSnapshot.ValidationDetailsFontMultiplier));
        File.WriteAllText(settingsPath, legacy.ToJsonString(JsonFileStorage.PrettyPrint));

        Assert.That(store.Load().ValidationDetailsFontMultiplier, Is.EqualTo(1.0));
    }

    // QueuePaused persistence regression tests
    [Test]
    public void SaveDocsPanelState_QueuePaused_RoundTripsWithWorkspaceFolder() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveDocsPanelState(repo, new WorkspaceDocsPanelState { QueuePaused = true });

        Assert.That(store.GetDocsPanelState(repo).QueuePaused, Is.True);
    }

    [Test]
    public void SaveDocsPanelState_QueuePaused_RoundTripsWithNullWorkspaceFolder() {
        // Regression: SaveDocsPanelState(null) stores under "__default__" key but
        // GetDocsPanelState(null) previously bypassed the dict and returned the legacy
        // fallback struct (no QueuePaused), so the paused state was lost on restart.
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveDocsPanelState(null, new WorkspaceDocsPanelState { QueuePaused = true });

        Assert.That(store.GetDocsPanelState(null).QueuePaused, Is.True);
    }

    [Test]
    public void SaveDocsPanelState_SubsequentSaveWithoutQueuePaused_PreservesValue() {
        // Simulates MainWindow_Closing saving panel layout without QueuePaused (null) —
        // the merge logic should preserve the previously saved QueuePaused=true.
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        store.SaveDocsPanelState(repo, new WorkspaceDocsPanelState { QueuePaused = true });
        // Subsequent shutdown save omits QueuePaused (null = don't overwrite)
        store.SaveDocsPanelState(repo, new WorkspaceDocsPanelState { Open = true });

        Assert.That(store.GetDocsPanelState(repo).QueuePaused, Is.True);
    }

    [Test]
    public void SaveDocsPanelState_SubsequentSaveWithoutQueuePaused_NullWorkspace_PreservesValue() {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));

        store.SaveDocsPanelState(null, new WorkspaceDocsPanelState { QueuePaused = true });
        store.SaveDocsPanelState(null, new WorkspaceDocsPanelState { Open = true });

        Assert.That(store.GetDocsPanelState(null).QueuePaused, Is.True);
    }
}

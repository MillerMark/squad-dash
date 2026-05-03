using System.Diagnostics;
using System.Reflection;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ByokAndDocsPanelTests {

    // ------------------------------------------------------------------
    // Priority 1 — ByokProviderSettings record
    // ------------------------------------------------------------------

    [Test]
    public void ByokProviderSettings_Constructor_SetsAllProperties() {
        var sut = new ByokProviderSettings("https://provider.example.com", "gpt-4", "openai", "sk-secret");

        Assert.Multiple(() => {
            Assert.That(sut.ProviderUrl, Is.EqualTo("https://provider.example.com"));
            Assert.That(sut.Model, Is.EqualTo("gpt-4"));
            Assert.That(sut.ProviderType, Is.EqualTo("openai"));
            Assert.That(sut.ApiKey, Is.EqualTo("sk-secret"));
        });
    }

    [Test]
    public void ByokProviderSettings_RecordEquality_SameValues_AreEqual() {
        var a = new ByokProviderSettings("https://provider.example.com", "gpt-4", "openai", "sk-secret");
        var b = new ByokProviderSettings("https://provider.example.com", "gpt-4", "openai", "sk-secret");

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void ByokProviderSettings_RecordEquality_DifferentUrl_AreNotEqual() {
        var a = new ByokProviderSettings("https://provider-a.example.com", "gpt-4", "openai", "sk-secret");
        var b = new ByokProviderSettings("https://provider-b.example.com", "gpt-4", "openai", "sk-secret");

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void ByokProviderSettings_NullOptionalFields_AreAllowed() {
        var sut = new ByokProviderSettings("https://provider.example.com", null, null, null);

        Assert.Multiple(() => {
            Assert.That(sut.ProviderUrl, Is.EqualTo("https://provider.example.com"));
            Assert.That(sut.Model, Is.Null);
            Assert.That(sut.ProviderType, Is.Null);
            Assert.That(sut.ApiKey, Is.Null);
        });
    }

    // ------------------------------------------------------------------
    // Priority 2 — ApplicationSettingsSnapshot BYOK fields
    // ------------------------------------------------------------------

    [Test]
    public void ApplicationSettingsSnapshot_ByokFields_DefaultToNull_OnNewInstall() {
        var snapshot = ApplicationSettingsSnapshot.Empty;

        Assert.Multiple(() => {
            Assert.That(snapshot.ByokProviderUrl, Is.Null);
            Assert.That(snapshot.ByokModel, Is.Null);
            Assert.That(snapshot.ByokProviderType, Is.Null);
            Assert.That(snapshot.ByokApiKey, Is.Null);
        });
    }

    [Test]
    public void ApplicationSettingsSnapshot_ByokFields_CanBeSetViaWithExpression() {
        var snapshot = ApplicationSettingsSnapshot.Empty with {
            ByokProviderUrl = "https://provider.example.com",
            ByokModel = "gpt-4o",
            ByokProviderType = "openai",
            ByokApiKey = "sk-mykey"
        };

        Assert.Multiple(() => {
            Assert.That(snapshot.ByokProviderUrl, Is.EqualTo("https://provider.example.com"));
            Assert.That(snapshot.ByokModel, Is.EqualTo("gpt-4o"));
            Assert.That(snapshot.ByokProviderType, Is.EqualTo("openai"));
            Assert.That(snapshot.ByokApiKey, Is.EqualTo("sk-mykey"));
        });
    }

    [Test]
    public void SaveByokSettings_PersistsAllFields() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveByokSettings("https://provider.example.com", "gpt-4", "openai", "sk-secret");
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded.ByokProviderUrl, Is.EqualTo("https://provider.example.com"));
            Assert.That(loaded.ByokModel, Is.EqualTo("gpt-4"));
            Assert.That(loaded.ByokProviderType, Is.EqualTo("openai"));
            Assert.That(loaded.ByokApiKey, Is.EqualTo("sk-secret"));
        });
    }

    [Test]
    public void SaveByokSettings_WhitespaceValues_NormalizesToNull() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveByokSettings("   ", "  ", null, "   ");
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded.ByokProviderUrl, Is.Null);
            Assert.That(loaded.ByokModel, Is.Null);
            Assert.That(loaded.ByokApiKey, Is.Null);
        });
    }

    [Test]
    public void SaveByokSettings_InvalidProviderType_NormalizesToNull() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveByokSettings("https://provider.example.com", null, "invalid-provider", null);
        var loaded = store.Load();

        Assert.That(loaded.ByokProviderType, Is.Null);
    }

    [TestCase("openai")]
    [TestCase("azure")]
    [TestCase("anthropic")]
    public void SaveByokSettings_ValidProviderType_Persists(string providerType) {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveByokSettings("https://provider.example.com", null, providerType, null);
        var loaded = store.Load();

        Assert.That(loaded.ByokProviderType, Is.EqualTo(providerType));
    }

    [Test]
    public void SaveByokSettings_TrimsWhitespaceFromValues() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveByokSettings("  https://provider.example.com  ", "  gpt-4  ", "openai", "  sk-key  ");
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded.ByokProviderUrl, Is.EqualTo("https://provider.example.com"));
            Assert.That(loaded.ByokModel, Is.EqualTo("gpt-4"));
            Assert.That(loaded.ByokApiKey, Is.EqualTo("sk-key"));
        });
    }

    // ------------------------------------------------------------------
    // Priority 3 — Docs panel Open state logic
    // The fixed guard in RestoreDocsPanelState is: if (savedState?.Open != true) return;
    // ------------------------------------------------------------------

    [Test]
    public void DocsPanelOpenGuard_WhenSavedStateIsNull_EvaluatesToEarlyReturn() {
        WorkspaceDocsPanelState? savedState = null;

        // null?.Open == null, and null != true → early return (don't restore)
        Assert.That(savedState?.Open != true, Is.True,
            "null savedState should trigger early return (panel stays hidden)");
    }

    [Test]
    public void DocsPanelOpenGuard_WhenOpenIsNull_EvaluatesToEarlyReturn() {
        var savedState = new WorkspaceDocsPanelState { Open = null };

        // null != true → early return (default new-install behaviour: panel stays hidden)
        Assert.That(savedState.Open != true, Is.True,
            "null Open should trigger early return (panel stays hidden)");
    }

    [Test]
    public void DocsPanelOpenGuard_WhenOpenIsFalse_EvaluatesToEarlyReturn() {
        var savedState = new WorkspaceDocsPanelState { Open = false };

        // false != true → early return (user explicitly closed it: keep closed)
        Assert.That(savedState.Open != true, Is.True,
            "false Open should trigger early return (panel stays hidden)");
    }

    [Test]
    public void DocsPanelOpenGuard_WhenOpenIsTrue_DoesNotEarlyReturn() {
        var savedState = new WorkspaceDocsPanelState { Open = true };

        // true != true → false → continue (restore the open panel)
        Assert.That(savedState.Open != true, Is.False,
            "true Open should NOT trigger early return (panel should be restored)");
    }

    // ------------------------------------------------------------------
    // Priority 4 — SquadSdkProcess BYOK env var injection
    // BuildDefaultStartInfo is private; accessed via reflection.
    // ------------------------------------------------------------------

    [Test]
    public void BuildDefaultStartInfo_ByokFullyConfigured_InjectsAllEnvVars() {
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ByokProviderSettings = new ByokProviderSettings(
            "https://provider.example.com", "gpt-4", "openai", "sk-mykey");

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.Multiple(() => {
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"],
                Is.EqualTo("https://provider.example.com"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_MODEL_ID"], Is.EqualTo("gpt-4"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_TYPE"], Is.EqualTo("openai"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"], Is.EqualTo("sk-mykey"));
        });
    }

    [Test]
    public void BuildDefaultStartInfo_ByokNotConfigured_DoesNotAddCopilotBaseUrlVar() {
        // ProcessStartInfo.EnvironmentVariables inherits machine env vars, so we only
        // assert that COPILOT_PROVIDER_BASE_URL — the key BYOK always injects first —
        // is absent. That key is not a standard machine variable and proves BYOK did not run.
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ByokProviderSettings = null;

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_BASE_URL"), Is.False,
            "BYOK env vars must not be injected when ByokProviderSettings is null");
    }

    [Test]
    public void BuildDefaultStartInfo_ByokUrlOnly_InjectsOnlyBaseUrlVar() {
        // Compute a baseline to identify which env vars already come from the machine
        // environment, so the assertion is robust regardless of the test host's env.
        var baselineVars = GetBaselineEnvVarKeys();

        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ByokProviderSettings = new ByokProviderSettings(
            "https://provider.example.com", null, null, null);

        var psi = InvokeBuildDefaultStartInfo(sut);
        var added = GetKeysAddedBeyondBaseline(psi, baselineVars);

        Assert.Multiple(() => {
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"],
                Is.EqualTo("https://provider.example.com"));
            // Only COPILOT_PROVIDER_BASE_URL should have been added — Model/Type/ApiKey are null
            Assert.That(added, Does.Contain("COPILOT_PROVIDER_BASE_URL"));
            Assert.That(added, Does.Not.Contain("COPILOT_PROVIDER_MODEL_ID"));
            Assert.That(added, Does.Not.Contain("COPILOT_PROVIDER_TYPE"));
            Assert.That(added, Does.Not.Contain("COPILOT_PROVIDER_API_KEY"));
        });
    }

    [Test]
    public void BuildDefaultStartInfo_ByokEmptyProviderUrl_DoesNotInjectAnyEnvVars() {
        // ProviderUrl is required to have a non-empty value; empty string should not trigger BYOK
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ByokProviderSettings = new ByokProviderSettings("", "gpt-4", "openai", "sk-key");

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_BASE_URL"), Is.False,
            "Empty ProviderUrl should not activate BYOK");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static ProcessStartInfo InvokeBuildDefaultStartInfo(SquadSdkProcess process) {
        var method = typeof(SquadSdkProcess).GetMethod(
            "BuildDefaultStartInfo",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(method, Is.Not.Null, "BuildDefaultStartInfo method must exist");
        return (ProcessStartInfo)method!.Invoke(process, null)!;
    }

    /// <summary>
    /// Returns the set of env var keys that ProcessStartInfo inherits from the machine
    /// environment (i.e. without any BYOK configuration applied).
    /// </summary>
    private static HashSet<string> GetBaselineEnvVarKeys() {
        var baseSut = new SquadSdkProcess(new FakeWorkspacePaths());
        var basePsi = InvokeBuildDefaultStartInfo(baseSut);
        return new HashSet<string>(
            basePsi.EnvironmentVariables.Keys.Cast<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns env var keys present in <paramref name="psi"/> that were NOT in
    /// <paramref name="baselineKeys"/> — i.e. keys injected by BYOK code.
    /// </summary>
    private static IReadOnlyList<string> GetKeysAddedBeyondBaseline(
        ProcessStartInfo psi,
        HashSet<string> baselineKeys) {
        return psi.EnvironmentVariables.Keys
            .Cast<string>()
            .Where(k => !baselineKeys.Contains(k))
            .ToList();
    }

    private sealed class FakeWorkspacePaths : IWorkspacePaths {
        public string ApplicationRoot => @"C:\fake\app";
        public string SquadSdkDirectory => @"C:\fake\app\Squad.SDK";
        public string RunRootDirectory => @"C:\fake\app\Run";
        public string AgentImageAssetsDirectory => @"C:\fake\app\Assets\Agents";
        public string RoleIconAssetsDirectory => @"C:\fake\app\Assets\Roles";
        public string ScreenshotsDirectory => @"C:\fake\app\docs\screenshots";
    }
}

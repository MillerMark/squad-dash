using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadSdkProcessProfileTests {

    // ------------------------------------------------------------------
    // PopulateProfileEnvironment — custom provider (non-empty ProviderUrl)
    // ------------------------------------------------------------------

    [Test]
    public void PopulateProfileEnvironment_CustomProfile_SetsAllEnvVars() {
        var psi = new ProcessStartInfo();
        var profile = new ModelProfile(
            Id: "custom-1",
            Alias: "Custom",
            ProviderType: "openai",
            ProviderUrl: "https://provider.example.com/v1",
            Model: "gpt-4",
            ApiKey: "sk-mykey",
            OfflineMode: true);

        SquadSdkProcess.PopulateProfileEnvironment(psi, profile);

        Assert.Multiple(() => {
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"],
                Is.EqualTo("https://provider.example.com/v1"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_MODEL_ID"], Is.EqualTo("gpt-4"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_TYPE"], Is.EqualTo("openai"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"], Is.EqualTo("sk-mykey"));
            Assert.That(psi.EnvironmentVariables["COPILOT_OFFLINE"], Is.EqualTo("true"));
        });
    }

    [Test]
    public void PopulateProfileEnvironment_CustomProfile_NullOptionalFields_SetsOnlyBaseUrl() {
        var psi = new ProcessStartInfo();
        var profile = new ModelProfile(
            Id: "minimal",
            Alias: "Minimal",
            ProviderType: "",
            ProviderUrl: "https://provider.example.com",
            Model: null,
            ApiKey: null);

        SquadSdkProcess.PopulateProfileEnvironment(psi, profile);

        Assert.Multiple(() => {
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"],
                Is.EqualTo("https://provider.example.com"));
            Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_MODEL_ID"), Is.False);
            Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_TYPE"), Is.False);
            Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_API_KEY"), Is.False);
            Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_OFFLINE"), Is.False);
        });
    }

    [Test]
    public void PopulateProfileEnvironment_OfflineModeFalse_DoesNotSetCopilotOffline() {
        var psi = new ProcessStartInfo();
        var profile = new ModelProfile(
            Id: "no-offline",
            Alias: "No Offline",
            ProviderType: "openai",
            ProviderUrl: "https://provider.example.com",
            Model: "gpt-4",
            ApiKey: null,
            OfflineMode: false);

        SquadSdkProcess.PopulateProfileEnvironment(psi, profile);

        Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_OFFLINE"), Is.False);
    }

    [Test]
    public void PopulateProfileEnvironment_LocalOllamaProfile_SetsWireApiToCompletions() {
        var psi = new ProcessStartInfo();
        var profile = new ModelProfile(
            Id: "ollama-local",
            Alias: "Ollama Local",
            ProviderType: "openai",
            ProviderUrl: "http://127.0.0.1:11434/v1",
            Model: "qwen3:latest",
            ApiKey: null,
            OfflineMode: true);

        SquadSdkProcess.PopulateProfileEnvironment(psi, profile);

        Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_WIRE_API"], Is.EqualTo("completions"));
    }

    [Test]
    public void PopulateProfileEnvironment_RemoteOpenAiProfile_DoesNotSetWireApi() {
        var psi = new ProcessStartInfo();
        var profile = new ModelProfile(
            Id: "remote-oai",
            Alias: "Remote OpenAI",
            ProviderType: "openai",
            ProviderUrl: "https://api.openai.example.com/v1",
            Model: "gpt-4",
            ApiKey: "sk-key");

        SquadSdkProcess.PopulateProfileEnvironment(psi, profile);

        Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_WIRE_API"), Is.False);
    }

    // ------------------------------------------------------------------
    // BuildDefaultStartInfo integration — copilot-type profile (no ProviderUrl)
    // ------------------------------------------------------------------

    [Test]
    public void BuildDefaultStartInfo_CopilotProfile_DoesNotInjectProviderEnvVars() {
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ActiveProfile = new ModelProfile(
            Id: "copilot-default",
            Alias: "Copilot",
            ProviderType: "copilot",
            ProviderUrl: null,
            Model: "claude-sonnet-4",
            ApiKey: null,
            IsDefault: true);

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_BASE_URL"), Is.False,
            "Copilot-type profile (null ProviderUrl) should not set COPILOT_PROVIDER_BASE_URL");
    }

    [Test]
    public void BuildDefaultStartInfo_CopilotProfileEmptyUrl_DoesNotInjectProviderEnvVars() {
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ActiveProfile = new ModelProfile(
            Id: "copilot-default",
            Alias: "Copilot",
            ProviderType: "copilot",
            ProviderUrl: "",
            Model: "claude-sonnet-4",
            ApiKey: null,
            IsDefault: true);

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.That(psi.EnvironmentVariables.ContainsKey("COPILOT_PROVIDER_BASE_URL"), Is.False,
            "Copilot-type profile (empty ProviderUrl) should not set COPILOT_PROVIDER_BASE_URL");
    }

    [Test]
    public void BuildDefaultStartInfo_CustomProfile_InjectsAllProviderEnvVars() {
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ActiveProfile = new ModelProfile(
            Id: "custom-1",
            Alias: "Custom Provider",
            ProviderType: "openai",
            ProviderUrl: "https://my-provider.example.com/v1",
            Model: "gpt-4o",
            ApiKey: "sk-test-key",
            OfflineMode: true);

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.Multiple(() => {
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"],
                Is.EqualTo("https://my-provider.example.com/v1"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_MODEL_ID"], Is.EqualTo("gpt-4o"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_TYPE"], Is.EqualTo("openai"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"], Is.EqualTo("sk-test-key"));
            Assert.That(psi.EnvironmentVariables["COPILOT_OFFLINE"], Is.EqualTo("true"));
        });
    }

    [Test]
    public void BuildDefaultStartInfo_ActiveProfileTakesPrecedenceOverByokSettings() {
        var sut = new SquadSdkProcess(new FakeWorkspacePaths());
        sut.ByokProviderSettings = new ByokProviderSettings(
            "https://old-provider.example.com", "old-model", "openai", "old-key");
        sut.ActiveProfile = new ModelProfile(
            Id: "new-profile",
            Alias: "New Profile",
            ProviderType: "openai",
            ProviderUrl: "https://new-provider.example.com/v1",
            Model: "new-model",
            ApiKey: "new-key");

        var psi = InvokeBuildDefaultStartInfo(sut);

        Assert.Multiple(() => {
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"],
                Is.EqualTo("https://new-provider.example.com/v1"),
                "ActiveProfile should take precedence over ByokProviderSettings");
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_MODEL_ID"], Is.EqualTo("new-model"));
            Assert.That(psi.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"], Is.EqualTo("new-key"));
        });
    }

    // ------------------------------------------------------------------
    // ResolveProfileWireApi
    // ------------------------------------------------------------------

    [Test]
    public void ResolveProfileWireApi_LocalOllamaOpenAi_ReturnsCompletions() {
        var profile = new ModelProfile("id", "Alias", "openai", "http://127.0.0.1:11434/v1", "model", null);
        Assert.That(SquadSdkProcess.ResolveProfileWireApi(profile), Is.EqualTo("completions"));
    }

    [Test]
    public void ResolveProfileWireApi_RemoteOpenAi_ReturnsNull() {
        var profile = new ModelProfile("id", "Alias", "openai", "https://api.openai.example.com/v1", "model", null);
        Assert.That(SquadSdkProcess.ResolveProfileWireApi(profile), Is.Null);
    }

    [Test]
    public void ResolveProfileWireApi_NonOpenAi_ReturnsNull() {
        var profile = new ModelProfile("id", "Alias", "anthropic", "http://127.0.0.1:11434/v1", "model", null);
        Assert.That(SquadSdkProcess.ResolveProfileWireApi(profile), Is.Null);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static ProcessStartInfo InvokeBuildDefaultStartInfo(SquadSdkProcess process) {
        var method = typeof(SquadSdkProcess).GetMethod(
            "BuildDefaultStartInfo",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(method, Is.Not.Null, "BuildDefaultStartInfo method must exist");
        try {
            return (ProcessStartInfo)method!.Invoke(process, null)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private sealed class FakeWorkspacePaths : IWorkspacePaths {
        public string ApplicationRoot => @"C:\fake\app";
        public string SquadSdkDirectory => Path.Combine(ApplicationRoot, "Squad.SDK");
        public string RunRootDirectory => Path.Combine(ApplicationRoot, "Run");
        public string AgentImageAssetsDirectory => Path.Combine(ApplicationRoot, "Assets", "Agents");
        public string RoleIconAssetsDirectory => Path.Combine(ApplicationRoot, "Assets", "Roles");
        public string ScreenshotsDirectory => Path.Combine(ApplicationRoot, "docs", "screenshots");
    }
}

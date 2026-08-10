using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ModelProfileStoreTests {

    [Test]
    public void LegacyMigration_CopilotProvider_ProducesDefaultCopilotProfile() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        // Save settings with Copilot provider and a specific model — no ModelProfiles yet
        store.SaveModelSettings(ModelProvider.GitHubCopilot, "gpt-4o");
        var loaded = store.Load();
        Assert.That(loaded.ModelProfiles, Is.Null);

        var profileStore = new ModelProfileStore(store);
        var profiles = profileStore.GetProfiles();

        Assert.Multiple(() => {
            Assert.That(profiles, Has.Count.EqualTo(1));
            var profile = profiles[0];
            Assert.That(profile.Id, Is.EqualTo("default"));
            Assert.That(profile.Alias, Is.EqualTo("Default"));
            Assert.That(profile.ProviderType, Is.EqualTo("copilot"));
            Assert.That(profile.ProviderUrl, Is.Null);
            Assert.That(profile.Model, Is.EqualTo("gpt-4o"));
            Assert.That(profile.ApiKey, Is.Null);
            Assert.That(profile.OfflineMode, Is.False);
            Assert.That(profile.IsDefault, Is.True);
        });

        // Verify profiles were persisted so next load doesn't re-migrate
        var reloaded = store.Load();
        Assert.That(reloaded.ModelProfiles, Is.Not.Null);
        Assert.That(reloaded.ModelProfiles!, Has.Count.EqualTo(1));
    }

    [Test]
    public void LegacyMigration_CustomByokProvider_ProducesMatchingProfile() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveModelSettings(ModelProvider.Custom, "auto");
        store.SaveByokSettings(
            providerUrl: "http://localhost:11434/v1",
            model: "qwen3-coder:30b",
            providerType: "openai",
            apiKey: "sk-test-key-123",
            offlineMode: true);

        var profileStore = new ModelProfileStore(store);
        var profiles = profileStore.GetProfiles();

        Assert.Multiple(() => {
            Assert.That(profiles, Has.Count.EqualTo(1));
            var profile = profiles[0];
            Assert.That(profile.Id, Is.EqualTo("default"));
            Assert.That(profile.ProviderType, Is.EqualTo("openai"));
            Assert.That(profile.ProviderUrl, Is.EqualTo("http://localhost:11434/v1"));
            Assert.That(profile.Model, Is.EqualTo("qwen3-coder:30b"));
            Assert.That(profile.ApiKey, Is.Not.Null.And.Not.Empty);
            Assert.That(profile.OfflineMode, Is.True);
            Assert.That(profile.IsDefault, Is.True);
        });
    }

    [Test]
    public void LegacyMigration_DefaultCopilotModel_UsedWhenNoneSet() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        // Fresh store — no model settings saved at all
        var profileStore = new ModelProfileStore(store);
        var profiles = profileStore.GetProfiles();

        Assert.Multiple(() => {
            Assert.That(profiles, Has.Count.EqualTo(1));
            Assert.That(profiles[0].ProviderType, Is.EqualTo("copilot"));
            Assert.That(profiles[0].Model, Is.EqualTo(ApplicationSettingsSnapshot.DefaultCopilotModel));
            Assert.That(profiles[0].IsDefault, Is.True);
        });
    }

    [Test]
    public void GetDefaultProfile_ReturnsProfileMarkedAsDefault() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        var profiles = new List<ModelProfile> {
            new("profile-a", "Profile A", "openai", "http://example.com", "gpt-4", null),
            new("profile-b", "Profile B", "copilot", null, "auto", null, IsDefault: true),
        };
        store.SaveModelProfiles(profiles);

        var profileStore = new ModelProfileStore(store);
        var defaultProfile = profileStore.GetDefaultProfile();

        Assert.That(defaultProfile, Is.Not.Null);
        Assert.That(defaultProfile!.Id, Is.EqualTo("profile-b"));
        Assert.That(defaultProfile.IsDefault, Is.True);
    }

    [Test]
    public void CategoryAssignments_RoundTrip() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        var assignments = new Dictionary<string, string> {
            [ModelProfileCategory.Coordinator] = "profile-a",
            [ModelProfileCategory.Scribe] = "profile-b",
        };
        profileStore.SaveCategoryAssignments(assignments);

        var loaded = profileStore.GetCategoryAssignments();
        Assert.Multiple(() => {
            Assert.That(loaded[ModelProfileCategory.Coordinator], Is.EqualTo("profile-a"));
            Assert.That(loaded[ModelProfileCategory.Scribe], Is.EqualTo("profile-b"));
        });
    }

    [Test]
    public void LegacyMigration_AnthropicProvider_ProducesAnthropicProfile() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        store.SaveModelSettings(ModelProvider.Custom, "auto");
        store.SaveByokSettings(
            providerUrl: "https://api.anthropic.com/v1",
            model: "claude-sonnet-4-20250514",
            providerType: "anthropic",
            apiKey: "sk-ant-test",
            offlineMode: false);

        var profileStore = new ModelProfileStore(store);
        var profiles = profileStore.GetProfiles();

        Assert.Multiple(() => {
            Assert.That(profiles, Has.Count.EqualTo(1));
            Assert.That(profiles[0].ProviderType, Is.EqualTo("anthropic"));
            Assert.That(profiles[0].Model, Is.EqualTo("claude-sonnet-4-20250514"));
            Assert.That(profiles[0].OfflineMode, Is.False);
        });
    }

    [Test]
    public void ExistingProfiles_NotOverwrittenByMigration() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);

        // Save explicit profiles
        var profiles = new List<ModelProfile> {
            new("custom-1", "My Custom", "openai", "http://localhost:1234", "llama3", null, IsDefault: true),
        };
        store.SaveModelProfiles(profiles);

        // Also save some legacy BYOK settings (these should be ignored)
        store.SaveByokSettings("http://other:5678", "other-model", "azure", "key", true);

        var profileStore = new ModelProfileStore(store);
        var loaded = profileStore.GetProfiles();

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].Id, Is.EqualTo("custom-1"));
        Assert.That(loaded[0].ProviderUrl, Is.EqualTo("http://localhost:1234"));
    }
}

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
            Assert.That(profile.Alias, Is.EqualTo("GitHub Copilot"));
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
        
        // Verify default category assignments were created
        var assignments = profileStore.GetCategoryAssignments();
        Assert.That(assignments, Is.Not.Null);
        Assert.That(assignments.Count, Is.GreaterThan(0));
        Assert.That(assignments[ModelProfileCategory.Coordinator], Is.EqualTo("default"), "Should use migrated profile ID");
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
            Assert.That(profile.Alias, Is.EqualTo("Custom Provider"));
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
            Assert.That(profiles[0].Alias, Is.EqualTo("GitHub Copilot"));
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
    public void GetProfiles_FullResponsesEndpoint_MigratesBaseUrlAndWireApi() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        store.SaveModelProfiles([
            new ModelProfile(
                "profile-a",
                "Profile A",
                "openai",
                "https://resource.services.ai.azure.com/openai/v1/responses",
                "gpt-5.4-mini",
                null,
                IsDefault: true)
        ]);

        var profile = new ModelProfileStore(store).GetProfiles().Single();

        Assert.Multiple(() => {
            Assert.That(
                profile.ProviderUrl,
                Is.EqualTo("https://resource.services.ai.azure.com/openai/v1"));
            Assert.That(profile.WireApi, Is.EqualTo("responses"));
            var persisted = store.Load().ModelProfiles!.Single();
            Assert.That(persisted.ProviderUrl, Is.EqualTo(profile.ProviderUrl));
            Assert.That(persisted.WireApi, Is.EqualTo("responses"));
        });
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
    public void AgentOverrides_RoundTripAndClear() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        profileStore.SaveAgentOverride("agent-alpha", "profile-a");

        var loaded = profileStore.GetAgentOverrides();
        Assert.That(loaded["agent-alpha"], Is.EqualTo("profile-a"));

        profileStore.ClearAgentOverride("agent-alpha");
        var cleared = profileStore.GetAgentOverrides();
        Assert.That(cleared.ContainsKey("agent-alpha"), Is.False);
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

    // ── Regression tests for user-reported bugs ────────────────────────────

    [Test]
    public void RenameProfile_PersistsCorrectly() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        // Create initial profile
        var initialProfiles = new List<ModelProfile> {
            new("user-profile-1", "Original Name", "openai", "http://localhost:1234", "gpt-4", "sk-key-123", IsDefault: true),
        };
        profileStore.SaveProfiles(initialProfiles);

        // Rename the profile (changing Alias but keeping Id)
        var renamedProfiles = new List<ModelProfile> {
            new("user-profile-1", "Renamed Profile", "openai", "http://localhost:1234", "gpt-4", "sk-key-123", IsDefault: true),
        };
        profileStore.SaveProfiles(renamedProfiles);

        // Verify renamed profile persists correctly
        var loaded = profileStore.GetProfiles();
        Assert.Multiple(() => {
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].Id, Is.EqualTo("user-profile-1"));
            Assert.That(loaded[0].Alias, Is.EqualTo("Renamed Profile"), "Profile alias should persist after rename");
            Assert.That(loaded[0].ProviderUrl, Is.EqualTo("http://localhost:1234"), "Other settings should remain unchanged");
            Assert.That(loaded[0].ApiKey, Is.EqualTo("sk-key-123"), "API key should remain unchanged");
        });
    }

    [Test]
    public void CustomProvider_DescriptionDoesNotRegressToCopilotText() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        // Create custom provider profile
        var customProfiles = new List<ModelProfile> {
            new("custom-ollama", "My Ollama", "openai", "http://localhost:11434/v1", "llama3", null, IsDefault: true),
        };
        profileStore.SaveProfiles(customProfiles);

        // Save and reload multiple times (simulating multiple sessions)
        for (int i = 0; i < 3; i++) {
            var reloaded = profileStore.GetProfiles();
            profileStore.SaveProfiles(reloaded);
        }

        // Verify custom provider info does not regress to "copilot" or GitHub Copilot defaults
        var final = profileStore.GetProfiles();
        Assert.Multiple(() => {
            Assert.That(final, Has.Count.EqualTo(1));
            Assert.That(final[0].ProviderType, Is.EqualTo("openai"), "Provider type should not regress to copilot");
            Assert.That(final[0].ProviderUrl, Is.EqualTo("http://localhost:11434/v1"), "Provider URL should persist");
            Assert.That(final[0].Alias, Is.EqualTo("My Ollama"), "Profile alias should not change to GitHub Copilot text");
        });
    }

    [Test]
    public void DefaultCategoryAssignments_LoadedOnInitialOpen() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        // Save some profiles (no category assignments yet)
        var profiles = new List<ModelProfile> {
            new("prof-a", "Profile A", "openai", null, "gpt-4", null, IsDefault: true),
            new("prof-b", "Profile B", "anthropic", null, "claude-4", null),
        };
        profileStore.SaveProfiles(profiles);

        // On first load of category assignments, they should be auto-initialized with default profile ID
        var initialAssignments = profileStore.GetCategoryAssignments();
        Assert.That(initialAssignments, Is.Not.Null, "Category assignments should not be null");
        Assert.That(initialAssignments.Count, Is.GreaterThan(0), "Category assignments should be initialized");
        Assert.That(initialAssignments[ModelProfileCategory.Coordinator], Is.EqualTo("prof-a"), "Should use actual default profile ID");
        Assert.That(initialAssignments[ModelProfileCategory.Scribe], Is.EqualTo("prof-a"), "All categories should default to default profile");

        // Verify assignments were persisted
        var persisted = store.Load().CategoryAssignments;
        Assert.That(persisted, Is.Not.Null);
        Assert.That(persisted!.Count, Is.GreaterThan(0));

        // Save some custom assignments
        var customAssignments = new Dictionary<string, string> {
            [ModelProfileCategory.Coordinator] = "prof-a",
            [ModelProfileCategory.Scribe] = "prof-b",
        };
        profileStore.SaveCategoryAssignments(customAssignments);

        // Reload and verify persistence
        var reloaded = profileStore.GetCategoryAssignments();
        Assert.Multiple(() => {
            Assert.That(reloaded, Has.Count.EqualTo(2));
            Assert.That(reloaded[ModelProfileCategory.Coordinator], Is.EqualTo("prof-a"));
            Assert.That(reloaded[ModelProfileCategory.Scribe], Is.EqualTo("prof-b"));
        });
    }

    [Test]
    public void SwitchSelectedProfile_DoesNotWipeCustomProviderSettings() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        // Create two profiles: one custom provider, one copilot
        var profiles = new List<ModelProfile> {
            new("custom-local", "Local LLM", "openai", "http://localhost:11434/v1", "qwen3:30b", "sk-test-123", OfflineMode: true, IsDefault: true),
            new("copilot-main", "GitHub Copilot", "copilot", null, "gpt-4o", null, IsDefault: false),
        };
        profileStore.SaveProfiles(profiles);

        // Simulate switching default: mark copilot as default
        var switchedProfiles = new List<ModelProfile> {
            new("custom-local", "Local LLM", "openai", "http://localhost:11434/v1", "qwen3:30b", "sk-test-123", OfflineMode: true, IsDefault: false),
            new("copilot-main", "GitHub Copilot", "copilot", null, "gpt-4o", null, IsDefault: true),
        };
        profileStore.SaveProfiles(switchedProfiles);

        // Reload and verify the custom profile's settings are preserved
        var reloaded = profileStore.GetProfiles();
        var customProfile = reloaded.First(p => p.Id == "custom-local");

        Assert.Multiple(() => {
            Assert.That(customProfile.ProviderType, Is.EqualTo("openai"), "Provider type should not be wiped");
            Assert.That(customProfile.ProviderUrl, Is.EqualTo("http://localhost:11434/v1"), "Provider URL should not be wiped");
            Assert.That(customProfile.Model, Is.EqualTo("qwen3:30b"), "Model should not be wiped");
            Assert.That(customProfile.ApiKey, Is.EqualTo("sk-test-123"), "API key should not be wiped");
            Assert.That(customProfile.OfflineMode, Is.True, "Offline mode should not be wiped");
            Assert.That(customProfile.IsDefault, Is.False, "IsDefault flag should be updated correctly");
        });
    }

    [Test]
    public void EditingOneProfile_WhileAnotherSelected_DoesNotCrossContaminate() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        // Create three profiles
        var profiles = new List<ModelProfile> {
            new("prof-1", "Profile 1", "openai", "http://host1", "model1", "key1", IsDefault: true),
            new("prof-2", "Profile 2", "anthropic", "http://host2", "model2", "key2"),
            new("prof-3", "Profile 3", "copilot", null, "gpt-4", null),
        };
        profileStore.SaveProfiles(profiles);

        // Simulate editing prof-2 while prof-1 is selected (default)
        // This is the scenario: user is editing prof-2's settings, prof-1 remains default
        var updatedProfiles = new List<ModelProfile> {
            new("prof-1", "Profile 1", "openai", "http://host1", "model1", "key1", IsDefault: true),
            new("prof-2", "Profile 2 - Updated", "anthropic", "http://new-host2", "new-model2", "new-key2"),
            new("prof-3", "Profile 3", "copilot", null, "gpt-4", null),
        };
        profileStore.SaveProfiles(updatedProfiles);

        // Verify prof-1 and prof-3 remain unchanged, only prof-2 updated
        var reloaded = profileStore.GetProfiles();
        var p1 = reloaded.First(p => p.Id == "prof-1");
        var p2 = reloaded.First(p => p.Id == "prof-2");
        var p3 = reloaded.First(p => p.Id == "prof-3");

        Assert.Multiple(() => {
            // Prof-1 unchanged
            Assert.That(p1.Alias, Is.EqualTo("Profile 1"));
            Assert.That(p1.ProviderUrl, Is.EqualTo("http://host1"));
            Assert.That(p1.Model, Is.EqualTo("model1"));
            Assert.That(p1.ApiKey, Is.EqualTo("key1"));
            Assert.That(p1.IsDefault, Is.True);

            // Prof-2 updated
            Assert.That(p2.Alias, Is.EqualTo("Profile 2 - Updated"));
            Assert.That(p2.ProviderUrl, Is.EqualTo("http://new-host2"));
            Assert.That(p2.Model, Is.EqualTo("new-model2"));
            Assert.That(p2.ApiKey, Is.EqualTo("new-key2"));

            // Prof-3 unchanged
            Assert.That(p3.Alias, Is.EqualTo("Profile 3"));
            Assert.That(p3.ProviderType, Is.EqualTo("copilot"));
            Assert.That(p3.Model, Is.EqualTo("gpt-4"));
        });
    }

    [Test]
    public void ExistingProfiles_GetCategoryAssignmentsInitialized() {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ApplicationSettingsStore(settingsPath);
        var profileStore = new ModelProfileStore(store);

        // Create profiles without category assignments (simulating existing user data)
        var profiles = new List<ModelProfile> {
            new("custom-ollama", "My Ollama", "openai", "http://localhost:11434/v1", "llama3", "sk-key", IsDefault: true),
            new("copilot-fallback", "GitHub Copilot", "copilot", null, "gpt-4o", null),
        };
        store.SaveModelProfiles(profiles);

        // Verify no assignments exist yet
        var initialSnapshot = store.Load();
        Assert.That(initialSnapshot.CategoryAssignments, Is.Null);

        // First call to GetCategoryAssignments should initialize and persist
        var assignments = profileStore.GetCategoryAssignments();
        
        Assert.Multiple(() => {
            Assert.That(assignments, Is.Not.Null);
            Assert.That(assignments.Count, Is.EqualTo(7), "All categories should be initialized");
            Assert.That(assignments[ModelProfileCategory.Coordinator], Is.EqualTo("custom-ollama"), "Should use actual default profile ID");
            Assert.That(assignments[ModelProfileCategory.Scribe], Is.EqualTo("custom-ollama"));
            Assert.That(assignments[ModelProfileCategory.Ralph], Is.EqualTo("custom-ollama"));
        });

        // Verify assignments were persisted to disk
        var persisted = store.Load();
        Assert.That(persisted.CategoryAssignments, Is.Not.Null);
        Assert.That(persisted.CategoryAssignments!.Count, Is.EqualTo(7));
        Assert.That(persisted.CategoryAssignments[ModelProfileCategory.Coordinator], Is.EqualTo("custom-ollama"));
    }
}

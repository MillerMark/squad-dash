using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ModelProfileRenameTests {
    
    [Test]
    public void ProfileRename_PreservesIdAndUpdatesAlias() {
        var profile = new ModelProfile(
            Id: "prof-123",
            Alias: "Original Name",
            ProviderType: "copilot",
            ProviderUrl: null,
            Model: "gpt-4o",
            ApiKey: null);
        
        var renamed = profile.WithAlias("New Name");
        
        Assert.Multiple(() => {
            Assert.That(renamed.Id, Is.EqualTo("prof-123"));
            Assert.That(renamed.Alias, Is.EqualTo("New Name"));
            Assert.That(renamed.ProviderType, Is.EqualTo("copilot"));
            Assert.That(renamed.Model, Is.EqualTo("gpt-4o"));
        });
    }
    
    [Test]
    public void GetDefaultCategoryAssignments_CreatesAllCategories() {
        var assignments = ModelProfileStore.GetDefaultCategoryAssignments("test-profile-id");
        
        Assert.Multiple(() => {
            Assert.That(assignments, Has.Count.EqualTo(7));
            Assert.That(assignments[ModelProfileCategory.Coordinator], Is.EqualTo("test-profile-id"));
            Assert.That(assignments[ModelProfileCategory.SpawnedNamedAgents], Is.EqualTo("test-profile-id"));
            Assert.That(assignments[ModelProfileCategory.TemporaryAgents], Is.EqualTo("test-profile-id"));
            Assert.That(assignments[ModelProfileCategory.RAI], Is.EqualTo("test-profile-id"));
            Assert.That(assignments[ModelProfileCategory.Scribe], Is.EqualTo("test-profile-id"));
            Assert.That(assignments[ModelProfileCategory.Ralph], Is.EqualTo("test-profile-id"));
            Assert.That(assignments[ModelProfileCategory.FactChecker], Is.EqualTo("test-profile-id"));
        });
    }

    [Test]
    public void CreateDuplicate_CopiesFields_AssignsNewId_AndClearsDefault() {
        var source = new ModelProfile(
            Id: "profile-1",
            Alias: "My Profile",
            ProviderType: "openai",
            ProviderUrl: "https://api.example.com/v1",
            Model: "gpt-5-mini",
            ApiKey: "sk-secret",
            OfflineMode: true,
            IsDefault: true,
            IsEnabled: false,
            WireApi: "responses",
            ReasoningEffort: "high");
        var existing = new List<ModelProfile> { source };

        var duplicate = ModelProfile.CreateDuplicate(source, existing, () => "profile-2");

        Assert.Multiple(() => {
            Assert.That(duplicate.Id, Is.EqualTo("profile-2"));
            Assert.That(duplicate.Alias, Is.EqualTo("My Profile - Copy"));
            Assert.That(duplicate.ProviderType, Is.EqualTo(source.ProviderType));
            Assert.That(duplicate.ProviderUrl, Is.EqualTo(source.ProviderUrl));
            Assert.That(duplicate.Model, Is.EqualTo(source.Model));
            Assert.That(duplicate.ApiKey, Is.EqualTo(source.ApiKey));
            Assert.That(duplicate.OfflineMode, Is.EqualTo(source.OfflineMode));
            Assert.That(duplicate.IsEnabled, Is.EqualTo(source.IsEnabled));
            Assert.That(duplicate.WireApi, Is.EqualTo(source.WireApi));
            Assert.That(duplicate.ReasoningEffort, Is.EqualTo(source.ReasoningEffort));
            Assert.That(duplicate.IsDefault, Is.False);
        });
    }

    [Test]
    public void CreateDuplicate_WhenCopyNameExists_AppendsNumericSuffix() {
        var source = new ModelProfile(
            Id: "profile-1",
            Alias: "My Profile",
            ProviderType: "copilot",
            ProviderUrl: null,
            Model: "auto",
            ApiKey: null);
        var existing = new List<ModelProfile> {
            source,
            source with { Id = "profile-2", Alias = "My Profile - Copy" },
            source with { Id = "profile-3", Alias = "My Profile - Copy 2" }
        };

        var duplicate = ModelProfile.CreateDuplicate(source, existing, () => "profile-4");

        Assert.Multiple(() => {
            Assert.That(duplicate.Id, Is.EqualTo("profile-4"));
            Assert.That(duplicate.Alias, Is.EqualTo("My Profile - Copy 3"));
        });
    }
}

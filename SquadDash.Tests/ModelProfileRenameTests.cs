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
}

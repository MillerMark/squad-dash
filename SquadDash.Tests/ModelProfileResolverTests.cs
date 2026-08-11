using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ModelProfileResolverTests {

    private static readonly ModelProfile ProfileA = new("prof-a", "Profile A", "openai", null, "gpt-4o", null);
    private static readonly ModelProfile ProfileB = new("prof-b", "Profile B", "anthropic", null, "claude-4", null);
    private static readonly ModelProfile DefaultProfile = new("prof-default", "Default", "copilot", null, "gpt-4o", null, IsDefault: true);

    private static IReadOnlyList<ModelProfile> AllProfiles => new[] { ProfileA, ProfileB, DefaultProfile };

    [Test]
    public void PerAgentOverride_WinsOverCategoryAndDefault() {
        var assignments = new Dictionary<string, string> {
            [ModelProfileCategory.Coordinator] = "prof-b"
        };

        var result = ModelProfileResolver.Resolve(AllProfiles, assignments, "prof-a", ModelProfileCategory.Coordinator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-a"));
    }

    [Test]
    public void CategoryAssignment_WinsOverDefault_WhenNoOverride() {
        var assignments = new Dictionary<string, string> {
            [ModelProfileCategory.SpawnedNamedAgents] = "prof-b"
        };

        var result = ModelProfileResolver.Resolve(AllProfiles, assignments, null, ModelProfileCategory.SpawnedNamedAgents);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-b"));
    }

    [Test]
    public void DefaultProfile_ReturnedWhenNeitherOverrideNorCategoryApplies() {
        var result = ModelProfileResolver.Resolve(AllProfiles, null, null, null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-default"));
        Assert.That(result.IsDefault, Is.True);
    }

    [Test]
    public void UnknownOverrideId_FallsThroughToCategoryAssignment() {
        var assignments = new Dictionary<string, string> {
            [ModelProfileCategory.RAI] = "prof-a"
        };

        var result = ModelProfileResolver.Resolve(AllProfiles, assignments, "nonexistent-id", ModelProfileCategory.RAI);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-a"));
    }

    [Test]
    public void UnknownCategory_FallsThroughToDefault() {
        var assignments = new Dictionary<string, string> {
            [ModelProfileCategory.Scribe] = "prof-b"
        };

        var result = ModelProfileResolver.Resolve(AllProfiles, assignments, null, "unknown-category");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-default"));
    }

    [Test]
    public void NullProfilesList_ReturnsNull() {
        var result = ModelProfileResolver.Resolve(null, null, "prof-a", ModelProfileCategory.Coordinator);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void EmptyProfilesList_ReturnsNull() {
        var result = ModelProfileResolver.Resolve(new List<ModelProfile>(), null, null, null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void NoDefaultMarked_ReturnsFirstProfile() {
        var profiles = new[] {
            new ModelProfile("first", "First", "openai", null, "gpt-4o", null),
            new ModelProfile("second", "Second", "anthropic", null, "claude-4", null),
        };

        var result = ModelProfileResolver.Resolve(profiles, null, null, null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("first"));
    }

    [Test]
    public void CategoryAssignment_WithUnknownProfileId_FallsThroughToDefault() {
        var assignments = new Dictionary<string, string> {
            [ModelProfileCategory.FactChecker] = "deleted-profile-id"
        };

        var result = ModelProfileResolver.Resolve(AllProfiles, assignments, null, ModelProfileCategory.FactChecker);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-default"));
    }

    [Test]
    public void IdLookup_IsCaseInsensitive() {
        var result = ModelProfileResolver.Resolve(AllProfiles, null, "PROF-A", null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-a"));
    }

    [Test]
    public void ResolveWithReason_ReturnsOverrideMetadata() {
        var overrides = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) {
            ["agent-123"] = "prof-b"
        };

        var result = ModelProfileResolver.ResolveWithReason(AllProfiles, null, "agent-123", null, overrides);

        Assert.Multiple(() => {
            Assert.That(result.Profile, Is.Not.Null);
            Assert.That(result.Profile!.Id, Is.EqualTo("prof-b"));
            Assert.That(result.Reason, Is.EqualTo(ModelProfileResolutionReason.Override));
            Assert.That(result.ExplicitOverrideProfileId, Is.EqualTo("prof-b"));
            Assert.That(result.ExplicitOverrideProfile, Is.Not.Null);
        });
    }

    [Test]
    public void CategoryLookup_IsCaseInsensitive() {
        var assignments = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) {
            ["COORDINATOR"] = "prof-b"
        };

        var result = ModelProfileResolver.Resolve(AllProfiles, assignments, null, ModelProfileCategory.Coordinator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("prof-b"));
    }
}

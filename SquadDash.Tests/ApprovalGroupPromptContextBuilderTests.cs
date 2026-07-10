namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalGroupPromptContextBuilderTests {
    [Test]
    public void Build_WithGroups_IncludesCanonicalListAndJsonContract() {
        var context = ApprovalGroupPromptContextBuilder.Build([
            "Guided Tour",
            "Developer Experience",
            "guided tour",
            "  Bug Fixes  "
        ]);

        Assert.Multiple(() => {
            Assert.That(context, Does.Contain("SquadDash approval group context"));
            Assert.That(context, Does.Contain("APPROVAL_GROUP_JSON:"));
            Assert.That(context, Does.Contain("{\"sha\":\"<7-char-hash>\",\"group\":\"<feature-group>\"}"));
            Assert.That(context, Does.Contain("- Guided Tour"));
            Assert.That(context, Does.Contain("- Developer Experience"));
            Assert.That(context, Does.Contain("- Bug Fixes"));
            Assert.That(context, Does.Contain("preserve spelling and capitalization exactly"));
            Assert.That(context!.IndexOf("- Guided Tour", StringComparison.Ordinal),
                Is.EqualTo(context.LastIndexOf("- Guided Tour", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Build_WithoutGroups_ReturnsNull() {
        var context = ApprovalGroupPromptContextBuilder.Build([" ", ""]);

        Assert.That(context, Is.Null);
    }
}

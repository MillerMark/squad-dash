namespace SquadDash.Tests;

[TestFixture]
internal sealed class FeatureGroupPromptBuilderTests
{
    [Test]
    public void BuildContext_SeparatesWorkspaceCategoriesFromStarterCategoriesAndIncludesCounts()
    {
        var groups = new[] { "UI & UX", "Commit History Visualizer", "Theming" };
        var items = new[]
        {
            MakeItem("Commit History Visualizer"),
            MakeItem("Commit History Visualizer"),
            MakeItem("Theming"),
            MakeItem("UI & UX"),
        };

        var context = FeatureGroupPromptBuilder.BuildContext(
            FeatureGroupPromptBuilder.BuildUsages(groups, items));

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Established workspace categories (strongly prefer these):"));
            Assert.That(context, Does.Contain("- Commit History Visualizer (2 commits)"));
            Assert.That(context, Does.Contain("- Theming (1 commit)"));
            Assert.That(context, Does.Contain("Generic starter categories (fallback scaffolding):"));
            Assert.That(context, Does.Contain("- UI & UX (1 commit)"));
            Assert.That(context, Does.Contain("prefer the more frequently used one"));
        });
    }

    private static CommitApprovalItem MakeItem(string group) => CommitApprovalItem.Create(
        Guid.NewGuid().ToString("N"), null, "Test commit", DateTimeOffset.UtcNow,
        null, null, featureGroup: group);
}

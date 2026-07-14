namespace SquadDash.Tests;

using SquadDash.Commands;

[TestFixture]
internal sealed class MergeFeatureCategoriesCommandHandlerTests
{
    [Test]
    public void Execute_ValidMerges_InvokesAtomicMergeCallback()
    {
        IReadOnlyList<(string Source, string Target)>? captured = null;
        var handler = new MergeFeatureCategoriesCommandHandler(merges => captured = merges);

        var result = handler.Execute(new Dictionary<string, string>
        {
            ["merges"] = """[{"source":"Commit Viewer","target":"Commit History Visualizer"}]"""
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(captured, Has.Count.EqualTo(1));
            Assert.That(captured![0], Is.EqualTo(("Commit Viewer", "Commit History Visualizer")));
        });
    }

    [Test]
    public void Execute_EmptyOrIdentityMerges_IsRejected()
    {
        var handler = new MergeFeatureCategoriesCommandHandler(_ => Assert.Fail("Callback should not run."));

        var result = handler.Execute(new Dictionary<string, string>
        {
            ["merges"] = """[{"source":"Theming","target":"Theming"}]"""
        });

        Assert.That(result.Success, Is.False);
    }
}

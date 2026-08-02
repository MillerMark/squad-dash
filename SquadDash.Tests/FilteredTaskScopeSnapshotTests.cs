namespace SquadDash.Tests;

[TestFixture]
internal sealed class FilteredTaskScopeSnapshotTests
{
    [Test]
    public void EncodeDecode_PreservesOriginalImmutableTaskSet()
    {
        var snapshot = new FilteredTaskScopeSnapshot(
            "parser",
            [
                new FilteredTaskScopeItem("line-a-1", "- [ ] Add parser tests"),
                new FilteredTaskScopeItem("line-b-1", "- [ ] Document parser behavior"),
            ]);

        var decoded = FilteredTaskScopeSnapshot.TryDecode(snapshot.Encode(), out var restored);

        Assert.That(decoded, Is.True);
        Assert.That(restored?.OriginalFilter, Is.EqualTo("parser"));
        Assert.That(restored?.Tasks.Select(task => task.Identity),
            Is.EqualTo(new[] { "line-a-1", "line-b-1" }));
    }

    [Test]
    public void BuildFilterInstruction_SnapshotDoesNotAskLoopToReapplyLiveFilter()
    {
        var snapshot = new FilteredTaskScopeSnapshot(
            "parser",
            [new FilteredTaskScopeItem("line-a-1", "- [ ] Add parser tests")]);

        var instruction = LoopMdParser.BuildFilterInstruction(snapshot.Encode());

        Assert.That(instruction, Does.Contain("immutable task snapshot"));
        Assert.That(instruction, Does.Contain("line-a-1"));
        Assert.That(instruction, Does.Contain("Do not re-evaluate"));
        Assert.That(instruction, Does.Not.Contain("Only process tasks whose description"));
    }
}

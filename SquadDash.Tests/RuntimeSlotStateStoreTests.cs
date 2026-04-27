namespace SquadDash.Tests;

[TestFixture]
internal sealed class RuntimeSlotStateStoreTests {
    [Test]
    public void Save_ThenLoad_PersistsActiveSlot() {
        using var workspace = new TestWorkspace();
        var store = new RuntimeSlotStateStore(workspace.RootPath);

        var saved = store.Save(new RuntimeSlotState(RuntimeSlotNames.SlotB, DateTimeOffset.UtcNow));
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(saved.ActiveSlot, Is.EqualTo(RuntimeSlotNames.SlotB));
            Assert.That(loaded.ActiveSlot, Is.EqualTo(RuntimeSlotNames.SlotB));
            Assert.That(loaded.UpdatedAt, Is.Not.Null);
            Assert.That(store.GetPayloadPath(RuntimeSlotNames.SlotB), Does.EndWith(@"B\SquadDash.App.exe"));
        });
    }

    [Test]
    public void Load_WhenMissing_ReturnsEmpty() {
        using var workspace = new TestWorkspace();
        var store = new RuntimeSlotStateStore(workspace.RootPath);

        var loaded = store.Load();

        Assert.That(loaded, Is.EqualTo(RuntimeSlotState.Empty));
    }
}

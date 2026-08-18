namespace SquadDash.Tests;

[TestFixture]
internal sealed class RuntimeSlotDeploymentPolicyTests {
    [TestCase(RuntimeSlotNames.SlotA, ExpectedResult = RuntimeSlotNames.SlotB)]
    [TestCase(RuntimeSlotNames.SlotB, ExpectedResult = RuntimeSlotNames.SlotA)]
    public string GetInactiveSlot_ReturnsOnlyOppositeSlot(string activeSlot) {
        return RuntimeSlotDeploymentPolicy.GetInactiveSlot(activeSlot);
    }

    [Test]
    public void IsSlotOccupied_WhenBothSlotsAreRunning_RejectsInactiveSlot() {
        using var workspace = new TestWorkspace();
        var store = new RuntimeSlotStateStore(workspace.RootPath);
        var runningPaths = new[] {
            store.GetPayloadPath(RuntimeSlotNames.SlotA),
            store.GetPayloadPath(RuntimeSlotNames.SlotB)
        };

        var candidate = RuntimeSlotDeploymentPolicy.GetInactiveSlot(RuntimeSlotNames.SlotA);
        var occupied = RuntimeSlotDeploymentPolicy.IsSlotOccupied(store, candidate, runningPaths);

        Assert.That(occupied, Is.True);
    }

    [Test]
    public void IsSlotOccupied_WhenOnlyActiveSlotIsRunning_AllowsInactiveSlot() {
        using var workspace = new TestWorkspace();
        var store = new RuntimeSlotStateStore(workspace.RootPath);
        var runningPaths = new[] { store.GetPayloadPath(RuntimeSlotNames.SlotA) };

        var candidate = RuntimeSlotDeploymentPolicy.GetInactiveSlot(RuntimeSlotNames.SlotA);
        var occupied = RuntimeSlotDeploymentPolicy.IsSlotOccupied(store, candidate, runningPaths);

        Assert.That(occupied, Is.False);
    }
}

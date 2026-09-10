namespace SquadDash.Tests;

[TestFixture]
internal sealed class RuntimeSlotDeploymentPolicyTests {
    [Test]
    public void Launcher_CopiesRestartCoordinatorBeforeResettingInactiveSlot() {
        var source = File.ReadAllText(FindRepoFile("SquadDashLauncher", "Program.cs"));
        var start = source.IndexOf("private static int DeployAndRestart(", StringComparison.Ordinal);
        var end = source.IndexOf("private static int CompleteDeferredDeployRestart(", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var deploy = source[start..end];

        var copyCoordinator = deploy.IndexOf(
            "CreateDetachedRestartCoordinatorLauncher(restartRequestId!)",
            StringComparison.Ordinal);
        var prepareSlot = deploy.IndexOf(
            "PrepareNextSlot(slotStore, activeState.ActiveSlot)",
            StringComparison.Ordinal);

        Assert.Multiple(() => {
            Assert.That(copyCoordinator, Is.GreaterThanOrEqualTo(0));
            Assert.That(prepareSlot, Is.GreaterThan(copyCoordinator),
                "The inactive slot may contain the executing launcher, so its coordinator files must be copied before the slot is reset.");
        });
    }

    [Test]
    public void Launcher_CoalescesDeferredBuildsAndGuardsCoordinatorGeneration() {
        var source = File.ReadAllText(FindRepoFile("SquadDashLauncher", "Program.cs"));

        Assert.Multiple(() => {
            Assert.That(source, Does.Contain("TryCoalesceDeferredDeployment("));
            Assert.That(source, Does.Contain("IsCurrentRestartRequest(restartStateStore, appRoot, requestId)"));
            Assert.That(source, Does.Contain("ClearRequestIfMatches(appRoot, requestId)"));
        });
    }

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

    [Test]
    public void IsSlotOccupied_WhenLauncherRunsFromInactiveSlot_RejectsInactiveSlot() {
        using var workspace = new TestWorkspace();
        var store = new RuntimeSlotStateStore(workspace.RootPath);
        var launcherPath = Path.Combine(
            store.GetSlotDirectory(RuntimeSlotNames.SlotB),
            "SquadDash.exe");

        var occupied = RuntimeSlotDeploymentPolicy.IsSlotOccupied(
            store,
            RuntimeSlotNames.SlotB,
            [launcherPath]);

        Assert.That(occupied, Is.True);
    }

    private static string FindRepoFile(params string[] pathParts) {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null) {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(pathParts)}'.");
    }
}

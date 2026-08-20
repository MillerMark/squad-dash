using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadCliSelectionStoreTests
{
    [Test]
    public async Task SaveSelectionAsync_PerWorkspaceIsolation_PersistsIndependentEntries()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var applicationSettingsStore = new ApplicationSettingsStore(settingsPath);
        var store = new SquadCliSelectionStore(applicationSettingsStore);
        var workspaceOne = workspace.GetPath("repo-one");
        var workspaceTwo = workspace.GetPath("repo-two");
        Directory.CreateDirectory(workspaceOne);
        Directory.CreateDirectory(workspaceTwo);

        await store.SaveSelectionAsync(workspaceOne, CliSelectionMode.CustomPath, @"C:\tools\squad-one.cmd");
        await store.SaveSelectionAsync(workspaceTwo, CliSelectionMode.GlobalNpm, null);

        Assert.Multiple(() =>
        {
            Assert.That(store.GetSelectionMode(workspaceOne), Is.EqualTo(CliSelectionMode.CustomPath));
            Assert.That(store.GetCustomPath(workspaceOne), Is.EqualTo(@"C:\tools\squad-one.cmd"));
            Assert.That(store.GetSelectionMode(workspaceTwo), Is.EqualTo(CliSelectionMode.GlobalNpm));
            Assert.That(store.GetCustomPath(workspaceTwo), Is.Null);
        });
    }

    [Test]
    public async Task SaveSelectionAsync_RoundTripPersistence_ReturnsSavedModeAndCustomPath()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var workspaceDir = workspace.GetPath("repo");
        Directory.CreateDirectory(workspaceDir);

        var store = new SquadCliSelectionStore(new ApplicationSettingsStore(settingsPath));
        await store.SaveSelectionAsync(workspaceDir, CliSelectionMode.CustomPath, @"C:\tools\squad-custom.cmd");

        var reloadedStore = new SquadCliSelectionStore(new ApplicationSettingsStore(settingsPath));

        Assert.Multiple(() =>
        {
            Assert.That(reloadedStore.GetSelectionMode(workspaceDir), Is.EqualTo(CliSelectionMode.CustomPath));
            Assert.That(reloadedStore.GetCustomPath(workspaceDir), Is.EqualTo(@"C:\tools\squad-custom.cmd"));
        });
    }

    [Test]
    public void ISquadCliSelectionStore_HasSingleMutationMethod_SaveSelectionAsyncOnly()
    {
        var methods = typeof(ISquadCliSelectionStore).GetMethods();
        var mutationMethods = methods
            .Where(method =>
                method.ReturnType == typeof(Task) ||
                method.Name.StartsWith("Save", System.StringComparison.Ordinal) ||
                method.Name.StartsWith("Set", System.StringComparison.Ordinal) ||
                method.Name.StartsWith("Update", System.StringComparison.Ordinal))
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.That(mutationMethods, Is.EqualTo(new[] { "SaveSelectionAsync" }));
    }

    [Test]
    public void GetSelection_ForWorkspaceWithoutStoredEntry_ReturnsStableDefaults()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var workspaceDir = workspace.GetPath("repo");
        Directory.CreateDirectory(workspaceDir);
        var store = new SquadCliSelectionStore(new ApplicationSettingsStore(settingsPath));

        Assert.That(() => store.GetSelectionMode(workspaceDir), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(store.GetSelectionMode(workspaceDir), Is.EqualTo(CliSelectionMode.Auto));
            Assert.That(store.GetCustomPath(workspaceDir), Is.Null);
        });
    }
}

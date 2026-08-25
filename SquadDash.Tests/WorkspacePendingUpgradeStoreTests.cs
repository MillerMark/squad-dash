using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspacePendingUpgradeStoreTests
{
    [Test]
    public async Task SavePendingThenResolve_PersistsAcrossFreshStoreInstances()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var workspaceDir = workspace.GetPath("repo");
        Directory.CreateDirectory(workspaceDir);

        var firstStore = new WorkspacePendingUpgradeStore(new ApplicationSettingsStore(settingsPath));
        await firstStore.SavePendingAsync(workspaceDir, " 0.12.0 ");

        var reloadedStore = new WorkspacePendingUpgradeStore(new ApplicationSettingsStore(settingsPath));
        var pending = await reloadedStore.GetAsync(workspaceDir);

        Assert.Multiple(() =>
        {
            Assert.That(pending.IsPending, Is.True);
            Assert.That(pending.RecordedCliVersion, Is.EqualTo("0.12.0"));
            Assert.That(pending.ResolvedAt, Is.Null);
        });

        await reloadedStore.SaveResolvedAsync(workspaceDir);

        var finalStore = new WorkspacePendingUpgradeStore(new ApplicationSettingsStore(settingsPath));
        var resolved = await finalStore.GetAsync(workspaceDir);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsPending, Is.False);
            Assert.That(resolved.RecordedCliVersion, Is.EqualTo("0.12.0"));
            Assert.That(resolved.ResolvedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task SavePending_IsIsolatedPerWorkspace()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var workspaceOne = workspace.GetPath("repo-one");
        var workspaceTwo = workspace.GetPath("repo-two");
        Directory.CreateDirectory(workspaceOne);
        Directory.CreateDirectory(workspaceTwo);

        var store = new WorkspacePendingUpgradeStore(new ApplicationSettingsStore(settingsPath));
        await store.SavePendingAsync(workspaceOne, "0.11.0");
        await store.SavePendingAsync(workspaceTwo, "0.12.0");
        await store.SaveResolvedAsync(workspaceOne);

        var reloadedStore = new WorkspacePendingUpgradeStore(new ApplicationSettingsStore(settingsPath));
        var stateOne = await reloadedStore.GetAsync(workspaceOne);
        var stateTwo = await reloadedStore.GetAsync(workspaceTwo);

        Assert.Multiple(() =>
        {
            Assert.That(stateOne.IsPending, Is.False);
            Assert.That(stateOne.RecordedCliVersion, Is.EqualTo("0.11.0"));
            Assert.That(stateOne.ResolvedAt, Is.Not.Null);

            Assert.That(stateTwo.IsPending, Is.True);
            Assert.That(stateTwo.RecordedCliVersion, Is.EqualTo("0.12.0"));
            Assert.That(stateTwo.ResolvedAt, Is.Null);
        });
    }
}

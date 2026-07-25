namespace SquadDash.Tests;

[TestFixture]
internal sealed class SettingsSnapshotManagerTests
{
    [Test]
    public void MutateAndReplace_UpdateOwnedSnapshot()
    {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, ApplicationSettingsSnapshot.Empty);

        manager.Mutate(snapshot => snapshot with { UserName = "First" });
        Assert.That(manager.Current.UserName, Is.EqualTo("First"));

        manager.Replace(manager.Current with { UserName = "Second" });
        Assert.That(manager.Current.UserName, Is.EqualTo("Second"));
    }

    [Test]
    public void MutateAndSave_PersistsUpdatedSnapshotThroughStoreCallback()
    {
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, store.Load());

        manager.MutateAndSave(
            snapshot => snapshot with { UserName = "Persisted" },
            (settingsStore, snapshot) => settingsStore.SaveUserName(snapshot.UserName));

        Assert.Multiple(() =>
        {
            Assert.That(manager.Current.UserName, Is.EqualTo("Persisted"));
            Assert.That(store.Load().UserName, Is.EqualTo("Persisted"));
        });
    }
}

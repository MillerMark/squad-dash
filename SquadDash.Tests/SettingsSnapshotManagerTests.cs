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

    [Test]
    public void Mutate_CurrentReflectsLatestMutation()
    {
        // Verifies that each Mutate call independently updates Current — representative of
        // the non-persisted mutation pattern being migrated (GODCLASS-013).
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, ApplicationSettingsSnapshot.Empty);

        manager.Mutate(s => s with { UserName = "Alpha" });
        Assert.That(manager.Current.UserName, Is.EqualTo("Alpha"));

        manager.Mutate(s => s with { UserName = "Beta" });
        Assert.That(manager.Current.UserName, Is.EqualTo("Beta"));
    }

    [Test]
    public void SyncPattern_LocalSnapshotEqualsCurrentAfterMutate()
    {
        // Simulates the migration sync line:
        //   _settingsManager.Mutate(s => s with { … });
        //   _settingsSnapshot = _settingsManager.Current;
        // Verifies the local copy stays consistent with the manager's Current.
        using var workspace = new TestWorkspace();
        var store = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, ApplicationSettingsSnapshot.Empty);

        manager.Mutate(s => s with { UserName = "Synced" });
        var localSnapshot = manager.Current; // mirrors _settingsSnapshot = _settingsManager.Current

        Assert.Multiple(() =>
        {
            Assert.That(localSnapshot.UserName, Is.EqualTo("Synced"));
            Assert.That(localSnapshot, Is.EqualTo(manager.Current));
        });
    }

    // GODCLASS-014 regression tests — verify the Replace-based persistence pattern

    [Test]
    public void Replace_UpdatesCurrent()
    {
        // Verifies that Replace() changes Current to the supplied snapshot.
        using var workspace = new TestWorkspace();
        var store   = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, ApplicationSettingsSnapshot.Empty);

        var replacement = ApplicationSettingsSnapshot.Empty with { UserName = "Replaced" };
        manager.Replace(replacement);

        Assert.That(manager.Current.UserName, Is.EqualTo("Replaced"));
    }

    [Test]
    public void Replace_CurrentEqualsReplacedValue()
    {
        // Verifies that Current is reference-equal to the exact snapshot passed to Replace().
        using var workspace = new TestWorkspace();
        var store   = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, ApplicationSettingsSnapshot.Empty);

        var replacement = ApplicationSettingsSnapshot.Empty with { UserName = "ExactMatch" };
        manager.Replace(replacement);

        Assert.That(manager.Current, Is.SameAs(replacement));
    }

    [Test]
    public void Replace_PersistenceFlow_CurrentUpdatedAfterStoreReturnsNewSnapshot()
    {
        // Simulates the GODCLASS-014 migration pattern:
        //   _settingsManager.Replace(_settingsStore.SaveXxx(args));
        //   _settingsSnapshot = _settingsManager.Current; // keep in sync
        // Verifies both the manager and the local copy reflect the persisted value.
        using var workspace = new TestWorkspace();
        var store   = new ApplicationSettingsStore(workspace.GetPath("settings", "settings.json"));
        var manager = new SettingsSnapshotManager(store, store.Load());

        var newSnapshot = store.SaveUserName("PersistenceFlowUser");
        manager.Replace(newSnapshot);
        var localSnapshot = manager.Current; // mirrors _settingsSnapshot = _settingsManager.Current

        Assert.Multiple(() =>
        {
            Assert.That(manager.Current.UserName, Is.EqualTo("PersistenceFlowUser"));
            Assert.That(localSnapshot.UserName,   Is.EqualTo("PersistenceFlowUser"));
            Assert.That(localSnapshot, Is.SameAs(manager.Current));
        });
    }
}

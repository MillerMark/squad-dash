using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class FeatureGroupStoreTests {
    private TestWorkspace _workspace = null!;
    private FeatureGroupStore _store = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = new FeatureGroupStore(_workspace.RootPath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void Load_WhenFileAbsent_ReturnsSeedDefaults() {
        var result = _store.Load();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(FeatureGroupStore.Defaults.Count));
            foreach (var defaultGroup in FeatureGroupStore.Defaults)
                Assert.That(result, Contains.Item(defaultGroup));
        });
    }

    [Test]
    public void EnsureGroup_NewGroup_AddsToList() {
        var result = _store.EnsureGroup("My Custom Group");

        Assert.That(result, Contains.Item("My Custom Group"));
    }

    [Test]
    public void EnsureGroup_ExistingGroup_NoDuplicate() {
        var beforeCount = _store.Load().Count;

        // Add an existing default group (case-insensitive variant)
        var result = _store.EnsureGroup("ui & ux");

        Assert.That(result.Count, Is.EqualTo(beforeCount));
    }

    [Test]
    public void Save_ThenLoad_RoundTrips() {
        var groups = new List<string> { "Alpha", "Beta", "Gamma" };

        _store.Save(groups);
        var result = _store.Load();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Is.EquivalentTo(groups));
        });
    }
}

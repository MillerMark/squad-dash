using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PlanVisualizerViewportStoreTests
{
    private string _root = null!;
    private string _squad = null!;
    private string _appData = null!;
    private string _previousLogDirectory = null!;
    private PlanVisualizerViewportStore _store = null!;

    private string ViewportPath => Path.Combine(_squad, "tmp", "decompose", "layout", "plan.viewport.json");
    private string DefaultZoomPath => Path.Combine(_appData, "plan-visualizer-zoom.json");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.TestDirectory, $"viewport-tests-{Guid.NewGuid():N}");
        _squad = Path.Combine(_root, "workspace", ".squad");
        _appData = Path.Combine(_root, "app-data");
        Directory.CreateDirectory(_root);
        _previousLogDirectory = Path.GetDirectoryName(SquadDashTrace.CurrentLogPath)!;
        SquadDashTrace.SetWorkspace(_root);
        _store = new PlanVisualizerViewportStore(_squad, _appData);
    }

    [TearDown]
    public void TearDown()
    {
        SquadDashTrace.SetWorkspace(_previousLogDirectory);
        Directory.Delete(_root, recursive: true);
    }

    [TestCase(0.25)]
    [TestCase(1.23456789012345)]
    [TestCase(4.0)]
    public void Save_ReloadsExactStateAndDefaultZoom(double zoom)
    {
        var state = new PlanVisualizerViewportState(zoom, 123.456789012345, 987.654321098765);
        _store.Save("plan", state);
        _store.SaveDefaultZoom(zoom);

        var reloaded = new PlanVisualizerViewportStore(_squad, _appData);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Load("plan"), Is.EqualTo(state));
            Assert.That(reloaded.LoadDefaultZoom(), Is.EqualTo(zoom));
            Assert.That(File.Exists(ViewportPath), Is.True);
            Assert.That(File.Exists(DefaultZoomPath), Is.True);
        });
    }

    [Test]
    public void Save_OverwritesAtomicallyWithoutTouchingLegacyNudges()
    {
        var first = new PlanVisualizerViewportState(1.25, 10, 20);
        var second = new PlanVisualizerViewportState(2.5, 30, 40);
        _store.Save("plan", first);
        _store.SaveDefaultZoom(first.Zoom);
        var legacyPath = Path.Combine(Path.GetDirectoryName(ViewportPath)!, "plan.json");
        const string legacyJson = """{"step-1": 42}""";
        File.WriteAllText(legacyPath, legacyJson);

        _store.Save("plan", second);
        _store.SaveDefaultZoom(second.Zoom);
        using var defaultBackup = JsonDocument.Parse(File.ReadAllText(DefaultZoomPath + ".backup"));

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load("plan"), Is.EqualTo(second));
            Assert.That(_store.LoadDefaultZoom(), Is.EqualTo(second.Zoom));
            Assert.That(JsonSerializer.Deserialize<PlanVisualizerViewportState>(
                File.ReadAllText(ViewportPath + ".backup")), Is.EqualTo(first));
            Assert.That(defaultBackup.RootElement.GetProperty("Zoom").GetDouble(), Is.EqualTo(first.Zoom));
            Assert.That(File.Exists(ViewportPath + ".tmp"), Is.False);
            Assert.That(File.Exists(DefaultZoomPath + ".tmp"), Is.False);
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(legacyJson));
        });
    }

    [Test]
    public void PlansAndWorkspaces_AreIsolated()
    {
        var other = new PlanVisualizerViewportStore(Path.Combine(_root, "other-squad"), _appData);
        var first = new PlanVisualizerViewportState(1.25, 10, 20);
        var second = new PlanVisualizerViewportState(2, 30, 40);
        var third = new PlanVisualizerViewportState(3, 50, 60);
        _store.Save("plan", first);
        _store.Save("other-plan", second);
        other.Save("plan", third);

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load("plan"), Is.EqualTo(first));
            Assert.That(_store.Load("other-plan"), Is.EqualTo(second));
            Assert.That(other.Load("plan"), Is.EqualTo(third));
            Assert.That(other.Load("other-plan"), Is.Null);
        });
    }

    [Test]
    public void NewPlans_InheritExplicitAppDefault_NotPerPlanSaves()
    {
        _store.SaveDefaultZoom(1.75);
        _store.Save("plan", new PlanVisualizerViewportState(3, 10, 20));
        var other = new PlanVisualizerViewportStore(Path.Combine(_root, "other-squad"), _appData);

        Assert.That(other.Load("unseen")?.Zoom ?? other.LoadDefaultZoom(), Is.EqualTo(1.75));
        other.SaveDefaultZoom(2.25);
        other.Save("unseen", new PlanVisualizerViewportState(4, 0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(_store.LoadDefaultZoom(), Is.EqualTo(2.25));
            Assert.That(_store.Load("plan")!.Zoom, Is.EqualTo(3));
            Assert.That(new PlanVisualizerViewportStore(_squad, Path.Combine(_root, "other-app"))
                .LoadDefaultZoom(), Is.EqualTo(1));
        });
    }

    [Test]
    public void MissingFiles_ReturnFallbacksWithoutCreatingDirectories()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_store.Load("plan"), Is.Null);
            Assert.That(_store.LoadDefaultZoom(), Is.EqualTo(1));
            Assert.That(Directory.Exists(_squad), Is.False);
            Assert.That(Directory.Exists(_appData), Is.False);
        });
    }

    [Test]
    public void NullSquad_AllowsDefaultZoomButRejectsPlanSave()
    {
        var store = new PlanVisualizerViewportStore(null, _appData);
        store.SaveDefaultZoom(2);
        Assert.Multiple(() =>
        {
            Assert.That(store.Load("plan"), Is.Null);
            Assert.That(store.LoadDefaultZoom(), Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() =>
                store.Save("plan", new PlanVisualizerViewportState(1, 0, 0)));
        });
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("""{"Zoom":1}""")]
    [TestCase("""{"Zoom":1,"HorizontalOffset":0}""")]
    [TestCase("""{"Zoom":1,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":null,"HorizontalOffset":0,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":0.249,"HorizontalOffset":0,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":4.001,"HorizontalOffset":0,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":1e999,"HorizontalOffset":0,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":"NaN","HorizontalOffset":0,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":1,"HorizontalOffset":-1,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":1,"HorizontalOffset":0,"VerticalOffset":-1}""")]
    [TestCase("""{"Zoom":1,"HorizontalOffset":1e999,"VerticalOffset":0}""")]
    [TestCase("""{"Zoom":1,"HorizontalOffset":0,"VerticalOffset":1e999}""")]
    public void InvalidStoredViewport_ReturnsNullAndLogs(string json)
    {
        WriteJson(ViewportPath, json);
        Assert.That(_store.Load("plan"), Is.Null);
        Assert.That(File.ReadAllText(SquadDashTrace.CurrentLogPath), Does.Contain("[PlanViewport]"));
        Assert.That(File.ReadAllText(ViewportPath), Is.EqualTo(json));
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("""{"Zoom":null}""")]
    [TestCase("""{"Zoom":0.249}""")]
    [TestCase("""{"Zoom":4.001}""")]
    [TestCase("""{"Zoom":1e999}""")]
    [TestCase("""{"Zoom":"NaN"}""")]
    [TestCase("""{"Zoom":"Infinity"}""")]
    public void InvalidStoredDefault_ReturnsOneAndLogs(string json)
    {
        WriteJson(DefaultZoomPath, json);
        Assert.That(_store.LoadDefaultZoom(), Is.EqualTo(1));
        Assert.That(File.ReadAllText(SquadDashTrace.CurrentLogPath), Does.Contain("[PlanViewport]"));
        Assert.That(File.ReadAllText(DefaultZoomPath), Is.EqualTo(json));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(0.249)]
    [TestCase(4.001)]
    public void InvalidZoom_IsRejectedWithoutWriting(double zoom)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _store.Save("plan", new PlanVisualizerViewportState(zoom, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.SaveDefaultZoom(zoom));
        Assert.That(Directory.Exists(_squad), Is.False);
        Assert.That(Directory.Exists(_appData), Is.False);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(-0.001)]
    public void InvalidOffsets_AreRejected(double offset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _store.Save("plan", new PlanVisualizerViewportState(1, offset, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _store.Save("plan", new PlanVisualizerViewportState(1, 0, offset)));
        Assert.That(File.Exists(ViewportPath), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("..")]
    [TestCase(@"..\escape")]
    [TestCase(@"nested\plan")]
    [TestCase("nested/plan")]
    [TestCase(@"C:\escape")]
    public void InvalidGroupId_IsRejected(string? groupId)
    {
        Assert.That(() => _store.Save(groupId!, new PlanVisualizerViewportState(1, 0, 0)),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(() => _store.Load(groupId!), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void NullState_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Save("plan", null!));
    }

    [Test]
    public void ReadIoErrors_ReturnFallbacksAndLog()
    {
        _store.Save("plan", new PlanVisualizerViewportState(1, 0, 0));
        _store.SaveDefaultZoom(2);
        using var viewportLock = new FileStream(ViewportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var defaultLock = new FileStream(DefaultZoomPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(_store.Load("plan"), Is.Null);
        Assert.That(_store.LoadDefaultZoom(), Is.EqualTo(1));
        Assert.That(File.ReadAllText(SquadDashTrace.CurrentLogPath), Does.Contain("Could not load viewport data"));
    }

    [Test]
    public void SaveIoErrors_Propagate()
    {
        var blocked = Path.Combine(_root, "file-not-directory");
        File.WriteAllText(blocked, "blocked");
        var store = new PlanVisualizerViewportStore(blocked, blocked);
        Assert.That(() => store.Save("plan", new PlanVisualizerViewportState(1, 0, 0)),
            Throws.InstanceOf<IOException>());
        Assert.That(() => store.SaveDefaultZoom(2), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void ConcurrentInstances_DoNotCollideOnAtomicWritePaths()
    {
        Parallel.For(0, 32, i =>
        {
            var store = new PlanVisualizerViewportStore(_squad, _appData);
            store.Save("plan", new PlanVisualizerViewportState(1 + i / 32.0, i, i * 2));
            store.SaveDefaultZoom(1 + i / 32.0);
        });

        var state = _store.Load("plan")!;
        Assert.Multiple(() =>
        {
            Assert.That(state.Zoom, Is.EqualTo(1 + state.HorizontalOffset / 32.0));
            Assert.That(state.VerticalOffset, Is.EqualTo(state.HorizontalOffset * 2));
            Assert.That(_store.LoadDefaultZoom(), Is.InRange(1, 2));
            Assert.That(File.Exists(ViewportPath + ".tmp"), Is.False);
            Assert.That(File.Exists(DefaultZoomPath + ".tmp"), Is.False);
        });
    }

    private static void WriteJson(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}

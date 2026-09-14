using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CommitViewerCodeFontStoreTests
{
    private string _root = null!;
    private string _appData = null!;
    private string _previousLogDirectory = null!;
    private CommitViewerCodeFontStore _store = null!;

    private string FontPath => Path.Combine(_appData, "commit-viewer-code-font.json");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.TestDirectory, $"code-font-tests-{Guid.NewGuid():N}");
        _appData = Path.Combine(_root, "app-data");
        Directory.CreateDirectory(_root);
        _previousLogDirectory = Path.GetDirectoryName(SquadDashTrace.CurrentLogPath)!;
        SquadDashTrace.SetWorkspace(_root);
        _store = new CommitViewerCodeFontStore(_appData);
    }

    [TearDown]
    public void TearDown()
    {
        SquadDashTrace.SetWorkspace(_previousLogDirectory);
        Directory.Delete(_root, recursive: true);
    }

    [TestCase(8)]
    [TestCase(14)]
    [TestCase(14.123456789012345)]
    [TestCase(32)]
    public void Save_RoundTripsAcrossInstances(double fontSize)
    {
        _store.Save(fontSize);
        var reloaded = new CommitViewerCodeFontStore(_appData);
        using var json = JsonDocument.Parse(File.ReadAllText(FontPath));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Load(12), Is.EqualTo(fontSize));
            Assert.That(json.RootElement.GetProperty("FontSize").GetDouble(), Is.EqualTo(fontSize));
            Assert.That(json.RootElement.EnumerateObject().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Save_OverwritesWithBackupAndNoTemporaryResidue()
    {
        _store.Save(14);
        new CommitViewerCodeFontStore(_appData).Save(18);
        using var backup = JsonDocument.Parse(File.ReadAllText(FontPath + ".backup"));

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load(12), Is.EqualTo(18));
            Assert.That(backup.RootElement.GetProperty("FontSize").GetDouble(), Is.EqualTo(14));
            Assert.That(Directory.GetFiles(_appData), Is.EquivalentTo(new[] { FontPath, FontPath + ".backup" }));
        });
    }

    [TestCase(6.5)]
    [TestCase(13.25)]
    [TestCase(40)]
    public void MissingFile_PreservesThemedDefaultWithoutCreatingDirectory(double defaultFontSize)
    {
        Assert.Multiple(() =>
        {
            Assert.That(_store.Load(defaultFontSize), Is.EqualTo(defaultFontSize));
            Assert.That(Directory.Exists(_appData), Is.False);
        });
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("""{"FontSize":null}""")]
    [TestCase("""{"FontSize":"14"}""")]
    [TestCase("""{"FontSize":"NaN"}""")]
    [TestCase("""{"FontSize":"Infinity"}""")]
    [TestCase("""{"FontSize":1e999}""")]
    [TestCase("""{"FontSize":-1e999}""")]
    [TestCase("""{"Zoom":14}""")]
    public void CorruptFile_PreservesDefaultAndLogsWithoutRewriting(string json)
    {
        WriteJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load(40), Is.EqualTo(40));
            Assert.That(_store.Load(6.5), Is.EqualTo(6.5));
            Assert.That(File.ReadAllText(SquadDashTrace.CurrentLogPath), Does.Contain("[CommitViewerCodeFont]"));
            Assert.That(File.ReadAllText(FontPath), Is.EqualTo(json));
            Assert.That(File.Exists(FontPath + ".backup"), Is.False);
        });
    }

    [TestCase(double.MinValue, 8)]
    [TestCase(-1, 8)]
    [TestCase(0, 8)]
    [TestCase(7.999, 8)]
    [TestCase(8, 8)]
    [TestCase(14.25, 14.25)]
    [TestCase(32, 32)]
    [TestCase(32.001, 32)]
    [TestCase(double.MaxValue, 32)]
    public void ClampAndSave_ClampFiniteSizes(double fontSize, double expected)
    {
        Assert.That(CommitViewerCodeFontStore.Clamp(fontSize), Is.EqualTo(expected));
        _store.Save(fontSize);
        using var json = JsonDocument.Parse(File.ReadAllText(FontPath));

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load(12), Is.EqualTo(expected));
            Assert.That(json.RootElement.GetProperty("FontSize").GetDouble(), Is.EqualTo(expected));
        });
    }

    [TestCase("""{"FontSize":-1e300}""", 8)]
    [TestCase("""{"FontSize":7.999}""", 8)]
    [TestCase("""{"FontSize":32.001}""", 32)]
    [TestCase("""{"FontSize":1e300}""", 32)]
    public void Load_ClampsPersistedSizesAndLogsAdjustment(string json, double expected)
    {
        WriteJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load(12), Is.EqualTo(expected));
            Assert.That(File.ReadAllText(SquadDashTrace.CurrentLogPath),
                Does.Contain("[CommitViewerCodeFont]").And.Contain("Adjusted font size"));
            Assert.That(File.ReadAllText(FontPath), Is.EqualTo(json));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void NonfiniteSize_IsRejectedBeforeWriting(double fontSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitViewerCodeFontStore.Clamp(fontSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.Save(fontSize));
        Assert.That(Directory.Exists(_appData), Is.False);

        _store.Save(14);
        var original = File.ReadAllText(FontPath);
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.Save(fontSize));
        Assert.That(File.ReadAllText(FontPath), Is.EqualTo(original));
        Assert.That(File.Exists(FontPath + ".backup"), Is.False);
    }

    [TestCase("")]
    [TestCase(" ")]
    public void EmptyAppDataPath_IsRejected(string appDataPath)
    {
        Assert.That(() => new CommitViewerCodeFontStore(appDataPath), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ReadIoError_PreservesDefaultAndLogs()
    {
        _store.Save(14);
        using var fileLock = new FileStream(FontPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(_store.Load(40), Is.EqualTo(40));
        Assert.That(File.ReadAllText(SquadDashTrace.CurrentLogPath),
            Does.Contain("[CommitViewerCodeFont]").And.Contain("Could not load font size data"));
    }

    [Test]
    public void SaveIoError_Propagates()
    {
        var blocked = Path.Combine(_root, "file-not-directory");
        File.WriteAllText(blocked, "blocked");
        var store = new CommitViewerCodeFontStore(blocked);

        Assert.That(() => store.Save(14), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void ConcurrentInstances_DoNotCollideOnAtomicWritePaths()
    {
        Parallel.For(0, 32, i =>
        {
            var store = new CommitViewerCodeFontStore(_appData);
            store.Save(8 + i * 0.5);
            Assert.That(store.Load(0), Is.InRange(8, 23.5));
        });

        using var current = JsonDocument.Parse(File.ReadAllText(FontPath));
        using var backup = JsonDocument.Parse(File.ReadAllText(FontPath + ".backup"));
        Assert.Multiple(() =>
        {
            Assert.That(current.RootElement.GetProperty("FontSize").GetDouble(), Is.InRange(8, 23.5));
            Assert.That(backup.RootElement.GetProperty("FontSize").GetDouble(), Is.InRange(8, 23.5));
            Assert.That(_store.Load(0), Is.EqualTo(current.RootElement.GetProperty("FontSize").GetDouble()));
            Assert.That(File.Exists(FontPath + ".tmp"), Is.False);
        });
    }

    private void WriteJson(string json)
    {
        Directory.CreateDirectory(_appData);
        File.WriteAllText(FontPath, json);
    }
}

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspacePathsProviderTests {
    [Test]
    public void Constructor_NormalisesTrailingSlash() {
        using var dir = new TempDirectory();
        var withSlash = dir.Path + Path.DirectorySeparatorChar;
        var provider = new WorkspacePathsProvider(withSlash);
        Assert.That(provider.ApplicationRoot, Does.Not.EndWith(Path.DirectorySeparatorChar.ToString()));
    }

    [Test]
    public void Constructor_RejectsEmptyString() {
        Assert.Throws<ArgumentException>(() => new WorkspacePathsProvider(string.Empty));
    }

    [Test]
    public void Constructor_RejectsWhitespace() {
        Assert.Throws<ArgumentException>(() => new WorkspacePathsProvider("   "));
    }

    [Test]
    public void AllProperties_ReturnNonEmptyStrings() {
        using var dir = new TempDirectory();
        var provider = new WorkspacePathsProvider(dir.Path);
        Assert.That(provider.ApplicationRoot, Is.Not.Empty);
        Assert.That(provider.SquadSdkDirectory, Is.Not.Empty);
        Assert.That(provider.RunRootDirectory, Is.Not.Empty);
        Assert.That(provider.AgentImageAssetsDirectory, Is.Not.Empty);
    }

    [Test]
    public void Discover_FindsRootWithBothDirectories() {
        var provider = WorkspacePathsProvider.Discover();
        Assert.That(Directory.Exists(Path.Combine(provider.ApplicationRoot, "SquadDash")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(provider.ApplicationRoot, "Squad.SDK")), Is.True);
    }

    [Test]
    public void SquadSdkDirectory_IsNestedUnderApplicationRoot() {
        using var dir = new TempDirectory();
        var provider = new WorkspacePathsProvider(dir.Path);
        Assert.That(provider.SquadSdkDirectory, Does.StartWith(provider.ApplicationRoot));
    }

    [Test]
    public void RunRootDirectory_EndsWithRunFolderName() {
        using var dir = new TempDirectory();
        var provider = new WorkspacePathsProvider(dir.Path);
        Assert.That(Path.GetFileName(provider.RunRootDirectory), Is.EqualTo("Run"));
    }

    [Test]
    public void SquadSdkDirectory_EndsWithSquadSdkFolderName() {
        using var dir = new TempDirectory();
        var provider = new WorkspacePathsProvider(dir.Path);
        Assert.That(Path.GetFileName(provider.SquadSdkDirectory), Is.EqualTo("Squad.SDK"));
    }

    [Test]
    public void AgentImageAssetsDirectory_EndsWithAgentsFolderName() {
        using var dir = new TempDirectory();
        var provider = new WorkspacePathsProvider(dir.Path);
        Assert.That(Path.GetFileName(provider.AgentImageAssetsDirectory), Is.EqualTo("agents"));
    }

    private sealed class TempDirectory : IDisposable {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}

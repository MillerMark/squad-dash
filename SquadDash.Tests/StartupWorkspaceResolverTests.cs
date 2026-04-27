namespace SquadDash.Tests;

[TestFixture]
internal sealed class StartupWorkspaceResolverTests {
    [Test]
    public void NormalizePath_RemovesTrailingDirectorySeparator() {
        var path = Path.Combine(Path.GetTempPath(), "TestRepo") + Path.DirectorySeparatorChar;

        var result = StartupWorkspaceResolver.NormalizePath(path);

        Assert.That(result, Does.Not.EndWith(Path.DirectorySeparatorChar.ToString()));
        Assert.That(result, Does.Not.EndWith(Path.AltDirectorySeparatorChar.ToString()));
    }

    [Test]
    public void NormalizePath_ResolvesRelativeSegments() {
        var tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.Combine(tempPath, "a", "..", "b");

        var result = StartupWorkspaceResolver.NormalizePath(path);

        Assert.That(result, Is.EqualTo(Path.Combine(tempPath, "b")));
    }

    [Test]
    public void LooksLikeWorkspaceRoot_ReturnsTrueForFolderWithDotSquad() {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.GetPath(".squad"));

        Assert.That(StartupWorkspaceResolver.LooksLikeWorkspaceRoot(workspace.RootPath), Is.True);
    }

    [Test]
    public void LooksLikeWorkspaceRoot_ReturnsTrueForFolderWithDotGit() {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.GetPath(".git"));

        Assert.That(StartupWorkspaceResolver.LooksLikeWorkspaceRoot(workspace.RootPath), Is.True);
    }

    [Test]
    public void LooksLikeWorkspaceRoot_ReturnsTrueForFolderWithSlnFile() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("MyApp.sln");

        Assert.That(StartupWorkspaceResolver.LooksLikeWorkspaceRoot(workspace.RootPath), Is.True);
    }

    [Test]
    public void LooksLikeWorkspaceRoot_ReturnsTrueForFolderWithSlnxFile() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("MyApp.slnx");

        Assert.That(StartupWorkspaceResolver.LooksLikeWorkspaceRoot(workspace.RootPath), Is.True);
    }

    [Test]
    public void LooksLikeWorkspaceRoot_ReturnsFalseForEmptyFolder() {
        using var workspace = new TestWorkspace();

        Assert.That(StartupWorkspaceResolver.LooksLikeWorkspaceRoot(workspace.RootPath), Is.False);
    }

    [Test]
    public void LooksLikeWorkspaceRoot_ReturnsFalseForNonExistentPath() {
        Assert.That(
            StartupWorkspaceResolver.LooksLikeWorkspaceRoot(@"C:\does\not\exist\xyz_no_such_folder"),
            Is.False);
    }

    [Test]
    public void Resolve_PrefersStartupFolderWhenItLooksLikeWorkspaceRoot() {
        using var workspace = new TestWorkspace();
        var startupFolder = workspace.GetPath("startup");
        var lastOpened = workspace.GetPath("last-opened");
        Directory.CreateDirectory(lastOpened);
        Directory.CreateDirectory(Path.Combine(lastOpened, ".git"));
        Directory.CreateDirectory(startupFolder);
        Directory.CreateDirectory(Path.Combine(startupFolder, ".git"));

        var result = StartupWorkspaceResolver.Resolve(startupFolder, lastOpened, null);

        Assert.That(result, Is.EqualTo(StartupWorkspaceResolver.NormalizePath(startupFolder)));
    }

    [Test]
    public void Resolve_FallsBackToLastOpenedFolderWhenStartupFolderIsNull() {
        using var workspace = new TestWorkspace();
        var lastOpened = workspace.GetPath("last-opened");
        Directory.CreateDirectory(lastOpened);
        Directory.CreateDirectory(Path.Combine(lastOpened, ".git"));

        var result = StartupWorkspaceResolver.Resolve(null, lastOpened, null);

        Assert.That(result, Is.EqualTo(StartupWorkspaceResolver.NormalizePath(lastOpened)));
    }

    [Test]
    public void Resolve_FallsBackToApplicationRootWhenOtherFoldersAreMissing() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(Path.Combine(appRoot, ".git"));

        var result = StartupWorkspaceResolver.Resolve(
            @"C:\does\not\exist\folder1",
            @"C:\does\not\exist\folder2",
            appRoot);

        Assert.That(result, Is.EqualTo(StartupWorkspaceResolver.NormalizePath(appRoot)));
    }

    [Test]
    public void Resolve_ReturnsNullWhenAllCandidatesAreNullOrMissing() {
        var result = StartupWorkspaceResolver.Resolve(
            @"C:\does\not\exist\1",
            @"C:\does\not\exist\2",
            @"C:\does\not\exist\3");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Resolve_DeduplicatesCandidates() {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, ".git"));

        // If the same path appears twice it is still found correctly
        var result = StartupWorkspaceResolver.Resolve(
            workspace.RootPath,
            workspace.RootPath,
            null);

        Assert.That(result, Is.EqualTo(StartupWorkspaceResolver.NormalizePath(workspace.RootPath)));
    }

    [Test]
    public void Resolve_ReturnsFallbackFolderEvenWhenItDoesNotLookLikeWorkspaceRoot() {
        using var workspace = new TestWorkspace();
        var plain = workspace.GetPath("plain");
        Directory.CreateDirectory(plain);

        var result = StartupWorkspaceResolver.Resolve(plain, null, null);

        Assert.That(result, Is.EqualTo(StartupWorkspaceResolver.NormalizePath(plain)));
    }
}

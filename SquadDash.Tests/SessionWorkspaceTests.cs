namespace SquadDash.Tests;

[TestFixture]
internal sealed class SessionWorkspaceTests {
    [Test]
    public void Create_NoSolutionFiles_ReturnNullSolutionPathAndName() {
        using var workspace = new TestWorkspace();

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.FolderPath, Is.EqualTo(workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(result.SolutionPath, Is.Null);
            Assert.That(result.SolutionName, Is.Null);
        });
    }

    [Test]
    public void Create_SingleSlnFile_SetsPathAndName() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("MyApp.sln", "");

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.SolutionPath, Is.EqualTo(workspace.GetPath("MyApp.sln")));
            Assert.That(result.SolutionName, Is.EqualTo("MyApp.sln"));
        });
    }

    [Test]
    public void Create_SingleSlnxFile_SetsPathAndName() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("MyApp.slnx", "");

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.SolutionPath, Is.EqualTo(workspace.GetPath("MyApp.slnx")));
            Assert.That(result.SolutionName, Is.EqualTo("MyApp.slnx"));
        });
    }

    [Test]
    public void Create_BothSlnAndSlnx_PrefersSlnOverSlnx() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("MyApp.slnx", "");
        workspace.CreateFile("MyApp.sln", "");

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.That(result.SolutionName, Is.EqualTo("MyApp.sln"));
    }

    [Test]
    public void Create_MultipleSlnFiles_PicksAlphabeticallyFirst() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("zeta.sln", "");
        workspace.CreateFile("alpha.sln", "");
        workspace.CreateFile("middle.sln", "");

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.That(result.SolutionName, Is.EqualTo("alpha.sln"));
    }

    [Test]
    public void Create_TrailingSlashInInput_NormalizesPath() {
        using var workspace = new TestWorkspace();
        var pathWithSlash = workspace.RootPath + Path.DirectorySeparatorChar;

        var result = SessionWorkspace.Create(pathWithSlash);

        Assert.That(result.FolderPath, Does.Not.EndWith(Path.DirectorySeparatorChar.ToString()));
    }

    [Test]
    public void SquadFolderPath_ReturnsSquadSubdirectory() {
        using var workspace = new TestWorkspace();

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.That(result.SquadFolderPath, Is.EqualTo(Path.Combine(result.FolderPath, ".squad")));
    }

    [Test]
    public void Create_SlnInSubdirectory_IsNotDetected() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(Path.Combine("subdir", "Hidden.sln"), "");

        var result = SessionWorkspace.Create(workspace.RootPath);

        Assert.That(result.SolutionPath, Is.Null);
    }
}

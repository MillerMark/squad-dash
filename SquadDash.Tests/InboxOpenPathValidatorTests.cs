namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxOpenPathValidatorTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "SquadDash-InboxOpenPathValidator", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Validate_AcceptsExistingWorkspaceRelativeFileAndFolder()
    {
        var folder = Path.Combine(_root, "samples", "kit");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "runbook.md"), "instructions");
        var body =
            "[Open runbook](app://open-path:samples/kit/runbook.md) " +
            "[Open folder](app://open-path:samples/kit)";

        Assert.That(InboxOpenPathValidator.Validate(body, _root), Is.Null);
    }

    [Test]
    public void Validate_AcceptsExistingAbsoluteTargetOutsideWorkspace()
    {
        var workspace = Path.Combine(_root, "Package");
        var repositoryArtifact = Path.Combine(_root, "Plugins", "ManualTestKit", "Sample.sln");
        Directory.CreateDirectory(Path.GetDirectoryName(repositoryArtifact)!);
        Directory.CreateDirectory(workspace);
        File.WriteAllText(repositoryArtifact, string.Empty);
        var body = $"[Open sample](app://open-path:{repositoryArtifact})";

        Assert.That(InboxOpenPathValidator.Validate(body, workspace), Is.Null);
    }

    [Test]
    public void Validate_RejectsRepositoryRelativeTargetWhenWorkspaceIsNested()
    {
        var workspace = Path.Combine(_root, "Package");
        var repositoryArtifact = Path.Combine(_root, "Plugins", "ManualTestKit", "Sample.sln");
        Directory.CreateDirectory(Path.GetDirectoryName(repositoryArtifact)!);
        Directory.CreateDirectory(workspace);
        File.WriteAllText(repositoryArtifact, string.Empty);

        var error = InboxOpenPathValidator.Validate(
            "[Open sample](app://open-path:Plugins/ManualTestKit/Sample.sln)",
            workspace);

        Assert.That(error, Does.Contain(Path.Combine(workspace, "Plugins", "ManualTestKit", "Sample.sln")));
        Assert.That(error, Does.Contain("workspace-relative, not Git-repository-relative"));
    }

    [Test]
    public void Validate_IgnoresMessagesWithoutOpenPathLinks()
    {
        Assert.That(
            InboxOpenPathValidator.Validate("See [the documentation](https://example.test).", _root),
            Is.Null);
    }
}

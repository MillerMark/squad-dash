namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxLinkCopyTargetResolverTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "SquadDash-InboxLinkCopy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Resolve_WebUrl_CopiesLink()
    {
        var result = InboxLinkCopyTargetResolver.Resolve("https://example.test/path?q=1", _root);

        Assert.Multiple(() =>
        {
            Assert.That(result?.MenuLabel, Is.EqualTo("Copy Link"));
            Assert.That(result?.ClipboardText, Is.EqualTo("https://example.test/path?q=1"));
        });
    }

    [Test]
    public void Resolve_WorkspaceRelativeFile_CopiesAbsoluteFilePath()
    {
        var file = Path.Combine(_root, "kit", "runbook.md");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "test");

        var result = InboxLinkCopyTargetResolver.Resolve(
            "app://open-path:kit/runbook.md",
            _root);

        Assert.Multiple(() =>
        {
            Assert.That(result?.MenuLabel, Is.EqualTo("Copy File Path"));
            Assert.That(result?.ClipboardText, Is.EqualTo(file));
        });
    }

    [Test]
    public void Resolve_WorkspaceRelativeFolder_CopiesAbsoluteFolderPath()
    {
        var folder = Path.Combine(_root, "kit", "fixture");
        Directory.CreateDirectory(folder);

        var result = InboxLinkCopyTargetResolver.Resolve(
            "app://open-path:kit/fixture",
            _root);

        Assert.Multiple(() =>
        {
            Assert.That(result?.MenuLabel, Is.EqualTo("Copy Folder Path"));
            Assert.That(result?.ClipboardText, Is.EqualTo(folder));
        });
    }

    [Test]
    public void Resolve_UnknownPath_StillOffersCopyPath()
    {
        var expected = Path.Combine(_root, "missing", "future.sln");

        var result = InboxLinkCopyTargetResolver.Resolve(
            "app://open-path:missing/future.sln",
            _root);

        Assert.Multiple(() =>
        {
            Assert.That(result?.MenuLabel, Is.EqualTo("Copy Path"));
            Assert.That(result?.ClipboardText, Is.EqualTo(expected));
        });
    }
}

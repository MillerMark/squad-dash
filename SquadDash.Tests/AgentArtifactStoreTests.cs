using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentArtifactStoreTests
{
    [Test]
    public void TryMaterialize_RelativeTempPath_ReadsAndArchivesFile()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(
            Path.Combine(".squad", "tmp", "agent-artifacts", "report.json"),
            "{ \"ok\": true }");

        var result = AgentArtifactStore.TryMaterialize(
            workspace.RootPath,
            new AgentArtifactReference
            {
                Path = ".squad/tmp/agent-artifacts/report.json",
                Language = "json"
            },
            AgentArtifactStore.DefaultMaxDisplayBytes,
            archive: true,
            out var artifact,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, error);
            Assert.That(artifact, Is.Not.Null);
            Assert.That(artifact!.Content, Is.EqualTo("{ \"ok\": true }"));
            Assert.That(artifact.ArchivedRelativePath, Does.StartWith(Path.Combine(".squad", "archive", "agent-artifacts")));
            Assert.That(File.Exists(artifact.ArchivedPath), Is.True);
        });
    }

    [Test]
    public void TryMaterialize_PathOutsideArtifactRoots_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("report.json", "{}");

        var result = AgentArtifactStore.TryMaterialize(
            workspace.RootPath,
            new AgentArtifactReference { Path = "report.json" },
            AgentArtifactStore.DefaultMaxDisplayBytes,
            archive: false,
            out _,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(error, Does.Contain(".squad/tmp/agent-artifacts"));
        });
    }

    [Test]
    public void ExpandDisplayArtifacts_ReplacesManifestWithFencedCodeBlock()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(
            Path.Combine(".squad", "tmp", "agent-artifacts", "payload.json"),
            "{ \"marker\": \"INBOX_MESSAGE_JSON:\" }");
        const string response = """
            Here is the payload.

            SQUADDASH_ARTIFACT_JSON:
            { "path": ".squad/tmp/agent-artifacts/payload.json", "language": "json", "display": "code_block" }
            """;

        var expanded = AgentArtifactBlockExpander.ExpandDisplayArtifacts(response, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(expanded, Does.Contain("Here is the payload."));
            Assert.That(expanded, Does.Contain("```json"));
            Assert.That(expanded, Does.Contain("{ \"marker\": \"INBOX_MESSAGE_JSON:\" }"));
            Assert.That(expanded, Does.Not.Contain("SQUADDASH_ARTIFACT_JSON:"));
        });
    }

    [Test]
    public void ResolveActiveWorkspaceRoot_PrefersWorkspaceOverApplicationRoot()
    {
        using var application = new TestWorkspace();
        using var workspace = new TestWorkspace();

        var resolved = AgentArtifactStore.ResolveActiveWorkspaceRoot(
            workspace.RootPath,
            application.RootPath);

        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(workspace.RootPath)));
    }

    [Test]
    public void ExpandDisplayArtifacts_MissingFile_UsesMarkdownSafePathSeparators()
    {
        using var workspace = new TestWorkspace();
        const string response = """
            SQUADDASH_ARTIFACT_JSON:
            { "path": ".squad/tmp/agent-artifacts/missing.json", "language": "json" }
            """;

        var expanded = AgentArtifactBlockExpander.ExpandDisplayArtifacts(response, workspace.RootPath);

        Assert.That(expanded, Does.Contain(".squad/tmp/agent-artifacts/missing.json"));
    }

    [Test]
    public void CleanupExpiredArchives_RemovesOldArchivedFiles()
    {
        using var workspace = new TestWorkspace();
        var archivePath = workspace.GetPath(".squad", "archive", "agent-artifacts", "2026-01-01", "old.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        File.WriteAllText(archivePath, "old");
        File.SetLastWriteTimeUtc(archivePath, DateTimeOffset.UtcNow.AddDays(-30).UtcDateTime);

        AgentArtifactStore.CleanupExpiredArchives(
            workspace.RootPath,
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(14));

        Assert.That(File.Exists(archivePath), Is.False);
    }
}

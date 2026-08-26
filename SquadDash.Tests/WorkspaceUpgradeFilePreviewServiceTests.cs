using System.Text.Json;
using global::SquadDash;

namespace SquadDash.Tests;

[TestFixture]
public sealed class WorkspaceUpgradeFilePreviewServiceTests
{
    [Test]
    public async Task CreateAsync_TemplateFile_ReturnsUnifiedDiffAgainstIncomingTemplate()
    {
        using var fixture = new PreviewFixture();
        fixture.WriteWorkspaceFile(@".squad\templates\charter.md", "current charter\n");
        fixture.WriteTemplateFile("charter.md", "incoming charter\n");

        var preview = await WorkspaceUpgradeFilePreviewService.CreateAsync(
            fixture.Candidate,
            fixture.WorkspacePath,
            @"templates\charter.md",
            WorkspaceUpgradeFileCategory.SafeAdditionOrRefresh);

        Assert.Multiple(() =>
        {
            Assert.That(preview.IsAvailable, Is.True);
            Assert.That(preview.IsExactTemplateCopy, Is.True);
            Assert.That(preview.CurrentPath, Is.EqualTo(Path.Combine(fixture.WorkspacePath, ".squad", "templates", "charter.md")));
            Assert.That(preview.DiffText, Does.Contain("-current charter"));
            Assert.That(preview.DiffText, Does.Contain("+incoming charter"));
        });
    }

    [Test]
    public async Task CreateAsync_CoordinatorAgent_MapsOutsideSquadFolderAndMarksTemplateLevelPreview()
    {
        using var fixture = new PreviewFixture(version: "0.13.0");
        fixture.WriteWorkspaceFile(@".github\agents\squad.agent.md", "<!-- version: 0.12.0 -->\ncurrent\n");
        fixture.WriteTemplateFile("squad.agent.md.template", "<!-- version: dev -->\nincoming\n");

        var preview = await WorkspaceUpgradeFilePreviewService.CreateAsync(
            fixture.Candidate,
            fixture.WorkspacePath,
            @".github\agents\squad.agent.md",
            WorkspaceUpgradeFileCategory.TrackedLocalChange);

        Assert.Multiple(() =>
        {
            Assert.That(preview.IsAvailable, Is.True);
            Assert.That(preview.IsExactTemplateCopy, Is.False);
            Assert.That(preview.DiffText, Does.Contain("+<!-- version: 0.13.0 -->"));
            Assert.That(preview.DiffText, Does.Contain("-current"));
            Assert.That(preview.DiffText, Does.Contain("+incoming"));
        });
    }

    [Test]
    public async Task CreateAsync_PathOutsideWorkspace_RejectsPreview()
    {
        using var fixture = new PreviewFixture();

        var preview = await WorkspaceUpgradeFilePreviewService.CreateAsync(
            fixture.Candidate,
            fixture.WorkspacePath,
            @"..\..\outside.txt",
            WorkspaceUpgradeFileCategory.GeneratedFile);

        Assert.Multiple(() =>
        {
            Assert.That(preview.IsAvailable, Is.False);
            Assert.That(preview.UnavailableReason, Does.Contain("outside this workspace"));
        });
    }

    private sealed class PreviewFixture : IDisposable
    {
        private readonly string _rootPath = Path.Combine(
            Path.GetTempPath(),
            "SquadDash.Tests",
            nameof(WorkspaceUpgradeFilePreviewServiceTests),
            Guid.NewGuid().ToString("N"));

        internal PreviewFixture(string version = "0.13.0")
        {
            WorkspacePath = Path.Combine(_rootPath, "workspace");
            PackagePath = Path.Combine(_rootPath, "package");
            Directory.CreateDirectory(Path.Combine(WorkspacePath, ".squad"));
            Directory.CreateDirectory(Path.Combine(PackagePath, "dist"));
            Directory.CreateDirectory(Path.Combine(PackagePath, "templates"));
            File.WriteAllText(
                Path.Combine(PackagePath, "package.json"),
                JsonSerializer.Serialize(new { name = "@bradygaster/squad-cli", version }));

            Candidate = new ResolvedCliCandidate(
                CliSelectionMode.CustomPath,
                CliInstallSource.Custom,
                CliRuntimeType.NodeScript,
                "node",
                Path.Combine(PackagePath, "dist", "cli-entry.js"),
                version,
                IsAvailable: true,
                CliCapabilityStatus.Supported,
                ProbeError: null,
                new CliUpdateInstruction(null, false, null, null));
        }

        internal string WorkspacePath { get; }

        internal string PackagePath { get; }

        internal ResolvedCliCandidate Candidate { get; }

        internal void WriteWorkspaceFile(string relativePath, string content) =>
            WriteFile(Path.Combine(WorkspacePath, relativePath), content);

        internal void WriteTemplateFile(string relativePath, string content) =>
            WriteFile(Path.Combine(PackagePath, "templates", relativePath), content);

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}

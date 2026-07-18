using System.Text.Json.Serialization;
using SquadDash.GuidedTours;

namespace SquadDash.Tests
{
 [TestFixture]
 internal sealed class GuidedTourSaverTests
 {
    [Test]
    public void Save_ChangedContent_WritesTrackedAsset()
    {
        using var workspace = new TestWorkspace();

        GuidedTourSaver.Save(Tours("saved-version"), workspace.RootPath);

        var path = Path.Combine(workspace.RootPath, "SquadDash", "Assets", "guided-tours.json");
        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(GuidedTourSaver.GetPath(workspace.RootPath)));
            Assert.That(File.ReadAllText(path), Does.Contain("saved-version"));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
            Assert.That(Directory.Exists(Path.Combine(workspace.RootPath, ".squad")), Is.False);
        });
    }

    [Test]
    public void Save_UnchangedContent_DoesNotRewriteTrackedAsset()
    {
        using var workspace = new TestWorkspace();
        GuidedTourSaver.Save(Tours("one"), workspace.RootPath);
        GuidedTourSaver.Save(Tours("two"), workspace.RootPath);

        var path = GuidedTourSaver.GetPath(workspace.RootPath);
        var fileTimeBefore = File.GetLastWriteTimeUtc(path);
        Thread.Sleep(20);
        GuidedTourSaver.Save(Tours("two"), workspace.RootPath);
        var fileTimeAfter = File.GetLastWriteTimeUtc(path);
        var recoveryDirectory = GuidedTourSaver.GetRecoveryDirectory(workspace.RootPath);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Does.Contain("two"));
                Assert.That(fileTimeAfter, Is.EqualTo(fileTimeBefore));
                Assert.That(Directory.GetFiles(recoveryDirectory, "*.json"), Has.Length.EqualTo(1));
                Assert.That(File.ReadAllText(Directory.GetFiles(recoveryDirectory, "*.json").Single()), Does.Contain("one"));
            });
        }
        finally
        {
            if (Directory.Exists(recoveryDirectory))
                Directory.Delete(recoveryDirectory, recursive: true);
        }
    }

    [Test]
    public void Load_SourceAssetExists_ReadsSameTrackedFile()
    {
        using var workspace = new TestWorkspace();
        GuidedTourSaver.Save(Tours("single-source"), workspace.RootPath);

        var tours = GuidedTourLoader.Load(workspace.RootPath);

        Assert.That(tours.Single().Steps.Single().Title, Is.EqualTo("single-source"));
    }

    [Test]
    public void Rebind_FreshListContainsSameTourId_ReturnsFreshInstance()
    {
        var detached = new GuidedTour { Id = "prompts", Name = "Old display name" };
        var fresh = new GuidedTour { Id = "prompts", Name = "Prompts and the Queue" };

        var rebound = GuidedTourObjectGraph.Rebind(detached, [fresh]);

        Assert.That(rebound, Is.SameAs(fresh));
    }

    private static List<GuidedTour> Tours(string title) =>
        [new GuidedTour { Name = "Tour", Steps = [new GuidedTourStep { Title = title }] }];
 }
}

// Minimal test-side tour model for the linked saver source.
namespace SquadDash.GuidedTours
{
    internal sealed class GuidedTour
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("steps")] public List<GuidedTourStep> Steps { get; set; } = new();
    }

    internal sealed class GuidedTourStep
    {
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    }
}

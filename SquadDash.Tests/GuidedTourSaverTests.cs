using System.Text.Json.Serialization;
using SquadDash.GuidedTours;

namespace SquadDash.Tests
{
 [TestFixture]
 internal sealed class GuidedTourSaverTests
 {
    [Test]
    public void Save_ChangedContent_RotatesFiveBackupsNewestFirst()
    {
        using var workspace = new TestWorkspace();

        for (var version = 0; version < 7; version++)
            GuidedTourSaver.Save(Tours($"version-{version}"), workspace.RootPath);

        var path = Path.Combine(workspace.RootPath, ".squad", "guided-tours.json");
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(path), Does.Contain("version-6"));
            for (var index = 1; index <= GuidedTourSaver.BackupCount; index++)
                Assert.That(File.ReadAllText($"{path}.bak.{index}"), Does.Contain($"version-{6 - index}"));
            Assert.That(File.Exists($"{path}.bak.6"), Is.False);
        });
    }

    [Test]
    public void Save_UnchangedContent_DoesNotCreateAnotherBackup()
    {
        using var workspace = new TestWorkspace();
        GuidedTourSaver.Save(Tours("one"), workspace.RootPath);
        GuidedTourSaver.Save(Tours("two"), workspace.RootPath);
        GuidedTourSaver.Save(Tours("two"), workspace.RootPath);

        var path = Path.Combine(workspace.RootPath, ".squad", "guided-tours.json");
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText($"{path}.bak.1"), Does.Contain("one"));
            Assert.That(File.Exists($"{path}.bak.2"), Is.False);
        });
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
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("steps")] public List<GuidedTourStep> Steps { get; set; } = new();
    }

    internal sealed class GuidedTourStep
    {
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    }
}

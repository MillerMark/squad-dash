using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class JsonFileStorageStrictWriteTests {
    [Test]
    public void AtomicWrite_CreatesThenReplacesCompleteDocument() {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("document.json");
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "first" });
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "second\nline" });
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.That(document.RootElement.GetProperty("Value").GetString(), Is.EqualTo("second\nline"));
        Assert.That(Directory.GetFiles(workspace.RootPath, "*.tmp"), Is.Empty);
    }

    [Test]
    public void SerializationFailure_PreservesPreviousBytesAndRemovesPartialTemporaryFile() {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("document.json");
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "keep me" });
        var before = File.ReadAllBytes(path);
        var cycle = new List<object>();
        cycle.Add(cycle);
        Assert.That(() => JsonFileStorage.AtomicWritePreservingPrevious(path, cycle), Throws.TypeOf<JsonException>());
        Assert.Multiple(() => {
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(workspace.RootPath, "*.tmp"), Is.Empty);
        });
    }
}

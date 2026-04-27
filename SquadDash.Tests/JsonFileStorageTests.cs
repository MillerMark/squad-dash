using System;
using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class JsonFileStorageTests {
    private string _testPath = null!;

    [SetUp]
    public void SetUp() {
        _testPath = Path.Combine(Path.GetTempPath(), $"JsonFileStorageTests-{Guid.NewGuid()}.json");
    }

    [TearDown]
    public void TearDown() {
        if (File.Exists(_testPath))
            File.Delete(_testPath);

        var tempPath = _testPath + ".tmp";
        if (File.Exists(tempPath))
            File.Delete(tempPath);
        if (Directory.Exists(tempPath))
            Directory.Delete(tempPath, recursive: true);
    }

    [Test]
    public void AtomicWrite_NewFile_CreatesFileWithNoLeftoverTemp() {
        var payload = new TestPayload("Test", 42);

        JsonFileStorage.AtomicWrite(_testPath, payload);

        Assert.Multiple(() => {
            Assert.That(File.Exists(_testPath), Is.True, "Target file should exist");
            Assert.That(File.Exists(_testPath + ".tmp"), Is.False, "Temp file should not remain");
        });
    }

    [Test]
    public void AtomicWrite_ExistingFile_OverwritesWithNoLeftoverTemp() {
        JsonFileStorage.AtomicWrite(_testPath, new TestPayload("Original", 1));

        JsonFileStorage.AtomicWrite(_testPath, new TestPayload("Updated", 2));

        Assert.Multiple(() => {
            Assert.That(File.Exists(_testPath), Is.True, "Target file should exist");
            Assert.That(File.Exists(_testPath + ".tmp"), Is.False, "Temp file should not remain");
            Assert.That(File.ReadAllText(_testPath), Does.Contain("Updated"),
                "File should contain the updated value");
            Assert.That(File.ReadAllText(_testPath), Does.Not.Contain("Original"),
                "File should not contain the original value");
        });
    }

    [Test]
    public void AtomicWrite_RoundTrip_DeserializesBackToOriginal() {
        var payload = new TestPayload("Hello, World!", 99);

        JsonFileStorage.AtomicWrite(_testPath, payload);

        var json = File.ReadAllText(_testPath);
        var deserialized = JsonSerializer.Deserialize<TestPayload>(json);

        Assert.Multiple(() => {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Label, Is.EqualTo(payload.Label));
            Assert.That(deserialized.Count, Is.EqualTo(payload.Count));
        });
    }

    [Test]
    public void AtomicWrite_ExceptionBeforeRename_LeavesOriginalUntouched() {
        // Write a known original so the file exists
        var original = new TestPayload("Original", 1);
        JsonFileStorage.AtomicWrite(_testPath, original);
        var originalContent = File.ReadAllText(_testPath);

        // Block the temp path by creating a directory there — WriteAllText will throw
        // (IOException on Linux/macOS, UnauthorizedAccessException on Windows)
        var tempPath = _testPath + ".tmp";
        Directory.CreateDirectory(tempPath);

        // The write should fail because the temp path is now a directory
        bool threw = false;
        try {
            JsonFileStorage.AtomicWrite(_testPath, new TestPayload("New", 2));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            threw = true;
        }
        Assert.That(threw, Is.True, "Expected an IOException or UnauthorizedAccessException");

        // Original file must be untouched
        Assert.That(File.ReadAllText(_testPath), Is.EqualTo(originalContent));
    }

    private sealed record TestPayload(string Label, int Count);
}

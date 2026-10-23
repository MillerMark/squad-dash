using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ButtonsStoreTests {
    private static readonly JsonFileCommitRetrySettings FastCommitRetries = new(2, _ => 0);
    private TestWorkspace _workspace = null!;
    private ButtonsStore _store = null!;
    private string _stateDirectory = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _stateDirectory = SquadDashPaths.WorkspaceStateDirectory(_workspace.RootPath);
        _store = new ButtonsStore(_stateDirectory, FastCommitRetries);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_stateDirectory))
            Directory.Delete(_stateDirectory, recursive: true);
        _workspace.Dispose();
    }

    [Test]
    public void Load_MissingStorage_IsEmptyWithoutCreatingFile() {
        Assert.That(_store.Load(), Is.Empty);
        Assert.That(File.Exists(_store.FilePath), Is.False);
    }

    [TestCase(ButtonCommandType.PowerShell, null)]
    [TestCase(ButtonCommandType.RawCommand, @"tools\release")]
    [TestCase(ButtonCommandType.Url, null)]
    public void Create_Reload_RoundTripsEveryField(ButtonCommandType type, string? directory) {
        var content = new ButtonContent("**Deploy** app", "Hover \"tip\"\nsecond line",
            "$message = 'caf\u00e9 \"quoted\"'\r\nWrite-Output $message\n{ branch braces }", type, directory);
        var created = _store.Create(content);
        var loaded = new ButtonsStore(_stateDirectory).Load().Single();

        Assert.Multiple(() => {
            Assert.That(created.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(created.SortOrder, Is.Zero);
            Assert.That(created.ContentRevision, Is.EqualTo(1));
            Assert.That(loaded, Is.EqualTo(created));
            Assert.That(loaded.Content, Is.EqualTo(content));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Create_Reload_RoundTripsRunAsAdministrator(bool runAsAdministrator) {
        var content = new ButtonContent("Restart service", "tip", "restart-service audiosrv",
            ButtonCommandType.PowerShell, WorkingDirectory: null, RunAsAdministrator: runAsAdministrator);
        _store.Create(content);
        var loaded = new ButtonsStore(_stateDirectory).Load().Single();

        Assert.That(loaded.Content.RunAsAdministrator, Is.EqualTo(runAsAdministrator));
    }

    [Test]
    public void Load_LegacyJsonMissingRunAsAdministrator_DefaultsToFalse() {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        var payload = $$"""
                        {
                          "SchemaVersion": 1,
                          "Items": [
                            {
                              "Id": "{{id}}",
                              "SortOrder": 0,
                              "ContentRevision": 1,
                              "Content": {
                                "Caption": "Test",
                                "Tooltip": "tip",
                                "CommandText": "echo hello",
                                "CommandType": 0,
                                "WorkingDirectory": null
                              }
                            }
                          ]
                        }
                        """;
        File.WriteAllText(_store.FilePath, payload);

        var loaded = _store.Load().Single();

        Assert.That(loaded.Content.RunAsAdministrator, Is.False);
    }

    [Test]
    public void DifferentWorkspaceDirectories_DoNotShareButtons() {
        using var otherWorkspace = new TestWorkspace();
        var otherDirectory = SquadDashPaths.WorkspaceStateDirectory(otherWorkspace.RootPath);
        try {
            var other = new ButtonsStore(otherDirectory);
            var first = _store.Create(Content("First"));
            Assert.That(other.Load(), Is.Empty);
            var second = other.Create(Content("Second"));
            _store.Update(first.Id, first.ContentRevision, Content("Changed"));
            Assert.Multiple(() => {
                Assert.That(other.Load(), Is.EqualTo(new[] { second }));
                Assert.That(_store.Load().Single().Content.Caption, Is.EqualTo("Changed"));
                Assert.That(_store.FilePath, Is.Not.EqualTo(other.FilePath));
            });
        }
        finally {
            if (Directory.Exists(otherDirectory))
                Directory.Delete(otherDirectory, recursive: true);
        }
    }

    [Test]
    public void CrudAndReorder_PreserveConcurrentContentAndCompactSortOrder() {
        var a = _store.Create(Content("A"));
        var b = _store.Create(Content("B"));
        var c = _store.Create(Content("C"));
        var expected = new[] { a.Id, b.Id, c.Id };
        var edited = new ButtonsStore(_stateDirectory).Update(b.Id, 1, Content("B edited"));
        var ordered = _store.Reorder(expected, [c.Id, a.Id, b.Id]);
        Assert.Multiple(() => {
            Assert.That(ordered.Select(item => item.SortOrder), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(ordered[2].Content, Is.EqualTo(edited.Content));
            Assert.That(ordered.Select(item => item.ContentRevision), Is.EqualTo(new long[] { 1, 1, 2 }));
        });

        var updatedAfterReorder = _store.Update(c.Id, c.ContentRevision, Content("C edited"));
        Assert.That(updatedAfterReorder.SortOrder, Is.Zero);
        _store.Delete(a.Id, a.ContentRevision);
        var d = _store.Create(Content("D"));
        var reloaded = new ButtonsStore(_stateDirectory).Load();
        Assert.Multiple(() => {
            Assert.That(reloaded.Select(item => item.Id), Is.EqualTo(new[] { c.Id, b.Id, d.Id }));
            Assert.That(reloaded.Select(item => item.SortOrder), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(reloaded[0].ContentRevision, Is.EqualTo(2));
        });
    }

    [Test]
    public void StaleEditAndDelete_ThrowWithoutOverwritingNewerContent() {
        var original = _store.Create(Content("Original"));
        var edited = new ButtonsStore(_stateDirectory).Update(original.Id, 1, Content("Newer"));
        var before = File.ReadAllBytes(_store.FilePath);
        Assert.Multiple(() => {
            Assert.That(() => _store.Update(original.Id, 1, Content("Stale")), Throws.TypeOf<ButtonConflictException>());
            Assert.That(() => _store.Delete(original.Id, 1), Throws.TypeOf<ButtonConflictException>());
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
            Assert.That(_store.Load().Single(), Is.EqualTo(edited));
        });
        _store.Delete(edited.Id, edited.ContentRevision);
        Assert.That(() => _store.Update(original.Id, 2, Content("Resurrect")), Throws.TypeOf<ButtonConflictException>());
        Assert.That(_store.Load(), Is.Empty);
    }

    [TestCase("create")]
    [TestCase("delete")]
    [TestCase("reorder")]
    public void Reorder_StaleMembershipOrOrder_RejectsWithoutPartialSave(string competingChange) {
        var a = _store.Create(Content("A"));
        var b = _store.Create(Content("B"));
        var old = new[] { a.Id, b.Id };
        var other = new ButtonsStore(_stateDirectory);
        switch (competingChange) {
            case "create": other.Create(Content("C")); break;
            case "delete": other.Delete(b.Id, 1); break;
            case "reorder": other.Reorder(old, [b.Id, a.Id]); break;
        }
        var before = File.ReadAllBytes(_store.FilePath);
        Assert.That(() => _store.Reorder(old, [b.Id, a.Id]), Throws.TypeOf<ButtonConflictException>());
        Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
    }

    [TestCase("duplicate")]
    [TestCase("missing")]
    [TestCase("unknown")]
    public void Reorder_InvalidIdSet_DoesNotModifyStorage(string scenario) {
        var a = _store.Create(Content("A"));
        var b = _store.Create(Content("B"));
        Guid[] invalid = scenario switch {
            "duplicate" => [a.Id, a.Id],
            "missing" => [a.Id],
            _ => [a.Id, Guid.NewGuid()]
        };
        var before = File.ReadAllBytes(_store.FilePath);
        Assert.That(() => _store.Reorder([a.Id, b.Id], invalid), Throws.TypeOf<ArgumentException>());
        Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
    }

    [TestCase("{ not json")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"SchemaVersion\":2,\"Items\":[]}")]
    [TestCase("{\"SchemaVersion\":1,\"Items\":null}")]
    public void CorruptDocument_LoadAndEveryMutationReportErrorWithoutReset(string json) {
        var original = _store.Create(Content("Original"));
        File.WriteAllText(_store.FilePath, json);
        var before = File.ReadAllBytes(_store.FilePath);
        TestDelegate[] actions = [
            () => _store.Load(),
            () => _store.Create(Content("New")),
            () => _store.Update(original.Id, 1, Content("Edit")),
            () => _store.Delete(original.Id, 1),
            () => _store.Reorder([original.Id], [original.Id])
        ];
        foreach (var action in actions) {
            Assert.That(action, Throws.Exception.TypeOf<JsonException>().Or.TypeOf<InvalidDataException>());
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
        }
    }

    [TestCase("duplicate-id")]
    [TestCase("empty-id")]
    [TestCase("revision")]
    [TestCase("sort-order")]
    [TestCase("content")]
    public void InvalidPersistedItems_AreReportedWithoutRepairingFile(string scenario) {
        var item = _store.Create(Content("Original"));
        var invalid = scenario switch {
            "empty-id" => item with { Id = Guid.Empty },
            "revision" => item with { ContentRevision = 0 },
            "sort-order" => item with { SortOrder = 2 },
            "content" => item with { Content = Content("") },
            _ => item
        };
        ButtonDefinition[] items = scenario == "duplicate-id" ? [item, item] : [invalid];
        File.WriteAllText(_store.FilePath, JsonSerializer.Serialize(new { SchemaVersion = 1, Items = items }));
        var before = File.ReadAllBytes(_store.FilePath);
        Assert.That(() => _store.Load(), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => _store.Create(Content("New")), Throws.TypeOf<InvalidDataException>());
        Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
    }

    [TestCase("PowerShell", ButtonCommandType.PowerShell)]
    [TestCase("RawCommand", ButtonCommandType.RawCommand)]
    [TestCase("raw-cmd", ButtonCommandType.RawCommand)]
    [TestCase("URL", ButtonCommandType.Url)]
    [TestCase(0, ButtonCommandType.PowerShell)]
    [TestCase(1, ButtonCommandType.RawCommand)]
    [TestCase(2, ButtonCommandType.Url)]
    public void Load_AcceptsLegacyStringAndNumericCommandTypes(object persistedType, ButtonCommandType expectedType) {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        var payload = $$"""
                        {
                          "SchemaVersion": 1,
                          "Items": [
                            {
                              "Id": "{{id}}",
                              "SortOrder": 0,
                              "ContentRevision": 1,
                              "Content": {
                                "Caption": "Test",
                                "Tooltip": "tip",
                                "CommandText": "echo hello",
                                "CommandType": {{JsonSerializer.Serialize(persistedType)}},
                                "WorkingDirectory": null
                              }
                            }
                          ]
                        }
                        """;
        File.WriteAllText(_store.FilePath, payload);

        var loaded = _store.Load().Single();

        Assert.That(loaded.Content.CommandType, Is.EqualTo(expectedType));
    }

    [TestCase("create")]
    [TestCase("update")]
    [TestCase("delete")]
    [TestCase("reorder")]
    public void FailedReplace_PreservesPriorBytesAndLeavesNoTemporaryFile(string operation) {
        var a = _store.Create(Content("A"));
        var b = _store.Create(Content("B"));
        var before = File.ReadAllBytes(_store.FilePath);
        // Allow the store's read, but deny File.Replace's delete access.
        using (var held = new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            TestDelegate action = operation switch {
                "create" => () => _store.Create(Content("C")),
                "update" => () => _store.Update(a.Id, 1, Content("Changed")),
                "delete" => () => _store.Delete(a.Id, 1),
                _ => () => _store.Reorder([a.Id, b.Id], [b.Id, a.Id])
            };
            Assert.That(action, Throws.InstanceOf<IOException>());
        }
        Assert.Multiple(() => {
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
            Assert.That(_store.Load(), Is.EqualTo(new[] { a, b }));
            Assert.That(Directory.GetFiles(_stateDirectory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public async Task ConcurrentStoreInstances_CreateWithoutLostUpdates() {
        var created = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            Task.Run(() => new ButtonsStore(_stateDirectory).Create(Content($"Button {index}")))));
        var loaded = _store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded.Select(item => item.Id), Is.EquivalentTo(created.Select(item => item.Id)));
            Assert.That(loaded.Select(item => item.SortOrder), Is.EqualTo(Enumerable.Range(0, 12)));
        });
    }

    [Test]
    public void StateDirectory_RejectsRepositoryStorage() {
        Assert.That(() => new ButtonsStore(_workspace.RootPath), Throws.TypeOf<ArgumentException>());
        Assert.That(File.Exists(_workspace.GetPath("buttons.json")), Is.False);
    }

    private static ButtonContent Content(string caption) =>
        new(caption, "tip", "Write-Output 'test only; never executed'", ButtonCommandType.PowerShell);
}

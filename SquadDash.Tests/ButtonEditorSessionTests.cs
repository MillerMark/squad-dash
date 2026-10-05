using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ButtonEditorSessionTests {
    private TestWorkspace _workspace = null!;
    private ButtonsStore _store = null!;
    private string _stateDirectory = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _stateDirectory = SquadDashPaths.WorkspaceStateDirectory(_workspace.RootPath);
        _store = new ButtonsStore(_stateDirectory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_stateDirectory))
            Directory.Delete(_stateDirectory, recursive: true);
        _workspace.Dispose();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Save_ChangedWorkspaceOrInvalidatedEditor_DoesNotPersist(bool invalidate) {
        var current = true;
        var session = new ButtonEditorSession(_store, null, 0, () => current);
        if (invalidate) session.Invalidate();
        else current = false;
        Assert.That(() => session.Save(Content("New"), 0), Throws.TypeOf<ButtonConflictException>());
        Assert.That(session.SavedDefinition, Is.Null);
        Assert.That(_store.Load(), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Save_ConcurrentEditOrDeletion_RejectsStaleEditor(bool delete) {
        var item = _store.Create(Content("Original"));
        _store.Create(Content("Keep sort position valid"));
        var session = new ButtonEditorSession(_store, item, 0, () => true);
        if (delete) _store.Delete(item.Id, 1);
        else _store.Update(item.Id, 1, Content("Other editor"));
        var before = File.ReadAllBytes(_store.FilePath);
        Assert.That(() => session.Save(Content("Stale"), 0), Throws.TypeOf<ButtonConflictException>());
        Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
        Assert.That(session.SavedDefinition, Is.EqualTo(item));
    }

    [Test]
    public void Save_CreateAtRequestedPositionThenEdit_RetainsIdentityAndNewRevision() {
        var a = _store.Create(Content("A"));
        var session = new ButtonEditorSession(_store, null, 1, () => true);
        var created = session.Save(Content("B"), 0);
        var edited = session.Save(Content("B edited"), 0);
        Assert.Multiple(() => {
            Assert.That(created.Id, Is.EqualTo(edited.Id));
            Assert.That(edited.ContentRevision, Is.EqualTo(2));
            Assert.That(session.SavedDefinition, Is.EqualTo(edited));
            Assert.That(session.InitialSortOrder, Is.Zero);
            Assert.That(_store.Load().Select(item => item.Id), Is.EqualTo(new[] { edited.Id, a.Id }));
        });
    }

    [Test]
    public void Save_AfterConcurrentReorder_UnchangedEditorOrderDoesNotUndoDrag() {
        var a = _store.Create(Content("A"));
        var b = _store.Create(Content("B"));
        var session = new ButtonEditorSession(_store, a, 0, () => true);
        _store.Reorder([a.Id, b.Id], [b.Id, a.Id]);
        var edited = session.Save(Content("A edited"), 0);
        Assert.That(edited.SortOrder, Is.EqualTo(1));
        Assert.That(_store.Load().Select(item => item.Id), Is.EqualTo(new[] { b.Id, a.Id }));
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void Save_InvalidNewSortOrder_DoesNotCreateButton(int order) {
        var session = new ButtonEditorSession(_store, null, 0, () => true);
        Assert.That(() => session.Save(Content("New"), order), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(_store.Load(), Is.Empty);
    }

    [Test]
    public void SaveWithoutOrder_AppendsToCurrentListAndPreservesConcurrentReorder() {
        var a = _store.Create(Content("A"));
        var session = new ButtonEditorSession(_store, null, 1, () => true);
        var b = _store.Create(Content("B"));
        var created = session.Save(Content("C"));
        _store.Reorder([a.Id, b.Id, created.Id], [created.Id, b.Id, a.Id]);
        var edited = session.Save(Content("C edited"));
        Assert.Multiple(() => {
            Assert.That(created.SortOrder, Is.EqualTo(2));
            Assert.That(edited.Id, Is.EqualTo(created.Id));
            Assert.That(edited.ContentRevision, Is.EqualTo(2));
            Assert.That(edited.SortOrder, Is.Zero);
            Assert.That(_store.Load().Select(item => item.Id), Is.EqualTo(new[] { created.Id, b.Id, a.Id }));
        });
    }

    [Test]
    public void SaveWithoutOrder_ConcurrentDeletionDoesNotInvalidateRemainingEditorsSortPosition() {
        var a = _store.Create(Content("A"));
        var b = _store.Create(Content("B"));
        var session = new ButtonEditorSession(_store, b, 1, () => true);
        _store.Delete(a.Id, a.ContentRevision);
        Assert.That(session.Save(Content("B edited")).SortOrder, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SaveWithoutOrder_StaleWorkspaceOrClosedEditorIsRejected(bool close) {
        var current = true;
        var session = new ButtonEditorSession(_store, null, 0, () => current);
        if (close) session.Invalidate();
        else current = false;
        Assert.That(() => session.Save(Content("No save")), Throws.TypeOf<ButtonConflictException>());
        Assert.That(_store.Load(), Is.Empty);
    }

    [Test]
    public void SaveWithoutOrder_ConcurrentContentEditIsRejected() {
        var original = _store.Create(Content("Original"));
        var session = new ButtonEditorSession(_store, original, 0, () => true);
        _store.Update(original.Id, original.ContentRevision, Content("Other editor"));
        Assert.That(() => session.Save(Content("Stale")), Throws.TypeOf<ButtonConflictException>());
    }

    [Test]
    public void ValidateFields_ReportsErrorsAgainstTheirControls() {
        var errors = ButtonEditorSession.ValidateFields(new(" ", "", "", ButtonCommandType.PowerShell));
        Assert.That(errors.Keys, Is.EquivalentTo(new[] { nameof(ButtonContent.Caption), nameof(ButtonContent.CommandText) }));
    }

    [TestCase("https://example.com/{branch}?name={workspaceName}", true)]
    [TestCase("https://{workspaceName}.example.com/", true)]
    [TestCase("not a URL", false)]
    [TestCase("file:///C:/Windows/notepad.exe", false)]
    [TestCase("", false)]
    public void ValidateFields_UrlErrorsBelongToCommandControl(string url, bool valid) {
        var errors = ButtonEditorSession.ValidateFields(new("Open", "", url, ButtonCommandType.Url));
        Assert.That(errors.ContainsKey(nameof(ButtonContent.CommandText)), Is.EqualTo(!valid));
    }

    [TestCase(ButtonCommandType.PowerShell)]
    [TestCase(ButtonCommandType.RawCommand)]
    [TestCase(ButtonCommandType.Url)]
    public void Save_CommandType_RoundTripsIntoReopenedEditor(ButtonCommandType type) {
        var session = new ButtonEditorSession(_store, null, 0, () => true);
        var content = new ButtonContent("Open", "", "https://example.com/", type);
        var created = session.Save(content);
        var loaded = _store.Load().Single();
        var reopened = new ButtonEditorSession(_store, loaded, loaded.SortOrder, () => true);
        Assert.That(reopened.SavedDefinition!.Content.CommandType, Is.EqualTo(type));

        var nextType = (ButtonCommandType)(((int)type + 1) % 3);
        reopened.Save(content with { CommandType = nextType });
        Assert.Multiple(() => {
            Assert.That(_store.Load().Single().Id, Is.EqualTo(created.Id));
            Assert.That(_store.Load().Single().Content.CommandType, Is.EqualTo(nextType));
        });
    }

    private static ButtonContent Content(string caption) =>
        new(caption, "", "echo test", ButtonCommandType.RawCommand);
}

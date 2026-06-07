using System;
using System.IO;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ClipboardEditorStateStoreTests {

    // ------------------------------------------------------------------
    // 1. Save and load round-trip — all fields
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsAllFields() {
        using var workspace = new TestWorkspace();
        var store = new ClipboardEditorStateStore(workspace.RootPath);
        var editorId = "test-editor-001";

        var original = new ClipboardEditorSessionState {
            Version         = 1,
            EditorId        = editorId,
            SessionId       = Guid.NewGuid().ToString(),
            WorkspaceFolder = @"C:\projects\my-workspace",
            WindowGeometry  = new WindowGeometryState { X = 100, Y = 200, Width = 800, Height = 600, IsMaximized = false },
            AttachedPrompt  = new AttachedPromptInfo  { Type = "draft", DraftId = "active-draft", PromptText = "Hello world" },
            ToolState       = new ToolStateInfo       { SelectedTool = "arrow", ZoomLevel = 1.5 },
            AnnotationState = new ClipboardAnnotationState {
                Version      = 2,
                CanvasScaleX = 1.0,
                CanvasScaleY = 1.0,
                HasCrop      = true,
                CropX        = 10.5,
                CropY        = 20.5,
                CropW        = 500.0,
                CropH        = 300.0,
                CursorEnabled = false,
            },
            ImageMetadata   = new ImageMetadataInfo   {
                SourceImagePath    = @"C:\temp\clipboard-source.png",
                OriginalImageHash  = "sha256:abcd1234",
                OriginalImageWidth  = 1920,
                OriginalImageHeight = 1080,
            },
            SavedAt    = DateTimeOffset.UtcNow.ToString("O"),
            AppVersion = "1.0.0.1234",
        };

        await store.SaveAsync(original, "round-trip-test");
        var loaded = await store.LoadAsync(editorId);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(loaded!.Version,         Is.EqualTo(original.Version));
            Assert.That(loaded.EditorId,         Is.EqualTo(original.EditorId));
            Assert.That(loaded.SessionId,        Is.EqualTo(original.SessionId));
            Assert.That(loaded.WorkspaceFolder,  Is.EqualTo(original.WorkspaceFolder));
            Assert.That(loaded.AppVersion,       Is.EqualTo(original.AppVersion));

            // WindowGeometry
            Assert.That(loaded.WindowGeometry,             Is.Not.Null);
            Assert.That(loaded.WindowGeometry!.X,          Is.EqualTo(original.WindowGeometry!.X));
            Assert.That(loaded.WindowGeometry.Y,           Is.EqualTo(original.WindowGeometry.Y));
            Assert.That(loaded.WindowGeometry.Width,       Is.EqualTo(original.WindowGeometry.Width));
            Assert.That(loaded.WindowGeometry.Height,      Is.EqualTo(original.WindowGeometry.Height));
            Assert.That(loaded.WindowGeometry.IsMaximized, Is.EqualTo(original.WindowGeometry.IsMaximized));

            // AttachedPrompt
            Assert.That(loaded.AttachedPrompt,            Is.Not.Null);
            Assert.That(loaded.AttachedPrompt!.Type,       Is.EqualTo(original.AttachedPrompt!.Type));
            Assert.That(loaded.AttachedPrompt.DraftId,     Is.EqualTo(original.AttachedPrompt.DraftId));
            Assert.That(loaded.AttachedPrompt.PromptText,  Is.EqualTo(original.AttachedPrompt.PromptText));

            // ToolState
            Assert.That(loaded.ToolState,                 Is.Not.Null);
            Assert.That(loaded.ToolState!.SelectedTool,    Is.EqualTo(original.ToolState!.SelectedTool));
            Assert.That(loaded.ToolState.ZoomLevel,        Is.EqualTo(original.ToolState.ZoomLevel));

            // AnnotationState
            Assert.That(loaded.AnnotationState,           Is.Not.Null);
            Assert.That(loaded.AnnotationState!.Version,   Is.EqualTo(original.AnnotationState!.Version));
            Assert.That(loaded.AnnotationState.HasCrop,    Is.EqualTo(original.AnnotationState.HasCrop));
            Assert.That(loaded.AnnotationState.CropX,      Is.EqualTo(original.AnnotationState.CropX));
            Assert.That(loaded.AnnotationState.CropW,      Is.EqualTo(original.AnnotationState.CropW));

            // ImageMetadata
            Assert.That(loaded.ImageMetadata,                      Is.Not.Null);
            Assert.That(loaded.ImageMetadata!.SourceImagePath,     Is.EqualTo(original.ImageMetadata!.SourceImagePath));
            Assert.That(loaded.ImageMetadata.OriginalImageHash,    Is.EqualTo(original.ImageMetadata.OriginalImageHash));
            Assert.That(loaded.ImageMetadata.OriginalImageWidth,   Is.EqualTo(original.ImageMetadata.OriginalImageWidth));
            Assert.That(loaded.ImageMetadata.OriginalImageHeight,  Is.EqualTo(original.ImageMetadata.OriginalImageHeight));
        });
    }

    // ------------------------------------------------------------------
    // 2. Minimal state — only required fields
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveAsync_ThenLoadAsync_MinimalState_Succeeds() {
        using var workspace = new TestWorkspace();
        var store    = new ClipboardEditorStateStore(workspace.RootPath);
        var editorId = "minimal-editor";

        var minimal = new ClipboardEditorSessionState {
            Version  = 1,
            EditorId = editorId,
        };

        await store.SaveAsync(minimal, "minimal-test");
        var loaded = await store.LoadAsync(editorId);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(loaded!.Version,  Is.EqualTo(1));
            Assert.That(loaded.EditorId, Is.EqualTo(editorId));
            Assert.That(loaded.ToolState,       Is.Null);
            Assert.That(loaded.WindowGeometry,  Is.Null);
            Assert.That(loaded.AnnotationState, Is.Null);
            Assert.That(loaded.ImageMetadata,   Is.Null);
        });
    }

    // ------------------------------------------------------------------
    // 3. File not found → returns null, no throw
    // ------------------------------------------------------------------

    [Test]
    public async Task LoadAsync_FileNotFound_ReturnsNull() {
        using var workspace = new TestWorkspace();
        var store = new ClipboardEditorStateStore(workspace.RootPath);

        var result = await store.LoadAsync("nonexistent-editor-id");

        Assert.That(result, Is.Null);
    }

    // ------------------------------------------------------------------
    // 4. Invalid JSON → returns null, no throw
    // ------------------------------------------------------------------

    [Test]
    public async Task LoadAsync_InvalidJson_ReturnsNull() {
        using var workspace = new TestWorkspace();
        var editorId   = "corrupt-editor";
        var activePath = Path.Combine(workspace.RootPath, $"clipboard-editor-{editorId}-active.json");

        File.WriteAllText(activePath, "{ this is not valid json !! }");

        var store  = new ClipboardEditorStateStore(workspace.RootPath);
        var result = await store.LoadAsync(editorId);

        Assert.That(result, Is.Null);
    }

    // ------------------------------------------------------------------
    // 5. Multiple editors — different editorIds do not collide
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveAsync_MultipleEditors_DoNotCollide() {
        using var workspace = new TestWorkspace();
        var store = new ClipboardEditorStateStore(workspace.RootPath);

        var stateA = new ClipboardEditorSessionState {
            Version  = 1,
            EditorId = "editor-alpha",
            WorkspaceFolder = @"C:\alpha",
        };
        var stateB = new ClipboardEditorSessionState {
            Version  = 1,
            EditorId = "editor-beta",
            WorkspaceFolder = @"C:\beta",
        };

        await store.SaveAsync(stateA, "test");
        await store.SaveAsync(stateB, "test");

        var loadedA = await store.LoadAsync("editor-alpha");
        var loadedB = await store.LoadAsync("editor-beta");

        Assert.Multiple(() => {
            Assert.That(loadedA, Is.Not.Null);
            Assert.That(loadedB, Is.Not.Null);
            Assert.That(loadedA!.WorkspaceFolder, Is.EqualTo(@"C:\alpha"));
            Assert.That(loadedB!.WorkspaceFolder, Is.EqualTo(@"C:\beta"));
        });
    }

    // ------------------------------------------------------------------
    // 6. .pending → .active rename on save success
    // ------------------------------------------------------------------

    [Test]
    public async Task SaveAsync_ActiveFileExists_PendingFileDoesNot() {
        using var workspace = new TestWorkspace();
        var store    = new ClipboardEditorStateStore(workspace.RootPath);
        var editorId = "rename-test-editor";

        var state = new ClipboardEditorSessionState {
            Version  = 1,
            EditorId = editorId,
        };

        await store.SaveAsync(state, "rename-test");

        var activePath  = store.GetStateFilePath(editorId, isPending: false);
        var pendingPath = store.GetStateFilePath(editorId, isPending: true);

        Assert.Multiple(() => {
            Assert.That(File.Exists(activePath),  Is.True,  ".active file should exist after save");
            Assert.That(File.Exists(pendingPath), Is.False, ".pending file should not exist after successful rename");
        });
    }

    // ------------------------------------------------------------------
    // 7. Stale file cleanup
    // ------------------------------------------------------------------

    [Test]
    public async Task CleanupStaleFilesAsync_OldPendingFiles_AreDeleted() {
        using var workspace = new TestWorkspace();
        var store = new ClipboardEditorStateStore(workspace.RootPath);

        // Create a .pending file that is 8 days old
        var staleEditorId  = "stale-editor-cleanup";
        var stalePath      = store.GetStateFilePath(staleEditorId, isPending: true);
        File.WriteAllText(stalePath, "{}");
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddDays(-8));

        // Create a fresh .pending file that is only 1 hour old
        var freshEditorId = "fresh-editor-cleanup";
        var freshPath     = store.GetStateFilePath(freshEditorId, isPending: true);
        File.WriteAllText(freshPath, "{}");
        File.SetLastWriteTimeUtc(freshPath, DateTime.UtcNow.AddHours(-1));

        await store.CleanupStaleFilesAsync(maxAgeHours: 168); // 7 days

        Assert.Multiple(() => {
            Assert.That(File.Exists(stalePath), Is.False, "8-day-old pending file should be deleted");
            Assert.That(File.Exists(freshPath), Is.True,  "1-hour-old pending file should be kept");
        });
    }

    [Test]
    public async Task CleanupStaleFilesAsync_ActiveFiles_AreNotDeleted() {
        using var workspace = new TestWorkspace();
        var store    = new ClipboardEditorStateStore(workspace.RootPath);
        var editorId = "stale-active-editor";

        // Create a .active file that is 8 days old — cleanup only targets .pending
        var activePath = store.GetStateFilePath(editorId, isPending: false);
        File.WriteAllText(activePath, "{}");
        File.SetLastWriteTimeUtc(activePath, DateTime.UtcNow.AddDays(-8));

        await store.CleanupStaleFilesAsync(maxAgeHours: 168);

        Assert.That(File.Exists(activePath), Is.True,
            ".active files should NOT be cleaned up by stale cleanup");
    }

    // ------------------------------------------------------------------
    // 8. Delete method
    // ------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_AfterSave_FileIsGone() {
        using var workspace = new TestWorkspace();
        var store    = new ClipboardEditorStateStore(workspace.RootPath);
        var editorId = "delete-test-editor";

        var state = new ClipboardEditorSessionState { Version = 1, EditorId = editorId };
        await store.SaveAsync(state, "pre-delete");

        var activePath = store.GetStateFilePath(editorId, isPending: false);
        Assert.That(File.Exists(activePath), Is.True, "file should exist before delete");

        await store.DeleteAsync(editorId, isPending: false);

        Assert.That(File.Exists(activePath), Is.False, "file should be gone after delete");
    }

    [Test]
    public async Task DeleteAsync_MissingFile_DoesNotThrow() {
        using var workspace = new TestWorkspace();
        var store = new ClipboardEditorStateStore(workspace.RootPath);

        Assert.DoesNotThrowAsync(async () =>
            await store.DeleteAsync("nonexistent-editor", isPending: false));
    }
}

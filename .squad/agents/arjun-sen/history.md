# Arjun Sen — History & Learnings

## Core Context

**Project:** SquadUI — WPF dashboard for Squad CLI AI agent management  
**Stack:** C# / WPF / .NET 10, NUnit 4.4+, TypeScript SDK  
**Key paths:**
- `SquadDash/` — main application (backend services, stores)
- `SquadDash.Tests/` — NUnit test suite
- `.squad/decisions.md` — architectural decision log

---

## Learnings

📌 Team update (2026-04-18T17-38): Arjun Sen owns three delegated tasks from Orion Vale's audit — decided by Orion Vale

**Task 1 — Implement JsonFileStorage atomic-write helper:**  
5 store classes (`ApplicationSettingsStore`, `PromptHistoryStore`, `RestartCoordinatorStateStore`, `RuntimeSlotStateStore`, `WorkspaceConversationStore`) each duplicate a ~10-line atomic temp-file write pattern. Create `SquadDash\JsonFileStorage.cs` with `JsonFileStorage.AtomicWrite<T>(string path, T payload, JsonSerializerOptions? options = null)` and replace all 5 duplicates. Tests: `JsonFileStorageTests.cs` — new file (Move path), existing file (Copy/Delete path), partial-write safety, round-trip. Acceptance: all 5 stores delegate to `JsonFileStorage.AtomicWrite`; 379 original + new tests passing.

**Task 2 — Seal `AgentThreadRegistry` collections (DEL-2):**  
Replace `internal Dictionary<string, TranscriptThreadState> ThreadsByKey` (and other three exposed collections) with read-only facade accessors (`IReadOnlyDictionary`, `IReadOnlyList`). All mutation must go through `AgentThreadRegistry` methods. Add `ContainsThread`, `TryGetByToolCallId`, `AllThreads` accessors as needed. Callers: MainWindow (6 sites), BackgroundTaskPresenter (3 sites), TranscriptConversationManager (4 sites).

**Task 3 — Encapsulate `_toolEntries` in `AgentThreadRegistry` (DEL-4, bundle with DEL-2):**  
Move `_toolEntries: Dictionary<string, ToolTranscriptEntry>` from MainWindow into `AgentThreadRegistry`. Expose typed accessors: `GetOrAddToolEntry`, `TryGetToolEntry`, `AllIncompleteToolEntries`, `ClearAll`. Removes 8 raw-dict mutations from MainWindow.

**Task 4 — Wire IWorkspacePaths (backend files):**  
Replace all `WorkspacePaths.*` static calls in backend services (`SquadCliAdapter`, `SquadSdkProcess`, `SquadDashRuntimeStamp`, `PromptExecutionController`) with constructor-injected `IWorkspacePaths`. Coordinate with Lyra Morn (UI files) and Jae Min Kade (launcher). Delete `WorkspacePaths.cs` only after all call sites are migrated. Tests: `WorkspacePathsProviderTests.cs` — `Discover()`, constructor normalisation, all 4 properties non-empty, empty-string rejection.

**Task 5 — P2 Fixture Loaders: ✅ COMPLETE (commit `b3a6f88`, 2026-04-25)**  
Delivered `BackgroundTaskFixtureLoader` (domain `"backgroundTask"`) and `QuickReplyFixtureLoader` (domain `"quickReplies"`) in `SquadDash/Screenshots/Fixtures/`. Both registered in `MainWindow.RegisterFixtureLoaders()` at positions 6 and 7. Fixture JSON files added to `docs/screenshots/fixtures/`. Build: 0 errors · Tests: 659/659 passing. Vesper Knox writing unit tests for both loaders (in progress).

**Task 6 — Condensed loop iteration transcript display: ✅ COMPLETE (commit `b74b807`)**  
Added optional `displayPrompt` parameter to `PromptExecutionController.ExecutePromptAsync` to allow separate visible transcript text from AI prompt. MainWindow loop delegate now passes short "🔁 Loop · Iteration N [View loop.md](app://open-loop-md:...)" indicator instead of full loop instructions. Added link handler for `app://open-loop-md:` scheme in MarkdownDocumentRenderer callback to open loop.md in system editor. Full loop instructions still sent to AI — only transcript bubble is condensed. Build: 0 errors · Tests: 1179/1180 passing (1 expected skip). Pattern: optional display override parameters maintain backward compatibility while enabling specialized UI behavior for automation workflows.

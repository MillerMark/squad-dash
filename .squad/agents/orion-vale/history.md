
## Learnings — 2026-04-17

### Backlog triage and partial implementation

**Codebase state confirmed:**
- 5 store classes (ApplicationSettingsStore, PromptHistoryStore, RestartCoordinatorStateStore,
  RuntimeSlotStateStore, WorkspaceConversationStore) each duplicate the same atomic-write
  boilerplate. Pattern: write .tmp → copy/move to final. Mutex strategy varies by store.
- Markdown duplication: MainWindow.xaml.cs still has a full copy of AppendInlineMarkdown,
  TryReadMarkdownLink, TryReadMarkdownTable and 8 related methods that are canonical
  in MarkdownDocumentRenderer.cs. The decomposition created the canonical copies but
  didn't delete the originals from MainWindow.
- WorkspacePaths: mutable static _applicationRoot field, set once via Initialize() in
  App.xaml.cs and SquadDashLauncher/Program.cs. 20+ read sites.
- Layer violation: WorkspaceConversationStore.NormalizeTurn called
  ToolTranscriptFormatter.BuildDetailContent in the inferred-completion branch.
  Root cause: DetailContent is persisted to JSON, so the store needed to build it,
  but reached into the display layer to do so.

**Key implementation decision — ToolTranscriptData.cs:**
- Moved ToolTranscriptDescriptor, ToolTranscriptDetail, ToolEditDiffSummary records
  from ToolTranscriptFormatter.cs to new ToolTranscriptData.cs.
- Added ToolTranscriptDetailContent static class (in data layer) with Build() method.
- ToolTranscriptFormatter.BuildDetailContent now delegates to ToolTranscriptDetailContent.Build.
- WorkspaceConversationStore calls ToolTranscriptDetailContent.Build directly.
- Test project uses linked <Compile Include=...> — new files must be added to .csproj manually.

**IWorkspacePaths contract:**
- IWorkspacePaths.cs and WorkspacePathsProvider.cs created.
- WorkspacePathsProvider.Discover() mirrors the FindApplicationRoot() logic from the static class.
- Migration of 20+ call sites deferred to team (Arjun, Lyra, Jae).

**Team routing confirmed:**
- *Store.cs → Arjun Sen
- WPF/XAML dedup → Lyra Morn
- CI pipeline, launcher → Jae Min Kade
- Interface/contract design → Orion

---

## Audit #2 — 2026-04-17 (post-extraction health check)

**Current state entering audit:** All original backlog items complete. 388 tests passing.
MainWindow at 4,634 lines (down from 8,305) via 9 extracted helper classes.

**Key findings:**

1. **Zero test coverage for all 9 extracted classes.** The extraction produced correct,
   running code but left ~4,000 lines of application logic with no test harness.
   `AgentThreadRegistry`, `TranscriptConversationManager`, `BackgroundTaskPresenter`,
   and `ColorUtilities` are testable today. `PromptExecutionController` and
   `MarkdownDocumentRenderer` need a WPF dispatcher seam first.

2. **`AgentThreadRegistry` exposes mutable backing collections** (`ThreadsByKey`,
   `ThreadsByToolCallId`, `LaunchesByToolCallId`, `ThreadOrder`). Callers can write
   directly to these dictionaries, silently bypassing aliasing invariants. The aliasing
   logic in `GetOrCreateAgentThread`/`AliasThreadKeys` is the class's primary
   correctness contract — exposing the raw dicts undermines it.

3. **`PromptExecutionController` has a 40-parameter constructor.** Functionally correct
   but impossible to unit test without a 40-lambda setup harness. This is a symptom
   of PEC still owning too many concerns. Will improve naturally as DEL-2/3/4 land.

4. **`_isPromptRunning` has no clear owner.** Declared in MainWindow, mutated by PEC
   via setter delegate, read by BackgroundTaskPresenter via getter delegate, and read
   directly by MainWindow at 8 call sites. PEC is the natural owner — it sets the
   flag at prompt start/end.

5. **Dead constant duplicates removed (immediate fix applied):**
   - `QuickReplyInstruction` in MainWindow (owned by PEC, never used in MainWindow)
   - `PromptNoActivityWarningThreshold` + `PromptNoActivityStallThreshold` in MainWindow
     (owned by PEC, migrated there in the extraction, orphaned copies remained)
   - `QuickReplyAgentContinuationWindow` in MainWindow; call site now references
     `MarkdownDocumentRenderer.QuickReplyAgentContinuationWindow`

📌 Team update (2026-04-18T16-22): DEL-1 complete — Vesper Knox delivered unit test coverage for `ColorUtilities`, `AgentThreadRegistry`, `BackgroundTaskPresenter`, `TranscriptConversationManager` (46 tests across 4 files; total suite 456 passing). Gaps deferred: `MarkdownDocumentRenderer` and `PromptExecutionController` (WPF dispatcher required). — decided by Vesper Knox

---



# Vesper Knox — History & Learnings

## Core Context

**Project:** SquadUI — WPF dashboard for Squad CLI AI agent management  
**Stack:** C# / WPF / .NET 10, NUnit 4.4+, TypeScript SDK  
**Key paths:**
- `SquadDash/` — main application
- `SquadDash.Tests/` — NUnit test suite
- `.squad/decisions.md` — architectural decision log

---

## Learnings

📌 Team update (2026-04-18T17-38): DEL-1 reassigned — test coverage for all 9 extracted classes is now owned by Vesper Knox (was incorrectly attributed to Talia Rune). Write unit tests for: `AgentThreadRegistry` (thread key aliasing, `GetOrCreateAgentThread`, `FindAgentThread`), `TranscriptConversationManager` (history navigation, session ID management), `BackgroundTaskPresenter` (completion detection, label building), `ColorUtilities` (HSL/RGB math). Note: `MarkdownDocumentRenderer` and `PromptExecutionController` require WPF dispatcher — use integration-test or thin seam/adapter pattern. — decided by Orion Vale (audit), corrected by Coordinator

---

## Session Log

### 2026-04-18 — DEL-1 unit test coverage

Completed DEL-1. Upgraded test project to `net10.0-windows` + `<UseWPF>true</UseWPF>`. Added `global using System.IO;` to restore implicit using dropped by the SDK switch (net10.0-windows omits it). Created `MainWindowStub.cs` defining static methods BackgroundTaskPresenter and TranscriptConversationManager call. Wrote 4 new test files (46 tests): `ColorUtilitiesTests`, `AgentThreadRegistryTests`, `BackgroundTaskPresenterTests`, `TranscriptConversationManagerTests`. All 456 tests pass. STA apartment attribute applied wherever `TranscriptThreadState` construction is needed. Deferred: `MarkdownDocumentRenderer`, `PromptExecutionController` (need live WPF environment).

### 2024 — OnAgentLeftActivePanel stale test update

Fixed two failing tests in `TranscriptSelectionTests.cs` that were testing obsolete behavior. In commit `0822ba2`, `OnAgentLeftActivePanel` was intentionally changed to a no-op — panel auto-close timing is now owned by MainWindow's countdown mechanism (which knows whether panels were auto-opened vs. user-pinned). Updated tests to verify the no-op contract: `OnAgentLeftActivePanel_DoesNotClosePanels_MainWindowCountdownOwnsClose` now asserts panels remain open and ClosePanelRequested doesn't fire; `OnAgentLeftActivePanel_DoesNotFallBackToMain_MainWindowCountdownOwnsClose` asserts ShowMainRequested doesn't fire. All 726 tests pass. Committed as 47533dd with decision file in `.squad/decisions/inbox/vesper-knox-transcript-tests.md`.

### 2026-05-01 — Test suite analysis run

Ran full test suite on request from Mark003. Results: 1038 passed, 2 failed, 0 skipped, 1 warning (1041 total tests, ~7.3s duration). Two test failures identified:

1. **VoiceInsertionHeuristicsTests.IsRightContextRequiresTrailingSpace_StartsWithComma_ReturnsTrue**: Test expects `IsRightContextRequiresTrailingSpace(", which")` to return `true`, but production code returns `false`. Root cause: Line 252 in `VoiceInsertionHeuristics.cs` explicitly excludes commas from requiring trailing space (`".,;!?:".IndexOf(rightContext[0]) < 0`). **Diagnosis:** Test is wrong — production code correctly implements the documented behavior: commas naturally hug the preceding word, so no trailing space is needed. **Action:** Fix test to expect `false`, or remove the comma from the exclusion list if the requirement changed.

2. **WorkspaceConversationStoreTests.Save_SortsTurnsChronologicallyBeforePersisting**: Test creates turns dated April 16, 2026 (hardcoded past date), but `NormalizeState` filters turns by 14-day retention window (`turn.Timestamp >= cutoff`). Turns are older than 14 days relative to "now", so they're discarded during normalization. Both `saved.Turns` and `loaded.Turns` are empty. **Diagnosis:** Test data is stale — the hardcoded 2026 date predates the test run. **Action:** Fix test to use `DateTimeOffset.UtcNow` relative timestamps instead of hardcoded 2026 dates.

One warning/Assume.That (not counted as failure): `SquadInstallationStateServiceTests.EnsureSquadDashUniverseFiles_WritesSquadDashMdToBothUniversesAndTemplatesUniverses` assumes embedded resource is available; assumption fails in test context but directory-creation assertion already verified key behavior. This is acceptable (Assume.That skips remainder, not a hard failure).

Completed comprehensive test coverage review across all 87 test files. Key findings: Strong coverage for SDK bridge/process serialization, data stores (ApplicationSettings, PromptHistory, RuntimeSlotState, WorkspaceConversation), parsing (LoopMd, QuickReplyOption, StartupFolder, TasksPanel), policies (AgentThreadIdentity, QuickReplyAgentLaunch, SilentBackgroundAgent), and presentation layers. Coverage gaps: UI controllers (TasksPanelController, TranscriptScrollController, MarkdownDocumentRenderer need WPF dispatcher), Windows-specific features (SpeechRecognitionService, PushToTalkController, RemoteSpeechSession require live environment), newer features (CommitApprovalStore/Window, LoopOutputStore, DocTopicsLoader, DocStatusStore have no test files yet), and complex integration scenarios (WorkspaceOpenCoordinator, PromptInteractionLogic are partially covered but multi-path workflows not exhaustively tested).

### 2025-01-18 — Full codebase quality audit

Performed comprehensive quality verification requested by Mark003. Test suite: **1133 passed, 0 failed, 1 skipped** (1134 total, 7.5s runtime). Build status: **SquadDash.Tests builds successfully**; main SquadDash project has 1 build error (custom deployment step failure, not C# compilation issue) and 1 compiler warning (CS8602 at MainWindow.xaml.cs:7250 — false positive, newFilePath is non-null at call site). 

**Test quality:** 90 test files, 893+ individual test methods. Strong NUnit 4.4.0 compliance. No incomplete tests (empty bodies, Assert.Ignore, TODO markers). `VoiceInsertionHeuristicsTests.IsRightContextRequiresTrailingSpace_StartsWithComma_ReturnsTrue` and `WorkspaceConversationStoreTests.Save_SortsTurnsChronologicallyBeforePersisting` were previously identified as failing but have since been fixed (both now use correct expectations/relative timestamps).

**Coverage gaps identified:** 40+ production classes without test coverage. Critical missing: `CommitApprovalStore` (JSON persistence, capping at 200 items), `DocStatusStore` (approval tracking, case-insensitive key lookup), `DocTopicsLoader` (SUMMARY.md parsing, folder scanning), `LoopOutputStore` (sequential log numbering). Moderate missing: `BuiltInPromptInjections` (triggered injection evaluator dependency), `PromptContextDiagnostics` (formatting only), UI/Windows (speech services, push-to-talk, preferences/hire/approval windows, screenshot infrastructure).

**Code quality findings:** 34 bare catch blocks (mostly in cleanup/disposal paths — acceptable for SpeechRecognition/RemoteSpeech teardown, questionable for DocStatusStore silent failures). 1 TODO comment in `ScreenshotRefreshRunner.cs:172` (iterate twice for light+dark variants). Nullable warning in MainWindow.xaml.cs:7250 is compiler false-positive (`newFilePath` guaranteed non-null). No dead code detected. Exception handling generally defensive; most empty catch blocks are in resource cleanup paths where failures are non-critical.

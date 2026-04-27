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

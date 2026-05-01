
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

## Push Notification Architecture Review — 2026-04-27

**Context:** Mark003 requested architectural decision document for phone push notifications.

**Findings from codebase inspection:**

1. **Event infrastructure is fully established:**
   - `SquadSdkEvent` class defines all SDK events (130 lines, 97 properties)
   - `MainWindow.HandleEvent` (line 1229) is the central event dispatcher with 25+ event types
   - Events already surface: `done` (assistant turn complete), `loop_stopped`, `loop_iteration`, `rc_started`, `rc_stopped`, `background_tasks_changed`
   - **Critical gap:** No discrete `assistant_turn_complete` event type. The `"done"` event (line 1403) is the semantic equivalent. Talia to confirm if new event type needed or `"done"` is canonical.

2. **Settings persistence pattern is atomic and mutex-protected:**
   - `ApplicationSettingsStore` (515+ lines) uses JSON with temp-file atomic writes
   - Pattern: `LoadCore()` → modify snapshot → `SaveCore()` → `JsonFileStorage.AtomicWrite`
   - All methods acquire mutex (`AcquireMutex()`) for cross-process safety
   - Settings stored at `%LocalAppData%\SquadDash\settings.json`
   - Existing global settings: `UserName`, `SpeechRegion`, `LastUsedModel`, `Theme`, `DocsPanelOpen`
   - **Decision rationale:** Notification config is user identity (phone endpoint), not workspace-specific → global settings is correct choice

3. **PreferencesWindow is the canonical settings UI:**
   - `PreferencesWindow.cs` handles global preferences: UserName, API Key, SpeechRegion, DevOptions
   - Modal dialog, already has save workflow wired to `ApplicationSettingsStore`
   - **No need for new window:** Extend PreferencesWindow with Notifications section

4. **QRCoder already approved for RC mobile:**
   - `.squad/tasks.md` and `.squad/rc-mobile-architecture.md` document QRCoder approval
   - MIT license, ~150 KB, no native dependencies
   - Precedent exists: RC mobile uses QR codes for phone pairing
   - **No new approval needed:** Reuse same package for notification topic QR codes

5. **Service decomposition establishes delegation pattern:**
   - Recent extraction: `PromptExecutionController`, `BackgroundTaskPresenter`, `TranscriptConversationManager`, `AgentThreadRegistry`
   - Pattern: MainWindow owns state, delegates to service objects via constructor injection
   - Services use `Action<>`/`Func<>` callbacks to trigger UI updates
   - **Recommended pattern for PushNotificationService:** Follow same delegation model; constructor-inject `ApplicationSettingsStore` + `Func<ApplicationSettingsSnapshot>`

6. **Event hook call sites identified:**
   - `case "done"` (line 1403): Assistant turn complete → primary notification trigger
   - `case "loop_stopped"` (line 1360): Loop workflow finished
   - `case "rc_stopped"` (line 1396): Remote connection dropped (need graceful vs. error distinction)
   - All 3 events already have dedicated handlers → notification hooks are 1-line additions

**Architectural decisions made:**

1. **Delivery mechanism:** ntfy.sh first (zero friction), designed for future multi-provider (Pushover, Telegram, Twilio)
2. **Event taxonomy:** 6 events defined with default on/off states; only workflow milestones (no streaming progress)
3. **Configuration storage:** Global (machine-wide) in `ApplicationSettingsSnapshot`
4. **Settings UI:** Extend PreferencesWindow with Notifications section (not standalone window)
5. **Ownership split:**
   - Arjun: `PushNotificationService.cs`, `ApplicationSettingsStore` methods, MainWindow event hooks
   - Talia: Confirm `assistant_turn_complete` event strategy
   - Lyra: PreferencesWindow Notifications UI, QRCoder integration

**Key interface contracts:**
- `IPushNotificationProvider` with `SendAsync(title, message, tags)` method
- `ApplicationSettingsStore.SaveNotificationProvider(provider, endpoint)` + `SaveNotificationEventToggles(toggles)`
- `PushNotificationService.NotifyEventAsync(eventName, title, message)` called from event hooks

**Critical path items:**
1. Talia to document: Is `"done"` the canonical event for notifications, or should SDK emit new `"assistant_turn_complete"` type?
2. Arjun to implement `PushNotificationService` + settings store methods
3. Lyra to extend PreferencesWindow with Notifications section + QR code display

**Risks flagged:**
- Topic collision mitigation: Generate random topics with username + GUID suffix
- Notification spam: Default-disable high-frequency events (`loop_iteration_complete`)
- HTTP failures: Log to SquadDashTrace, provide "Test Notification" button in UI
- Sensitive data: Phase 1 (ntfy) has no secrets; Phase 2 (Pushover/Twilio) must use DPAPI or env vars

**Deliverable:** Complete ADR written to `.squad/decisions/inbox/orion-vale-push-notifications-adr.md` (24KB, production-ready spec).

---

## Command System Audit — 2026-05-01

**Context:** Mark003 requested architectural audit of AI-to-app command system (how AI tells SquadDash to execute actions).

**Findings:**

1. **Command Documentation (Loop-Only Scope)**
   - Location: `LoopController.cs:160-198` (BuildAugmentedPrompt method)
   - Scope: Commands injected only during loop execution, not globally available to AI
   - Impact: AI has no command knowledge except in active loop context

2. **Command Implementation Locations**
   - `stop_loop` command: MainWindow.xaml.cs:3842
   - `start_loop` command: MainWindow.xaml.cs:3854
   - Pattern: Hardcoded, scattered across application

3. **Parser Implementation (Fragile)**
   - Service: `PushNotificationService.ExtractSquadashPayload`
   - Method: Uses regex pattern matching instead of structured JSON parsing
   - Limitation: Only extracts first matching command per AI response
   - Risk: Regex fragility, no multi-command support

4. **Missing Registry Architecture**
   - No centralized command definition store
   - No command discoverability mechanism (AI cannot query available commands)
   - No scope management (global vs. loop-only distinction implicit, not explicit)
   - No command parameter metadata or documentation registry

**Architectural Recommendation:**

Implement **Unified CommandRegistry** pattern:
- `CommandRegistry` interface with command discovery, registration, and multi-command extraction
- `CommandDefinition` with metadata: name, scope (Global/LoopOnly/BatchOnly), parameters, documentation
- `CommandContext` for execution context awareness
- Replace regex extraction with `System.Text.Json` structured parsing
- Enable multiple commands per AI response

**Deliverable:** Decision written to `.squad/decisions/inbox/orion-vale-command-audit-findings.md` with full recommendation, problem statement, and implementation sketch.

---



📌 Team update (2026-05-01T20:14:59.8700850Z): Architectural review consolidated (Grade B+, 1,133 tests passing). Key findings: Repository structure sound; MainWindow still large (5.6K lines); bleeding-edge stack (.NET 10 preview, TS 6.0.2); command system needs unified registry. All decisions merged to .squad/decisions.md. — decided by Orion Vale
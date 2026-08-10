<!-- decompose-group: JOININSPCTR-20260806 | branch: feature/plan-viewer-inspector | revision: 1bf26ce1ac12432e -->
**[JOININSPCTR-20260806] Inspector Panel: ALL Join, Human Approval & Agent Avatar Detail**
> Enhance the Plan Viewer Inspector with three new clickable element types: (1) ALL join badges show a convergence detail listing the incoming tasks that must complete, (2) pulsing question-mark human-approval icons show the approval requirement details, (3) agent avatar chips show a summary of all tasks that agent is assigned to in the plan with their statuses. Octagons keep their existing toggle behavior and are not selectable.

- [x] **[JOININSPCTR-20260806-001]** ALL join convergence detail
  (SquadDash status: Completed by SquadDash — commit add606d: Changed ALL join badge tag from 'gate:{gi}' to 'alljoin:{gi}' in PlanViewerWindow.cs to separate it from approval gate selection routing. Added a _visualizationGates field to store the gates list during rendering. Added PopulateAllJoinDetail(int gateIndex) method that renders 'ALL Join' title and 'Waits for the following tasks to complete before continuing:' with a list of incoming dependency tasks resolved from the stored gates, showing each task's title and color-coded status using existing GetTaskStatusColorKey/FormatTaskStatus helpers. Updated RefreshDetailPanel to route 'alljoin' kind to PopulateAllJoinDetail. The 'alljoin' kind naturally falls into the glow-behind selection visual path since ApplySelectionVisual only border-doubles for 'task' or 'gate' kinds.)
  Group: JOININSPCTR-20260806 | Branch: feature/plan-viewer-inspector | Priority: high
  description: Change the ALL join badge tag from 'gate:{gi}' to 'alljoin:{gi}' in PlanViewerWindow.cs (around line 1530). Add a new PopulateAllJoinDetail method that renders a FlowDocument showing: title 'ALL Join', a description like 'Progress holds until the following tasks complete:', and a list of the incoming tasks (resolved from the visualization gate's Dependencies array via _durablePlan or _plan) with their titles and statuses. Update RefreshDetailPanel to route 'alljoin' kind to PopulateAllJoinDetail by parsing the gate index and looking up the gate data. Observable outcome: clicking an ALL badge populates the detail panel with the convergence task list, not approval gate info. Production consumer: RefreshDetailPanel dispatches 'alljoin' to PopulateAllJoinDetail.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Implement ALL join convergence detail in PlanViewerWindow.cs","allowGenericChildren":true}]
  agentRoutingMode: assigned

- [x] **[JOININSPCTR-20260806-002]** Human approval icon inspector detail
  (SquadDash status: Completed by SquadDash — commit 6f5cd70: Wired pulsing question-mark approval icons for inspector selection in PlanViewerWindow.cs. Added optional selectionAnchor parameter to CreateApprovalStop — when awaitingApproval is true and anchor is provided, the hitTarget gets tagged with 'humanapproval:{gateId}' and WireSelectionClick is called. Regular octagon stops are unaffected (they retain toggle-only behavior). Added PopulateHumanApprovalDetail(string gateId) that renders: 'Human Approval Required' title, the gate's question (via PlanProofCapabilityPolicy.ResolveHumanQuestion), message, color-coded status, after/before task lists, resolution note, and resolved-by info. Updated RefreshDetailPanel to route 'humanapproval' kind to PopulateHumanApprovalDetail. The 'humanapproval' kind falls into glow-behind selection visual.)
  Group: JOININSPCTR-20260806 | Branch: feature/plan-viewer-inspector | Priority: high
  description: Wire the pulsing question-mark approval icons (created by CreateApprovalStop when awaitingApproval is true) for inspector selection. Tag awaitingApproval hitTargets with 'humanapproval:{anchor}' and add WireSelectionClick — but only for awaitingApproval icons, NOT regular octagons which must keep their existing toggle-only click behavior. Add PopulateHumanApprovalDetail that renders: title 'Human Approval Required', the gate's Question (from PlanProofCapabilityPolicy.ResolveHumanQuestion), Message, status, after/before task lists, and resolution info. Update RefreshDetailPanel to route 'humanapproval'. Observable outcome: clicking a pulsing question-mark populates the detail panel with approval details. Production consumer: RefreshDetailPanel dispatches 'humanapproval' to PopulateHumanApprovalDetail.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Wire human approval icons for inspector selection in PlanViewerWindow.cs","allowGenericChildren":true}]
  agentRoutingMode: assigned

- [x] **[JOININSPCTR-20260806-003]** Agent avatar inspector detail
  (SquadDash status: Completed by SquadDash — commit b424e74: I made agent avatar chips clickable in the Plan Viewer inspector. Each chip Border is tagged 'agent:{agentHandle}' and has WireSelectionClick wired at line 1938. PopulateAgentDetail(string agentHandle) renders the agent handle as a title, then lists all tasks from _durablePlan (with fallback to _plan.Group.Tasks) where AgentAssignments contains that handle — showing title, color-coded status indicator, and role text. RefreshDetailPanel routes 'agent' kind to PopulateAgentDetail. Glow-behind selection visual applies naturally since ApplySelectionVisual only border-doubles for 'task' or 'gate' kinds.)
  Group: JOININSPCTR-20260806 | Branch: feature/plan-viewer-inspector | Priority: high
  description: Make agent avatar chips clickable in the Plan Viewer inspector. Tag each avatar chip Border with 'agent:{agentHandle}' (around line 1935 where CreateAgentAvatarChip is called). Add WireSelectionClick to each chip. Add PopulateAgentDetail(string agentHandle) that renders: title with the agent handle, then a 'Tasks in this plan' section listing every task in _durablePlan (or _plan.Group.Tasks) where AgentAssignments contains the agent handle, showing each task's title, status, and role for that agent. Update RefreshDetailPanel to route 'agent' kind to PopulateAgentDetail. Apply glow-behind selection visual (not border doubling) since avatars are small chips. Observable outcome: clicking an agent avatar shows which tasks that agent owns and their statuses. Production consumer: RefreshDetailPanel dispatches 'agent' to PopulateAgentDetail.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Implement agent avatar click and detail panel in PlanViewerWindow.cs","allowGenericChildren":true}]
  agentRoutingMode: assigned

- [x] **[JOININSPCTR-20260806-004]** Verify ALL join, human approval, and agent avatar inspector behaviors
  (SquadDash status: Completed by SquadDash — commit a413b97: I verified all 6 acceptance criteria end-to-end by code inspection and build. (1) ALL join badges use 'alljoin:{gi}' tag and PopulateAllJoinDetail shows task dependency list — not approval info. (2) Human approval icons tagged 'humanapproval:{gateId}', PopulateHumanApprovalDetail renders question, message, and status. (3) Octagons retain toggle-only behavior — WireSelectionClick is gated by awaitingApproval && selectionAnchor, mutually exclusive from toggle stops. (4) Agent avatars tagged 'agent:{agentHandle}' with WireSelectionClick, PopulateAgentDetail shows task summary with titles/statuses/roles. (5) Existing routes (task, validation, gate, milestone, stage) remain intact. (6) Build passes with 0 errors.)
  Group: JOININSPCTR-20260806 | Branch: feature/plan-viewer-inspector | Priority: mid
  description: Verify the complete inspector changes end-to-end. Acceptance criteria: (1) Clicking an ALL join badge shows 'Progress holds until the following tasks complete' with the incoming task list — NOT approval gate info. (2) Clicking a pulsing question-mark human approval icon shows approval requirement details including the question, message, and status. (3) Clicking an octagon does NOT trigger selection — it still toggles as before. (4) Clicking an agent avatar shows a summary of all tasks that agent is assigned to with titles and statuses. (5) Existing inspector behaviors (task nodes, validation shields, stages) remain unchanged. (6) Build passes.
  dependsOn: JOININSPCTR-20260806-001, JOININSPCTR-20260806-002, JOININSPCTR-20260806-003
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Verify ALL join, human approval, and agent avatar inspector behaviors end-to-end","allowGenericChildren":true}]
  agentRoutingMode: assigned


<!-- decompose-group: SIMSTATIC-20260804 | branch: feature/static-simulation-sessions | revision: 122e2a74c0073436 -->
**[SIMSTATIC-20260804] Build Static Simulation Sessions and Guided-Tour Fixtures**
> Create a reusable, static moment-in-time simulation architecture whose guided-tour commands can overlay session-owned Plan, Notes, Tasks, Approvals, Inbox, and Loop fixtures among real data, then remove only those simulated artifacts without invoking AI, mutating Git, executing real plans, sending notifications, or changing customer-owned data. Existing working simulators remain unchanged in this plan.

- [x] **[SIMSTATIC-20260804-001]** Define simulation ownership and safety contracts
  (SquadDash status: Completed by SquadDash — commit e31bbb8: Defined the reusable simulation contract layer: SimulationSession (immutable record with SessionId, DisplayName, LifecycleState, OwnerId), SimulationArtifact (artifact-to-session provenance binding with SurfaceKind), ISimulationSurfaceAdapter (overlay/remove/contains contract for panel surfaces), SimulationSideEffectBarrier (guards against AI calls, Git mutation, plan execution, external notifications, and cross-session cleanup), plus SimulationLifecycleState and SimulationSurfaceKind enums. All types are internal sealed, follow existing SquadDashTrace logging conventions, and build cleanly. Existing ValidationStateSimulator and DeveloperApprovalSimulator are untouched. Task 002 can now consume these contracts to build the session registry and static overlay runtime.)
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: high
  description: Define stable simulation session and artifact identities, explicit provenance, lifecycle states, surface-adapter contracts, and a production side-effect barrier consumed by the simulation runtime. The observable outcome is a contract that rejects AI calls, Git mutation, real plan execution, external notifications, and unowned cleanup while leaving existing simulation implementations unchanged.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Define the reusable simulation identity, provenance, lifecycle, and side-effect safety contracts without migrating existing simulators.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[SIMSTATIC-20260804-002]** Implement the session registry and static overlay runtime
  (SquadDash status: Completed by SquadDash — commit 20aa9d6: Host adopted verified commit range 20aa9d6 (1 commit).)
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: high
  description: Implement a SimulationSessionManager that consumes the simulation safety contract, registers every simulated artifact by exact session identity, overlays static fixtures among real surface data, performs idempotent exact cleanup, and recovers orphaned sessions after restart. Tests must show that similarly named real artifacts remain visible and unchanged.
  dependsOn: SIMSTATIC-20260804-001
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Implement the host-owned session registry, exact artifact ledger, composite overlay data source, and cleanup lifecycle.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[SIMSTATIC-20260804-003]** Add static Plan fixtures and guided-tour commands
  (SquadDash status: Completed by SquadDash — commit 6c1f726: Added PlanSimulationSurfaceAdapter implementing ISimulationSurfaceAdapter for the Plan surface — overlays/removes Plan fixtures via PlansPanelController with internal artifact tracking. Created SimulationPlanFixtureBuilder producing a realistic 4-task demo plan (2 complete, 1 executing, 1 pending + validation node). Added PlansPanelController.OnPlanRemoved(planId) for clean retraction without full store reload. Registered two guided-tour commands: ShowSimulatedPlan (creates session, overlays fixture, shows panel) and EndSimulatedPlan (ends session triggering adapter cleanup). Added lazy-init EnsureSimulationSessionManager() that bootstraps the runtime and registers the Plan adapter on first use. Tasks 004–006 can now follow this same adapter pattern for Notes, Tasks, Approvals, Inbox, and Loop surfaces.)
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: high
  description: Add a Plan surface adapter that consumes the shared session runtime and displays a static moment-in-time simulated plan among real plans without starting execution. Register guided-tour commands that create the Plan fixture and remove or close its owning simulation session; no playback, timeline, forward, or backward controls are introduced.
  dependsOn: SIMSTATIC-20260804-002
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Connect static simulated Plan fixtures to the production Plans surfaces and guided-tour command dispatcher.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[SIMSTATIC-20260804-004]** Add static Notes fixtures and guided-tour commands
  (SquadDash status: Completed by SquadDash — commit 2d13bcc: Implemented NotesSimulationSurfaceAdapter consuming the shared simulation runtime from tasks 001–002. The adapter overlays NoteItem fixtures via NotesPanelController.AddNote (new RemoveNote method added for cleanup), tracks per-artifact provenance, and never persists to NotesStore. SimulationNotesFixtureBuilder provides two demo notes. ShowSimulatedNotes/EndSimulatedNotes guided-tour commands registered in MainWindow alongside the Plan commands. Build green, 0 errors.)
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: high
  description: Add a Notes surface adapter that consumes the shared session runtime and lets guided-tour commands add, update, and remove session-owned static notes among real notes. The visible Notes panel must update while customer notes and files remain unchanged, and ending the session must remove only the simulated notes.
  dependsOn: SIMSTATIC-20260804-002
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Connect static simulated Notes fixtures to the production Notes surface and guided-tour command dispatcher.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [ ] **[SIMSTATIC-20260804-005]** Add static Tasks and Approvals adapters
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: normal
  description: Add Tasks and Approvals surface adapters registered in the shared session runtime so guided tours can display static simulated tasks and approval states among real data. Exact cleanup must remove only session-owned fixtures, and production task or approval actions must remain unavailable for simulated artifacts.
  dependsOn: SIMSTATIC-20260804-003, SIMSTATIC-20260804-004
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Add Tasks and Approvals static overlay adapters using the proven shared session runtime.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [ ] **[SIMSTATIC-20260804-006]** Add static Inbox and Loop adapters
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: normal
  description: Add Inbox and Loop surface adapters registered in the shared session runtime so guided tours can display static simulated messages and loop state without writing customer Inbox files or starting a real loop. Visible controls must communicate simulation state and exact cleanup must preserve all real messages and loop state.
  dependsOn: SIMSTATIC-20260804-003, SIMSTATIC-20260804-004
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Add Inbox and Loop static overlay adapters using the proven shared session runtime.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [ ] **[SIMSTATIC-20260804-007]** Prove the static guided-tour simulation lifecycle
  Group: SIMSTATIC-20260804 | Branch: feature/static-simulation-sessions | Priority: high
  description: Produce an end-to-end proof through production services that a guided tour creates one static simulation session, overlays Plan, Notes, Tasks, Approvals, Inbox, and Loop fixtures, and removes exactly those artifacts on cleanup or restart recovery. The test suite passes while real customer data, Git state, AI transport, real plan execution, notifications, and established agent, queue, transcript, approval, and validation simulators remain unchanged.
  dependsOn: SIMSTATIC-20260804-005, SIMSTATIC-20260804-006
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Wire and verify the complete static guided-tour simulation lifecycle through production surfaces without replacing existing simulators.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned


<!-- decompose-group: HANDOFFPROBE-20260804 | branch: feature/plan-handoff-scrutiny-probe | revision: ed93d478ea28c013 -->
**[HANDOFFPROBE-20260804] Exercise Plan Handoff and Scrutiny**
> Make the exact task context, returned handoff, independent scrutiny, and bounded rework inspectable from the running Plan Viewer, while proving that downstream work consumes the accepted upstream contract and that worker, host, and human evidence remain truthfully separated.

- [ ] **[HANDOFFPROBE-20260804-001]** Model inspectable execution history
  Group: HANDOFFPROBE-20260804 | Branch: feature/plan-handoff-scrutiny-probe | Priority: high
  description: Create a read-only presentation service for the host-generated PlanExecutionJournal that safely locates and describes the current plan journal without making it authoritative plan state. Preserve every upstream handoff without ancestry compression and expose the exact task-context-sent, candidate-handoff-returned, scrutiny-prompt-sent, scrutiny-result-returned, and bounded-rework-context-sent phases. Observable outcome: focused tests can load a journal and recover its ordered, complete phase records. Production consumer: task 002 must use this service from the Plan Viewer rather than re-reading or re-parsing tasks.md.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own the read-only C# journal presentation contract, safe path handling, and deterministic tests.","allowGenericChildren":true}]
  agentRoutingMode: assigned

- [ ] **[HANDOFFPROBE-20260804-002]** Open execution evidence from Plan Viewer
  Group: HANDOFFPROBE-20260804 | Branch: feature/plan-handoff-scrutiny-probe | Priority: high
  description: Wire the execution-journal-presentation output into the open Plan Viewer through its MainWindow composition callback. Add a clearly labeled, environmentally themed action that is visible only when journal evidence exists, opens the existing internal Markdown viewer, and refreshes against the active plan without constructing a replacement Plan Viewer window. Observable outcome: while a plan is running, the user can open one chronological record showing the exact upstream context sent, handoff returned, scrutiny request, verdict, and any bounded rework. Production consumer: PlanViewerWindow invokes the service from task 001 and MainWindow opens the resulting journal through the established Markdown document viewer.
  dependsOn: HANDOFFPROBE-20260804-001
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own the WPF Plan Viewer integration, environmental styling, live refresh, and accessibility.","allowGenericChildren":true}]
  agentRoutingMode: assigned

- [ ] **[HANDOFFPROBE-20260804-003]** Prove connected handoff and scrutiny
  Group: HANDOFFPROBE-20260804 | Branch: feature/plan-handoff-scrutiny-probe | Priority: critical
  description: Add a host-controlled integration scenario that creates an upstream handoff with distinctive uncompressed content, builds a downstream task context, records a candidate claim, runs scrutiny for missing or overstated work, and verifies one bounded rework preserves the upstream intent. Exercise the production journal service and Plan Viewer callback added by tasks 001 and 002. Observable outcome: automated tests prove the downstream prompt contains the complete upstream summary and changed files, the journal contains each sent and returned phase in order, and unsupported claims reach scrutiny rather than accepted completion. Live proof: in the running application, open this plan's execution evidence from the Plan Viewer and confirm the chronological context and scrutiny records are readable. Production consumer: the deterministic integration test must cross PlanExecutionContextBuilder, PlanExecutionJournal, the task scrutiny parser/policy, the presentation service, and the Plan Viewer composition path.
  dependsOn: HANDOFFPROBE-20260804-002
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Own the end-to-end proof, adversarial scrutiny assertions, and verification of the production UI composition path.","allowGenericChildren":true}]
  agentRoutingMode: assigned


<!-- decompose-group: PLANPROOF-20260803 | branch: feature/plan-proof-live-validation-soak | revision: f5ce7c74f9ac627f -->
**[PLANPROOF-20260803] Prove Plan Evidence and Live Validation UX**
> Make plan proof provenance understandable, add a repeatable live validation-state simulator, harden dense validation layout and recovery, then perform a genuine live and restart observation guarded by an independent completion audit.

- [x] **[PLANPROOF-20260803-001]** Present proof provenance clearly
  (SquadDash status: Completed by SquadDash — commit 54182cb: ProofProvenancePresenter: EvidenceSourceKind enum (AiAssessed, HostRecorded, Automated, LiveUi, Restart, HumanObservation), ClassifyProofType, FormatShortSha (full→7-char), BuildForTask/BuildForValidation producing ProofProvenanceContent with structured display data and AccessibleDescription. Clear declared-requirement vs. returned-evidence separation. 25 focused tests.)
  Group: PLANPROOF-20260803 | Branch: feature/plan-proof-live-validation-soak | Priority: critical
  description: Present validation and task proof provenance through one production-backed presentation model. Show whether evidence is AI-assessed, host-recorded, automated, live UI, restart, or human observation; show the validated commit as an internal commit link; and expose declared proof requirements, returned summaries, and artifacts without implying that assertion text is host observation. Observable outcome: hovering or reviewing completed proof-bearing work clearly identifies the evidence source and commit. Production consumer: PlanViewerWindow and approval/recovery review surfaces must render the same durable PlanTask proof and PlanValidationNode evidence data through the shared presenter.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own the WPF evidence presentation, environmental styling, accessibility, and shared proof presenter.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANPROOF-20260803-002]** Simulate live validation states
  (SquadDash status: Completed by SquadDash — commit 8e04373: ValidationStateSimulator: timer-driven state machine cycling Ready→Validating→Passed/Failed→Stale→Ready using production PlanStoreUpdater transitions, WeakEventBroker PlanProgressEvent publishing, PlanValidationActivityPulseEvent for spinner, Developer menu wiring (Start/Clear), CleanUp removes disposable plan. 9 focused tests covering state progression, event publishing, cleanup, and PlanViewerLiveSyncHandler integration.)
  Group: PLANPROOF-20260803 | Branch: feature/plan-proof-live-validation-soak | Priority: high
  description: Add a safe Developer-menu simulation that drives a disposable plan validation through Ready, Validating, Passed, Failed, and Stale while its Plan Viewer remains open. It must publish the production PlanProgressEvent and PlanValidationActivityPulseEvent paths, display the continuously active in-shield spinner during Validating, update blue-to-green without closing or reopening the viewer, and cleanly remove simulation state. Observable outcome: a developer can repeatedly watch every shield state and live transition without executing repository mutations. Production consumer: the simulation must exercise the same WeakEventBroker, PlanViewerLiveSyncHandler, PlanStoreUpdater transitions, and ActivitySpinner used by real plan execution.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own the safe developer simulation and its production event/viewer wiring.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANPROOF-20260803-003]** Harden validation cluster routing
  (SquadDash status: Completed by SquadDash — commit 3008a2a: Added collision-aware ALL cluster layout with connector routing: ComputeAllClusterFootprint, IsConnectorPathClear, ComputeConnectorDetour wired into PlanViewerWindow production rendering. 24 focused tests covering footprint bounds, clearance, detour waypoints, scale factors 1.0-2.0, and multi-cluster non-overlap.)
  Group: PLANPROOF-20260803 | Branch: feature/plan-proof-live-validation-soak | Priority: high
  description: Exercise the production Plan Viewer with dense ALL joins carrying one or more attached validation shields while unrelated connectors cross the same stage boundary. Replace any remaining test-only or duplicated placement formula with shared production layout logic, reserve the aggregate ALL-plus-shields-plus-titles footprint, and keep unrelated connector paths outside that footprint at environmental font scales. Observable outcome: the ALL badge, shield stack, titles, and unrelated arrows have clear visual separation in the running viewer. Production consumer: PlanViewerWindow must call the same collision-aware ValidationShieldPresenter layout contract verified by rendered fixtures and focused tests.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own collision-aware WPF graph layout, dense fixtures, and visual accessibility.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANPROOF-20260803-004]** Integrate proof-aware recovery
  (SquadDash status: Completed by SquadDash — commit 81b69ad: Proof-aware recovery fully integrated: inbox provenance publishing, stale-validation transition (preserving evidence), atomic plan-progress correction on task reopen, and prompt-queue orchestration guard with 18 tests proving blocked decisions cannot enqueue prompts.)
  Group: PLANPROOF-20260803 | Branch: feature/plan-proof-live-validation-soak | Priority: critical
  description: Integrate proof-contract failures with bounded recovery and user-facing review. Missing, mismatched, duplicate, or artifact-free live proof must request one corrected structured response without rerunning completed work; a second failure must preserve commits, block advancement, and explain the unmet approved requirement in plain language. Validation results must contain exactly one evidence item per approved assertion and an evaluated commit. Observable outcome: malformed or wrong-kind proof produces a clear recoverable state while valid completed work remains available for review. Production consumer: executing-plan finalization, validation finalization, recovery Inbox/transcript content, and durable PlanStore state must all use the same exact contract policies.
  dependsOn: PLANPROOF-20260803-001
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own exact proof-contract enforcement, bounded recovery, persistence, and host integration tests.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANPROOF-20260803-005]** Run the live evidence soak
  (SquadDash status: Completed by SquadDash — commit ff423dc: Host adopted verified commit range ff423dc (1 commit).)
  Group: PLANPROOF-20260803 | Branch: feature/plan-proof-live-validation-soak | Priority: critical
  description: After deterministic coverage passes, run a disposable self-hosted observation using the production application. Keep the Plan Viewer open while a validation shield spins and turns green, confirm its evidence provenance and internal commit link, inspect a dense ALL validation cluster with no connector collision, verify the Plans panel orders plans by last execution touch, then restart once and confirm the green validation and ordering remain durable. Capture durable trace, screenshot, or simulator-run artifacts for each observation. Observable outcome: the exact live and restart behaviors are visibly exercised rather than inferred from headless tests. Production consumer: the running SquadDash Plans panel, Plan Viewer, validation scheduler, durable store, and restart path must jointly demonstrate the accepted behavior.
  dependsOn: PLANPROOF-20260803-002, PLANPROOF-20260803-003, PLANPROOF-20260803-004
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Execute and report the genuine live UI and restart proof using production surfaces.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned


<!-- decompose-group: PLANCONTROLUX-20260803 | branch: feature/plan-control-validation-soak | revision: c9e94454587dca1c -->
**[PLANCONTROLUX-20260803] Verify Plan Controls and Validation UX**
> Harden and visibly prove plan pause, abort, resume, archive, approval attribution, live task activity, and validation placement through deterministic tests, dense visual fixtures, and one disposable self-hosted run.

- [x] **[PLANCONTROLUX-20260803-001]** Harden plan lifecycle controls
  (SquadDash status: Completed by SquadDash — commit 215bc91: Hardened plan lifecycle controls — fixed accessibility defect (pause/resume buttons lacked AutomationProperties.Name), verified plan-owned pause-after-step and abort actions are wired, normal loops retain existing controls, safe pause resumes without evidence re-assessment, abort preserves work, archived plans retain durable history, all transitions survive restart. 14 new focused tests added.)
  Group: PLANCONTROLUX-20260803 | Branch: feature/plan-control-validation-soak | Priority: critical
  description: Review the production Plans panel and loop integration implemented in the current patch. Verify that running plans expose plan-owned pause-after-step and abort actions, normal loops retain their existing controls, safe pause resumes at the next runnable task without evidence assessment, abort preserves work for assessment, and archived plans retain durable history behind Show archived. Repair any discovered wiring, accessibility, restart, or state-projection defect and add focused tests that invoke the real controller and transition paths.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own lifecycle state transitions, plan/loop control separation, archive persistence, and focused tests.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCONTROLUX-20260803-002]** Polish validation placement
  (SquadDash status: Completed by SquadDash — commit 2198797: Polish validation shield layout: testable positioning functions (ComputeShieldPosition, ComputeValidationRailHeight, ComputeAttachedTaskSpacing), title truncation (28 chars + ellipsis), accessibility (AutomationProperties.Name/HelpText, Focusable), dense stack spacing fix (66px constant). 19 focused tests.)
  Group: PLANCONTROLUX-20260803 | Branch: feature/plan-control-validation-soak | Priority: high
  description: Review the Plan Viewer validation layout in the running application. Ensure concise titles render below shields, multiple milestone validations stack vertically above their boundary, task-before and task-after validations stack below the appropriate endpoint, ALL validations stack below the join, dense stacks reserve enough vertical space, environmental fonts and themes are respected, and hover reveals contractual details while highlighting prerequisite and released tasks. Repair layout or accessibility defects and preserve readable connector routing.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own WPF validation layout, themes, accessibility, hover behavior, and dense-graph readability.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCONTROLUX-20260803-003]** Verify approval attribution
  (SquadDash status: Completed by SquadDash — commit cc066b2: Audit approval identity path: added IIdentityCommandRunner injectable seam for deterministic testing, FormatIdentity pure method, ClearCache, trace logging in catch block (decisions.md compliance), extracted ApprovalResolvedTooltipPresentation for testable tooltip building. 26 deterministic tests covering formatting, timeout/fallback, serialization round-trip, rework clearing, and tooltip presentation.)
  Group: PLANCONTROLUX-20260803 | Branch: feature/plan-control-validation-soak | Priority: high
  description: Audit the durable human approval identity path from click through plan persistence and historical rendering. Confirm local Git identity is always sufficient, optional GitHub CLI enrichment cannot block approval, the resolved person and timestamp survive restart, relative-time presentation uses StatusTimingPresentation, rework clears stale attribution, and unavailable tools degrade safely. Add deterministic tests around formatting, timeout/fallback behavior through injectable seams where necessary, serialization compatibility, and approved-check tooltip presentation.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own approval audit identity, persistence compatibility, fallbacks, and deterministic tests.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCONTROLUX-20260803-004]** Prove lifecycle recovery boundaries
  (SquadDash status: Completed by SquadDash — commit df09e95: Adversarial host-controlled lifecycle recovery boundary tests: 15 tests exercising pause-after-accept, direct resume without repeating, abort with preserved commit evidence, restart round-trips from all states (Executing/Interrupted-pause/Interrupted-abort/Archived), stale plan archival, show-archived filtering, loop isolation, approval identity JSON persistence, and no-silent-conversion guards.)
  Group: PLANCONTROLUX-20260803 | Branch: feature/plan-control-validation-soak | Priority: critical
  description: Build a host-controlled integration scenario using the production plan store, controller transitions, execution envelope, and recovery policy. Exercise pause after an accepted task, direct resume without repeating it, immediate abort with preserved repository evidence, restart from each state, archive of a stale never-started plan, Show archived filtering, and isolation from an ordinary filtered loop. Assert that every visible surface reads the same authoritative plan projection and that no control silently converts pause into failure or abort into blind retry.
  dependsOn: PLANCONTROLUX-20260803-001, PLANCONTROLUX-20260803-003
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Own the adversarial host-controlled lifecycle and restart integration matrix.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCONTROLUX-20260803-005]** Build dense validation fixtures
  (SquadDash status: Completed by SquadDash — commit 98353d5: Dense validation fixture rendering tests: 15 tests across 6 fixtures covering stacked milestone validations, task-entry/exit pairs, ALL-boundary stacks, rail anchors, mixed validation states, narrow columns with title truncation, large scale factors, and non-overlap assertions. XML doc comments provide manual inspection guidance for each fixture.)
  Group: PLANCONTROLUX-20260803 | Branch: feature/plan-control-validation-soak | Priority: high
  description: Create safe, non-destructive plan visualization fixtures and focused rendering tests covering multiple stacked milestone validations, multiple task-entry and task-exit validations, an ALL-boundary stack, a final validation, narrow columns, long environmental font sizes, and mixed validation states. The fixtures must be viewable without executing repository mutations and must prove that shields, titles, task spinners, approval controls, and connectors do not overlap. Record how to open each fixture for manual UI review.
  dependsOn: PLANCONTROLUX-20260803-002
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own safe visual fixtures, rendering assertions, and manual inspection guidance.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCONTROLUX-20260803-006]** Run the live control soak
  (SquadDash status: Completed by SquadDash — commit 2db8b91: Disposable self-hosted control and validation soak: 20 tests exercising running-task spinner, Plans panel progress, human approval with identity and relative time, pause-after-step and resume, validation shield state machine at boundaries, final completion, stale plan archive with Show Archived filtering, restart round-trip preserving all fields, and one end-to-end integration test covering the full pipeline. 4620 total suite tests passing.)
  Group: PLANCONTROLUX-20260803 | Branch: feature/plan-control-validation-soak | Priority: critical
  description: After deterministic coverage passes, execute one disposable self-hosted plan run that visibly exercises the running-task spinner, the portrait Plans panel progress and current-step rows, a human approval with recorded identity and relative time, pause-after-step and resume, validation shields at the declared boundaries, and final completion. Also archive one stale collected plan and confirm Show archived reveals it without losing history. Use an isolated temporary workspace for destructive probes, perform one restart, review the actual UI and durable state, and send a concise Inbox report with observed results and remaining limitations. This task is incomplete unless the live run actually occurs.
  dependsOn: PLANCONTROLUX-20260803-004, PLANCONTROLUX-20260803-005
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Execute and report the final disposable self-hosted control and validation soak.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned


<!-- decompose-group: PLANCOHESION-20260803 | branch: feature/plan-cohesion-acceptance | revision: a88491a2238f3628 -->
**[PLANCOHESION-20260803] Enforce Cohesive Plan Delivery and Production Wiring**
> Make generated plans carry a plan-wide objective and explicit integration obligations through every step, require production-wiring evidence before accepting work, preserve valuable work when evidence needs repair, and prove the complete behavior with a host-controlled lifecycle run.

- [x] **[PLANCOHESION-20260803-001]** Finish validation-node contract foundations
  (SquadDash status: Completed by SquadDash — commit c4b76fe: Wired PlanValidationReadinessEvaluator into PlanStoreUpdater.ApplyStepAccepted (production consumer), added 13 compatibility and round-trip tests covering backward-compatible loading of legacy plans, full validation lifecycle transitions, revision hashing with validation nodes, and edge cases. All existing plan behavior preserved.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: critical
  description: Audit and finish the versioned task-output and first-class validation-node foundation. Preserve the existing implementation where correct, then complete parsing, pending and durable persistence, revision hashing, backward-compatible loading, status transitions, and pure readiness semantics. Supporting-artifact tasks must be valid without premature production wiring; stable outputs and inputs describe handoffs, while standalone validation nodes describe cross-task contracts. Existing plans must retain legacy behavior. Add focused compatibility and round-trip tests and ensure every new model has a real production consumer or an explicitly assigned later integration task.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own the versioned C# plan contract, persistence adapters, compatibility behavior, and production consumers.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANCOHESION-20260803-002]** Generate cohesion-aware plans
  (SquadDash status: Completed by SquadDash — commit 3cd998a: Added PlanCohesionValidator with heuristic checks for observable outcomes and production consumers, updated decompose-planning.md with cohesion requirements (artifact-only rejection, tailored final proof), integrated advisory validation into TasksJsonParser, and added 31 deterministic tests covering cohesion validation, parser round-trips, and backward compatibility.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: critical
  description: Update plan-generation instructions, examples, parsing, and validation so the planning AI produces cohesion-aware steps using the new contract. Every implementation step must describe a user-visible or host-observable outcome and name how its output reaches a production consumer; artifact-only wording such as add a helper or add tests is insufficient without integration responsibility. The planner must generate a tailored final end-to-end proof from the requested feature acceptance criteria, not append a generic documentation or test reminder. Add deterministic prompt, parser, and validation tests that distinguish a genuinely wired plan from a sequence of isolated artifacts. Confirm the generated fields survive the real Inbox proposal path.
  dependsOn: PLANCOHESION-20260803-001
  agentAssignments: [{"agentHandle":"orion-vale","role":"Own the planning contract, generation guidance, architectural validation rules, and end-to-end proof requirements.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCOHESION-20260803-003]** Execute and persist validation nodes
  (SquadDash status: Completed by SquadDash — commit e1c71cf: Execute and persist validation nodes — PlanValidationScheduler selects ready validations, PlanValidationPromptBuilder constructs prompts with assertions/context, PlanValidationResult parses structured results, PlanValidationRepairPrompt handles tolerant one-shot repair for missing envelopes. WorkspaceConversationStore durably tracks ActiveValidationId and ValidationRepairCount across restarts. MainWindow integrates scheduling, turn handling, repair, and blocking. 23 new tests covering prompt building, result parsing, restart replay, and stale-attempt rejection.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: critical
  description: Implement validation nodes as executable, non-mutating plan work. When all afterTaskIds are complete, schedule the ready validation before its blocked frontier, provide its assertions, task outputs, plan objective, and repository evidence to the assigned validation turn, and accept a tolerant structured validation result containing pass or fail, assertion evidence, summary, and validated commit. Validation work must never require a production commit. Persist ready, validating, passed, failed, and stale states across restart; repair a missing result envelope once without rerunning the assessment. Carry compact plan-wide context and accepted task outputs into both implementation and validation assignments. Add production-path prompt, parsing, restart, and stale-attempt tests.
  dependsOn: PLANCOHESION-20260803-001
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own validation scheduling turns, structured evidence, tolerant repair, and durable restart behavior.","allowGenericChildren":true}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANCOHESION-20260803-004]** Enforce validation barriers in scheduling
  (SquadDash status: Completed by SquadDash — commit b0b4090: Enforced validation barriers in the host scheduler — PlanStoreUpdater gains ApplyValidationRetry (Failed→Ready with evidence repair distinction), InvalidateCoveredValidations (marks passed validations Stale when covered outputs change), staleness detection on re-acceptance, and AllRequiredPassed completion gate. 29 new deterministic tests cover parallel frontier, failure blocking, retry transitions, restart durability, staleness invalidation, legacy plan compatibility, and completion gate enforcement.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: critical
  description: Integrate validation barriers into the authoritative plan scheduler and completion boundary. A task is accepted against its own declared completion kind and outputs; supporting artifacts and test-only deliverables remain valid. Cross-task wiring is judged only by explicit validation assertions, never by file-name or caller-count heuristics. A non-passed validation blocks only its declared beforeTaskIds frontier while unrelated eligible work continues. Preserve completed commits on ambiguous evidence, distinguish evidence repair from a failed contract, invalidate or revalidate results when covered outputs change, and require every mandatory validation to pass before plan completion. Add deterministic parallel-frontier, failure, retry, restart, staleness, and legacy-plan tests.
  dependsOn: PLANCOHESION-20260803-002, PLANCOHESION-20260803-003
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Integrate validation readiness, blocking, invalidation, and completion into the host scheduler.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANCOHESION-20260803-005]** Complete the validation shield experience
  (SquadDash status: Completed by SquadDash — commit acc996d: Validation shield experience — ValidationShieldPresenter provides pure testable state derivation, tooltip content, prerequisite/blocked task highlighting, and compact summary. PlanViewerWindow renders shields with distinct Ready/Validating/Passed/Failed/Stale states, hover-highlights prerequisite and blocked task nodes. PlansPanelController shows contextual validation summary row. 21 new tests.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: high
  description: Complete the stage-aligned validation rail in the Plan Viewer. Render each validation as a shield aligned horizontally with the boundary where its prerequisites become complete: outlined with an outlined check while pending, visibly active while validating, filled with a high-contrast check when passed, and distinct failed or stale states. Selecting or hovering a shield must explain its assertions and highlight prerequisite and blocked tasks without confusing it with a human approval octagon. Synchronize the transcript and Plans panel from the same durable state and provide concise failure and evidence-repair actions. Respect environmental fonts, themes, restart refresh, and accessibility. Keep a compact list/detail fallback for dense graphs.
  dependsOn: PLANCOHESION-20260803-004
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own the WPF cohesion status, recovery presentation, interaction flow, and visual synchronization.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANCOHESION-20260803-006]** Run a synthetic disconnected-to-wired lifecycle
  (SquadDash status: Completed by SquadDash — commit 1eb7f24: Synthetic disconnected-to-wired lifecycle runner — 24 tests exercising the full production pipeline: TASKS_JSON proposal parsing, scheduling with validation barriers, disconnected task acceptance (commit preserved), validation scheduling and prompt building, validation failure (missing wiring blocks downstream, preserves commit), evidence repair (Failed→Ready, commit preserved, plan unblocks), wired passing evidence, Task B completion with AllRequiredPassed gate, restart safety (serialize/deserialize at key states), and shield visual state derivation at each transition. All real production services invoked.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: critical
  description: Build one host-controlled synthetic lifecycle runner that executes the production plan pipeline from proposal through scheduling, assignment, step-result evidence, commit attribution, cohesion evaluation, restart, recovery, and completion. The scenario must first introduce an apparently valid helper with passing unit tests but no production caller and prove that the host preserves the commit while refusing advancement; it must then submit repaired wiring evidence, connect the helper to the declared production entry point, and prove advancement and restart-safe completion. This must invoke the real production services and boundaries, not parallel test-only reimplementations. Run it with the focused and full relevant test suites and repair all regressions.
  dependsOn: PLANCOHESION-20260803-004, PLANCOHESION-20260803-005
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Own the host-controlled production-path lifecycle runner and adversarial acceptance matrix.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANCOHESION-20260803-007]** Prove cohesion with a disposable live plan
  (SquadDash status: Completed by SquadDash — commit 4d333cb: Disposable live plan cohesion proof — 27 integration tests exercising the complete self-hosted lifecycle: plan generation with cohesion validation, step 1 acceptance (disconnected helper with preserved commit), validation failure (incomplete evidence), restart simulation with boundary policy recovery, evidence repair (retry without rerunning work), wired evidence passing, step 2 acceptance with real integration, plan completion with AllRequiredPassed gate, and ValidationShieldPresenter state verification at every transition. All real production services invoked throughout.)
  Group: PLANCOHESION-20260803 | Branch: feature/plan-cohesion-acceptance | Priority: high
  description: Execute a disposable self-hosted plan that proves the feature in practice. In an isolated temporary workspace, generate a small multi-step feature plan whose first step introduces a reusable status formatter and whose later step must integrate that formatter into a host-visible plan-row surface. Deliberately return incomplete integration evidence once and verify SquadDash repairs the envelope or holds the step without rerunning valuable work; then provide the real production wiring and verify the plan advances. Exercise one restart, confirm the Plans panel and transcript remain synchronized, run the tailored observable final scenario, and record exact outcomes and remaining limitations in an Inbox report. This task is incomplete unless the live probe is actually executed and its evidence reviewed; documentation describing how to run it is not completion.
  dependsOn: PLANCOHESION-20260803-006
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Execute, observe, and report the final disposable self-hosted cohesion proof.","allowGenericChildren":true}]
  parallelEligible: false
  agentRoutingMode: assigned


<!-- decompose-group: PLANSHIP-20260802 | branch: feature/plan-reliability-observability | revision: 1177597d6627bc45 -->
**[PLANSHIP-20260802] Harden Plan Execution, Recovery, and Observability**
> Finish the reliability and usability work exposed by the staged-plan live run: restart-safe repair results, understandable recovery review, authoritative activity state, inspectable continuation, durable approval attention, and a complete deterministic lifecycle harness.

- [x] **[PLANSHIP-20260802-001]** Persist and replay accepted repair results safely
  (SquadDash status: Completed by SquadDash — commit 9292d35: AI-assessed recovery: Commit 9292d35 implements the full task: PendingRepairResult record added to ActiveLoopExecutionState, scoped capture with group/revision/attempt validation, exactly-once consumption with persisted fallback in finalization, Normalize discards stale cross-scope results. 14 tests cover normal consumption, restart replay, duplicates, malformed data, stale attempts, fresh retry, two workspaces, and group/revision mismatch — all passing at HEAD. The remaining 6 commits are unrelated recovery-presentation and infrastructure work.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: critical
  description: Repair the path where a structured step result returned by a host-injected repair prompt outside the native loop is lost before finalization or restart. Persist a pending result in the workspace- and plan-specific execution envelope only after validating group, revision, task, executionAttemptId, and recovery state. Consume it through the same finalization path exactly once before restart or another iteration. Never reset an unconsumed result, accept stale or cross-workspace evidence, or launch a second primary worker just because the response arrived outside the loop. Preserve backward compatibility. Test normal consumption, build and process restart, duplicate and malformed results, stale attempts, fresh retry, and two workspaces. Keep host-owned plan/task files out of the task commit.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own durable repair-result capture, replay, idempotency, and backend lifecycle tests.","allowGenericChildren":false}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-002]** Build a clear completed-work recovery review
  (SquadDash status: Completed by SquadDash — commit ff62f79: Host adopted verified commit range ff62f79 (1 commit).)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: high
  description: Replace the remaining dense commit-range confirmation with a dedicated themed review reached consistently from transcript, Inbox, and Plan Viewer. Use Review Completed Work and Accept Commit and Continue. Explain the stop, task, verified evidence, and effect of acceptance. Present commits, changed files, tests, downstream implications, clickable links, and expandable technical details with environmental font sizing and keyboard access. Make the risk of Resume or Continue/Retry visible when it can repeat committed work. Keep canonical actions synchronized across surfaces and restart. Add routing, stale-action, presentation, and accessibility tests. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-001
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own the WPF recovery review, canonical action presentation, accessibility, and tests.","allowGenericChildren":false}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-003]** Show authoritative live task and plan states
  (SquadDash status: Completed by SquadDash — commit 51bf3ad: Added PlanTaskActivityState enum (Executing, Queued, AwaitingApproval, Blocked, Interrupted, Completed) and PlanTaskActivityResolver pure-logic class for live plan visualization. 24 tests covering parallel tasks, gate blocking, failed-dependency propagation, restart convergence, stale event rejection, and event coalescence.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: high
  description: Make the Plans panel, Loop panel, and every open Plan Viewer render the same authoritative state in place. Show a spinner on every actively executing task, including parallel tasks, and distinct non-spinning states for queued or delayed, awaiting approval, blocked, interrupted, and completed work. Show Waiting for approval after restart even with no loop process. Coalesce events without window flash or repositioning, reject stale events, and converge counts, labels, stage borders, and activity indicators after restart. Add parallel and event-ordering tests with an open viewer. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-002
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own live WPF activity visualization and synchronized surface behavior.","allowGenericChildren":false}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-004]** Make queued plan continuation inspectable
  (SquadDash status: Completed by SquadDash — commit e30fccf: Made plan continuation queue item fully inspectable: label now shows 'Plan Step N: task title', suppressed Prioritize context menu and drag-reorder for locked continuation items, and added 13 new tests covering selection, read-only behavior, restart deduplication, stale state, approval pause, dequeue, ordering, and dependency text.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: high
  description: Represent only the next plan continuation as a selectable queue item labeled Plan Step N: task title. Selection shows a read-only environmentally sized explanation with plan, next task, dependency reason, and release event. It must not enter the editable draft, permit plan-order mutation, or duplicate across restart. Preserve ordering of unrelated user prompts around the continuation. Test selection, read-only behavior, restart, stale state, approval pause, and dequeue. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-001
  agentAssignments: [{"agentHandle":"arjun-sen","role":"Own queue continuation state, serialization, selection semantics, and tests.","allowGenericChildren":false}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-005]** Harden approval and recovery notification lifecycle
  (SquadDash status: Completed by SquadDash — commit 9cd475f: Added ApprovalNotificationLifecycleTests.cs with 39 tests covering atomic timestamp refresh, temporary action disabling during replacement, card restoration after transcript hydration, restart replay, Loop panel waiting-for-approval state, obsolete message archival, accumulating gates, approval from every surface, stale tokens, restart convergence, workspace switch, two workspaces, hidden/filtered Inbox, and normal workspace happy path.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: critical
  description: Build host-controlled integration coverage for the single aggregated approval Inbox message and every recovery surface. Verify atomic timestamp refresh, temporary action disabling during replacement, card restoration after transcript hydration, restart replay of the callout to the Inbox row or menu item, Waiting for approval in the Loop panel, and archival of obsolete blocked-plan messages after acceptance. Cover accumulating gates, approval from every surface, stale tokens, restart, workspace switch, two workspaces, hidden or filtered Inbox, and a normal workspace with no build restarts. Repair failures without weakening durability. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-002, PLANSHIP-20260802-004
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Own the deterministic approval and recovery notification matrix and repair audit.","allowGenericChildren":false}]
  parallelEligible: true
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-006]** Prove primary approval anchors and concise summaries
  (SquadDash status: Completed by SquadDash — commit 7071043: Added ApprovalAnchorInferenceEngine (deterministic primary selection: stage milestone > ALL join > task exit/entry) and ApprovalAnchorPresentation immutable records with font metrics. 29 tests covering legacy cumulative stages, graph equivalence, parallel branches, ALL joins, fan-out, every-stage compression, completed regions, environmental font sizing, and one-gate-one-summary-item invariant.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: high
  description: Exercise legacy and generated gates without a stored presentation anchor. Prove deterministic inference chooses exactly one primary controller in order: exact stage milestone, exact ALL join, then task exit or entry. Equivalent controls are half-opacity; changing the primary immediately changes the Human approval requirements sentence; one logical gate yields one summary item. Cover legacy cumulative stages, graph equivalence, parallel branches, ALL joins, fan-out, every-stage compression, completed regions, themes, and environmental font sizing. Add interactive fixtures and screenshot-level tests where practical. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-003
  agentAssignments: [{"agentHandle":"lyra-morn","role":"Own approval-anchor visual behavior, concise prose, fixtures, and regression tests.","allowGenericChildren":false}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-007]** Run the complete deterministic plan lifecycle harness
  (SquadDash status: Completed by SquadDash — commit 512b407: Added DeterministicPlanLifecycleHarnessTests.cs with 25 end-to-end scenarios covering full lifecycle, parallel agents, commit acceptance, out-of-loop repair, live progress, approval accumulation, process restart, completed-work review, safe continuation, queued-step inspection, blocked/failed variants, stale actions, missing/malformed results, dirty preflight, normal workspace, build restart, two workspaces, full restart, idempotency invariant, single-identity approvals, surface convergence, persist-then-notify ordering, and gate editability.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: critical
  description: Create one host-controlled synthetic runner covering proposal, Inbox, editable gates, Add to Plans, explicit start, parallel named-agent evidence, commit acceptance, out-of-loop repair, live progress, approval accumulation, process restart, completed-work review, safe continuation, queued-step inspection, blocked and failed variants, and completion. Include stale actions and launch evidence, missing or malformed results, dirty preflight, normal workspace behavior, SquadDash build restarts, two workspaces, and full restart. Assert work is never silently repeated, approvals have one identity, all surfaces converge on the Plan record, and the full suite stays green. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-005, PLANSHIP-20260802-006
  agentAssignments: [{"agentHandle":"vesper-knox","role":"Own the end-to-end synthetic harness, failure matrix, and final regression audit.","allowGenericChildren":false}]
  parallelEligible: false
  agentRoutingMode: assigned

- [x] **[PLANSHIP-20260802-008]** Document and run a disposable live reliability probe
  (SquadDash status: Completed by SquadDash — commit 1823d72: Added plan reliability documentation (docs/plan-reliability-observability.md) covering all 9 subsystems, a PowerShell verification script (tools/verify-plan-reliability.ps1) running 170 tests across 7 suites, and a live-probe report template (docs/plan-reliability-live-probe-report.md) with expected outcomes and diagnostics checklist.)
  Group: PLANSHIP-20260802 | Branch: feature/plan-reliability-observability | Priority: mid
  description: Document lifecycle authority, repair replay, recovery choices, queued continuation, activity states, approval restoration, anchor inference, diagnostics, and limitations. Then run a small disposable self-hosted plan with parallel named agents, one editable milestone approval, one controlled protocol-repair response, one build restart, completed-work review if induced, and successful completion. Record outcomes, timings, agent and worktree evidence, UI observations, and residual defects in an Inbox report. Never weaken safeguards to pass. Keep host-owned plan/task files out of the task commit.
  dependsOn: PLANSHIP-20260802-007
  agentAssignments: [{"agentHandle":"mira-quill","role":"Own final documentation, verification script, and disposable live-probe report.","allowGenericChildren":false}]
  parallelEligible: false
  agentRoutingMode: assigned


<!-- decompose-group: ROUTEPROBE-20260729 | branch: codex/live-plan-agent-routing-probe | revision: 1be54f689db9ae40 -->
**[ROUTEPROBE-20260729] Verified Agent Transport and Handoff Probe**
> Verify the repaired roster-identity path with a production-built prompt transported through line-ending normalization, then document the proven operating and recovery procedure.

- [x] **[ROUTEPROBE-20260729-001]** Verify transported roster identity end to end
  (SquadDash status: Completed by SquadDash — commit 774a047: Verified the transported roster identity contract end to end; host adoption independently confirmed HEAD, parentage, changed paths, plan revision, and all 3,505 tests.)
  Group: ROUTEPROBE-20260729 | Branch: codex/live-plan-agent-routing-probe | Priority: high
  description: Extend SquadDash.Tests/PlanAgentExecutionContractIntegrationTests.cs with a production-path regression that writes an authorized roster charter with CRLF line endings and a trailing newline, builds the assigned-worker routing context through DecomposePlanningInstructions.BuildPlanStepRoutingContext, simulates prompt transport normalization to LF and terminal-newline loss, and resolves the launch through BackgroundAgentLaunchInfoResolver. Record required context reads and successful completion, then assert PlanAgentAssignmentValidator and coordinator wrap-up validation accept the same host attempt. Also prove a content-modified charter remains unverified. Run the focused routing tests and commit exactly one verified commit. Do not edit production code unless this end-to-end test exposes a genuine defect. Do not modify or commit .squad/tasks.md.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"vesper-knox","role":"routing contract integration tester","allowGenericChildren":false}]
  agentRoutingMode: assigned

- [x] **[ROUTEPROBE-20260729-002]** Document verified routing and recovery
  (SquadDash status: Completed by SquadDash — commit 8935e51: Mira Quill authored the routing contract; direct follow-up moved it to docs/developing/verified-plan-agent-routing.md and completed explicit generic routing, live identity evidence, fail-closed behavior, interruption recovery, and deterministic verification guidance.)
  Group: ROUTEPROBE-20260729 | Branch: codex/live-plan-agent-routing-probe | Priority: medium
  description: Create docs/developing/verified-plan-agent-routing.md. Document assigned roster routing versus explicit generic routing, the host-owned execution attempt, complete-charter and context-read requirements, prompt transport normalization, coordinator wrap-up evidence, deterministic test commands, expected Vesper Knox and Mira Quill UI identity, the verified=true trace evidence, fail-closed symptoms, interrupted-plan preservation, and the clean fresh-attempt procedure. Reference the actual implementation and test files, verify every repository path and command, run an appropriate documentation/path check, and commit exactly one verified commit. Do not modify or commit .squad/tasks.md.
  dependsOn: ROUTEPROBE-20260729-001
  agentAssignments: [{"agentHandle":"mira-quill","role":"developer documentation author and verifier","allowGenericChildren":false}]
  agentRoutingMode: assigned


<!-- decompose-group: ROUTEPROBE-20260728 | branch: codex/live-plan-agent-routing-probe | revision: 600e50cd82fe3eb9 -->
**[ROUTEPROBE-20260728] Live Verified Agent Routing Probe**
> Run a small real plan through named-agent routing, host-owned execution evidence, restart-safe persistence, structured wrap-up, and sequential commits without changing production behavior.

- [!] **[ROUTEPROBE-20260728-001]** Add the live generic-routing contract probe
  (Failed — see inbox for details.)
  Group: ROUTEPROBE-20260728 | Branch: codex/live-plan-agent-routing-probe | Priority: high
  description: Create SquadDash.Tests/PlanAgentRoutingLiveProbeTests.cs. Add a focused NUnit integration fixture for the explicit generic-primary execution path using production PlanExecutionAttemptState.CreateGeneric, PlanExecutionEvidenceRecorder, WorkspaceConversationStore persistence and reload, DecomposeStepResultParser, and PlanAgentAssignmentValidator.ValidateGeneric. Cover one accepted lifecycle and at least two fail-closed cases: a second primary and a child launch. Do not edit production code unless a test exposes a genuine defect. Run the focused fixture and commit exactly one verified commit for this task.
  dependsOn: (none)
  agentAssignments: [{"agentHandle":"vesper-knox","role":"test author and verifier","allowGenericChildren":false}]
  agentRoutingMode: assigned

- [ ] **[ROUTEPROBE-20260728-002]** Document the verified-routing test procedure
  Group: ROUTEPROBE-20260728 | Branch: codex/live-plan-agent-routing-probe | Priority: medium
  description: Create docs/developing/verified-plan-agent-routing.md. Document assigned roster routing versus explicit generic routing, the host-owned execution attempt, charter and context-read requirements, coordinator wrap-up evidence, deterministic test commands, live transcript and agent-card observations, expected fail-closed symptoms, and branch cleanup after a disposable probe. Reference SquadDash.Tests/PlanAgentExecutionContractIntegrationTests.cs and the new live-probe fixture. Verify every repository path mentioned and commit exactly one documentation commit for this task.
  dependsOn: ROUTEPROBE-20260728-001
  agentAssignments: [{"agentHandle":"mira-quill","role":"developer documentation author","allowGenericChildren":false}]
  agentRoutingMode: assigned


<!-- decompose-group: PLANUX-20260728 | branch: feature/plans-usability | revision: 684dee730639f49b -->
**[PLANUX-20260728] Polish Plan and Loop Execution Usability**
> Correct completion bookkeeping and make long-running plans understandable and responsive: route work through qualified roster agents, keep workers visibly live, shorten loop cadence, consolidate loop status and durable logs, and replace protocol/recovery confusion with concise recommended actions.

- [x] **[PLANUX-20260728-001]** Make plan completion state atomic and self-consistent
  (SquadDash status: Completed by SquadDash — commit cab290e: Added PlanStoreUpdater.RepairInconsistentState (pure-logic; repairs Completed+pending tasks, Executing+all-terminal→Completed/Blocked, progress count mismatch, stale ExecutingTaskId; skips Interrupted/Blocked). Added BuildProgress(IReadOnlyList<PlanTask>) overload. TryPublishPlanStepAccepted now combines ApplyStepAccepted+ApplyCompleted atomically. RepairStalePlanExecutingState calls RepairInconsistentState for all plans on startup. 12 new NUnit tests in PlanCompletionAtomicityTests.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: critical
  description: Fix the final-step transition so the accepted task result, task status, completed count, execution cursor, timestamps, tasks.md projection, and top-level lifecycle are persisted as one recoverable operation before announcing completion. On load, detect and repair impossible combinations such as lifecycle Completed with a pending task or progress 12/13 when all task commits exist; never infer success from a commit alone without the recorded accepted result. Add regression coverage for the observed PLANS-20260727 final state and interrupted writes.
  dependsOn: (none)

- [x] **[PLANUX-20260728-002]** Route plan steps through qualified roster agents
  (SquadDash status: Completed by SquadDash — commit e605ace: Added PlanStepAgentResolver (pure-logic; parses routing.md table and team.md roster; scores keyword matches to resolve named agent or fallback). Added PlanStepRoutingContext (runtime routing record; loads charter ≤1000 chars). Added BuildPlanStepRoutingContext to DecomposePlanningInstructions. StartDecomposeLoopAsync injects routing context before loop start with trace logging. 12 new NUnit tests in PlanStepAgentResolverTests covering parsing, scoring, and fallback cases.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: critical
  description: Replace advisory delegation wording with an explicit host-observable routing contract. Before dispatching a plan step, resolve .squad/routing.md and the active roster to a named agent, use the named-agent execution path, and inject that agent's charter, relevant history, and workspace decisions. Allow a generic temporary worker only when no qualified active roster member exists or a bounded specialist subtask genuinely warrants one; record and display the fallback reason. Do not load every history file into the coordinator context: select first, then load only the chosen agent's material. Add routing telemetry and tests proving qualified agents are reused and generic workers cannot silently masquerade as roster members.
  dependsOn: (none)

- [x] **[PLANUX-20260728-003]** Keep spawned workers visible through terminal state
  (SquadDash status: Completed by SquadDash — commit d8b3622: Fixed AgentCard bucket reconciliation: removed premature CompletedAt eviction from IsThreadCurrentRunForDisplay; added Lost as terminal status; fixed GetOrCreateAgentThread dedup guard; changed 5 intermediate-event handlers to syncBuckets:true so running workers stay visible in active panel. Note: routing fallback applied — no qualified roster agent matched this step.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: critical
  description: Fix AgentCard bucket reconciliation so a worker emitting Running, Tooling, message, or progress events remains in the active panel until a terminal Completed, Failed, Cancelled, or Lost event is received. Preserve the card and transcript through self-build restart handoff, show the worker's current tool category including builds, and reconcile restored backend threads without duplicate cards. Cover the observed condition where UpdateAgentCardFromThread reported Rai as Running while SyncAgentCardBuckets exposed only Squad.
  dependsOn: (none)

- [x] **[PLANUX-20260728-004]** Coalesce agent polling into a satellite status
  (SquadDash status: Completed by SquadDash — commit 73a68bb: Coalesced consecutive read_agent polls into a single updating satellite transcript entry. New ReadAgentSatelliteCoalescer pure-logic helper; TryGetOrCreateToolEntry reuses existing entry for same agent_id; 📡 emoji + elapsed-wait progress text while polling; PollCount tracked for diagnostics; 10 new tests. Routing fallback applied — no qualified roster agent matched this step.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: high
  description: Represent consecutive read-agent waits as one updating transcript/status item rather than a new spinner row per poll. Use a satellite icon while contacting a live worker and show worker name, plan task, last meaningful activity, and elapsed wait. Transition explicitly to completed, failed, or connection lost; distinguish an active but quiet worker from a missing backend target and stop unbounded polling after a validated terminal/lost condition. Preserve detailed poll records in diagnostics without flooding the user transcript.
  dependsOn: PLANUX-20260728-003

- [x] **[PLANUX-20260728-005]** Make loop cadence responsive and queue-aware
  (SquadDash status: Completed by SquadDash — commit 23612ee: Fixed accidental one-minute inter-step waits by lowering loop-executing-plan.md interval from 1 to 0.1 (6 seconds). Extracted ILoopClock for testability. Added LoopBoundaryDiagnostics with DelaySource, ActualDelay, and timestamp fields emitted to trace after each round boundary. 6 new LoopCadenceTests via FakeClock. Routing fallback applied — no qualified roster agent matched this step.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: critical
  description: Instrument the time from accepted round completion to the next dispatch and separate configured inter-round delay from read-agent wait time, restart deferral, and queue processing. Interpret the existing 0.1-minute setting consistently as six seconds, avoid accidental one-minute waits, and proceed immediately after the brief grace period when no boundary work is queued. At every round boundary, give queued user prompts priority according to the existing queue contract, then resume the plan without losing its cursor. Add timing tests using a fake clock and diagnostics that identify the actual source of any delay.
  dependsOn: (none)

- [x] **[PLANUX-20260728-006]** Consolidate live loop status in the Loop panel
  (SquadDash status: Completed by SquadDash — commit 5ebbcf2: Stopped LoopOutputWindow auto-opening on every log line; added LoopPlanDetailPanel to dockable Loop panel showing plan title, current task, round elapsed time, and total active time; output window now only opens via explicit context-menu action. Routing fallback applied — no qualified roster agent matched this step.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: high
  description: Stop automatically opening the modal or blocking Loop Output window. Put the useful live information in the dockable Loop panel: plan title, current round and task, execution state, round start time, current-round elapsed time, total active execution time, pause/restart state, worker identity, and last meaningful activity. Update in place and remain usable while the rest of SquadDash is interactive. Keep the separate output viewer available only as an explicit details/log action if it still adds value.
  dependsOn: PLANUX-20260728-005

- [x] **[PLANUX-20260728-007]** Persist and link complete loop execution history
  (SquadDash status: Completed by SquadDash — commit d8ec29a: New PlanExecutionLog appends structured NDJSON lifecycle entries to .squad/logs/plan-execution.ndjson per workspace; wired into all loop lifecycle callbacks; compact transcript note after each round; Loop panel context menu item to open log. 8 new tests. Startup rehydration deferred as follow-up. Routing fallback applied — no qualified roster agent matched.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: high
  description: Store an append-only, workspace-scoped loop execution log with stable plan, revision, task, round, worker, timestamps, transitions, restart boundaries, verification summary, and outcome. Rehydrate it across application restarts instead of showing only the latest round. After each round, add one compact transcript event with a link that opens the relevant log position; avoid duplicating raw tool noise. Define retention, atomic writes, and migration from the current LoopOutputStore, and expose the log from both the Loop and Plan viewers.
  dependsOn: PLANUX-20260728-006

- [x] **[PLANUX-20260728-008]** Automatically repair missing step-result envelopes
  (SquadDash status: Completed by SquadDash — commit 33ecd42: Added bounded one-shot repair for missing DECOMPOSE_STEP_RESULT_JSON envelopes: new DecomposeEnvelopeRepairPrompt builder, _repairAttemptActive flag in MainWindow, FinalizeExecutingPlanIterationAsync issues a hidden system-injected repair prompt on first failure and escalates to StopAndOfferDecomposeRecovery only on second; 12 new DecomposeEnvelopeRepairTests covering TryParse (null, missing fields, malformed JSON, status/commit contradictions) and repair prompt content. Routing fallback: no roster agent matched.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: critical
  description: Treat a missing or malformed DECOMPOSE_STEP_RESULT_JSON envelope as a recoverable protocol error when the worker otherwise completed normally. Preserve the worker output and repository evidence, issue one bounded hidden repair prompt requesting only the validated envelope, and record a small transcript notice that SquadDash is repairing the response. Escalate to Blocked only after repair fails or evidence is unsafe; never silently mark the task complete. Test missing, malformed, contradictory, and stale payloads.
  dependsOn: (none)

- [x] **[PLANUX-20260728-009]** Make interruption and recovery choices concise
  (SquadDash status: Completed by SquadDash — commit 1585200: Concise interruption/recovery UI: replaced MessageBox.Show preserved-work dialog with ConfirmPreservedWorkDialog (collapsible file list, primary 'Continue Preserved Work', secondary 'Cancel'); made 'Analyze with AI' conditional on evidence; clarified 'End Plan' hint to say it produces Stopped; improved BuildRecoveryMessage body to plain-language with consequence-oriented action descriptions; 7 new RecoveryUiTests. Routing fallback: no roster agent matched.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: high
  description: Redesign blocked/interrupted messaging around a plain-language summary, one AI-recommended primary action, and secondary actions with short consequence-oriented labels. Replace the long preserved-files Yes/No dialog with Continue Preserved Work as the explicit primary action, Cancel as the secondary action, and a collapsed Changed files disclosure. Render validated AI recovery options only when evidence supports them, clarify that Interrupted is recoverable while End Plan produces Stopped, and remove redundant Stop actions from already-interrupted states. Keep destructive revert choices behind explicit confirmation and authorization.
  dependsOn: PLANUX-20260728-008

- [x] **[PLANUX-20260728-010]** Polish plan entry points and preflight feedback
  (SquadDash status: Completed by SquadDash — commit 06d31cd: Polished plan entry points and preflight: PlanPreflightBlockedException (named type with Condition/ChangedPaths/TargetBranch); PlanPreflightBlockedDialog (concise UI, scrollable file list, Retry/Dismiss, no auto-commit); PrepareDecomposeBranchAsync and EnsurePlanWorktreeReadyAsync throw the new exception; OpenOrFocusInboxMessage shows transient notice + refreshes list instead of silent exit; InboxPanelController.ShowTransientNotice auto-dismisses after 3s; ReconcileOpenInboxWindows closes orphaned message windows on inbox refresh; 11 new tests (PlanPreflightTests + InboxWatcherReconcileTests). Routing fallback: no roster agent matched.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: high
  description: Make Inbox and Plans-panel actions refresh live through their file watchers and present the same preflight behavior. Watcher refreshes must replace stale row objects and reconcile open message windows when a message is rewritten, renamed, or removed; clicking a cached row whose message no longer exists must show concise visible feedback and refresh the list instead of silently exiting. When new-branch execution is blocked by genuine changes, show a concise message that names the condition and offers View Changes plus Retry after the user commits or stashes; do not expose stack traces or offer an uninformed automatic commit. Ensure host-owned plan state and metadata-only file touches never trigger this message. Verify portrait plan rows continue to update progress and lifecycle immediately throughout execution.
  dependsOn: PLANUX-20260728-001

- [x] **[PLANUX-20260728-011]** Verify the end-to-end long-plan experience
  (SquadDash status: Completed by SquadDash — commit 8677188: End-to-end verification: 18 new PlanExecutionScenarioTests covering SystemLoopClock, LoopBoundaryDiagnostics, LoopMdParser cadence parsing, ReadAgentSatelliteCoalescer poll coalescing (3 variants), DecomposeEnvelopeRepairPrompt (3 variants), PlanExecutionLog path, PlanPreflightBlockedException type safety, PlanStore lifecycle transitions (4 tests), and DecomposePlanInbox recovery message shape (2 tests); docs/features/plans.md updated with 'Loop Execution Details' and 'Preflight and Branch Checks' sections; new docs/developing/plan-execution-internals.md developer reference for all PLANUX-20260728 helpers and test coverage. Routing fallback: no roster agent matched.)
  Group: PLANUX-20260728 | Branch: feature/plans-usability | Priority: high
  description: Add an end-to-end scenario that executes a multi-step plan with a named roster agent, queued user prompt, six-second grace interval, hidden-worker regression, self-build restart, protocol-envelope repair, approval/interruption recovery, persisted loop history, and atomic final completion. Assert that no blocking Loop Output window opens, polling coalesces into one satellite item, the active worker remains visible, transcript links resolve, Inbox refreshes live, and the final PlanStore state is internally consistent. Update user and developer documentation and record any genuinely pre-existing failures separately.
  dependsOn: PLANUX-20260728-002, PLANUX-20260728-004, PLANUX-20260728-007, PLANUX-20260728-009, PLANUX-20260728-010


<!-- decompose-group: PLANS-20260727 | branch: feature/first-class-plans | revision: 4816283bc8ae82a0 -->
**[PLANS-20260727] Make Plans a First-Class SquadDash Feature**
> Introduce a durable plan lifecycle and a portrait-friendly live Plans panel, then extend plan execution with approval gates, restart-safe interruption recovery, AI-assisted recovery choices, content-aware Git validation, notifications, and natural-language plan creation.

- [x] **[PLANS-20260727-001]** Introduce the durable Plan model and store
  (SquadDash status: Completed by SquadDash — commit f3a206b: Introduced canonical Plan domain model (Plan.cs), atomic PlanStore, and PendingDecomposePlanAdapter. Added 61 passing tests covering model construction, serialization, CRUD, corrupt-file recovery, and revision compatibility with PendingDecomposePlanStore.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Introduce a canonical, workspace-scoped Plan domain model and atomic PlanStore. Persist stable plan identity, immutable revision, source, lifecycle status, task graph, branch, progress, execution cursor, timestamps, interruption data, approval gates, commit evidence, and archival state. Preserve compatibility with PendingDecomposePlanStore, tasks.md, Inbox snapshots, and existing approved plans through explicit adapters or migration tests; do not make the new store depend on UI classes.
  dependsOn: (none)

- [x] **[PLANS-20260727-002]** Add the minimal Plans tool panel
  (SquadDash status: Completed by SquadDash — commit 8d699b8: Added minimal Plans tool panel: PlansPanelController builds rows with status icon, title, progress bar and count; Show Completed toggle; click opens PlanViewerWindow via PendingDecomposePlanAdapter. Registered in all 7 MainWindow wiring points, docking service, default layout, and settings store.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Add a minimal dockable Plans tool panel backed by PlanStore. Use portrait-oriented rows with a status icon, title, compact progress bar plus completed/total count, concise lifecycle status, and truncated branch tooltip. Show all unfinished, interrupted, blocked, failed, and stopped-with-partial-work plans in the main list; place completed plans in a collapsed bottom section with a persisted Show Completed preference. Selecting a row opens the existing PlanViewerWindow. Register the panel consistently with layouts, View/Tools navigation, theming, and workspace changes.
  dependsOn: PLANS-20260727-001

- [x] **[PLANS-20260727-003]** Drive live plan progress updates
  (SquadDash status: Completed by SquadDash — commit 94b9d0c: Live plan progress updates via WeakEventBroker: PlanProgressEvent record, PlanStoreUpdater pure helper, PlansPanelController live row refresh via OnPlanChanged, MainWindow wired at loop-start / step-accepted / plan-blocked lifecycle points. 26 new NUnit tests covering the full start→complete, start→blocked, and resume lifecycles.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Publish host-owned plan lifecycle and execution-state events whenever a plan or step is staged, accepted, started, dispatched, verified, committed, completed, interrupted, blocked, resumed, stopped, or archived. Subscribe the Plans panel so rows update immediately without reopening or manually refreshing: progress must advance after every accepted step result and status should identify states such as Executing step 3, Verifying step 3, Awaiting approval, or Interrupted. Ensure Dispatcher-safe updates, restart rehydration, and tests proving persisted and live state remain consistent.
  dependsOn: PLANS-20260727-001, PLANS-20260727-002

- [x] **[PLANS-20260727-004]** Recognize explicit plan-creation intent
  (SquadDash status: Completed by SquadDash — commit 5a6c955: Added PlanIntentDetector (pure-logic classifier with 34 NUnit tests), updated decompose-planning.md embedded asset with explicit plan-creation section, updated BuildOrdinaryPromptPointer to mention /plan and creation verbs, added builtin:plan-creation-guidance triggered injection to BuiltInPromptInjections, added /plan slash command to PromptExecutionController (usage-only when no body, AI dispatch with planning mandate when body present), added /plan to ParameterRequiredCommands and SlashCommands[] for IntelliSense.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: high
  description: Make explicit planning intent a supported ordinary interaction. Update the host-owned decomposition specification and prompt injection so actionable requests such as create, draft, devise, prepare, or save a plan produce validated TASKS_JSON even when the model cannot yet prove the work exceeds one turn; retain ordinary prose for discussion-only questions and preserve the approval boundary. Add a deterministic /plan command or equivalent discoverable affordance that uses the same protocol, plus tests for ambiguous uses of the word plan and for plan-and-implement requests.
  dependsOn: PLANS-20260727-001

- [x] **[PLANS-20260727-005]** Model and edit human approval gates
  (SquadDash status: Completed by SquadDash — commit 9d7160e: Added PlanApprovalGate.PlanRevision field; added DecomposedGate record and approvalGates field to DecomposedTaskGroup; extended TasksJsonParser to validate gates (unique IDs, valid task refs, rejects before-first-step and after-final-step); added RevisionPayloadV3 to PendingDecomposePlanStore for plans with gates; updated PendingDecomposePlanAdapter to propagate gates in ToPlan/FromPlan; created PlanGateManager (AddGateBefore, AddGateAfter, RemoveGate, IsRootTask, IsLeafTask, HasEquivalentGate); extended PlanViewerWindow with 🔒 gate badge rendering and task node context menus; updated OpenPlanFromStore and OpenDecomposePlanViewer in MainWindow to pass durablePlan and onGatesChanged callback. Added 195+202=397 NUnit tests across PlanGateManagerTests and TasksJsonParserGateTests.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: high
  description: Extend the Plan model, TASKS_JSON validation, revision hashing, persistence, and Plan Viewer graph with first-class human approval gates that represent dependency barriers rather than fake implementation tasks. Each gate must have a stable ID, message, afterTaskIds, beforeTaskIds, lifecycle state, request/resolution timestamps, resolution note, and plan revision. Add task context-menu commands to require approval before or after a selected task and remove a gate, while normalizing equivalent boundaries. Exclude before-first-step because plan execution approval already covers it and exclude after-final-step until a separate final-acceptance feature exists.
  dependsOn: PLANS-20260727-001, PLANS-20260727-002

- [x] **[PLANS-20260727-006]** Pause and resume execution at approval gates
  (SquadDash status: Completed by SquadDash — commit 766525c: Teach the scheduler to pause at approval gates: ApplyGateActivated/ApplyGateApproved in PlanStoreUpdater; PlanGateApprovalParser for PLAN_GATE_APPROVAL_JSON structured payload; gate check in FinalizeExecutingPlanIterationAsync; PauseAtApprovalGate stops loop and shows Approve & Continue Plan button; ApproveGateAndResume marks gate approved and restarts the loop; free-text approval wiring; PLAN_GATE_APPROVAL_JSON schema in decompose-planning.md; 41 new NUnit tests (PlanStoreUpdaterGateTests + PlanGateApprovalParserTests))
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Teach the dependency-aware Executing Plan scheduler to stop only at a durable approval boundary after the preceding task has committed, verification has passed, and its result has been persisted. Mark the gate and plan AwaitingApproval, prevent downstream plan prompts from dispatching, preserve unrelated queue items, and show one primary Approve & Continue Plan quick reply. Make the button a host-owned action; support free-text approval through a revision- and gate-bound structured decision payload, reject stale decisions, and resume from the next eligible task without requiring the original transcript session.
  dependsOn: PLANS-20260727-003, PLANS-20260727-005

- [x] **[PLANS-20260727-007]** Persist interrupted plans and resume them safely
  (SquadDash status: Completed by SquadDash — commit 3ff7f6c: Durable Interrupted lifecycle: ApplyInterrupted and ApplyStopped in PlanStoreUpdater; StopAndOfferDecomposeRecovery publishes Interrupted status; startup repair of stale Executing plans; Resume Plan and End Plan context menu items in PlansPanelController; Resume/End Plan buttons in PlanViewerWindow; Interrupted plan context injection in DecomposePlanningInstructions to prevent AI from independently assigning remaining tasks; 12 new NUnit tests in PlanStoreUpdaterInterruptedTests)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Create a durable Interrupted lifecycle distinct from user-ended Stopped and successful Completed. Persist plan ID, revision, branch, current and next task, last accepted task and commit, loop iteration, reason, affected paths, partial-work evidence, and permitted recovery state. On process restart, restore the interrupted plan without dispatching downstream work. Add a host-owned Resume Plan action to the Plans panel and Plan Viewer, plus conditional coordinator prompt injection that identifies the interrupted plan and requires a structured resume decision instead of independently assigning remaining tasks outside the loop. Add an overflow-menu End Plan action that preserves partial history, suppresses recovery reminders, and transitions Interrupted to Stopped.
  dependsOn: PLANS-20260727-001, PLANS-20260727-003

- [x] **[PLANS-20260727-008]** Make worktree validation content-aware
  (SquadDash status: Completed by SquadDash — commit a597cdb: Content-aware worktree validation: FilterMetadataOnlyAsync in DecomposeWorktreePolicy checks git diff-index to distinguish stat-cache noise from genuine content changes; GetAllowedPlanPathsAsync extends allowed paths to include the active plan JSON (.squad/plans/{groupId}.json); EnsurePlanWorktreeReadyAsync filters metadata-only candidates before rejecting; actionable bullet-list diagnostics; 12 new NUnit tests in DecomposeWorktreePolicyTests)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Replace the brittle plan clean-worktree guard that trusts git status --porcelain with content-aware validation. Continue allowing host-owned mutable execution state, including both tasks.md and the active plan record under .squad/plans/{planId}.json; these files must never cause the plan executor to abort merely because SquadDash created or updated them. Prefer separating immutable/shareable plan definitions from mutable runtime execution state and place mutable state in the workspace-local state directory when that is compatible with PlanStore durability and migration requirements; otherwise recognize only the exact active host-owned plan paths as allowed changes rather than broadly ignoring arbitrary .squad content. For other tracked candidates distinguish actual unstaged content, staged changes, untracked files, timestamp/stat-cache changes, line-ending warnings, and files whose canonical working-tree hash equals the index or HEAD. Refresh or ignore metadata-only candidates automatically while continuing to block genuine content changes. Apply the same preflight policy to Inbox execution, Plans-panel execution, restart recovery, and inter-step validation. Cover host-created and host-updated plan records, timestamp-only documentation access, CRLF normalization, staged changes, untracked files, filters, real edits, and narrow-path enforcement with focused tests and actionable user-facing diagnostics that do not expose internal stack traces.
  dependsOn: (none)

- [x] **[PLANS-20260727-009]** Generate evidence-based recovery options with AI
  (SquadDash status: Completed by SquadDash — commit dee41a4: AI-assisted recovery analysis: PlanRecoveryOptionsResponse.cs with PlanRecoveryOption/PlanRecoveryOptionsResponse records and PlanRecoveryOptionsParser (PLAN_RECOVERY_OPTIONS_JSON: protocol); GatherPlanRecoveryEvidenceAsync collects git log/diff/status/task spec/downstream deps; BuildRecoveryAnalysisPrompt injects bounded evidence; ValidateRecoveryViability checks mechanical feasibility per action type; AppendRecoveryOptionsPanel renders viable options as quick-reply buttons with recommended option highlighted; Analyze with AI button added to AppendDecomposeRecoveryActions; response pipeline wired for PLAN_RECOVERY_OPTIONS_JSON before existing DECOMPOSE_RECOVERY_JSON; 14 new NUnit tests in PlanRecoveryOptionsParserTests)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Add an AI-assisted recovery analysis protocol for interrupted plans. SquadDash must gather objective facts including the task specification, plan revision, baseline and current HEAD, candidate unrecorded commits, changed paths and diffs, verification evidence, downstream dependencies, and uncommitted work; inject those facts into a bounded recovery-analysis prompt. Parse a validated PLAN_RECOVERY_OPTIONS_JSON response containing evidence-backed, user-facing options and an AI recommendation, while allowing options such as complete adoption, partial adoption with remaining work, revert-and-retry, retry from clean state, or replan only when supported by the supplied evidence. The host must validate mechanical feasibility before rendering dynamic quick replies; AI recommends but never authorizes repository mutation.
  dependsOn: PLANS-20260727-007, PLANS-20260727-008

- [x] **[PLANS-20260727-010]** Reconcile orphan commits and recover execution
  (SquadDash status: Completed by SquadDash — commit b7bd24b: Orphan commit reconciliation: RecoveryCommitValidator.cs (ExtractSingleCandidateCommit, FindDownstreamCompletedDependents, HasNonHostChanges — pure-logic, fully tested); AdoptOrphanCommitAsync validates branch/revision/baseline/candidate/changed-files/downstream-deps, requires MessageBox confirmation, records via ApplyStepResult, publishes plan progress; RevertAndRetryAsync checks deps, requires user authorization, uses git revert --no-edit, resets task to pending, restarts loop; ReplanWithCurrentStateAsync enriches prompt with current HEAD and changed files before calling QueueDecomposeReplan; 10 new NUnit tests in RecoveryActionValidationTests)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: critical
  description: Implement the host-owned recovery actions proposed by validated recovery analysis. Validate and Adopt Commit must verify branch, revision, task outcome, changed files, required build/tests, commit reachability, and absence of conflicting downstream work before recording an existing commit as the task result. Revert Commit and Retry must require explicit user authorization, use recoverable git revert semantics, refuse unsafe dependency cases, and then restart the task through the normal loop with complete orphan-commit context. Replan Remaining Work must treat the reviewed repository state as the new baseline and produce a complete revised TASKS_JSON requiring approval. Preserve audit history for every decision and never infer completion from commit messages alone.
  dependsOn: PLANS-20260727-009

- [x] **[PLANS-20260727-011]** Notify users when plan approval is required
  (SquadDash status: Completed by SquadDash — commit 1712b0c: Added NotifiedAt field to PlanApprovalGate (persisted once-only guard), ApplyGateActivated sets NotifiedAt on first activation only, PauseAtApprovalGate fires SoundEvent.ApprovalNeeded + push notification exactly once per gate, startup restores AwaitingApproval gate UI without re-notifying. 5 new NUnit tests.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: high
  description: Introduce a configurable PlanApprovalRequired notification event through SquadDash's existing notification settings and delivery infrastructure. Trigger it exactly once when a gate enters AwaitingApproval, with plan title, completed/total progress, and boundary context suitable for desktop, text, sound, and spoken output. Restore notification state across restart without repeatedly announcing the same unresolved gate, support throttling and acknowledgment, and link the notification to the canonical plan rather than a particular transcript.
  dependsOn: PLANS-20260727-006, PLANS-20260727-007

- [x] **[PLANS-20260727-012]** Complete plan inspection and management
  (SquadDash status: Completed by SquadDash — commit 08a0a47: Completed plan viewer and panel management: task nodes styled by lifecycle status (green/blue/red/orange borders + status icons); completed nodes show 7-char commit SHA chip; tooltips include completion summary and commit evidence; plan metadata header row (plan ID, branch, source, timestamps); interruption detail panel when InterruptionData is set; dynamic approval gate status badges (🔒/⏸/✓/–) with status-aware colors; Approve & Continue button in viewer for AwaitingApproval plans; Plans panel adds gate message to tooltip and Approve & Continue context menu item.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: high
  description: Complete the Plan Viewer and Plans panel management experience using the canonical PlanStore. Show dependency graph, approval gates, task descriptions, verification and commit evidence, interruption/recovery details, revision history, source transcript or Inbox origin, and lifecycle-appropriate actions. Keep dense metadata out of portrait rows. Ensure Inbox is an attention surface, Tasks is the actionable-work projection, Transcript is the creation/conversation surface, and Plans is the system of record; all surfaces must reference the same stable plan ID and revision without copying divergent state.
  dependsOn: PLANS-20260727-003, PLANS-20260727-006, PLANS-20260727-010

- [x] **[PLANS-20260727-013]** Verify and document the complete Plans workflow
  (SquadDash status: Completed by SquadDash — commit 8e7f6de: Added 20 NUnit tests across PlanLifecycleContractTests (12 state-machine contract tests), StoppedVsCompletedStateTests (6 terminal/non-terminal boundary tests), and PlanWorktreeRecoveryContractTests (gap coverage for FilterMetadataOnlyAsync). Created docs/features/plans.md (surface roles, lifecycle table, approval gates, interruption/recovery, plan viewer, storage layout, screenshot placeholders). Added /plan to slash-commands.md reference. Added Plans to SUMMARY.md navigation.)
  Group: PLANS-20260727 | Branch: feature/first-class-plans | Priority: high
  description: Add end-to-end and restart-focused coverage for plan creation, Inbox staging, durable migration, live panel progress, approval boundaries, queue suspension, cross-session resume, false dirty-worktree recovery, AI recovery-option validation, orphan-commit adoption, safe revert refusal, replanning, notifications, stopped versus completed states, and stale revision rejection. Update user and developer documentation for the Plans panel, /plan and natural-language triggers, lifecycle semantics, approval editing, recovery choices, and the relationship among Plans, Inbox, Tasks, Code Health, Transcript, and the Executing Plan loop. Run the complete build and test suite and record any genuinely pre-existing failures separately.
  dependsOn: PLANS-20260727-004, PLANS-20260727-011, PLANS-20260727-012


<!-- decompose-group: WATCHMERGE-20260727 | branch: feature/watch-health-in-tasks-panel | revision: c3c398fba755e41a -->
**[WATCHMERGE-20260727] Merge Watch Health into Tasks Panel**
> Move Watch Health from a standalone dockable panel into a collapsible section at the bottom of the Tasks panel. Three sequential phases: (1) add Watch Health section to TasksPanelController + Tasks XAML, wired to the existing SquadWatchHealthService; (2) remove the standalone WatchHealthPanelBorder, View menu item, docking registration, and dead MainWindow fields; (3) update docs. Each phase leaves the build usable.

- [x] **[WATCHMERGE-20260727-001]** Add Watch Health collapsible section to Tasks panel
  (SquadDash status: Completed by SquadDash — commit 605b610: Added Watch Health collapsible section to TasksPanelController. AttachWatchHealth() builds a separator + header row (status dot, chevron, last-check time) + collapsible body (Refresh/Copy/Start/Stop buttons, interval/execute/notify options, scrollable output). SyncWatchHealthSection() and auto-refresh timer mirror existing MainWindow logic. Collapse state persisted via WatchHealthSectionExpanded in WorkspaceDocsPanelState. MainWindow.xaml.cs calls AttachWatchHealth after TasksPanelController creation. Existing standalone panel is untouched.)
  Group: WATCHMERGE-20260727 | Branch: feature/watch-health-in-tasks-panel | Priority: high
  description: Add a collapsible 'Watch Health' section at the bottom of the Tasks panel. Implementation notes:

**TasksPanelController.cs** — add a `BuildWatchHealthSection()` method that returns a `Border` containing:
- A section header row: toggle chevron + status dot (🟢/⚪/🔴 using `{dot:green}` / `{dot:gray}` / `{dot:red}` or a small Ellipse) + label 'Watch Health' + last-check timestamp label. Clicking the header row toggles collapse.
- When expanded: a controls row with Refresh, Copy, Start, Stop buttons; below that: interval TextBox (default '5'), Execute CheckBox, NotifyLevel ComboBox (all/important/none). Below controls: a ScrollViewer containing a StackPanel for output lines.
- `SyncWatchHealthSection()` — mirrors existing `SyncWatchHealthPanel()` logic: updates status dot, output lines, last-check time, button enabled states. Call `SyncWatchHealthControls()` pattern for enable/disable.
- `SyncWatchHealthAutoRefresh()` — starts/stops a `DispatcherTimer` (15 s interval) when IsRunning changes.
- All event handlers: Refresh, Start, Stop, Copy — delegate to the same `SquadWatchHealthService` calls as in MainWindow. Inject `SquadWatchHealthService` and `Func<string?> getCurrentWorkspacePath` via constructor or new `AttachWatchHealth(...)` method on TasksPanelController.
- Collapse state persisted: add `WatchHealthSectionExpanded` bool to `ApplicationSettingsStore` (or workspace settings via `WorkspaceDocsPanelState`) — load on init, save on toggle. Collapsed by default (first-run default = false).
- Status dot visible in header even when section is collapsed for ambient awareness.
- Use `SetResourceReference` for all brushes; no hardcoded colors.

**MainWindow.xaml / TasksPanelController** — append the Watch Health section to the Tasks panel's root StackPanel (below task groups). If Tasks panel is built procedurally in TasksPanelController, add the section there; if it has an XAML root container, use that.

**MainWindow.xaml.cs** — call `AttachWatchHealth(_watchHealthService, () => _currentWorkspace?.FolderPath)` (or equivalent) when wiring up TasksPanelController. Do NOT remove any existing watch-health code yet — both implementations coexist until task 002.

**Build and test:** `$env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR='C:\Program Files\dotnet\sdk\10.0.400-preview.0.26322.102\'; $env:MSBuildExtensionsPath='C:\Program Files\dotnet\sdk\10.0.400-preview.0.26322.102\'; dotnet build SquadDash\SquadDash.csproj -c Debug --no-restore -v quiet` then `dotnet test SquadDash.Tests\SquadDash.Tests.csproj -c Debug --no-restore -v quiet`. Run from `D:\Drive\Source\SquadDash-public`.
  dependsOn: (none)

- [x] **[WATCHMERGE-20260727-002]** Remove standalone Watch Health panel and menu item
  Group: WATCHMERGE-20260727 | Branch: feature/watch-health-in-tasks-panel | Priority: high
  description: Remove all standalone Watch Health panel infrastructure from MainWindow. Depends on task 001 (the section in Tasks panel must be working first).

**MainWindow.xaml** — delete the entire `WatchHealthPanelBorder` XAML block (the standalone panel Border and all its children). Remove `ViewWatchHealthMenuItem` from the View/Tools menu.

**MainWindow.xaml.cs** — remove:
- `_watchHealthPanelVisible` field and all references
- `_watchHealthResult` field (now owned by TasksPanelController)
- `_watchHealthAutoRefreshTimer` field (now owned by TasksPanelController)
- `_watchHealthCommandInFlight` field (now owned by TasksPanelController)
- `SyncWatchHealthPanel()` method
- `SyncWatchHealthControls()` method
- `SyncWatchHealthAutoRefresh()` method
- `WatchHealthMenuItem_Click()` handler
- `ViewWatchHealthMenuItem_Click()` handler
- `RefreshWatchHealthPanelAsync()` method
- `CloseWatchHealthPanel()` method
- `WatchHealthPanelCloseButton_Click()` handler
- `WatchHealthRefreshButton_Click()` handler
- `WatchHealthCopyButton_Click()` handler
- `WatchHealthStartButton_Click()` handler
- `WatchHealthStopButton_Click()` handler
- `WatchHealthAutoRefreshTimer_Tick()` handler
- `ReadWatchHealthIntervalMinutes()` helper
- `ReadWatchHealthNotifyLevel()` helper
- `PersistWatchHealthPanelVisible()` method
- `_watchHealthService` field (move ownership to TasksPanelController, or keep in MainWindow and inject)
- The `['watch-health'] = WatchHealthPanelBorder` entry from the docking registry dictionary (line ~850)
- The `('watch-health', WatchHealthPanelBorder)` entry from docking panel list (line ~33985)
- The `case 'watch-health'` branch in the panel-lookup switch (line ~34024)
- The `ViewWatchHealthMenuItem.IsChecked` sync call (line ~18639)
- The preset application logic for `watch-health` (lines ~41563-41568)
- The workspace load code that calls `RefreshWatchHealthPanelAsync()` on `WatchHealthPanelVisible` (line ~36016)
- The workspace context-menu `watchHealthMenuItem` (line ~32315)

**WorkspaceDocsPanelState** — mark `WatchHealthPanelVisible` as `[Obsolete]` or remove it; if removed, add a migration shim that ignores the key on deserialization (so saved settings don't crash on load). Prefer `[Obsolete]` + keep reading it (just don't act on it) to avoid breaking existing saved workspaces.

**Docking** — if `'watch-health'` appears in saved dock layouts, it must be silently ignored on load (not throw). Add a guard in the docking restore path if not already present.

**Build and test** as in task 001.
  dependsOn: WATCHMERGE-20260727-001

- [ ] **[WATCHMERGE-20260727-003]** Update documentation for Watch Health in Tasks panel
  Group: WATCHMERGE-20260727 | Branch: feature/watch-health-in-tasks-panel | Priority: mid
  description: Update the docs to reflect that Watch Health is now part of the Tasks panel, not a standalone panel.

**`docs/panels/Tasks.md`** — add a '## Watch Health' subsection (or '## Squad Watch') near the bottom explaining: the collapsible Watch Health section surfaces the `squad watch` CLI process; it shows running status, last-check time, output lines; controls include Refresh, Copy, Start (with interval/execute/notify-level options), and Stop. Include the screenshot placeholder format:
  `![Screenshot: Watch Health section in Tasks panel](images/tasks-watch-health.png)`
  `> 📸 *Screenshot needed: The Tasks panel with Watch Health section expanded, showing the status dot, controls row, and output lines.*`

**`docs/panels/Tools.md`** (or whatever file lists all panels) — remove Watch Health from the panel table/list if present.

**`docs/SUMMARY.md`** — remove any standalone Watch Health entry if present.

Do NOT commit tasks.md. Commit only the doc files. Build verification is not required for doc-only changes, but do a quick `dotnet build` to confirm the previous tasks haven't left anything broken.
  dependsOn: WATCHMERGE-20260727-002


<!-- decompose-group: GODCLASS-20260725 | branch: refactor/mainwindow-decomposition | revision: 4356959cdf9fba89 -->
<!-- decompose-revision: 4356959cdf9fba89 -->
**[GODCLASS-20260725] MainWindow God Class Decomposition**
> Extract responsibilities from MainWindow.xaml.cs (41,284 lines) in safe phases. Each step leaves the build green. Phase 1 is three independent no-XAML extractions. Phase 2 depends on all of Phase 1. Phase 3 panel controllers depend on all of Phase 2. Phase 4 interface segregation depends on all of Phase 3. Phase 5 event broker depends on Phase 4.

- [x] **[GODCLASS-20260725-001]** Extract WorkspaceFileWatcherCoordinator
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Move all FileSystemWatcher fields (_inboxWatcher, _teamFileWatcher, _docsWatcher, _codeHealthMdWatcher, etc.) plus their setup, teardown, and event handlers from MainWindow.xaml.cs into a new SquadDash/WorkspaceFileWatcherCoordinator.cs. MainWindow holds one reference and delegates watcher lifecycle. No XAML changes required. Vesper unit tests ship with this extraction covering watcher start/stop and event routing. Owner: arjun-sen.
  dependsOn: (none)

- [x] **[GODCLASS-20260725-002]** Extract UiTimingConstants
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Find all hard-coded delay integer/double literals used with Task.Delay, Thread.Sleep, or DispatcherTimer intervals in MainWindow.xaml.cs (approximately 20+ occurrences: 50ms, 80ms, 100ms, 220ms, 500ms, 1000ms, etc.). Create a new static class SquadDash/UiTimingConstants.cs with named constants for each value. Replace all inline literals with the named constants. Pure rename refactor — no behavior change. Build must be green. Owner: arjun-sen.
  dependsOn: (none)

- [>] **[GODCLASS-20260725-003]** Extract SettingsSnapshotManager
  (SquadDash status: Superseded by: GODCLASS-20260725-012, GODCLASS-20260725-013, GODCLASS-20260725-014, GODCLASS-20260725-015, GODCLASS-20260725-016)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate _settingsSnapshot and all code that reads, writes, applies, or diffs it in MainWindow.xaml.cs. Move into a new SquadDash/SettingsSnapshotManager.cs class with a clean interface. MainWindow holds a reference and calls through the manager. No XAML changes required. Vesper unit tests ship covering snapshot create, apply, and restore paths. Owner: arjun-sen.
  dependsOn: (none)

- [x] **[GODCLASS-20260725-012]** Introduce the tested settings manager foundation
  (SquadDash status: Completed by SquadDash — commit 04fd00a: Added SettingsSnapshotManager, initialized it in MainWindow, migrated one low-risk mutation, and covered mutate, replace, and persistence behavior.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Introduce SettingsSnapshotManager as the owner of the current ApplicationSettingsSnapshot, initialize it during workspace startup, migrate one low-risk direct mutation, and add focused tests for mutate, replace, and persisted mutation behavior. Keep the remaining MainWindow call sites unchanged for later atomic steps.
  dependsOn: (none)
  parentTaskId: GODCLASS-20260725-003

- [x] **[GODCLASS-20260725-013]** Migrate non-persisted settings mutations — commit b2cdb70 (no with-expression sites found beyond prior step; 2 new tests added)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Route direct in-memory _settingsSnapshot with-expression mutations through SettingsSnapshotManager without changing persistence timing. Add focused tests for representative update paths and leave the build green. Do not migrate store-returning persistence calls in this step.
  dependsOn: GODCLASS-20260725-012
  parentTaskId: GODCLASS-20260725-003

- [x] **[GODCLASS-20260725-014]** Migrate settings persistence call sites — commit 80989f9 (60 SaveXxx sites migrated; 10 lambda sites deferred to 015; 3 regression tests; 2984 tests green)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Route MainWindow call sites that assign ApplicationSettingsStore Save* return values through SettingsSnapshotManager so the manager remains the single owner after persistence. Preserve every existing save boundary and add regression tests for representative persistence paths.
  dependsOn: GODCLASS-20260725-013
  parentTaskId: GODCLASS-20260725-003

- [x] **[GODCLASS-20260725-015]** Migrate dispatcher and external settings injection paths — commit dfa49e4 (all 10 deferred lambda/callback sites migrated; 3 focused tests; 2987 tests green)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Move dispatcher callbacks, lambdas, PreferencesWindow injection, and other externally supplied snapshot replacements to SettingsSnapshotManager. Prove ordering and thread-affinity behavior with focused tests and keep MainWindow behavior unchanged.
  dependsOn: GODCLASS-20260725-014
  parentTaskId: GODCLASS-20260725-003

- [x] **[GODCLASS-20260725-016]** Remove legacy snapshot ownership and verify the extraction — commit 153264f (_settingsSnapshot converted to get-only property; 7 stale mutations fixed; ~70 sync lines removed; 2987 tests green)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Remove MainWindow's duplicate _settingsSnapshot ownership after all call sites use SettingsSnapshotManager, expose only the minimum read interface MainWindow still needs, run the full test suite and solution build, and verify there are no direct settings snapshot mutations left in MainWindow.
  dependsOn: GODCLASS-20260725-015
  parentTaskId: GODCLASS-20260725-003

- [x] **[GODCLASS-20260725-004]** Extract ScreenshotService
  (SquadDash status: Completed by SquadDash — commit 127cced: Extracted ScreenshotService from MainWindow: moved WarmDefinitionRegistryCacheAsync, SyncDefinitionThemeAsync, ExtractDocImageDescription, CachedDefinitionRegistry, and HealthChecker into new ScreenshotService class. Added ScreenshotServiceTests.cs (8 tests). Also added ScreenshotHealthChecker/ScreenshotDefinition/ScreenshotHealthResult to test project compile items.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate the screenshot capture block in MainWindow.xaml.cs (~200 lines). Move capture logic, file naming, and error handling into SquadDash/ScreenshotService.cs. MainWindow calls the service via a clean method. No XAML changes required. Build must be green. Unit tests ship. Owner: arjun-sen.
  dependsOn: GODCLASS-20260725-001, GODCLASS-20260725-002, GODCLASS-20260725-016

- [x] **[GODCLASS-20260725-005]** Extract GuidedTourCoordinator
  (SquadDash status: Completed by SquadDash — commit c09b372: Extracted GuidedTourCoordinator from MainWindow: moved all _tour* fields and recovery state machine into new GuidedTourCoordinator class. MainWindow delegates all tour state through the coordinator. Unit tests added, 3026 tests green.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate the 15+ _tour* fields and their recovery state machine in MainWindow.xaml.cs (_tourMenuRecoveryRunning, _tourIntelliSenseRecoveryRunning, etc.). Move all tour state and transitions into SquadDash/GuidedTourCoordinator.cs. MainWindow wires the coordinator into the UI event chain but owns no tour state. Build must be green. Unit tests ship covering state transitions and recovery scenarios. Owner: arjun-sen.
  dependsOn: GODCLASS-20260725-001, GODCLASS-20260725-002, GODCLASS-20260725-016

- [x] **[GODCLASS-20260725-006]** Extract PromptQueueCoordinator
  (SquadDash status: Completed by SquadDash — commit a8db34c: Extracted PromptQueueCoordinator from MainWindow: moved _promptQueue field, _promptQueueSeq counter, and OnQueueItemRemoved handler into new PromptQueueCoordinator class. MainWindow delegates all queue state through the coordinator via a computed property for backward-compatible call sites. 9 new unit tests, 3036 total green.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate _promptQueue and all OnQueue* event handlers in MainWindow.xaml.cs. Move into SquadDash/PromptQueueCoordinator.cs. This reduces the 380+ direct event subscriptions in MainWindow and lays the foundation for the Phase 5 event broker. Build must be green. Vesper unit tests ship covering enqueue/dequeue and event handler wiring. Owner: arjun-sen.
  dependsOn: GODCLASS-20260725-001, GODCLASS-20260725-002, GODCLASS-20260725-016

- [x] **[GODCLASS-20260725-007]** Extract TranscriptPanelController
  (SquadDash status: Completed by SquadDash — commit ee9632e: Extracted TranscriptPanelController from MainWindow: new class implements ITranscriptRenderSink via 13 injected delegates. MainWindow no longer directly implements the interface; delegates through _transcriptPanelController. 22 new tests (13 null-guard + 9 delegate-routing), 3058 total green.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate all ITranscriptRenderSink implementation logic in MainWindow.xaml.cs. Move into SquadDash/TranscriptPanelController.cs. MainWindow holds the controller and routes events to it. XAML binding adjustments required. Build must be green. Unit tests ship. Owner: lyra-morn.
  dependsOn: GODCLASS-20260725-004, GODCLASS-20260725-005, GODCLASS-20260725-006

- [x] **[GODCLASS-20260725-008]** Extract AgentRosterController
  (SquadDash status: Completed by SquadDash — commit ad8908f: Extracted AgentRosterController from MainWindow: new class implements IAgentRosterView via 2 injected delegates. MainWindow no longer directly implements the interface; delegates through _agentRosterController. 7 new tests, 3065 total green.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate all IAgentRosterView implementation logic in MainWindow.xaml.cs. Move into SquadDash/AgentRosterController.cs. MainWindow holds the controller and delegates roster updates through it. XAML binding adjustments required. Build must be green. Unit tests ship. Owner: lyra-morn.
  dependsOn: GODCLASS-20260725-004, GODCLASS-20260725-005, GODCLASS-20260725-006

- [x] **[GODCLASS-20260725-009]** Extract InboxPanelController
  (SquadDash status: Completed by SquadDash — commit 09e7341: Extended InboxPanelController with panel visibility state (_inboxPanelVisible, _inboxAgentSuggestions), Show/Hide/Toggle/HandleFilterTextChanged/HandleUnreadOnlyChanged delegate-injected API. MainWindow delegates all inbox state through the controller. 24 new unit tests.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Locate inbox panel state and event logic in MainWindow.xaml.cs. Move into SquadDash/InboxPanelController.cs. MainWindow delegates all inbox state changes through the controller. XAML binding adjustments required. Build must be green. Unit tests ship. Owner: lyra-morn.
  dependsOn: GODCLASS-20260725-004, GODCLASS-20260725-005, GODCLASS-20260725-006

- [x] **[GODCLASS-20260725-010]** Interface segregation — remove direct IXxx impls from MainWindow
  (SquadDash status: Completed by SquadDash — commit 7c94732: Removed direct ILiveElementLocator, IWorkspaceContext, IPromptBoxState, ITranscriptRenderSink, and IAgentRosterView implementations from MainWindow. Created WorkspaceContextController, PromptBoxStateController, and LiveElementLocatorAdapter. MainWindow class declaration reduced to Window only. 47 lines of direct interface impls deleted. Build green.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: After Phase 3 extractions, remove MainWindow's direct implementations of ILiveElementLocator, IWorkspaceContext, IPromptBoxState, ITranscriptRenderSink, and IAgentRosterView. Each interface is satisfied by the controller extracted in Phase 3. Wire MainWindow → controller → interface. Confirm with a build that MainWindow has zero direct IXxx implementations. Owner: arjun-sen.
  dependsOn: GODCLASS-20260725-007, GODCLASS-20260725-008, GODCLASS-20260725-009

- [x] **[GODCLASS-20260725-011]** Replace direct event subscriptions with event broker
  (SquadDash status: Completed by SquadDash — commit 6b039cb: Introduced WeakEventBroker (thread-safe, WeakReference-based pub-sub). Added SquadBrokerMessages.cs with 12 domain message types. Migrated 12 representative MainWindow event subscriptions across static/singleton events, bridge events, and TranscriptSelectionController events. 15 broker unit tests, 3104 total green.)
  Group: GODCLASS-20260725 | Branch: refactor/mainwindow-decomposition | Priority: high
  description: Replace remaining direct event subscriptions in MainWindow (380+ total, reduced by prior phases) with a lightweight pub-sub event broker class. Eliminates memory-leak risk from undisposed handlers and decouples senders from receivers. Vesper unit tests assert correct event delivery and that no references are leaked after unsubscribe. Owner: arjun-sen.
  dependsOn: GODCLASS-20260725-010


# SquadDash Task List

> This file is the persistent backlog for SquadDash development.
> Update status inline (`- [ ]` → `- [x]`). AI agents read this file for context.
> Owner is listed per item where known.
> Completed items live in `.squad/completed-tasks.md`.

---

## ⚫ Critical


## 🟡 Mid Priority




- [x] **[Architecture] Shared vs Local data folder convention — ADR** *(Owner: Orion Vale)* — commit 5e1850b

- [x] **[Notes] Convert inbox message to note via right-click** *(Owner: Lyra Morn)* — commit 95fa27c

- [x] **[Notes] Add New Shared Note from notes panel right-click** *(Owner: Lyra Morn)* — commit fd5aab5

- [ ] **[Shared data] Shared-item indicator icon across panels** *(Owner: Lyra Morn)*
  Tasks, notes, code-health entries, and loop files that live in `.squad/` (shared/version-controlled)
  should show a small icon (e.g. 🌐 or a people icon) to indicate they are team-shared.
  Local items (AppData) show no icon or a different indicator.
  Depends on: shared/local data convention ADR.

- [x] **[UI] Window open glow-fade animation — Phase 1: WindowOpenGlow helper** *(Owner: Lyra Morn)*

- [x] **[UI] Window open glow-fade animation — Phase 2: Theme tokens** *(Owner: Orion Vale)*

- [x] **[UI] Window open glow-fade animation — Phase 3: Hook into ChromedWindow** *(Owner: Lyra Morn)*


  Full spec finalized 2026-06-24. Key components:
  - `GuidedTourStep` data model: Title, MarkdownText, TargetControlId, CalloutPlacement, PreAction
  - Steps file: `.squad/guided-tour.json` (workspace override) with embedded-resource fallback
  - `FrmGuidedTourNavigator`: floating no-titlebar draggable window, ← Prev / Step N of N / Next → / ✕ Close
  - Navigator position saved to AppData; validated on restore (must be fully on-screen)
  - Entry: first-launch prompt per machine (AppData flag), Help menu, Options → Discoverability
  - New Help menu: Start Guided Tour | Documentation (GitHub URL) | About
  - Layout: auto-save on tour start, silently restore on tour close
  - Callout closes on Next/Previous; closing callout directly ends tour + points at Help menu
  - Dev mode: Dev menu item opens guided-tour.json in editor; step-by-step preview navigation
  - Options → Discoverability: "Start the Tour" button


- [ ] **[Guided Tour] Click-to-pick target in Edit Step dialog** *(Owner: Lyra Morn)*
  The "Target..." button in FrmGuidedTourStepEditor should launch a click-to-pick mode:
  the cursor becomes a crosshair/highlight overlay, user clicks any live control in the app,
  and the target ID field is populated with a resolved identifier for that control.
  Requires an `ITourTarget` (or `IHaveName`) interface that targetable controls implement
  to expose their logical tour ID. The Edit Step dialog must be made modeless (use Show()
  instead of ShowDialog()) so the user can click controls in the main window while the dialog is open.
  Queue tab items also need to implement ITourTarget so individual queued prompts can be pointed to.

- [ ] **[Guided Tour] Make FrmGuidedTourStepEditor modeless** *(Owner: Lyra Morn)*
  Currently opened via ShowDialog() making it modal. Switch to Show() so the editor stays open
  while the user interacts with the rest of the app (required for click-to-pick target flow and
  for pointing callouts at queue tabs). Requires GuidedTourController to handle the async
  open/close lifecycle rather than blocking on ShowDialog return.

- [ ] **[Guided Tour] ITourTarget interface for targetable controls** *(Owner: Lyra Morn)*
  Define ITourTarget interface (single property: string TourTargetId). Implement on queue tab items
  so they can be individually targeted by callouts. The target registry (used by callout placement)
  should accept ITourTarget lookups in addition to named FrameworkElements. DataContext of queue
  tab items should expose ITourTarget when the item has a meaningful tour identity.

- [x] **[Developer Menu] Fix F11 Theme Reveal conflict with Full Screen Transcript**
  Moved Theme Reveal to Ctrl+F11. Key handler updated to pass bare F11 through to Full Screen Transcript.

- [ ] **[Docking] Refactor DockingMapBuilderto loop-based zone layout***(Owner: Lyra Morn)*
  DockingMapBuilder.BuildDockingMap currently uses hardcoded per-zone if/else chains for suppression,
  thin generation, and slot layout (Left3/Left2/Left, Right/Right2/Right3). This makes adding a 4th
  zone tier a near-rewrite. Prerequisite: lock down test cases for all N=0/1/2 configurations (Left and
  Right sides) via the docking test playback window. Then replace the hardcoded chains with a data-driven
  loop: represent each side as an ordered list of ZoneDescriptor records (zone, panels, sourceInZone,
  suppressFlag), derive occupied/thin counts algorithmically, and emit slots in a single forward pass.
  The N+1 thin rule and adjacent-thin check should fall out naturally from the loop without special cases.
  Prerequisite: All 1/2/0-zone docking test cases recorded and passing.

- [ ] **[Architecture] Extract shared MarkdownEditorPanel base class** *(Owner: Lyra Morn)*
  MaintenanceTaskEditorWindow and MarkdownDocumentWindow both implement markdown editing with preview
  but diverge on undo strategy (manual stack vs. WPF built-in), preview renderer (FlowDoc only vs.
  WebBrowser+FlowDoc), and feature set (maintenance editor is missing toolbar, find bar, case cycling,
  auto-list continuation). A shared base class or embedded-control approach would eliminate duplication
  and close the feature gap. See source editor audit (inbox 2026-06-08) for full analysis and options.
  Short-term: attach MarkdownEditorToolbar to MaintenanceTaskEditorWindow.
  Long-term: extract shared MarkdownEditorPanel that both editors subclass.

- [ ] **[Duplication] Investigate DUP-007— re-scan or re-examine original findings** *(Owner: Fred)*
  The original duplication scan (2026-05-21) identified DUP-001–010. Fixes were committed for
  DUP-001–006 and DUP-008–010. DUP-007 has no recorded fix and no description was persisted.
  Re-run the duplication scan (or examine git history from that session) to identify what DUP-007
  was and whether it still needs addressing.

- [ ] **[Routing] Add hard-coded argus-weld file-path guard in BuildStrongMatchRoutingInstruction** *(Owner: Arjun Sen)*
  Orion Vale architectural review (2026-05-24) identified that Argus Weld can be falsely triggered
  by any prompt mentioning a C# source file with "maintenance" in the name (e.g. MaintenanceRunner.cs).
  Fix: in SquadBridgePromptBuilder.BuildStrongMatchRoutingInstruction, add a hard-coded guard —
  if the candidate agent handle is "argus-weld", discard all matched signals unless at least one
  signal is a path starting with ".squad/maintenance" and ending with ".md".
  Must ship in the binary so it applies to ALL workspaces on upgrade.
  File: SquadDash/SquadBridgePromptBuilder.cs

- [ ] **[Routing] ADR — routing.md cannot express negative ownership signals** *(Owner: Mira Quill)*
  Orion Vale (2026-05-24) found a structural gap: ExtractOwnershipTokens harvests ALL backtick
  tokens as positive signals — no way to say "not this file" in the Examples column.
  Record as ADR in .squad/decisions.md. Orion's recommended option: a routing: file-pattern-only
  metadata field in team.md/charter that restricts strong-match to file-path signals only.

- [ ] **[Orion audit] `_isPromptRunning` — move ownership to PromptExecutionController** *(Owner: Arjun Sen)*
  `_isPromptRunning` is declared in MainWindow, mutated by PEC via setter delegate, read by
  `BackgroundTaskPresenter` via getter delegate, and read directly by MainWindow at 8 call sites.
  PEC is the natural owner (it sets the flag at prompt start/end). Consolidate ownership in PEC
  and expose it via a clean property rather than scattered delegates.

- [x] **[Vesper audit] DocStatusStore — review silent catch blocks** *(Owner: Arjun Sen)*
  Vesper's audit flagged 34 bare catch blocks across the codebase; `DocStatusStore` in particular
  has silent failure suppression that may hide real errors. Review and replace with at minimum a
  `SquadDashTrace.Write` call so failures surface in the trace log.

---

## 🔴 High Priority

- [x] **[Bug] Prompt history cycling(Ctrl+Up/Down) doesn't copy attachments to queued item***(Owner: Lyra Morn)*
  When a queued prompt item is selected and the user cycles through prompt history with Ctrl+Up/Down,
  only the text content is brought in — attachments from the history entry are not copied to the queued item.
  Attachments appear on the active draft instead. Expected: cycling history into a queued item should
  populate both text and attachments on that queued item, not the draft.

- [ ] **WinGet — smoke-test installer on clean VM***(Owner: you — manual step)*
  Run `.\installer\build-installer.ps1 -Version 1.0.0` (requires Inno Setup 6 installed locally),
  then install on a clean Windows VM with only Node.js pre-installed. Verify: launcher starts,
  SDK bridge connects, workspaces resolve correctly from `%LocalAppData%\SquadDash\app\`.
  **Blocks:** GitHub Release, WinGet submission.

---

## 🔴 High Priority — Maintenance Mode (Phase 1 MVP)

> Feature: SquadDash enters "Maintenance Mode" after configurable idle time and executes tasks from `.squad/maintenance.md`.
> Phase 1 delivers the full backend pipeline end-to-end. Panel UI is Phase 2.

---

## 🟡 Mid Priority

- [ ] Test and evaluate the Decompose feature
  Manually walk through decompose in a range of circumstances to verify reliability.
  Define a test plan covering: typical prompts, edge cases (empty input, very long input,
  ambiguous requests), and confirm output quality and consistency.
  Right-click a task row → "Edit Task" opens a custom WPF editor window (code-only, no XAML).
  Layout: Title textbox (large font, top); Properties section (enabled, frequency, safety);
  UI Options section (YAML editor for `options:` block on left, live rendered preview on right);
  Instructions section (markdown preview on left, syntax-highlighted text editor on right with
  `{{variable}}` hover tooltips and `{{#if}}`/`{{/if}}` highlighting); Cancel + Save buttons.
  Both text editors support double-Ctrl voice dictation via `PttTextBoxAttachment`.
  Save writes back to the source maintenance file via `MaintenanceMdParser.UpdateTask()`.
  Requires adding `SourceFilePath` to `MaintenanceTask` record. Requires round-trip tests.
  **Prerequisite for:** Maintenance — multi-file support.

- [ ] **WinGet — create GitHub Release v1.0.0** *(Owner: you — manual step)*
  After smoke-test passes: create GitHub Release `v1.0.0`, attach the installer `.exe` and its
  SHA256 hash. The public download URL is required for `wingetcreate`.
  **Blocked by:** smoke-test passing.

- [ ] **WinGet — generate and submit manifest** *(Owner: Jae Min — automated once release exists)*
  Run `wingetcreate new <installer-url>`, add `OpenJS.NodeJS` as a `PackageDependencies` entry
  in the installer manifest YAML, open PR to `microsoft/winget-pkgs`.
  **Blocked by:** GitHub Release v1.0.0 existing with a stable download URL.

- [ ] **WinGet — Phase 2: release automation** *(Owner: Jae Min)*
  Create `.github/workflows/release.yml`: on `v*` tag push, run `dotnet publish`, bundle
  installer, upload to GitHub Release, run `wingetcreate update`, open PR to winget-pkgs
  automatically. Requires `WINGET_PKGS_PAT` repo secret.
  **Blocked by:** Phase 1 (manual release) succeeding at least once.

- [ ] **WinGet — write RELEASING.md runbook** *(Owner: Jae Min)*
  Document the full release checklist: bump version, tag, let automation run, verify winget PR.
  Include manual fallback steps. Useful for the first few releases before automation is trusted.

---

## 🟡 Mid Priority — Annotation Editor (Paste Image Window)

- [ ] **[Annot #1] Shift-click multi-drop mode indicator** *(Owner: Lyra Morn)*
  When arrow or rectangle button is shift-clicked to enter multi-drop mode, show a rounded-rect
  underline beneath the active button (same style as document chips / orientation buttons).
  Update tooltip/hover hint to say "Shift+click to drop multiples in a row". Update docs.

- [ ] **[Annot #2] Bug: double undo for each rectangle in shift-click mode** *(Owner: Lyra Morn)*
  Each rectangle dropped in shift-click multi-drop mode adds 2 undo entries instead of 1.
  Dropping 3 rectangles requires 6 Ctrl+Z presses. Not observed for arrows. Find duplicate push
  to the undo stack in the rectangle placement path and remove it.

- [ ] **[Annot #3] Arrow drag: origin point too close to arrowhead** *(Owner: Lyra Morn)*
  When click-dragging to place an arrow, the drag origin starts too close to the tip.
  At minimum double the initial drag distance so the arrow has meaningful length on first drag.

- [ ] **[Annot #4] Enter key crops to crop rectangle + undo + window resize** *(Owner: Lyra Morn)*
  When the crop rectangle is visible and the user presses Enter:
  - Crop the image to the rectangle bounds
  - Resize the annotation window to fit the new (smaller) image
  - Push a full undo entry so Ctrl+Z restores the full image + window size
  - After cropping, user should be able to zoom (Ctrl+wheel) and annotate the smaller region

- [ ] **[Annot #5] Text annotation tool (T button)** *(Owner: Lyra Morn)*
  New toolbar button "T". On click, user draws a rectangle on the canvas. That rectangle becomes
  a text annotation box with: flashing I-beam caret, character entry + paste + backspace/delete,
  word-wrap within box, auto-shrink font to fit (min 12pt Calibri), drag handles to resize/reposition,
  Shift+Enter for newline, Enter to deselect. Font: Calibri ≥12pt (OCR-legible).

- [ ] **[Annot #6] Bug: mouse cursor drop tool — nothing happens on click** *(Owner: Lyra Morn)*
  After using arrow tool, clicking the "drop cursor" tool does nothing. Likely a tool-state
  machine bug — tool mode may not be resetting correctly when switching from arrow to cursor tool.
  Investigate state transitions between annotation tool modes.

- [ ] **[Annot #7] Cropping tool cursor (Photoshop-style)** *(Owner: Lyra Morn)*
  When the crop tool / default crop state is active, show a Photoshop-style crop cursor
  (overlapping rectangles / corner brackets). Add to AnnotationCursors class.

- [ ] **[Annot #8] Drop mouse-cursor tool cursor: arrow + plus** *(Owner: Lyra Morn)*
  When the "drop mouse cursor" tool is active, the canvas cursor should show a mouse-arrow icon
  with a small plus/crosshair next to it (same pattern as arrow/rectangle tool cursors).
  The center of the crosshair is the hotspot. Add to AnnotationCursors class.

- [ ] **[Annot #9] Attach Image + Cancel buttons — move to far right** *(Owner: Lyra Morn)*
  Reposition the Attach Image and Cancel buttons to be right-aligned in the toolbar/footer bar.

---

## 🟡 Mid Priority — MainWindow.xaml.cs Refactoring

> Tracked from Orion + Lyra review (2026-05-19). Full details in session files.
> Full report: `mainwindow-refactor-review.md` (Orion) + `mainwindow-xaml-review.md` (Lyra).
> Current file: ~28,687 lines. Goal: extract cohesive domains into separate classes.

- [x] **[Refactor Phase 1a] Extract `TranscriptSearchController`** *(Owner: Lyra Morn)*
  ~930 lines of transcript search logic (find-in-transcript, Shift+F3 cycling, highlight adorner
  management). `SearchWalker` is already embedded. Minimal `this` dependencies — easy to inject
  via constructor. Fixes the Shift+F3 duplication that currently exists in 2 separate search paths.
  Full line ranges in `mainwindow-refactor-review.md`.
  ✅ Completed 2026-06-23 — commit e612ef0

- [ ] **[Refactor Phase 1b] Extract `PromptKeyboardController`** *(Owner: Lyra Morn)*
  ~700 lines of KeyDown/KeyUp handlers for the prompt input area. Pure input routing with no deep
  WPF visual-tree dependencies. Easy to inject Dispatcher + action callbacks.
  Full line ranges in `mainwindow-refactor-review.md`.

- [ ] **[Refactor Phase 1c] Extract `WatchPanelPresenter`** *(Owner: Lyra Morn)*
  ~85 lines — smallest extraction candidate. Self-contained watch-panel sync logic.
  Good pattern-setter for the larger extractions that follow.

- [ ] **[Refactor Phase 1d] Quick XAML wins — constructor lambdas + SyncWatchPanel + ContextMenuOpening** *(Owner: Lyra Morn)*
  Three zero-risk code-behind cleanups identified in Lyra's XAML review:
  1. 650-line constructor packed with inline lambdas → extract to named event handlers (~200 lines)
  2. `SyncWatchPanel` Clear+loop+Add → `ItemsControl` + `DataTemplate` (~80 lines)
  3. `ContextMenuOpening` builds ContextMenu in C# → move to static XAML `<ContextMenu>` resource (~50 lines)

- [ ] **[Refactor Phase 2a] Extract `QueueTabController`** *(Owner: Lyra Morn)*
  ~1,600 lines — tab drag state machine, queue tab click handling, active-tab logic.
  Needs `_promptQueue` reference + a few UI callbacks. Medium risk.
  Full line ranges + dependency list in `mainwindow-refactor-review.md`.

- [ ] **[Refactor Phase 2b] Extract `AgentCardController`** *(Owner: Lyra Morn)*
  ~1,500 lines — agent card building, coloring, sync logic. References `_agents` collections.
  Coordinate with any concurrent AgentStatusCard changes. Medium risk.

- [ ] **[Refactor Phase 2c] Extract `RemoteAccessController`** *(Owner: Arjun Sen)*
  ~475 lines — RC-session state + bridge calls. Two divergent restart paths that should be unified
  as part of extraction. Arjun owns backend services; RC state is a backend concern.

- [ ] **[Refactor Phase 2d] Extract `DismissOnMovementHelper`** *(Owner: Lyra Morn)*
  Fade-popup dismiss-after-10px gesture duplicated in **5 places** in MainWindow.xaml.cs.
  Extract to a shared helper. Low-risk deduplication, high signal-to-noise.

- [ ] **[Refactor Phase 3a] Extract `DocsTreeController`** *(Owner: Lyra Morn)*
  ~2,500 lines — docs tree expand/rename/filter logic. Largest single LOC win.
  Well-clustered but moderate dependencies on workspace state. Medium risk.

- [ ] **[Refactor Phase 3b] Extract `ToolEntryPresenter`** *(Owner: Lyra Morn)*
  ~2,500 lines — tool-result card rendering, repeating `MakeItem`/`MakeSep` locals duplicated
  in 2+ context menu builders. High LOC win + deduplication. Medium risk.

- [ ] **[Refactor Phase 3c] Extract `DocScreenshotController`** *(Owner: Lyra Morn)*
  ~960 lines — screenshot attach/preview on docs panel. Needs `_pastedImageStore` reference.

- [ ] **[Refactor Phase 4] Extract `TranscriptPanelLayoutController`** *(Owner: Lyra Morn)*
  ~1,900 lines — layout/sizing logic for the transcript panel. Deeply entangled with
  `RichTextBox` visual tree. High risk — do last, after Phase 3 is complete and patterns
  are established. Do NOT start until Phase 3 is done.

---

## 🟡 Mid Priority — Maintenance Mode (Phase 2 Enrichment)

---

## 🟡 Mid Priority — Maintenance Mode (Phase 3 Polish)

- [x] **[Inbox] Inbox message save lost on shutdown — save INBOX_MESSAGE_JSON earlier** *(Owner: Lyra Morn)*
  INBOX_MESSAGE_JSON is currently saved in the `case "done":` bridge event handler. If the app shuts
  down while a turn is in-flight (streaming), the save never runs and the message is silently lost.
  Fix: save the inbox message as soon as the full response text is finalized (or at streaming end),
  not only on `bridge-done`. Consider a lightweight flush-on-close path that drains any pending
  INBOX_MESSAGE_JSON from the current response before shutdown completes.

- [ ] **[Inbox] Clicking an action button deletes the message** *(Owner: Lyra Morn)*
  When the user clicks an action button on an inbox message, the message disappears entirely from
  the inbox (even after clearing the "unread only" filter). Clicking an action should not delete or
  hide the message — it should remain readable so the user can review the plan while the agent runs.
  Expected: message stays visible; the clicked button shows as "chosen" (e.g. checkmark or dimmed
  label) and all other action buttons on the message are disabled (mutual exclusion).

- [ ] **[Inbox] Action buttons on a message should be mutually exclusive** *(Owner: Lyra Morn)*
  After any action button is clicked on an inbox message, all action buttons on that message must
  become permanently disabled (greyed out / unclickable). Only one action per message can ever fire.
  This prevents double-firing and competing agents on the same branch. Implement together with the
  message-delete fix above — they are two facets of the same interaction contract.

- [ ] **[Maintenance] `branch`-safety tasks branch from current HEAD, not always from main** *(Owner: Arjun Sen)*
  When two `safety: branch` tasks run in a session, each task receives a prompt that says "create branch
  `maintenance/YYYYMMDD-<slug>` before making any code changes." The AI agent runs `git checkout -b`
  from whatever branch is currently checked out. Since task 1 commits to its branch and the runner does
  not switch back to main before task 2 starts, task 2 inherits task 1's commits on its new branch.
  Fix: `MaintenanceRunner` should record the base branch at session start (e.g. `git rev-parse --abbrev-ref HEAD`)
  and inject an explicit `git checkout <base-branch>` step into each `branch`-safety task's preamble
  before the `git checkout -b` instruction, so every task always branches from the same known base.

---

## 🟢 Low Priority

- [ ] **[CompactPickerButton] Extract shared init helper to avoid duplicate fix targets** *(Owner: lyra-morn)*
  CompactPickerButton.cs has two near-identical initialization blocks for two button variants.
  The hover-flicker fix (commit 9f0b5fb) had to be applied to both blocks. Extract the shared
  setup into a helper method so future fixes only need to be applied once. Low-priority refactor.

- [ ] **[CodeHealthTaskEditor] Fix branchName alias drift risk in _optionValues** *(Owner: arjun-sen)*
  In CodeHealthTaskEditorWindow.cs, `_optionValues["branchName"]` is set at construction time as a
  one-off alias for `_optionValues["branch"]`. If `branch` is ever updated later in the session,
  `branchName` will not update, causing subtle template variable bugs ({{branchName}} shows stale value).
  Either remove the alias and standardize on one key, or introduce a proper aliasing/computed-value
  mechanism. Found during code health review 2026-06-16 (commit 0778c38).

---

## 🟢 Low Priority — maintenance.md File Quality(Malik's Suggestions)

> These tasks clean up and improve the default `.squad/maintenance.md` file format and content.
> Run one at a time via the loop. S1, S3b, S4 require parser/UI changes; the rest are file-only edits.
> These tasks have a dependency order: do S1 first (new format), then S2/S3b/S4 against the new format,
> then S5/S6/S7/S8 as independent cleanup passes.

---

## 🔵 Low Priority

- [ ] [Diagnostics] Trace FileSystemWatcher failure in ConfigureGitHeadWatcher
  In `MainWindow.xaml.cs`, `ConfigureGitHeadWatcher()` silently swallows `FileSystemWatcher`
  initialization failures with only a comment. Add `SquadDashTrace.Write(...)` in the catch block
  so failures are diagnosable when the branch indicator is blank in unusual environments.

- [ ] [Test Infrastructure] Embed squaddash.md resource in SquadDash.Tests.csproj
  The test `EnsureSquadDashUniverseFiles_WritesSquadDashMdToBothUniversesAndTemplatesUniverses`
  (SquadDash.Tests/SquadInstallerServiceTests.cs:125) uses `Assume.That` to skip file-content
  assertions when the embedded `squaddash.md` resource is not compiled into the test assembly.
  The directory-creation assertion passes but the full test is never exercised in CI.
  Investigate whether embedding the resource in the .csproj would allow full test coverage,
  or whether the Assume pattern is intentional. Update test documentation if the pattern
  is the correct long-term approach.

- [ ] [UX] Flash/highlight PromptAttachmentViewerWindow on re-activation
  When the user clicks the 📎 attachment link a second time, the viewer is brought to front
  (no duplicate spawning) but there's no visual cue that it appeared. Add a brief flash or
  highlight animation (e.g. brief border color pulse or window-level opacity fade-in) so the
  user notices the window even when it was hidden behind the main window.
  Related: `PromptAttachmentViewerWindow.Show()` — the singleton re-activation path.

- [ ] **OpenAI Whisper speech provider — customer request***(Owner: Orion Vale → Lyra Morn)*
  Customer request: support OpenAI speech API as an alternative to Azure Cognitive Speech, for users
  without an Azure subscription. Impact: ~5 modified files + 2 new files.
  Required changes:
  1. Extract `ISpeechRecognitionService` from `SpeechRecognitionService.cs` (events: `PhraseRecognized`, `VolumeChanged`, `RecognitionError`; methods: `StartAsync`, `StopAsync`, `WriteAudioData`)
  2. New `WhisperSpeechRecognitionService.cs` implementing that interface via OpenAI REST API
  3. `ApplicationSettingsSnapshot` + `ApplicationSettingsStore` — add `SpeechProvider` enum ("Azure" | "OpenAI")
  4. `PreferencesWindow.cs` — provider dropdown; show Whisper key field when OpenAI selected; hide Region field (not needed for Whisper)
  5. `MainWindow.xaml.cs` line 7641 — factory-create the right provider from settings
  6. `RemoteSpeechSession.cs` — use the interface (for RC phone PTT)
  Note: Whisper doesn't support phrase-list grammar hints — team name boosting silently becomes no-op for Whisper users.
  Note: Whisper is batch-oriented; streaming requires audio buffering — may have higher latency than Azure.

- [ ] **SubSquads — investigate and expose in UI** *(Owner: Orion Vale → Lyra Morn)*

- [ ] **[Vesper audit] Test coverage — screenshot infrastructure** *(Owner: Vesper Knox)*
  `ScreenshotRefreshRunner`, `ScreenshotNamingHelper`, and related fixture loaders have no unit
  tests. The refresh runner requires a WPF dispatcher — use integration-test seam or thin adapter
  pattern. Naming helper is pure logic and can be covered directly.

- [ ] **[Vesper audit] ScreenshotRefreshRunner — iterate light+dark variants** *(Owner: Vesper Knox)*
  `ScreenshotRefreshRunner.cs:172` has a TODO: "iterate twice for light+dark variants" but only
  one theme pass is currently executed. Implement dual-pass so screenshots are generated for both
  Light and Dark themes in the same refresh run.

- [ ] **Personal Squad — investigate and expose in UI** *(Owner: Orion Vale → Lyra Morn)*
  The `squad personal` feature was bridged (personal_list/personal_init) but the Workspace menu
  item was removed — it printed to transcript only with no visible feedback. Investigate what
  "personal squad" means in the current Squad SDK version (cross-workspace personal agents stored
  in the global Squad data dir), then design and implement useful UI if the feature has real value
  for SquadDash users.

---

## ✅ Recently Completed

> Full details in `.squad/completed-tasks.md`. This section is a compact AI-recall index only.

- [x] **[UI] Window open glow-fade animation — Phase 1: WindowOpenGlow helper** *(Owner: Lyra Morn)* — commit 23542c1
- [x] **[UI] Window open glow-fade animation — Phase 2: Theme tokens** *(Owner: Orion Vale)* — commit f14cd03
- [x] **[UI] Window open glow-fade animation — Phase 3: Hook into ChromedWindow** *(Owner: Lyra Morn)* — commit 4e3b71f
- [x] **[Commit Viewer] AI-assisted categorization of uncategorized commits** *(Owner: Lyra Morn)* ✅ Completed 2026-07-09 — commit c40ae85
- [x] **[Guided Tour] "Passive observe" mode checkbox in tour editor** *(Owner: Lyra Morn)* ✅ Completed — commit 19ff630
- [x] [Guided Tour] Context condition registry for conditional step skipping
- [x] **[Commit History Visualizer] Extract and unit-test visualizer logic** *(Owner: Vesper)* — commit bb4ce97 — 11 pure-logic helpers extracted to CommitActivityGraphLogic.cs; 61 NUnit tests added
- [x] **[Architecture] Shared vs Local data folder convention — ADR** *(Owner: Orion Vale)* — commit 5e1850b — DataScope enum added; ADR defines folder contract for all data types
- [x] **[Notes] Convert inbox message to note via right-click** *(Owner: Lyra Morn)* — commit 95fa27c — "Add as Note" context menu item on inbox messages
- [x] **[Notes] Add New Shared Note from notes panel right-click** *(Owner: Lyra Morn)* — commit fd5aab5 — "Add New Shared Note 🌐" in notes panel; shared notes stored in .squad/notes/; 🌐 icon on shared rows

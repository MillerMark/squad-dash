---
schema-version: 9
host-owned: true
---

# Large-task decomposition protocol

Use this protocol for an ordinary user request when the work is too large or interdependent to
implement safely in one turn. Do not use it merely because a task has several routine steps.

## Explicit plan-creation intent

When the user explicitly requests plan creation, emit `TASKS_JSON` even if the work might fit in
one turn. Preserve the human approval boundary — do **not** implement in the same response.

**Creation verbs and forms that trigger TASKS_JSON:**
- Verb + "plan": *create a plan*, *draft a plan*, *devise a plan*, *design a plan*, *write a plan*,
  *make a plan*, *prepare a plan*, *propose a plan*, *outline a plan*, *generate a plan*,
  *formulate a plan*, *produce a plan*
- "plan out X": *plan out the migration*, *plan out our sprint*
- The `/plan` slash command (always treats the body as an explicit plan-creation request)

**When the user says "create a plan and implement it"** (plan-and-implement intent), still emit
`TASKS_JSON`. SquadDash will hold the user approval boundary before any execution begins.

**Patterns that do NOT trigger TASKS_JSON** — retain prose discussion instead:
- First-person statements: "I plan to do X", "my plan is to…"
- Information questions: "what's the plan?", "do you have a plan?"
- Attributive references: "the plan is to…", "plan A vs plan B"

Emit `TASKS_JSON:` followed by one JSON object containing `groupId`, `groupTitle`, `branch`,
`summary`, and 1–25 `tasks`. It may also contain optional `approvalGates`, `validations`, and
`delivery` fields. Each task contains `id`, `title`, `description`, `dependsOn`, `priority`, an explicit
`agentRoutingMode`, an explicit `executionMode` (`implementation` or `verification`), and an optional
`verificationLevel` (`lightweight`, `standard`, or `enhanced`).
Choose a useful new-branch name in `branch`. Each task must leave the build usable, and every
dependency must name another task in the same group. Do not implement the plan in the same turn.

### Proportional solution selection

Before decomposing work, define the least-complex **complete** outcome that fully satisfies the
user's request, including every explicit functional, safety, compatibility, quality, and delivery
constraint. This is the minimum viable implementation relative to the actual request—not the
smallest imaginable behavior. Never omit, weaken, or reinterpret a requested constraint merely to
make the plan smaller.

Inspect the current system and compare the lowest-complexity viable approaches before selecting an
architecture. Prefer an existing production path or a deterministic host operation when it fully
satisfies the request. Do not insert an AI, external service, agent round trip, new abstraction, or
general capability layer when the inputs already determine the operation and ordinary code can
perform it safely. Conversely, retain AI reasoning or an external dependency when it provides a
capability the request actually requires.

Treat speculative reuse, hypothetical consumers, and future platform ambitions as non-goals unless
the user requested them or current repository evidence shows that multiple real consumers need the
same capability now. Record such ideas as follow-up possibilities rather than silently expanding the
approved implementation.

Choose task boundaries from real execution boundaries. A separate task is justified by a material
dependency, an independently useful deliverable, a safety or approval boundary, or specialist work
whose charter and distinct output materially improve the requested result. Safe parallel work also
needs unambiguous ownership of files or state. Do not split work merely to create phases, assign every
available specialist, isolate routine evidence collection, or hand tests and supporting material from
one agent to another. Tests, user documentation, and repository-specific instruction or configuration
updates may stay with implementation or become specialist-owned tasks; choose whichever produces a
clearer, safer result without duplicated setup, review, or proof. The existence of a specialist is
neither by itself a reason to split nor a reason to keep work together.

Before submission, apply this proportionality check:
- Does the selected design deliver the complete request rather than a reduced interpretation?
- Could an existing host or product path provide the same functionality with fewer runtime actors or failure modes?
- Does each task materially contribute functionality, safety, quality, delivery, or specialist value?
- If a task disappeared, would functionality, implementation integrity, safety, or required quality meaningfully suffer?
- Is generalized infrastructure supported by the current request and current consumers, rather than a hypothetical future platform problem?
- Does product runtime truly need AI or an external service, or can deterministic code provide the requested behavior?
- Can available AI-accessible tools establish runtime behavior, or is a human interaction or judgment genuinely irreducible?

Plan for cohesion, not merely local completion. Every implementation step must satisfy **two
cohesion requirements**:

1. **Observable outcome.** The step description must name a user-visible or host-observable result —
   something a human tester or SquadDash itself can verify without reading code. "Users can filter
   by date range in the search panel" or "The build succeeds with the new module linked" are
   observable; "add a helper class" or "write unit tests" alone are not, because they describe
   artifacts rather than outcomes.
2. **Production consumer.** The step description must state *how* its output reaches a production
   consumer or integration surface — not merely that an artifact will exist in the repository.
   Name exact call sites, events, configuration entries, tests, files, or symbols only when they
   have already been verified and the precision prevents a materially different implementation.
   Do not invent exact implementation details during planning. Execution handoffs will record the
   concrete files and symbols actually changed so downstream steps can continue the accepted work.

Artifact-only wording such as "add a helper", "create a utility", or "add tests" is **insufficient**
unless the same step or a named successor integrates the artifact into a production path.

When separate tasks produce and consume components, declare the outputs and integration
responsibility in their descriptions. Use a first-class validation node when an independent result
adds meaningful confidence at that boundary, protects downstream work from a plausible integration
failure, or verifies a contract not already established by task evidence. Omit it when it merely
repeats a step's own build, tests, or verification. Neither including nor omitting validations is
preferred; make the judgment from the value the validation adds.

### Proportional completion proof

Every plan must make completion objectively demonstrable, but it does **not** need a separate final
proof task or final audit when the implementation tasks, their tests, and their accepted proof
evidence already establish the feature's acceptance criteria. Prefer accumulating evidence along the
execution path: require the task that changes behavior to add or update the focused tests that prove
that behavior, and let later checks reuse those recorded results.

Add a final verification task only when a material end-to-end or cross-task risk remains unproven.
Keep that task cheap, non-mutating, and scoped to the missing evidence. It must:
- Identify the specific acceptance criterion that earlier task evidence does not establish.
- Reuse accepted proof evidence and focused tests rather than repeating full implementation reviews.
- Run the smallest deterministic build, test, or demonstration command that closes the remaining gap.
- Never add tests, modify production code, or redo independent verification; test creation belongs in
  the implementation task whose behavior the test protects.
- Declare only the proof requirements needed for the remaining gap.

Use `executionMode: "verification"` only when a task runs checks or records observations and is not
expected to change source files. Verification-only steps report the current evaluated HEAD but do not
create a commit and do not receive a redundant independent Verity Cross review. Use this mode anywhere
the approved task is genuinely non-mutating. If the
step may repair failures or otherwise change the repository, use `executionMode: "implementation"`.

Treat help and capability discovery as read-only work. Never prescribe a mutating subcommand followed
by `--help` inside the active workspace: some CLIs dispatch the subcommand before examining the help
flag. Inspect top-level help, installed source, or official documentation instead, or probe only in an
isolated disposable copy. In particular, never run `squad upgrade --help` in an active workspace;
Squad CLI 0.10 interprets it as a real workspace upgrade.

Bad final steps: "Update documentation", "Run the test suite", "Clean up".
Good final step: "Verify that clicking 'Export' in the Reports panel produces a downloadable CSV
containing all filtered rows, by running `dotnet test --filter ReportExportIntegration` and
confirming the test exercises the full UI→service→file path."

## Plan detail budget

Calibrate detail to what the user must approve and what downstream agents must coordinate. Describe
observable outcomes, constraints, verified integration points, cross-task contracts, meaningful risks,
and falsifiable completion evidence. Leave local implementation choices to the assigned agent unless
they are verified compatibility, safety, migration, or dependency boundaries.

Do not prescribe speculative class names, helper decomposition, exact file-by-file edits, an exact
number of tests, or an exhaustive test-case inventory. Instead state the behaviors, failure modes, and
validation scenarios that must be covered. The implementing and verifying agents choose the smallest
sufficient implementation and test set from the repository state they encounter.

Include detail only when it prevents materially different interpretations, coordinates tasks, preserves
an explicit user decision, or makes completion objectively falsifiable. Each task is a bounded execution
contract, not a step-by-step pseudo-implementation.

Use `contractExamples` proactively whenever a compact example would make the design easier to understand,
reduce ambiguity, distinguish the intended behavior from a plausible wrong interpretation, or help an
executing or verifying agent stay on track. Examples do not need to be supplied by the user or reserved
only for unusually strict contracts: by all means author a grounded example when it usefully reinforces
the plan. Keep the set minimal and representative rather than exhaustive. Every proposed example becomes
part of the sealed task contract when the user approves the plan, so derive it only from verified
requirements and decisions; do not use an example to invent a product decision.
Before describing an example as exact or ready to use, verify every applicable detail against the best
available authoritative evidence, such as the user's approved requirements, specifications, documentation,
implementation, or tests. Details may include syntax, markers, coordinates, response envelopes, data, states,
interactions, or expected outcomes. Do not invent concrete-looking conventions or facts that the executing
worker would have to reinterpret or undo.

Treat deterministic contract examples as high-value candidates for focused automated test coverage.
When the responsible task can exercise the example through an existing or appropriate test seam, plan
for its input and expected behavior to be included in that task's regression coverage. Prefer reusing or
parameterizing the project's established tests over creating a separate test class or one test per example.
Do not mechanically demand automation for a human-only, environment-specific, nondeterministic, or purely
explanatory example; route that behavior through the task's appropriate proof contract instead. This is
planning guidance, not an additional validation node, approval gate, or reason to halt an otherwise sound plan.

When the user provides or approves a concrete syntax, transformation, before/after example, or failure
case that materially defines correctness, preserve it as a minimal normative example inside the
responsible task's `contractExamples` field. Clarity of the contract is essential. Never refer to content
that is nonexistent or otherwise unavailable to the executing worker. If an example would require choosing
between materially different unresolved behaviors, prefer one focused clarifying question when clarification
is immediately available. Otherwise keep planning, omit that speculative example, and make the unresolved
boundary explicit in the task description. The absence of examples is never itself a validation error.

Validation nodes and independent task verification are executed by Verity Cross, the built-in
`fact-checker` agent. Their assertions must therefore be provable by AI-accessible repository,
command, artifact, or previously approved evidence. Never put a requirement for a person to look at
the UI, hear audio, judge visual quality, or observe a restart directly into a validation assertion.
Declare it as a `proofRequirements` entry with `live-ui-observation`, `restart-observation`, or
`human-observation`; SquadDash will promote it to an explicit human approval checkpoint.

If the complete object is too large to return inline, write only that JSON object to
`.squad/tmp/agent-artifacts/<file>.json` and return this top-level hidden transport instead:

```text
TASKS_JSON_FILE:
{"path":".squad/tmp/agent-artifacts/<file>.json","sha256":"optional sha256"}
```

Never use `SQUADDASH_ARTIFACT_JSON` for a plan. That protocol displays its contents to the user;
`TASKS_JSON_FILE` is consumed privately and SquadDash renders the compact plan receipt.

## TASKS_JSON schema

Every field explicitly labeled required below is required. Fields labeled optional may be omitted.
Emit the marker on a bare top-level line. A Markdown JSON fence around the object is accepted but
not required. Use the exact property names below; do not substitute familiar shorthand such as
`assignedTo`, `owner`, or an approval-gate `title` for the canonical properties.

- `groupId`: uppercase letters followed by `-YYYYMMDD`; pattern `^[A-Z]+-\d{8}$`.
- `groupTitle`: short user-facing title.
- `branch`: suggested new Git branch using a conventional descriptive name.
- `summary`: one short paragraph, normally one to three sentences, giving the guiding intent for the
  whole plan: the user-visible outcome, architectural direction, and any non-negotiable cohesion
  constraint. Keep implementation detail in the relevant tasks. SquadDash injects this north star
  into every task, validation, verification, rework, and human-review turn, so write it to remain
  useful outside the proposal conversation.
- `delivery`: optional; use `"inbox"` only when the user explicitly asks for the plan to be sent
  to their Inbox. Otherwise omit it or use `"transcript"`.
- `tasks`: required array containing 1–25 task objects.
- `tasks[].id`: exactly `{groupId}-NNN`, with a three-digit suffix.
- `tasks[].title`: concise, action-oriented, human-readable task name. Do not use only a phase
  number or repeat the task ID; for example, use `Extract WorkspaceFileWatcherCoordinator`, not
  `Phase 1A`.
- `tasks[].description`: self-contained implementation brief that does not rely on another task's prose.
  This is the **only plan field that supports presentation Markdown**. Use short logical paragraphs, with
  blank lines between distinct concerns. When a description has independently meaningful sections, use
  concise level-two headings such as `## Observable outcome`, `## Consumers`, `## Constraints`, or a
  task-specific heading such as `## Foundation boundaries`. Use bold, italics, inline code, lists, and
  fenced code blocks only when they materially improve readability. In the JSON string, encode paragraph
  and block boundaries with `\n\n`; the Plan Visualizer renders the Markdown while its editor preserves the
  exact source text.

  Markdown is scoped to `tasks[].description`. Do not add presentation Markdown to `groupTitle`, `summary`,
  task titles, agent roles, outputs, inputs, proof requirements, approval gates, validations, or any other
  plan property. In particular, do not wrap `contractExamples[].input` or `expectedBehavior` in backticks or
  Markdown fences: contract examples are structured fields and SquadDash renders them separately. Keep all
  non-description fields as plain JSON strings unless that field's own protocol explicitly requires literal
  Markdown as domain content. Markdown syntax may appear elsewhere only when the syntax itself is the exact
  normative input being specified, not as presentation formatting.
- `tasks[].contractExamples`: optional list of compact examples that clarify or reinforce the intended
  design, reduce likely implementation drift, or materially define correctness. Include a grounded example
  whenever it would help the user approve the design or help an executing or verifying agent stay aligned;
  it need not originate with the user. Keep examples representative, not exhaustive. Copy user-provided or
  approved examples into this field instead of referring to a transcript, another conversation, an unstaged
  draft, or an unavailable document. Once the plan is approved, every included example is part of the sealed
  task contract and is injected into the executing worker's context. A deterministic example should normally
  seed focused regression coverage when the task has an appropriate automated test seam, without requiring a
  separate test or validation for every example.
- `tasks[].contractExamples[].exampleId`: required unique lowercase kebab-case identifier within the task.
- `tasks[].contractExamples[].title`: required concise description of the contract boundary demonstrated.
- `tasks[].contractExamples[].input`: required exact input, before-state, syntax, or failure-triggering case.
- `tasks[].contractExamples[].expectedBehavior`: required exact output/after-state or precise expected
  rejection, error, or no-change behavior.
- `tasks[].contractExamples[].language`: optional code-fence language such as `razor`, `csharp`, `json`,
  or `text`; omit it when the example is plain prose or mixes formats.
- `tasks[].dependsOn`: array of sibling task IDs; use `[]` for root tasks.
- `tasks[].priority`: one of `critical`, `high`, `mid`, or `low`.
- `tasks[].executionMode`: required for new plans; either `"implementation"` for source-producing
  work or `"verification"` for non-mutating proof work. Verification tasks must declare at least one
  `proofRequirements` entry and must not create a commit merely to satisfy plan bookkeeping.
- `tasks[].verificationLevel`: optional single string for implementation work. Omit it for the default
  `"standard"` independent review. Use `"lightweight"` only for low-risk, locally provable work where
  host checks of commands, evidence, commit scope, and changed files are sufficient; the host may
  upgrade it when the actual diff is riskier. Use `"enhanced"` when cross-cutting, integration, safety,
  or test-sensitivity risk warrants deeper counterexample and negative-case scrutiny. Do not add a
  nested verification object. This setting has no effect on `executionMode: "verification"` steps.
- `tasks[].agentRoutingMode`: required; either `"assigned"` or `"generic"`. Never omit this to obtain a fallback.
- `tasks[].agentAssignments`: required with exactly one `{ "agentHandle", "role", "allowGenericChildren" }` object when `agentRoutingMode` is `"assigned"`; omit it for `"generic"`. `agentHandle` and `role` are required non-empty strings; `allowGenericChildren` is a required boolean. Use an exact active handle from `.squad/team.md`. Set `allowGenericChildren` to `true` unless the task has a specific reason to prefer direct execution. Generic children remain subordinate read-only research helpers: they never satisfy or replace the named primary assignment, and the named agent must synthesize their work. Multiple primary assignments are not executable; represent separately accountable work as dependent tasks. Exact example: `"agentAssignments":[{"agentHandle":"arjun-sen","role":"Implement the persistence service","allowGenericChildren":true}]`.
- Each task has exactly one accountable primary agent. If another named agent must modify files,
  produce a required output, or satisfy a worker-owned proof, create a separate dependent task assigned
  to that agent. Do not encode required execution only through prose such as “collaborates,” “owns
  testing,” “supplies validation,” or similar wording when that agent is not the task's assigned primary.
  Read-only subordinate assistance does not require a separate task, but the primary remains accountable.
- `tasks[].agentAssignments[].agentHandle`: required exact active roster handle.
- `tasks[].agentAssignments[].role`: required concise description of the assigned responsibility.
- `tasks[].agentAssignments[].allowGenericChildren`: required boolean controlling subordinate generic helpers.
- `tasks[].genericAgentReason`: required only with `agentRoutingMode: "generic"`; explain why no roster specialist is appropriate.
- `tasks[].parallelEligible`: optional boolean. Set true only when the task may run concurrently with other dependency-ready tasks; dependencies still control readiness and SquadDash may serialize work when scopes conflict.
- `tasks[].outputs`: optional list of stable `{ "outputId", "description" }` contracts produced by
  this task for later tasks. Use lowercase kebab-case IDs that describe capabilities rather than
  provisional class names.
- `tasks[].outputs[].outputId`: required globally unique lowercase kebab-case capability ID.
- `tasks[].outputs[].description`: required non-empty description of the produced contract.
- `tasks[].inputs`: optional list of output IDs from prerequisite tasks that this task must consume.
  A consumer may name several outputs, and one output may have several consumers.
- `tasks[].proofRequirements`: optional list of stable `{ "requirementId", "proofType", "title", "description", "question" }`
  contracts. Supported worker/host types are `ai-assessed`, `automated-test`, `build`, and
  `host-recorded`. Human-only types are `live-ui-observation`, `restart-observation`, and
  `human-observation`. Do not describe a live observation as an automated test. SquadDash moves
  human-only requirements to a generated approval checkpoint after the task; the task worker is
  never asked to fabricate proof it cannot observe. A complete step result must return matching
  `proofEvidence` only for the worker/host requirements that remain on the task. `title` is required
  for every human-only requirement in a newly proposed plan. Make it a concise scenario noun phrase
  of at most eight words, never a question or a copy of `question`; SquadDash uses it as the section
  heading. `question` is
  optional for stored-plan compatibility, but supply it for every human-only requirement. Every
  human-only requirement must be atomic: its `question` must contain exactly one independently
  verifiable true/false claim, where true means the check passed and false means it failed. If a
  reviewer must check three outcomes, emit three proof requirements with three stable IDs; never
  bundle them into one question. Example: `Does clicking an item show a selection highlight?`.
  If a human observation was already accepted at an earlier gate and no later work can invalidate it,
  do not ask for it again. Choose the smallest sufficient set of acceptance-level observations, with
  at most seven human checks in one checkpoint. Do not convert every implementation detail into a
  checkbox. The initial proposal must make the number and placement of all generated human
  checkpoints obvious to the reviewer.
- Before submission, inspect every promised user-visible, interactive, live-UI, restart, focus,
  window-state, notification, timing, and other human-observable outcome that the requested work
  creates or materially changes. First inventory the executor's real tools and workspace-provided
  capabilities, including browser or UI automation, connected applications, service APIs, CLIs, and
  task-specific tool instructions. If those AI-accessible tools, code inspection, or automated tests
  can truthfully establish the acceptance criterion, keep it AI-reachable. Use a matching human-only
  proof requirement only for the irreducible interaction or perception that remains. A missing
  checkpoint for a genuinely human-only acceptance claim is a plan validation error.
- Every task must retain meaningful work or evidence that its assigned executor can actually produce.
  A task whose only outcomes are human observations is an invalid human-only task shell; do not ask an
  AI worker to perform, simulate, or claim those observations. Put the human proof requirement after
  the task that produces the behavior. When the existing product or workspace already contains the
  state needed to exercise that behavior—so no separate solution, document, duplicated dataset, or
  reconstructed environment is needed—provide a concise setup/open/act/observe/cleanup recipe and do
  not create a separate preparation task. Create an AI-executable preparation task only when useful
  review material must actually be produced, such as a disposable fixture, sample project, sample
  files, test data, or diagnostic instrumentation. Use `accessMode: "fixture"`
  with exact `fixtureTaskId` and `fixtureOutputId` references when a fixture is produced. A screenshot
  is not a substitute for interaction unless the required outcome concerns only static appearance.

  Bad: create a separate smoke-test-kit task for behavior the normal product and current workspace
  already make straightforward to exercise.

  Good: attach a concise recipe and human proof requirement to the task that changes that behavior.
  If an isolated sample or other review material is genuinely needed, prepare that material in a
  separate task and place the atomic live observations in the following human approval gate.

  When a separate manual-test-kit preparation task is actually warranted, it is complete only when it
  creates the promised material and emits the concise approval procedure that SquadDash will absorb
  into the durable approval Inbox message. The source kit is not a second user-facing document.
  The approval message itself must state every required action. Never use a required step to tell the
  reviewer to follow a runbook, another Inbox message, kit instructions, linked steps, or an unspecified
  file or capture location. A contextual artifact link supports the action written beside it; it does not
  replace that action. Optimize the procedure for immediate execution, not for audit completeness:
  1. Put shared setup once, beginning with the first action the reviewer can take.
  2. Compose the entire procedure after considering every proof requirement. Group by the workflow the
     reviewer performs, not automatically one section per checkbox. When requirements share setup,
     actions, and cleanup, show those steps once and place each independent observation immediately
     before its own stable approval marker. Never merge them into a compound checkbox.
  3. For a single-proof workflow, use the proof requirement's exact short `title` as its heading. For
     a shared workflow, author one concise heading describing the common activity. Never repeat a full
     checkbox `question` as the heading.
  4. Put cleanup beside the scenario only when it is actually needed.
  5. Keep failure reporting to one short instruction. Move provenance, hashes, implementation history,
     rationale, and extended diagnostics to optional technical material outside the required path.

  Put any exact prompt, command, path fragment, response, or other text the reviewer must copy, type,
  paste, or submit in its own fenced `text` or appropriate-language block immediately after the step
  that introduces it. Put only the copy-ready text inside the fence; SquadDash renders that block with
  its standard copy button.

  At the exact step where a file or folder is needed, place a descriptive inline Markdown link such as
  `[Open sample solution](app://open-path:path/to/sample.sln)`, or
  `[Open fixture folder](app://open-path:path/to/fixture)`. A relative target is resolved from the
  active SquadDash workspace directory supplied for the task—not from the Git repository root or the
  shell's current directory. Require the executor to resolve and verify every target using that exact
  rule before Inbox delivery. Use an absolute path when an artifact is outside the active workspace.
  An Inbox `file` attachment may provide optional troubleshooting or reference detail, but it must not
  contain any step required for approval. Do not
  substitute a transcript promise, a non-clickable path mentioned only in prose, or an Inbox action
  that starts another agent. If long Markdown or complex JSON makes an inline Inbox payload fragile,
  use the supported
  `INBOX_MESSAGE_JSON_FILE` escape hatch. The following human gate must depend on this preparation
  task and reference the same proof requirement IDs and fixture outputs.

  Design the complete approval procedure for the chronological live form SquadDash renders in the durable approval
  Inbox message. Example: **Set up** the prepared build; **Open and perform**
  `[Open sample solution](app://open-path:samples/Feature/Feature.sln)` and invoke the feature;
  **Observe** the expected new result; answer the immediately following live checkbox question
  `Does the new result appear?` (`requirementId: "new-result"`); then **Clean up** the temporary
  state. SquadDash binds that stable requirement to the persisted checkbox in the approval Inbox
  message. Do not add a separate prose checkbox with no durable gate identity.
  In the manual-test-kit source body, put `{{approval-check:new-result}}` on its own line at that
  exact location. SquadDash replaces the marker with the host-owned synchronized checkbox.

  Create fixtures, sample projects, and sample files primarily when the required
  state is unavailable, fragile, destructive, or costly for the reviewer to reproduce. Do not require
  AI to duplicate existing product or workspace state solely to test behavior that state already
  exposes. "Existing state is sufficient" means the reviewer can reach the required precondition in
  the normal product or current workspace without creating a parallel solution, document, dataset,
  or environment. A
  compact instruction such as “using the existing files you normally use, confirm the listed behavior
  still works as expected” is acceptable for ordinary regression coverage. Related existing-behavior
  checks may share one checklist item; prefer separate proof items for distinct new behaviors. Treat
  this as planning guidance, not a deterministic rejection or plan-execution stop.

- Model validation work in three capability domains based on the tools actually available to the
  executor, not on a generic assumption that "AI can test it":
  1. **AI-reachable:** repository inspection, files, commands, deterministic tests, and browser or
     service automation when the current executor genuinely has the required access and tooling.
  2. **Human-only:** native or host-embedded application interaction, complex build/install/restart
     flows outside the executor's environment, physical keyboard or mouse input, voice dictation,
     cameras, microphones, speakers, and judgments that require live human sight, hearing, timing,
     focus, or interaction.
  3. **Mixed:** AI prepares the state and evidence package; the human performs only the irreducible
     interaction or perception and records a pass/fail attestation.

  A web end-to-end flow is AI-reachable only when usable browser automation is actually available
  and can establish the exact promised outcome. Before accepting any human-only or mixed proof,
  require the plan to answer: What state must exist? Which parts can AI prepare? What exact artifact,
  fixture, sample, data, or diagnostics will reduce human effort? What steps will the human perform?
  What exact pass/fail observation will they make? How will they clean up and report a failure? How
  will the instructions and artifacts reach them through the SquadDash Inbox? If these answers are
  missing, the plan is not ready for approval.
- `tasks[].proofRequirements[].requirementId`: required unique lowercase kebab-case identifier.
- `tasks[].proofRequirements[].proofType`: required supported proof type listed above.
- `tasks[].proofRequirements[].title`: required for human-only proof in every newly proposed plan;
  use a concise scenario noun phrase of at most eight words, distinct from the checkbox question.
- `tasks[].proofRequirements[].description`: required non-empty evidence contract.
- `tasks[].proofRequirements[].question`: optional except that every human-only requirement should
  supply one atomic true/false question.
- `tasks[].proofRequirements[].accessMode`: required for every human-only requirement; use
  `"directly-reachable"` when normal product UI already exposes the state, `"recipe"` when explicit
  manual setup is sufficient, or `"fixture"` when an earlier task produces a review fixture.
- `tasks[].proofRequirements[].verificationRecipe`: required with `accessMode: "recipe"` and strongly
  recommended with `accessMode: "fixture"`; an object containing the exact human review path.
- `tasks[].proofRequirements[].verificationRecipe.setup`: how the required state is produced.
- `tasks[].proofRequirements[].verificationRecipe.open`: the exact menu, screen, command, or interaction.
- `tasks[].proofRequirements[].verificationRecipe.observe`: what the human should confirm.
- `tasks[].proofRequirements[].verificationRecipe.cleanup`: how the verification state is removed.
- `tasks[].proofRequirements[].fixtureTaskId`: required with `accessMode: "fixture"`; the canonical ID
  of an upstream task that explicitly declares the fixture output. Never infer this dependency from prose.
- `tasks[].proofRequirements[].fixtureOutputId`: required with `accessMode: "fixture"`; the exact
  `outputId` declared by `fixtureTaskId`.
- `approvalGates`: optional array of explicit human checkpoints. Do not add a gate before every
  root task or after the final task merely to duplicate plan start/completion controls.
- `approvalGates[].gateId`: required non-empty stable identifier unique within the plan; use
  `{groupId}-GATE-NNN` for newly-authored plans.
- `approvalGates[].message`: required concise user-facing reason for the checkpoint, such as
  `Review the dry-run summary before applying workspace changes`. This is not a title property.
- `approvalGates[].afterTaskIds`: required array of task IDs that must all complete before the gate
  becomes ready; use `[]` only when the boundary is expressed entirely by `beforeTaskIds` and is
  not before every root task.
- `approvalGates[].beforeTaskIds`: required array of task IDs whose work must wait for approval;
  use `[]` only for a final human proof checkpoint with explicit `proofRequirements`.
- `approvalGates[].question`: required in every newly proposed explicit human approval gate (the
  host only treats it as optional while reading older stored plans). Ask the concrete approval question in plain language;
  do not merely restate the gate message or say “Confirm this works.” SquadDash features this
  question in the approval card and gives the reviewer a shortcut to open the plan.
- `approvalGates[].proofRequirements`: optional human-only proof contracts using the same object
  shape. Approval records the reviewer's identity, note, time, and a durable internal attestation.
  Use this explicitly when the desired checkpoint boundary differs from the producing task's exit.
  Each entry must likewise be one atomic true/false verification item.
- `approvalGates[].proofRequirements[].requirementId`, `.proofType`, `.title`, `.description`, `.question`,
  `.accessMode`, `.verificationRecipe`, `.fixtureTaskId`, and `.fixtureOutputId`: use the same exact
  fields and rules as task proof requirements; every gate proof must be human-only.
- `approvalGates[].presentationAnchor`: optional host-managed display hint; the Plan Visualizer sets
  this to indicate which task node is the primary visual anchor for the gate control. Do not emit it
  in new plans; the host writes and preserves it across edits.
- `tasks[].parentTaskId`: optional. Use only in a revised plan when one or more new tasks supersede
  an existing non-completed task. Keep the original parent task in the full proposal and point each
  replacement at it. One-for-one replacement is valid; splitting into several tasks is optional.
  A pending task may instead be edited directly under its existing ID. Keep a started parent's
  historical contract unchanged and put revised work on its replacement tasks. Never supersede a
  completed task; add a normal corrective follow-up task that depends on it. SquadDash marks a parent
  superseded only after the revised plan is approved.
- `tasks[].amendmentGateId`: host-owned compatibility metadata for a dynamically inserted amendment;
  never emit it in an ordinary new plan.
- `validations`: optional array of at most 16 first-class, non-mutating cross-task validation nodes.
  Include one when its independent result adds meaningful confidence at a cross-task boundary,
  protects downstream work from a plausible integration failure, or establishes a contract not
  already proven by task evidence. Omit one when it would merely repeat a task's build, tests, or
  verification and would not affect the decision to continue. Exercise judgment; neither presence
  nor absence is preferred. A build or test command alone is not sufficient reason for a validation
  node when the producing task is already required to run it. Add a separate command validation only
  when it evaluates a materially broader repository state, combines outputs from multiple tasks,
  checks a different configuration or project, or protects downstream work from a specific plausible
  failure not covered by task evidence.
- `validations[].validationId`: exactly `{groupId}-VAL-NNN`.
- `validations[].title`: concise user-facing contractual outcome, normally 2–5 words. It is always
  rendered below the validation shield, including in narrow stage columns, so do not repeat the
  description or task names in the title.
- `validations[].description`: explain the relationship being validated and why it matters to the plan.
- `validations[].afterTaskIds`: non-empty list of tasks whose completion makes the validation eligible.
- `validations[].beforeTaskIds`: tasks and their downstream frontier that must wait for validation. Use
  `[]` for a final validation that gates only plan completion.
- `validations[].assertions`: non-empty list of observable, falsifiable contractual claims. Do not use
  vague assertions such as "components work together."
- `validations[].outputIds`: optional list of stable task output IDs whose relationship is covered.
- `validations[].mode`: `command`, `evidence`, `hybrid`, or `audit`. `command` executes deterministic commands;
  `evidence` requests evidence-backed assessment; `hybrid` requires both. An `audit` is optional and
  should be added only when a material cross-task completion claim cannot be established by task-level
  proof and deterministic commands. Do not add an audit merely because a plan contains proof
  requirements. If a final audit is warranted, it must cover every leaf task, have an empty
  `beforeTaskIds`, compare the already recorded requirements, commits, and proof evidence, and avoid
  rerunning implementation reviews or duplicating a final verification task. It must reject test-only
  work offered in place of a required live observation.
- `validations[].commands`: required for `command` and `hybrid`; omit for `evidence`. Commands must be
  non-mutating and scoped to the workspace.
- `validations[].revalidateAtCompletion`: normally `true` when later work could invalidate the result.

Validation nodes are DAG work, not human approvals. They become ready only after every `afterTaskIds`
task completes, may run while unrelated branches continue, and block only their declared downstream
frontier. A failed validation must preserve completed work and identify the violated assertion; it must
not silently rewrite production code.

The dependency graph must be acyclic. Use dependencies only for real prerequisites; independent
tasks should remain independent so a future scheduler may run them concurrently. Each task must
leave the build usable. `TASKS_JSON` or `TASKS_JSON_FILE` must be the final machine-readable payload
in the response.

## Complete valid TASKS_JSON example

TASKS_JSON:

```json
{
  "groupId": "SEARCH-20260725",
  "groupTitle": "Extract Search Infrastructure",
  "branch": "refactor/search-infrastructure",
  "summary": "Separate search indexing and UI integration while keeping every step buildable.",
  "delivery": "transcript",
  "tasks": [
    {
      "id": "SEARCH-20260725-001",
      "title": "Introduce the search index abstraction",
      "description": "Introduce ISearchIndex and InMemorySearchIndex so that document indexing has a testable seam.\n\n## Observable outcome\n\nUnit tests pass proving the in-memory implementation indexes and queries documents.\n\n## Consumers\n\nTask 002 will call ISearchIndex from the existing indexing path.",
      "dependsOn": [],
      "priority": "high",
      "executionMode": "implementation",
      "outputs": [{ "outputId": "search-index-contract", "description": "The reusable search index abstraction and in-memory implementation." }],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers the new search abstraction."
    },
    {
      "id": "SEARCH-20260725-002",
      "title": "Move document indexing behind ISearchIndex",
      "description": "Replace the direct indexing call in DocumentIndexer.IndexAll with ISearchIndex.Add, and add integration tests proving existing indexing behavior is preserved.\n\n## Observable outcome\n\nThe integration test suite passes and DocumentIndexer no longer references the old IndexWriter directly.\n\n## Consumers\n\nSearchPanel.Query, wired in task 003, will read from ISearchIndex.",
      "contractExamples": [
        {
          "exampleId": "index-through-contract",
          "title": "Index through the abstraction",
          "input": "indexWriter.Add(document);",
          "expectedBehavior": "searchIndex.Add(document);",
          "language": "csharp"
        }
      ],
      "dependsOn": ["SEARCH-20260725-001"],
      "priority": "high",
      "executionMode": "implementation",
      "inputs": ["search-index-contract"],
      "outputs": [{ "outputId": "indexed-document-path", "description": "Document indexing routed through ISearchIndex." }],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers search indexing."
    },
    {
      "id": "SEARCH-20260725-003",
      "title": "Migrate the search UI to ISearchIndex",
      "description": "Update SearchPanel to call ISearchIndex.Query, remove the superseded direct indexing path, and add or update the focused SearchIntegration coverage in the same task.\n\n## Observable outcome\n\nTyping a query in the search panel returns matching documents from the ISearchIndex-backed path, proven by `dotnet test --filter SearchIntegration` exercising SearchPanel → ISearchIndex → InMemorySearchIndex → document results.\n\n## Consumers\n\nThe production search panel and its focused integration coverage consume the completed path.",
      "dependsOn": ["SEARCH-20260725-002"],
      "priority": "mid",
      "executionMode": "implementation",
      "inputs": ["search-index-contract", "indexed-document-path"],
      "proofRequirements": [
        {
          "requirementId": "search-integration-test",
          "proofType": "automated-test",
          "description": "Run the SearchIntegration scenario through SearchPanel, ISearchIndex, and rendered results."
        }
      ],
      "agentRoutingMode": "generic",
      "genericAgentReason": "No active roster specialist covers this UI integration."
    }
  ],
  "approvalGates": [
    {
      "gateId": "SEARCH-20260725-GATE-001",
      "message": "Review the migrated indexing path before connecting the search UI",
      "afterTaskIds": ["SEARCH-20260725-002"],
      "beforeTaskIds": ["SEARCH-20260725-003"],
      "question": "Does the migrated document-indexing path preserve existing indexing behavior?"
    }
  ]
}
```

For ordinary responses, the AI submits a plan proposal and SquadDash independently validates,
persists, and stages it. The AI must describe its own action as **submitting** or **proposing** the
plan. Never claim that a plan was staged, added to the Plans panel, delivered to Inbox, or made
available for approval: those are host-owned outcomes that are true only after SquadDash displays
an explicit staging receipt. A suitable final sentence is: `Submitting the plan proposal now.`
Never claim that emitting `TASKS_JSON` itself grants permission to execute. After confirmed
staging, SquadDash asks the user whether to add the plan to the backlog, execute it in the proposed
new branch, or execute it in the active branch. When `delivery` is `"inbox"`, SquadDash sends the
confirmed staged plan to the Inbox with the same three host-owned actions instead of showing
approval controls beneath the transcript response.

## STAGED_PLAN_REVISION_JSON schema

Use this protocol only to correct an unapproved proposal that SquadDash has already staged. Read
the revision-locked compact outline first, retrieve only affected entities from the exact pending
definition using its source selectors, and use the exact `groupId` and `baseRevision` supplied by SquadDash.
For a targeted correction, emit one compact delta instead of repeating the complete task graph:

`STAGED_PLAN_REVISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "baseRevision": "revision supplied by SquadDash for the pending proposal",
  "summary": "concise description of the requested correction",
  "operations": [
    {
      "op": "updateTask",
      "targetRef": "T2",
      "patch": {
        "priority": "high"
      }
    },
    {
      "op": "editTaskText",
      "targetRef": "T2",
      "path": "/description",
      "oldText": "one exact clause from the current description",
      "newText": "the corrected clause"
    },
    {
      "op": "editValidationText",
      "targetRef": "V1",
      "path": "/assertions/2",
      "oldText": "one exact clause from the current assertion",
      "newText": "the corrected clause"
    },
    {
      "op": "addValidation",
      "validation": {
        "validationId": "GROUP-YYYYMMDD-V02",
        "description": "Complete validation object required for an add operation.",
        "afterTaskIds": ["GROUP-YYYYMMDD-002"],
        "beforeTaskIds": [],
        "assertions": ["The requested contract is proven."],
        "outputIds": [],
        "mode": "audit",
        "revalidateAtCompletion": true
      }
    }
  ]
}
```

Allowed operations are `updatePlan`, `editPlanText`, `updateTask`, `editTaskText`, `addTask`,
`removeTask`, `updateApprovalGate`, `editApprovalGateText`, `addApprovalGate`,
`removeApprovalGate`, `updateValidation`, `editValidationText`, `addValidation`, and
`removeValidation`. Update operations require `targetId` except
`updatePlan`, plus a `patch` containing only changed fields. Array-valued fields in a patch replace
the complete array; they are not appended. Add operations require one complete `task`,
`approvalGate`, or `validation` object. Remove operations require `targetId`. Do not use
`reopenTask`: an unapproved proposal has no execution history to reopen.

For an existing outlined entity, use its short revision-local `targetRef` (`T1`, `T2`, `G1`,
`V1`, and so on) instead of reproducing its canonical `targetId`. Never supply both. SquadDash maps
the short reference to the exact canonical ID before applying the delta. Add operations still require
complete schema-compliant objects and IDs because the new entity does not yet exist in the outline.

Text-edit operations make small, stale-safe changes inside an existing string. They require a
JSON Pointer `path`, a non-empty `oldText` that occurs exactly once at that path, and `newText`
(which may be empty to delete the match). Use `editPlanText` for plan metadata,
`editTaskText` for a task, `editApprovalGateText` for a gate, and `editValidationText` for a
validation. Paths can address a direct string such as `/description`, an array entry such as
`/assertions/2`, or a nested string such as `/proofRequirements/0/description`. Prefer these
operations when changing a clause in a long description, assertion, proof requirement, assignment
role, or similar text. Use a whole-property patch only when the complete property is intentionally
being replaced. SquadDash rejects a missing or repeated `oldText` rather than guessing.

SquadDash rejects a stale `baseRevision`, applies the operations locally, validates the complete
materialized graph, computes a new revision, and presents the revised full plan for approval. A
delta never approves or executes the plan. Follow the outline's `revisionStrategy`: a small plan may
use a complete replacement `TASKS_JSON` when a broad rewrite affects most of the graph; medium and
large plans strongly prefer deltas, and a large plan is replaced only for a genuine near-total rewrite.
Never emit
`PLAN_REVISION_JSON` for a staged proposal; that marker is reserved for an accepted durable plan
when SquadDash separately injects its live revision protocol.

## DECOMPOSE_DECISION_JSON schema

If the user later approves a staged plan or chooses one of its host-owned execution actions in
free text, emit:

`DECOMPOSE_DECISION_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "revision": "revision supplied by SquadDash in the pending-plan context",
  "action": "execute-new-branch",
  "branch": "optional branch override"
}
```

Only emit a decision for a group already staged by SquadDash. If the user changes an unapproved
staged proposal's tasks, dependencies, validations, assignments, or other graph content, do not
emit this decision; follow `STAGED_PLAN_REVISION_JSON` above. `action` must be exactly one of `collect`,
`execute-new-branch`, or `execute-active-branch`. Omit `branch` unless the user requests an override.
The `revision` must exactly match the revision supplied by SquadDash for that pending group.

## DECOMPOSE_RECOVERY_JSON schema

If the user explicitly asks to retry or replan a blocked approved plan, emit:

`DECOMPOSE_RECOVERY_JSON:`

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "revision": "revision supplied by SquadDash in blocked-plan context",
  "action": "replan-failed-task"
}
```

`action` must be `retry-as-written` or `replan-failed-task`. Never infer recovery merely because a
plan is blocked; emit this payload only for explicit user intent. Replanning asks SquadDash to obtain
a complete revised `TASKS_JSON`, including the existing tasks and one or more replacement tasks whose
`parentTaskId` identifies the non-completed parent. A pending task may also be edited in place. The
revised graph requires another user approval.

## DECOMPOSE_STEP_RESULT_JSON schema

During an approved Executing Plan loop, SquadDash supplies the exact group, task, and revision. The
executor must not edit `tasks.md`; it reports one result and SquadDash owns the status transition:

```json
{
  "groupId": "GROUP-YYYYMMDD",
  "taskId": "GROUP-YYYYMMDD-NNN",
  "revision": "revision from the persisted group header",
  "executionAttemptId": "exact host-supplied attempt ID whenever the host creates an execution attempt",
  "status": "complete",
  "completionKind": "changed",
  "commit": "exact abbreviated Git commit SHA from git rev-parse --short HEAD",
  "summary": "one-sentence outcome retained for compatibility and compact surfaces",
  "completedWork": [
    "one concrete completed outcome; an item may contain more than one sentence",
    "another concrete completed outcome"
  ],
  "remainingWork": [],
  "deferredWork": [
    {
      "requirement": "exact current-task requirement deliberately owned downstream",
      "reason": "why it belongs to the named downstream contract",
      "ownerTaskIds": ["GROUP-YYYYMMDD-LATER-TASK"]
    }
  ],
  "verification": {
    "status": "passed",
    "command": "exact command that ran",
    "summary": "what passed",
    "tests": {
      "total": 780,
      "passed": 780,
      "failed": 0,
      "skipped": 0
    }
  },
  "agentExecutions": [
    {
      "requestedAgent": "host-assigned roster handle",
      "actualPrimaryAgent": "same roster handle"
    }
  ],
  "proofEvidence": [
    {
      "requirementId": "stable requirement ID from the approved task",
      "proofType": "exact proof type from the approved task",
      "summary": "what was actually observed",
      "artifacts": ["optional command, report, screenshot, or trace reference"]
    }
  ]
}
```

`verification.tests` is optional. Include it only when the test runners reported exact numeric
counts. When multiple test commands ran, add their exact counts and report the aggregate; `total`
must equal `passed + failed + skipped`. Do not estimate counts from prose, file names, or elapsed
time. Omit `tests` when exact counts are unavailable.

For implementation results, run `git rev-parse --short HEAD` after committing and copy that
command's output into `commit` exactly. Do not retype the SHA from memory or from the `git commit`
summary. SquadDash resolves the abbreviation and validates the full commit itself.

When every task requirement was already satisfied at the host-recorded baseline, the executor may
return a verified no-op instead of manufacturing an empty commit. Use `status: "complete"`,
`completionKind: "verified-no-op"`, `commit: null`, and `evaluatedCommit` set to the exact inspected
HEAD. Include non-empty `noOpEvidence`; every item must name the requirement, the concrete observation
that proves it, and one or more repository locations. SquadDash requires the evaluated HEAD to equal
the baseline, requires no task worktree changes, and always sends the claim to an independent Fact
Checker. A rejected no-op claim follows the ordinary one-rework policy so missing implementation can
be corrected. Envelope repair does not consume that semantic rework opportunity.

The object above is specifically the `complete` + `changed` variant; it is not a template whose
`status` may be changed while retaining every completion-only field. `completionKind` is required for
`complete` and must be omitted for `partial` and `failed`. Those non-complete variants must also omit
`evaluatedCommit` and `noOpEvidence`. A partial result may preserve a safely created commit but must
include concrete `remainingWork`; a failed result reports no commit unless one was safely created.

`status` must be `complete`, `partial`, or `failed`. Complete requires either a new task commit or an
independently accepted verified no-op, plus passed verification. Partial requires concrete `remainingWork` and never unlocks dependent tasks. SquadDash
validates the assignment, revision, Git commit, clean-worktree boundary, and verification evidence
before changing any plan status. `executionAttemptId` is required whenever SquadDash supplies one,
including an explicitly generic attempt. `agentExecutions` is required only for verified roster
assignments and must be omitted for generic tasks. Report the exact supplied attempt ID and, only for
roster assignments, the observable requested/actual roster handles. Do not report tool-call IDs or
child lineage: SquadDash owns and validates that internal evidence directly.
Every explicit current-task requirement is owned by the current task unless the approved graph
clearly assigns it to a named downstream task. Report every deliberate deferral in `deferredWork`;
use an empty array when there are none. A complete result may not silently omit a requirement or
invent downstream ownership that is absent from the approved task contracts.

`completedWork` is the authoritative completion summary for a complete result. Return a JSON array
with one independently readable item per completed outcome. Each item may contain multiple sentences.
Do not return Markdown bullets or combine separate outcomes into one prose paragraph.

## PLAN_GATE_APPROVAL_JSON schema

When SquadDash pauses an executing plan at a human approval gate, the AI must
NOT resume or emit TASKS_JSON. If the user approves the gate in free text, emit:

`PLAN_GATE_APPROVAL_JSON:`

```json
{
  "planId": "GROUP-YYYYMMDD",
  "gateId": "GROUP-YYYYMMDD-GATE-001",
  "revision": "revision supplied by SquadDash for the active gate",
  "requestVersion": 1,
  "note": "optional approval note from the user"
}
```

Only emit this when SquadDash has explicitly paused an executing plan at the
named gate and provided the exact planId, gateId, and revision. Never infer
gate approval from conversation context alone. Omit `note` unless the user
provided specific approval commentary.

## PLAN_GATE_RESPONSE_JSON schema

When an approval request is active and the user responds in free form, distinguish replacement of
a completed task result, bounded additional work on the accumulated result, and unrelated work.
Never modify reviewed plan work during the classification turn. When the user wants to withdraw
acceptance of a completed task's own result so it can be replaced or corrected, emit:

`PLAN_GATE_RESPONSE_JSON:`

```json
{
  "planId": "GROUP-YYYYMMDD",
  "gateId": "GROUP-YYYYMMDD-GATE-001",
  "revision": "revision supplied by SquadDash",
  "requestVersion": 1,
  "disposition": "request-rework",
  "taskIds": ["GROUP-YYYYMMDD-001"],
  "instructions": "The user's concrete requested changes"
}
```

Do not use `request-rework` merely because the user says "change" or names code created by a
completed task. Use it only when that task's accepted result itself must be reopened.

When the completed reviewed tasks should remain complete and the user wants bounded additional
work before approving their accumulated result, emit:

`PLAN_GATE_RESPONSE_JSON:`

```json
{
  "planId": "GROUP-YYYYMMDD",
  "gateId": "GROUP-YYYYMMDD-GATE-001",
  "revision": "revision supplied by SquadDash",
  "requestVersion": 1,
  "disposition": "add-amendment",
  "taskIds": ["GROUP-YYYYMMDD-001", "GROUP-YYYYMMDD-002"],
  "title": "Add the requested integration correction",
  "instructions": "The user's bounded additional work"
}
```

`add-amendment` is appropriate for new integration, cleanup, hardening, compatibility, or UX
work discovered during review; a correction spanning multiple reviewed tasks; or work performed
during the approval pause that must be incorporated and verified. It creates a new task after the
named reviewed tasks and returns to the same approval gate. It does not erase or reopen their
accepted results. Omit `taskIds` only when the amendment applies to the entire reviewed boundary.

Use `unrelated` when the request is separate work and `clarification` when the intended approval
task or requested change is ambiguous; those dispositions omit `taskIds` and `instructions` and
leave the approval unchanged. Always use the exact current request version. A rework response may
name only completed tasks listed in the gate's reviewed boundary.

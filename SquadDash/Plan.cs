using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>Plan lifecycle status constants.</summary>
internal static class PlanLifecycleStatus
{
    internal const string Staged           = "staged";            // pending user approval
    internal const string Approved         = "approved";          // accepted, not yet executing
    internal const string Executing        = "executing";         // actively running
    internal const string AwaitingApproval = "awaiting-approval"; // paused at a human gate
    internal const string Interrupted      = "interrupted";       // stopped mid-execution (needs recovery)
    internal const string Stopped          = "stopped";           // ended by user; partial history preserved
    internal const string Completed        = "completed";         // all tasks finished
    internal const string Archived         = "archived";          // hidden from active lists
    internal const string Blocked          = "blocked";           // one or more tasks failed

    internal static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Staged, Approved, Executing, AwaitingApproval,
            Interrupted, Stopped, Completed, Archived, Blocked,
        };

    internal static bool IsTerminal(string status) =>
        status is Stopped or Completed or Archived;
}

/// <summary>Per-task status constants.</summary>
internal static class PlanTaskStatus
{
    internal const string Pending    = "pending";
    internal const string Executing  = "executing";
    internal const string Complete   = "complete";
    internal const string Partial    = "partial";
    internal const string Failed     = "failed";
    internal const string Superseded = "superseded";
}

/// <summary>Approval-gate status constants.</summary>
internal static class PlanGateStatus
{
    internal const string Pending          = "pending";
    internal const string AwaitingApproval = "awaiting-approval";
    internal const string Approved         = "approved";
    internal const string Skipped          = "skipped";
}

/// <summary>Plan source constants — how the plan entered SquadDash.</summary>
internal static class PlanSource
{
    internal const string TasksJson         = "tasks_json";
    internal const string DecomposeDecision = "decompose_decision";
    internal const string Inbox             = "inbox";
    internal const string Manual            = "manual";
}

/// <summary>Recovery state constants for an interrupted plan.</summary>
internal static class PlanRecoveryState
{
    internal const string PendingRecovery      = "pending-recovery";
    internal const string RecoveryInProgress   = "recovery-in-progress";
    internal const string Recovered            = "recovered";
    internal const string Ended                = "ended";
}

/// <summary>Plan-validation status constants.</summary>
internal static class PlanValidationStatus
{
    internal const string Pending    = "pending";
    internal const string Ready      = "ready";
    internal const string Validating = "validating";
    internal const string Passed     = "passed";
    internal const string Failed     = "failed";
    internal const string Stale      = "stale";
}

internal sealed record PlanAgentAssignment(
    [property: JsonPropertyName("agentHandle")] string AgentHandle,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("allowGenericChildren")] bool AllowGenericChildren = true);

// ─── Value objects ─────────────────────────────────────────────────────────────

/// <summary>Immutable audit entry retained when an accepted task attempt is sent back for rework.</summary>
internal sealed record PlanTaskAttempt(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("commit")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Commit,
    [property: JsonPropertyName("completedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("summary")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Summary,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("note")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Note = null);

internal sealed record PlanTaskOutput(
    [property: JsonPropertyName("outputId")] string OutputId,
    [property: JsonPropertyName("description")] string Description);

/// <summary>Immutable task entry inside a Plan.</summary>
internal sealed record PlanTask(
    [property: JsonPropertyName("taskId")]      string TaskId,
    [property: JsonPropertyName("title")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dependsOn")]   IReadOnlyList<string> DependsOn,
    [property: JsonPropertyName("priority")]    string Priority,
    [property: JsonPropertyName("status")]      string Status,
    [property: JsonPropertyName("parentTaskId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? ParentTaskId      = null,
    [property: JsonPropertyName("commit")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? Commit            = null,
    [property: JsonPropertyName("completedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                DateTimeOffset? CompletedAt = null,
    [property: JsonPropertyName("completionSummary")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? CompletionSummary = null,
    [property: JsonPropertyName("agentAssignments")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<PlanAgentAssignment>? AgentAssignments = null,
    [property: JsonPropertyName("parallelEligible")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                bool? ParallelEligible = null,
    [property: JsonPropertyName("agentRoutingMode")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? AgentRoutingMode = null,
    [property: JsonPropertyName("genericAgentReason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? GenericAgentReason = null,
    [property: JsonPropertyName("attemptHistory")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<PlanTaskAttempt>? AttemptHistory = null,
    [property: JsonPropertyName("outputs")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<PlanTaskOutput>? Outputs = null,
    [property: JsonPropertyName("inputs")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<string>? Inputs = null);

/// <summary>
/// A first-class human approval gate — a dependency barrier between task groups.
/// The gate has no implementation; it blocks downstream work until a human approves.
/// </summary>
internal sealed record PlanApprovalGate(
    [property: JsonPropertyName("gateId")]        string GateId,
    [property: JsonPropertyName("message")]       string Message,
    [property: JsonPropertyName("afterTaskIds")]  IReadOnlyList<string> AfterTaskIds,
    [property: JsonPropertyName("beforeTaskIds")] IReadOnlyList<string> BeforeTaskIds,
    [property: JsonPropertyName("status")]        string Status,
    [property: JsonPropertyName("requestedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? RequestedAt  = null,
    [property: JsonPropertyName("resolvedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? ResolvedAt   = null,
    [property: JsonPropertyName("resolutionNote")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  string? ResolutionNote       = null,
    [property: JsonPropertyName("planRevision")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  string? PlanRevision         = null,
    [property: JsonPropertyName("notifiedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? NotifiedAt   = null,
    [property: JsonPropertyName("presentationAnchor")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  string? PresentationAnchor  = null,
    [property: JsonPropertyName("reworkCount")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
                                                  int ReworkCount = 0,
    [property: JsonPropertyName("lastReworkRequestedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? LastReworkRequestedAt = null,
    [property: JsonPropertyName("lastReworkInstructions")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  string? LastReworkInstructions = null);

/// <summary>
/// Durable cross-task validation node. Unlike a human approval gate, this is executable plan
/// work with no repository mutation: it evaluates declared assertions and records evidence.
/// </summary>
internal sealed record PlanValidationNode(
    [property: JsonPropertyName("validationId")] string ValidationId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("afterTaskIds")] IReadOnlyList<string> AfterTaskIds,
    [property: JsonPropertyName("beforeTaskIds")] IReadOnlyList<string> BeforeTaskIds,
    [property: JsonPropertyName("assertions")] IReadOnlyList<string> Assertions,
    [property: JsonPropertyName("outputIds")] IReadOnlyList<string> OutputIds,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("commands")] IReadOnlyList<string> Commands,
    [property: JsonPropertyName("revalidateAtCompletion")] bool RevalidateAtCompletion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                DateTimeOffset? StartedAt = null,
    [property: JsonPropertyName("completedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                DateTimeOffset? CompletedAt = null,
    [property: JsonPropertyName("validatedCommit")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? ValidatedCommit = null,
    [property: JsonPropertyName("summary")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? Summary = null,
    [property: JsonPropertyName("evidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<string>? Evidence = null);

/// <summary>Lightweight progress snapshot — does not store per-task detail.</summary>
internal sealed record PlanProgress(
    [property: JsonPropertyName("completedCount")]  int CompletedCount,
    [property: JsonPropertyName("totalCount")]      int TotalCount,
    [property: JsonPropertyName("executingTaskId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                    string? ExecutingTaskId = null);

/// <summary>Lifecycle timestamps for a Plan.</summary>
internal sealed record PlanTimestamps(
    [property: JsonPropertyName("createdAt")]     DateTimeOffset CreatedAt,
    [property: JsonPropertyName("acceptedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? AcceptedAt    = null,
    [property: JsonPropertyName("startedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? StartedAt     = null,
    [property: JsonPropertyName("completedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? CompletedAt   = null,
    [property: JsonPropertyName("interruptedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? InterruptedAt = null,
    [property: JsonPropertyName("stoppedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? StoppedAt     = null,
    [property: JsonPropertyName("archivedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  DateTimeOffset? ArchivedAt    = null);

/// <summary>
/// Durable interruption record persisted when execution stops unexpectedly.
/// Carried forward so restart and recovery can surface the right choices.
/// </summary>
internal sealed record PlanInterruptionData(
    [property: JsonPropertyName("reason")]              string Reason,
    [property: JsonPropertyName("recoveryState")]       string RecoveryState,
    [property: JsonPropertyName("loopIteration")]       int LoopIteration,
    [property: JsonPropertyName("interruptedTaskId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        string? InterruptedTaskId   = null,
    [property: JsonPropertyName("lastCompletedTaskId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        string? LastCompletedTaskId = null,
    [property: JsonPropertyName("lastCommit")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        string? LastCommit          = null,
    [property: JsonPropertyName("affectedPaths")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        IReadOnlyList<string>? AffectedPaths      = null,
    [property: JsonPropertyName("partialWorkEvidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        string? PartialWorkEvidence = null,
    [property: JsonPropertyName("taskCommitEvidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        PlanTaskCommitEvidence? TaskCommitEvidence = null);

/// <summary>
/// Host-validated provenance for a commit produced by the interrupted task. This is deliberately
/// scoped to a plan task and execution attempt; elapsed time, author identity, and position in the
/// branch history are not sufficient to attribute a commit to plan work.
/// </summary>
internal sealed record PlanTaskCommitEvidence(
    [property: JsonPropertyName("taskId")]             string TaskId,
    [property: JsonPropertyName("executionAttemptId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        string? ExecutionAttemptId,
    [property: JsonPropertyName("baselineCommit")]     string BaselineCommit,
    [property: JsonPropertyName("commit")]             string Commit,
    [property: JsonPropertyName("summary")]            string Summary,
    [property: JsonPropertyName("verification")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                        DecomposeStepVerification? Verification = null);

// ─── Root aggregate ────────────────────────────────────────────────────────────

/// <summary>
/// Canonical workspace-scoped Plan domain model.
/// Persisted under <c>.squad/plans/{PlanId}.json</c> by <see cref="PlanStore"/>.
/// Never depends on WPF or UI types; all identity and state is immutable at the record level.
/// </summary>
internal sealed record Plan(
    /// <summary>
    /// Stable plan identity. Matches the decompose group ID (e.g. "PLANS-20260727").
    /// Never changes across the plan lifecycle.
    /// </summary>
    [property: JsonPropertyName("planId")]          string PlanId,

    /// <summary>
    /// Approved content revision hash. Computed by <see cref="PendingDecomposePlanStore.ComputeRevision"/>
    /// and sealed for that approved definition. An explicitly approved replacement definition may
    /// advance the revision while retaining the stable PlanId. Used to reject stale decisions.
    /// </summary>
    [property: JsonPropertyName("revision")]        string Revision,

    /// <summary>How the plan entered SquadDash — see <see cref="PlanSource"/>.</summary>
    [property: JsonPropertyName("source")]          string Source,

    /// <summary>Current lifecycle state — see <see cref="PlanLifecycleStatus"/>.</summary>
    [property: JsonPropertyName("lifecycleStatus")] string LifecycleStatus,

    [property: JsonPropertyName("title")]           string Title,
    [property: JsonPropertyName("branch")]          string Branch,
    [property: JsonPropertyName("summary")]         string Summary,

    /// <summary>
    /// Ordered task graph. Ordering matches the original TASKS_JSON declaration.
    /// Dependencies are expressed by each task's <see cref="PlanTask.DependsOn"/> list.
    /// </summary>
    [property: JsonPropertyName("tasks")]           IReadOnlyList<PlanTask> Tasks,

    /// <summary>
    /// First-class approval gates that act as dependency barriers between groups of tasks.
    /// Empty for plans that have no human gates.
    /// </summary>
    [property: JsonPropertyName("approvalGates")]   IReadOnlyList<PlanApprovalGate> ApprovalGates,

    [property: JsonPropertyName("progress")]        PlanProgress Progress,
    [property: JsonPropertyName("timestamps")]      PlanTimestamps Timestamps,

    /// <summary>Populated only when <see cref="LifecycleStatus"/> is "interrupted".</summary>
    [property: JsonPropertyName("interruptionData")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                    PlanInterruptionData? InterruptionData = null,

    /// <summary>
    /// The revision token embedded in the tasks.md header comment.
    /// Allows the store to detect external edits; not serialised when absent.
    /// </summary>
    [property: JsonPropertyName("hostRevision")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                    string? HostRevision = null,

    /// <summary>
    /// First-class cross-task validation nodes. Null preserves compatibility with plans written
    /// before validation nodes were introduced.
    /// </summary>
    [property: JsonPropertyName("validations")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                    IReadOnlyList<PlanValidationNode>? Validations = null);

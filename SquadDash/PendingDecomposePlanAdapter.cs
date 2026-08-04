using System.Linq;

namespace SquadDash;

/// <summary>
/// Converts between <see cref="PendingDecomposePlan"/> (transient approval state) and
/// the canonical <see cref="Plan"/> domain model (durable lifecycle record).
/// <para>
/// Preserving compatibility: the <see cref="Plan.Revision"/> produced here is identical
/// to the value computed by <see cref="PendingDecomposePlanStore.ComputeRevision"/> so
/// that approval decisions referencing the pending plan's revision remain valid against
/// the durable plan.
/// </para>
/// </summary>
internal static class PendingDecomposePlanAdapter
{
    /// <summary>
    /// Creates a durable <see cref="Plan"/> from an approved or staged
    /// <see cref="PendingDecomposePlan"/>.
    /// The plan is placed in <see cref="PlanLifecycleStatus.Staged"/> because
    /// acceptance (writing to tasks.md) has not yet happened at this point.
    /// The caller should transition the status to
    /// <see cref="PlanLifecycleStatus.Approved"/> once the user confirms.
    /// </summary>
    internal static Plan ToPlan(PendingDecomposePlan pending, DateTimeOffset timestamp)
    {
        var group = pending.Group;
        var tasks = group.Tasks
            .Select(t => new PlanTask(
                TaskId:      t.Id,
                Title:       t.Title,
                Description: t.Description,
                DependsOn:   t.DependsOn ?? [],
                Priority:    t.Priority,
                Status:      PlanTaskStatus.Pending,
                ParentTaskId: t.ParentTaskId,
                AgentAssignments: t.AgentAssignments?.Select(a => new PlanAgentAssignment(
                    a.AgentHandle, a.Role, a.AllowGenericChildren)).ToArray(),
                ParallelEligible: t.ParallelEligible,
                AgentRoutingMode: t.AgentRoutingMode,
                GenericAgentReason: t.GenericAgentReason,
                Outputs: t.Outputs?.Select(output => new PlanTaskOutput(
                    output.OutputId, output.Description)).ToArray(),
                Inputs: t.Inputs,
                ProofRequirements: t.ProofRequirements?.Select(requirement =>
                    new PlanTaskProofRequirement(
                        requirement.RequirementId,
                        requirement.ProofType,
                        requirement.Description)).ToArray()))
            .ToArray();

        var totalCount = tasks.Count(t => t.Status != PlanTaskStatus.Superseded);

        var approvalGates = MapApprovalGates(group, pending.Revision);
        var validations = MapValidations(group);

        return new Plan(
            PlanId:          group.GroupId,
            Revision:        pending.Revision,
            Source:          PlanSource.TasksJson,
            LifecycleStatus: PlanLifecycleStatus.Staged,
            Title:           group.GroupTitle,
            Branch:          group.Branch,
            Summary:         group.Summary,
            Tasks:           tasks,
            ApprovalGates:   approvalGates,
            Progress:        new PlanProgress(CompletedCount: 0, TotalCount: totalCount),
            Timestamps:      new PlanTimestamps(CreatedAt: timestamp),
            HostRevision:    group.HostRevision,
            Validations:     validations);
    }

    /// <summary>
    /// Projects the approval contract sealed into a decomposed-plan revision into the durable
    /// gate model used by scheduling and visualization.
    /// </summary>
    internal static IReadOnlyList<PlanApprovalGate> MapApprovalGates(
        DecomposedTaskGroup group,
        string revision) =>
        group.ApprovalGates is { Count: > 0 }
            ? group.ApprovalGates.Select(g => new PlanApprovalGate(
                GateId:       g.GateId,
                Message:      g.Message,
                AfterTaskIds:  g.AfterTaskIds  ?? [],
                BeforeTaskIds: g.BeforeTaskIds ?? [],
                Status:       PlanGateStatus.Pending,
                PlanRevision: revision)).ToArray()
            : [];

    internal static IReadOnlyList<PlanValidationNode> MapValidations(DecomposedTaskGroup group) =>
        group.Validations is { Count: > 0 }
            ? group.Validations.Select(validation => new PlanValidationNode(
                ValidationId: validation.ValidationId,
                Title: validation.Title,
                Description: validation.Description,
                AfterTaskIds: validation.AfterTaskIds,
                BeforeTaskIds: validation.BeforeTaskIds,
                Assertions: validation.Assertions,
                // Preserve null versus an explicitly empty collection. Revision V4 sealed the
                // serialized validation contract, so collapsing [] to null (or vice versa) makes
                // an otherwise identical approved plan fail durable initialization.
                OutputIds: validation.OutputIds,
                Mode: validation.Mode,
                Commands: validation.Commands,
                RevalidateAtCompletion: validation.RevalidateAtCompletion,
                Status: PlanValidationStatus.Pending)).ToArray()
            : [];

    /// <summary>
    /// Reconstructs a <see cref="PendingDecomposePlan"/> from a durable <see cref="Plan"/>.
    /// Used when an Inbox attachment or Plan Viewer needs the legacy format, and to
    /// verify that the durable plan's revision still matches the computed value.
    /// </summary>
    internal static PendingDecomposePlan FromPlan(Plan plan)
    {
        var tasks = plan.Tasks
            .Select(t => new DecomposedSubTask(
                Id:          t.TaskId,
                Description: t.Description,
                DependsOn:   t.DependsOn,
                Priority:    t.Priority,
                Title:       t.Title,
                ParentTaskId: t.ParentTaskId,
                AgentAssignments: t.AgentAssignments?.Select(a => new DecomposedAgentAssignment(
                    a.AgentHandle, a.Role, a.AllowGenericChildren)).ToArray(),
                ParallelEligible: t.ParallelEligible,
                AgentRoutingMode: t.AgentRoutingMode,
                GenericAgentReason: t.GenericAgentReason,
                Outputs: t.Outputs?.Select(output => new DecomposedTaskOutput(
                    output.OutputId, output.Description)).ToArray(),
                Inputs: t.Inputs,
                ProofRequirements: t.ProofRequirements?.Select(requirement =>
                    new DecomposedTaskProofRequirement(
                        requirement.RequirementId,
                        requirement.ProofType,
                        requirement.Description)).ToArray()))
            .ToArray();

        var gates = plan.ApprovalGates is { Count: > 0 }
            ? (IReadOnlyList<DecomposedGate>?)plan.ApprovalGates
                .Select(g => new DecomposedGate(
                    GateId:       g.GateId,
                    Message:      g.Message,
                    AfterTaskIds:  g.AfterTaskIds.Count  > 0 ? g.AfterTaskIds  : null,
                    BeforeTaskIds: g.BeforeTaskIds.Count > 0 ? g.BeforeTaskIds : null))
                .ToArray()
            : null;

        var validations = plan.Validations is { Count: > 0 }
            ? (IReadOnlyList<DecomposedValidationNode>?)plan.Validations
                .Select(validation => new DecomposedValidationNode(
                    ValidationId: validation.ValidationId,
                    Title: validation.Title,
                    Description: validation.Description,
                    AfterTaskIds: validation.AfterTaskIds,
                    BeforeTaskIds: validation.BeforeTaskIds,
                    Assertions: validation.Assertions,
                    OutputIds: validation.OutputIds,
                    Mode: validation.Mode,
                    Commands: validation.Commands,
                    RevalidateAtCompletion: validation.RevalidateAtCompletion))
                .ToArray()
            : null;

        var group = new DecomposedTaskGroup(
            GroupId:       plan.PlanId,
            GroupTitle:    plan.Title,
            Branch:        plan.Branch,
            Summary:       plan.Summary,
            Tasks:         tasks,
            ApprovalGates: gates,
            Validations:   validations,
            HostRevision:  plan.HostRevision);

        return new PendingDecomposePlan(plan.Revision, group);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the stored <see cref="Plan.Revision"/> still
    /// matches the value that <see cref="PendingDecomposePlanStore.ComputeRevision"/>
    /// would produce from the plan's task graph.
    /// Stale plans are plans whose tasks.md content drifted from the persisted revision.
    /// </summary>
    internal static bool RevisionIsValid(Plan plan)
    {
        var pending = FromPlan(plan);
        var expected = PendingDecomposePlanStore.ComputeRevision(pending.Group);
        return string.Equals(plan.Revision, expected, StringComparison.Ordinal);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SquadDash;

internal enum PlanTaskProjectionRepairOutcome
{
    Current,
    Repaired,
    Conflict,
}

internal sealed record PlanTaskProjectionRepairResult(
    PlanTaskProjectionRepairOutcome Outcome,
    string? Error = null);

/// <summary>
/// Reconciles only one host-managed plan block in tasks.md from the canonical durable Plan.
/// Unrelated backlog and task edits remain untouched. An older revision is overwritten only when
/// it is provably the legacy topology of a host-migrated approval amendment.
/// </summary>
internal static class PlanTaskProjectionRepair
{
    internal static TaskParseResult? ReadManagedProjection(string tasksPath, string planId)
    {
        if (!File.Exists(tasksPath)) return null;
        var lines = File.ReadAllLines(tasksPath);
        var marker = $"<!-- decompose-group: {planId} |";
        var starts = lines
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.TrimStart().StartsWith(marker, StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToArray();
        return starts.Length == 1 ? ParseIsolatedGroup(lines, starts[0]) : null;
    }

    internal static PlanTaskProjectionRepairResult Ensure(string tasksPath, Plan plan)
        => EnsureCore(tasksPath, plan, allowHostMigratedAmendmentTopology: false);

    /// <summary>
    /// Synchronizes the deterministic topology migration that moved an approval amendment into
    /// its boundary and rewired only its still-pending downstream frontier. This is deliberately
    /// narrower than ordinary conflict repair: any change to task content, membership, ordinary
    /// ordering, or an unrelated dependency continues to block replacement.
    /// </summary>
    internal static PlanTaskProjectionRepairResult EnsureAfterHostTopologyMigration(
        string tasksPath,
        Plan plan)
        => EnsureCore(tasksPath, plan, allowHostMigratedAmendmentTopology: true);

    private static PlanTaskProjectionRepairResult EnsureCore(
        string tasksPath,
        Plan plan,
        bool allowHostMigratedAmendmentTopology)
    {
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);
        var group = pending.Group with { HostRevision = plan.Revision };
        if (!HasValidGraph(group, out var graphError))
            return new(PlanTaskProjectionRepairOutcome.Conflict, graphError);

        var lines = File.Exists(tasksPath) ? File.ReadAllLines(tasksPath) : [];
        var marker = $"<!-- decompose-group: {plan.PlanId} |";
        var starts = lines
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.TrimStart().StartsWith(marker, StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToArray();
        if (starts.Length > 1)
            return new(PlanTaskProjectionRepairOutcome.Conflict, $"Plan {plan.PlanId} appears more than once in tasks.md.");

        if (starts.Length == 1)
        {
            var isolated = ParseIsolatedGroup(lines, starts[0]);
            if (PlanTaskProjectionValidator.TryGetValidatedItems(
                    plan, isolated, plan.PlanId, requireAllComplete: false, out _, out _))
                return new(PlanTaskProjectionRepairOutcome.Current);

            var projectedRevision = ReadRevision(lines[starts[0]]);
            if (!string.IsNullOrWhiteSpace(projectedRevision) &&
                !string.Equals(projectedRevision, plan.Revision, StringComparison.Ordinal) &&
                (!allowHostMigratedAmendmentTopology ||
                 !IsLegacyAmendmentTopology(isolated, group)))
                return new(
                    PlanTaskProjectionRepairOutcome.Conflict,
                    $"Plan {plan.PlanId} has revision {projectedRevision} in tasks.md, not canonical revision {plan.Revision}.");
        }

        var original = File.Exists(tasksPath) ? File.ReadAllText(tasksPath) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tasksPath)!);
            var writer = new DecomposedTasksWriter();
            if (starts.Length == 0)
                writer.WriteGroup(tasksPath, group, plan.Revision);
            else if (!writer.ReplaceGroup(tasksPath, group, plan.Revision))
                throw new InvalidDataException($"Plan {plan.PlanId} could not be located for projection repair.");
            ApplyCanonicalStatuses(writer, tasksPath, plan);

            var repairedLines = File.ReadAllLines(tasksPath);
            var repairedStart = Array.FindIndex(repairedLines, line =>
                line.TrimStart().StartsWith(marker, StringComparison.Ordinal));
            var repaired = repairedStart >= 0 ? ParseIsolatedGroup(repairedLines, repairedStart) : null;
            if (!PlanTaskProjectionValidator.TryGetValidatedItems(
                    plan, repaired, plan.PlanId, requireAllComplete: false, out _, out var validationError))
                throw new InvalidDataException(validationError);

            return new(PlanTaskProjectionRepairOutcome.Repaired);
        }
        catch (Exception ex)
        {
            if (original is null)
            {
                if (File.Exists(tasksPath)) File.Delete(tasksPath);
            }
            else
            {
                WriteAtomically(tasksPath, original);
            }
            return new(PlanTaskProjectionRepairOutcome.Conflict, ex.Message);
        }
    }

    private static bool IsLegacyAmendmentTopology(
        TaskParseResult projected,
        DecomposedTaskGroup canonical)
    {
        if (!projected.DecomposeGroups.TryGetValue(canonical.GroupId, out var existing))
            return false;
        if (!string.Equals(existing.GroupTitle, canonical.GroupTitle, StringComparison.Ordinal) ||
            !string.Equals(existing.Branch, canonical.Branch, StringComparison.Ordinal) ||
            !string.Equals(existing.Summary, canonical.Summary, StringComparison.Ordinal) ||
            !string.Equals(existing.Delivery, canonical.Delivery, StringComparison.Ordinal))
            return false;

        var canonicalById = canonical.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var existingById = existing.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        if (canonicalById.Count != existingById.Count ||
            canonicalById.Keys.Any(id => !existingById.ContainsKey(id)))
            return false;

        var amendmentIds = canonical.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.AmendmentGateId))
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (amendmentIds.Count == 0)
            return false;

        var affectedDependencies = new HashSet<string>(amendmentIds, StringComparer.Ordinal);
        foreach (var gate in canonical.ApprovalGates ?? [])
        {
            if ((gate.AfterTaskIds ?? []).Any(amendmentIds.Contains))
                affectedDependencies.UnionWith(gate.BeforeTaskIds ?? []);
        }

        var knownIds = canonicalById.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var (id, canonicalTask) in canonicalById)
        {
            var existingTask = existingById[id];
            if (!SameTaskContract(existingTask, canonicalTask))
                return false;
            if ((existingTask.DependsOn ?? []).Any(dependency => !knownIds.Contains(dependency)))
                return false;
            if (!affectedDependencies.Contains(id) &&
                !(existingTask.DependsOn ?? []).SequenceEqual(
                    canonicalTask.DependsOn ?? [], StringComparer.Ordinal))
                return false;
        }

        // A migration may move amendment records, but it must not reorder ordinary plan work.
        var oldOrdinaryOrder = existing.Tasks.Where(task => !amendmentIds.Contains(task.Id)).Select(task => task.Id);
        var newOrdinaryOrder = canonical.Tasks.Where(task => !amendmentIds.Contains(task.Id)).Select(task => task.Id);
        return oldOrdinaryOrder.SequenceEqual(newOrdinaryOrder, StringComparer.Ordinal);
    }

    private static bool SameTaskContract(DecomposedSubTask left, DecomposedSubTask right)
    {
        // tasks.md intentionally projects only the fields emitted by DecomposedTasksWriter.
        // Outputs, inputs, and proof requirements remain authoritative in the durable plan and
        // cannot be compared with (or edited through) this lossy Markdown projection.
        var leftContract = left with
        {
            DependsOn = [],
            Outputs = null,
            Inputs = null,
            ProofRequirements = null,
        };
        var rightContract = right with
        {
            DependsOn = [],
            Outputs = null,
            Inputs = null,
            ProofRequirements = null,
        };
        return string.Equals(
            JsonSerializer.Serialize(leftContract),
            JsonSerializer.Serialize(rightContract),
            StringComparison.Ordinal);
    }

    private static void ApplyCanonicalStatuses(DecomposedTasksWriter writer, string tasksPath, Plan plan)
    {
        foreach (var task in plan.Tasks)
            writer.ResetTaskPending(tasksPath, task.TaskId);
        foreach (var task in plan.Tasks)
        {
            switch (task.Status)
            {
                case PlanTaskStatus.Complete:
                    writer.MarkTaskComplete(
                        tasksPath,
                        task.TaskId,
                        task.Commit ?? "unrecorded",
                        task.CompletionSummary ?? "Completed before the projection was restored.");
                    break;
                case PlanTaskStatus.Partial:
                    writer.MarkTaskPartial(
                        tasksPath,
                        task.TaskId,
                        task.Commit,
                        task.CompletionSummary ?? "Partially completed before the projection was restored.",
                        []);
                    break;
                case PlanTaskStatus.Failed:
                    writer.MarkTaskFailed(tasksPath, task.TaskId);
                    break;
                case PlanTaskStatus.Superseded:
                    writer.MarkTaskSuperseded(
                        tasksPath,
                        task.TaskId,
                        plan.Tasks
                            .Where(candidate => string.Equals(candidate.ParentTaskId, task.TaskId, StringComparison.Ordinal))
                            .Select(candidate => candidate.TaskId)
                            .ToArray());
                    break;
            }
        }
    }

    private static TaskParseResult ParseIsolatedGroup(string[] lines, int start)
    {
        var end = start + 1;
        while (end < lines.Length &&
               !lines[end].TrimStart().StartsWith("<!-- decompose-group:", StringComparison.Ordinal) &&
               !lines[end].StartsWith("# ", StringComparison.Ordinal))
            end++;
        return TasksPanelParser.Parse(lines[start..end]);
    }

    private static string? ReadRevision(string header)
    {
        const string token = "| revision:";
        var start = header.IndexOf(token, StringComparison.Ordinal);
        if (start < 0) return null;
        start += token.Length;
        var end = header.IndexOf("-->", start, StringComparison.Ordinal);
        if (end < 0) end = header.Length;
        return header[start..end].Trim().TrimEnd('|').Trim();
    }

    private static bool HasValidGraph(DecomposedTaskGroup group, out string? error)
    {
        var ids = group.Tasks.Select(task => task.Id).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            error = $"Plan {group.GroupId} contains missing or duplicate task IDs.";
            return false;
        }
        var known = ids.ToHashSet(StringComparer.Ordinal);
        var unknown = group.Tasks.SelectMany(task => task.DependsOn ?? []).FirstOrDefault(id => !known.Contains(id));
        if (unknown is not null)
        {
            error = $"Plan {group.GroupId} references unknown dependency {unknown}.";
            return false;
        }
        if (!CodeHealthGroupRunner.HasNoDependencyCycle(group, out var cycleError))
        {
            error = $"Plan {group.GroupId} contains a dependency cycle: {string.Join(", ", cycleError ?? [])}.";
            return false;
        }
        error = null;
        return true;
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporary = path + ".projection-repair.tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }
}

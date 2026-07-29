using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SquadDash;

/// <summary>
/// Host-owned authorization and evidence for one attempt to execute one plan task.
/// The capability is deliberately scoped to the task, revision, and attempt so evidence
/// from an earlier retry cannot satisfy a later attempt.
/// </summary>
internal sealed record PlanExecutionAssignmentAttempt(
    string AgentHandle,
    string Role,
    bool AllowGenericChildren,
    string Capability,
    string CharterPath,
    string CharterSha256,
    IReadOnlyList<string> RequiredContextPaths,
    string? PrimaryToolCallId = null,
    DateTimeOffset? LaunchedAt = null,
    DateTimeOffset? CompletedAt = null,
    bool? Succeeded = null,
    IReadOnlyList<string>? ObservedContextPaths = null,
    IReadOnlyList<string>? ChildToolCallIds = null);

internal sealed record PlanExecutionAttemptState(
    string AttemptId,
    string PlanId,
    string TaskId,
    string Revision,
    string WorkspacePath,
    DateTimeOffset StartedAt,
    IReadOnlyList<PlanExecutionAssignmentAttempt> Assignments,
    IReadOnlyList<string>? UnexpectedPrimaryToolCallIds = null,
    string Status = "active",
    bool AllowsGenericPrimary = false,
    string? GenericPrimaryToolCallId = null,
    IReadOnlyList<string>? GenericChildToolCallIds = null,
    DateTimeOffset? GenericCompletedAt = null,
    bool? GenericSucceeded = null)
{
    internal static PlanExecutionAttemptState Create(
        string planId,
        string taskId,
        string revision,
        string workspacePath,
        string squadFolderPath,
        IReadOnlyList<DecomposedAgentAssignment> assignments,
        IReadOnlyList<RosterAgent> roster)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        var assignmentAttempts = assignments.Select(assignment =>
        {
            var agent = roster.FirstOrDefault(candidate =>
                candidate.IsActive &&
                string.Equals(candidate.Handle, assignment.AgentHandle, StringComparison.OrdinalIgnoreCase));
            if (agent is null)
                throw new InvalidDataException(
                    $"Task {taskId} assigns unavailable active roster agent '{assignment.AgentHandle}'.");
            var charterPath = agent.CharterPath is { Length: > 0 }
                ? Path.Combine(squadFolderPath, agent.CharterPath.Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(squadFolderPath, "agents", agent.Handle, "charter.md");
            var charter = File.ReadAllText(charterPath);
            var requiredContext = new List<string>();
            var historyPath = Path.Combine(Path.GetDirectoryName(charterPath)!, "history.md");
            if (File.Exists(historyPath))
                requiredContext.Add(Path.GetFullPath(historyPath));
            var decisionsPath = Path.Combine(squadFolderPath, "decisions.md");
            if (File.Exists(decisionsPath))
                requiredContext.Add(Path.GetFullPath(decisionsPath));

            return new PlanExecutionAssignmentAttempt(
                agent.Handle,
                assignment.Role,
                assignment.AllowGenericChildren,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                Path.GetFullPath(charterPath),
                Sha256(charter),
                requiredContext);
        }).ToArray();

        return new PlanExecutionAttemptState(
            attemptId,
            planId,
            taskId,
            revision,
            Path.GetFullPath(workspacePath),
            DateTimeOffset.UtcNow,
            assignmentAttempts);
    }

    internal static PlanExecutionAttemptState CreateGeneric(
        string planId,
        string taskId,
        string revision,
        string workspacePath) =>
        new(
            Guid.NewGuid().ToString("N"),
            planId,
            taskId,
            revision,
            Path.GetFullPath(workspacePath),
            DateTimeOffset.UtcNow,
            [],
            AllowsGenericPrimary: true);

    internal PlanExecutionAssignmentAttempt? FindAuthorization(
        string? attemptId,
        string? taskId,
        string? revision,
        string? agentHandle,
        string? capability)
    {
        if (!string.Equals(AttemptId, attemptId, StringComparison.Ordinal) ||
            !string.Equals(TaskId, taskId, StringComparison.Ordinal) ||
            !string.Equals(Revision, revision, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(agentHandle) ||
            string.IsNullOrWhiteSpace(capability))
            return null;

        return Assignments.FirstOrDefault(item =>
            string.Equals(item.AgentHandle, agentHandle, StringComparison.OrdinalIgnoreCase) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(item.Capability),
                Encoding.UTF8.GetBytes(capability)));
    }

    internal PlanExecutionAttemptState RecordPrimaryLaunch(BackgroundAgentLaunchInfo launch)
    {
        var existing = Assignments.FirstOrDefault(item =>
            string.Equals(item.AgentHandle, launch.AssignedAgentHandle, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return RecordUnexpectedPrimaryLaunch(launch.ToolCallId);
        if (!string.IsNullOrWhiteSpace(existing.PrimaryToolCallId) &&
            !string.Equals(existing.PrimaryToolCallId, launch.ToolCallId, StringComparison.Ordinal))
            return RecordUnexpectedPrimaryLaunch(launch.ToolCallId);

        var updated = Assignments.Select(item =>
            string.Equals(item.AgentHandle, launch.AssignedAgentHandle, StringComparison.OrdinalIgnoreCase)
                ? item with {
                    PrimaryToolCallId = launch.ToolCallId,
                    LaunchedAt = launch.StartedAt ?? item.LaunchedAt ?? DateTimeOffset.UtcNow
                }
                : item).ToArray();
        return this with { Assignments = updated };
    }

    internal PlanExecutionAttemptState RecordUnexpectedPrimaryLaunch(string toolCallId)
    {
        var ids = (UnexpectedPrimaryToolCallIds ?? [])
            .Append(toolCallId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return this with { UnexpectedPrimaryToolCallIds = ids };
    }

    internal PlanExecutionAttemptState RecordGenericPrimaryLaunch(BackgroundAgentLaunchInfo launch)
    {
        if (!AllowsGenericPrimary)
            return RecordUnexpectedPrimaryLaunch(launch.ToolCallId);
        if (string.IsNullOrWhiteSpace(GenericPrimaryToolCallId) ||
            string.Equals(GenericPrimaryToolCallId, launch.ToolCallId, StringComparison.Ordinal))
            return this with {
                GenericPrimaryToolCallId = launch.ToolCallId
            };
        return RecordUnexpectedPrimaryLaunch(launch.ToolCallId);
    }

    internal PlanExecutionAttemptState RecordPrimaryCompletion(
        string primaryToolCallId,
        DateTimeOffset completedAt,
        bool succeeded)
    {
        var matchedAssignment = false;
        var updated = Assignments.Select(item =>
        {
            if (!string.Equals(item.PrimaryToolCallId, primaryToolCallId, StringComparison.Ordinal))
                return item;
            matchedAssignment = true;
            return item with { CompletedAt = completedAt, Succeeded = succeeded };
        }).ToArray();
        if (matchedAssignment)
            return this with { Assignments = updated };
        if (string.Equals(GenericPrimaryToolCallId, primaryToolCallId, StringComparison.Ordinal))
            return this with { GenericCompletedAt = completedAt, GenericSucceeded = succeeded };
        return this;
    }

    internal PlanExecutionAttemptState RecordChildLaunch(string primaryToolCallId, string childToolCallId)
    {
        var updated = Assignments.Select(item =>
        {
            if (!string.Equals(item.PrimaryToolCallId, primaryToolCallId, StringComparison.Ordinal))
                return item;
            var children = (item.ChildToolCallIds ?? [])
                .Append(childToolCallId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return item with { ChildToolCallIds = children };
        }).ToArray();
        if (updated.Any(item => string.Equals(item.PrimaryToolCallId, primaryToolCallId, StringComparison.Ordinal)))
            return this with { Assignments = updated };
        if (string.Equals(GenericPrimaryToolCallId, primaryToolCallId, StringComparison.Ordinal))
        {
            var children = (GenericChildToolCallIds ?? [])
                .Append(childToolCallId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return this with { GenericChildToolCallIds = children };
        }
        return this;
    }

    internal PlanExecutionAttemptState RecordContextRead(string primaryToolCallId, string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var updated = Assignments.Select(item =>
        {
            if (!string.Equals(item.PrimaryToolCallId, primaryToolCallId, StringComparison.Ordinal) ||
                !item.RequiredContextPaths.Any(required => PathsEqual(required, normalizedPath)))
                return item;
            var observed = (item.ObservedContextPaths ?? [])
                .Append(normalizedPath)
                .Distinct(PathComparer)
                .ToArray();
            return item with { ObservedContextPaths = observed };
        }).ToArray();
        return this with { Assignments = updated };
    }

    internal static string Sha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal static class PlanContextReadEvidence
{
    private static readonly HashSet<string> ReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "read", "read_file"
    };

    internal static string? TryResolveFullPath(SquadSdkEvent evt, string workspacePath)
    {
        if (!ReadTools.Contains(evt.ToolName ?? string.Empty))
            return null;

        var path = evt.Path;
        if (string.IsNullOrWhiteSpace(path) && evt.Args.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var key in new[] { "path", "file_path" })
            {
                if (evt.Args.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    path = value.GetString();
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(workspacePath, path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

/// <summary>
/// Applies host-observed launch lineage to an execution attempt. Keeping this transition
/// outside the window makes the same contract available to deterministic integration tests.
/// </summary>
internal static class PlanExecutionEvidenceRecorder
{
    internal static PlanExecutionAttemptState RecordLaunch(
        PlanExecutionAttemptState attempt,
        BackgroundAgentLaunchInfo launch,
        bool launchedByCoordinator,
        string? ownerPrimaryToolCallId)
    {
        if (launchedByCoordinator)
        {
            if (launch.IsVerifiedRosterAssignment)
                return attempt.RecordPrimaryLaunch(launch);
            return attempt.AllowsGenericPrimary
                ? attempt.RecordGenericPrimaryLaunch(launch)
                : attempt.RecordUnexpectedPrimaryLaunch(launch.ToolCallId);
        }

        return string.IsNullOrWhiteSpace(ownerPrimaryToolCallId)
            ? attempt
            : attempt.RecordChildLaunch(ownerPrimaryToolCallId, launch.ToolCallId);
    }
}

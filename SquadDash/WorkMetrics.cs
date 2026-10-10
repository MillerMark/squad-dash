using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal static class PlanWorkItemKinds
{
    internal const string Implementation = "implementation";
    internal const string Verification = "verification";
    internal const string Validation = "validation";
    internal const string Unscoped = "unscoped";
}

internal enum WorkToolCategory
{
    Read,
    Search,
    Write,
    Delegation,
    Shell,
    Other,
}

/// <summary>
/// Deliberately exact tool-name classification used by work metrics and system steering.
/// Unknown names remain Other; command text and output are never interpreted.
/// </summary>
internal static class WorkToolClassifier
{
    private static readonly HashSet<string> ReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "read", "read_file", "read_agent", "read_powershell", "web_fetch",
        "fetch_copilot_cli_documentation", "list_agents", "list_powershell",
    };

    private static readonly HashSet<string> SearchTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "rg", "grep", "glob", "find", "search", "web_search",
    };

    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "apply_patch", "edit", "create", "write", "write_file", "create_file",
        "str_replace_editor",
    };

    private static readonly HashSet<string> DelegationTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "task", "delegate_roster_agent", "delegate_temporary_agent",
    };

    private static readonly HashSet<string> ShellTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "write_powershell", "stop_powershell",
    };

    internal static WorkToolCategory Classify(string? toolName)
    {
        var normalized = toolName?.Trim();
        if (string.IsNullOrEmpty(normalized)) return WorkToolCategory.Other;
        if (ReadTools.Contains(normalized)) return WorkToolCategory.Read;
        if (SearchTools.Contains(normalized)) return WorkToolCategory.Search;
        if (WriteTools.Contains(normalized)) return WorkToolCategory.Write;
        if (DelegationTools.Contains(normalized)) return WorkToolCategory.Delegation;
        if (ShellTools.Contains(normalized)) return WorkToolCategory.Shell;
        return WorkToolCategory.Other;
    }

    internal static bool IsWrite(string? toolName) => Classify(toolName) == WorkToolCategory.Write;
}

internal sealed record WorkMetricsRunSnapshot(
    string RunId,
    string AgentName,
    string? ParentRunId,
    string? PlanId,
    string WorkItemKind,
    string? WorkItemId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool IsLive,
    int ToolCalls,
    int ReadCalls,
    int SearchCalls,
    int WriteCalls,
    int DelegationCalls,
    int ShellCalls,
    int OtherCalls,
    int FilesChanged,
    int AddedLines,
    int RemovedLines)
{
    internal TimeSpan ElapsedAt(DateTimeOffset now) =>
        (CompletedAt ?? now) > StartedAt ? (CompletedAt ?? now) - StartedAt : TimeSpan.Zero;

    internal double ReadSearchPercentage => ToolCalls == 0
        ? 0
        : (ReadCalls + SearchCalls) * 100.0 / ToolCalls;
}

internal static class WorkMetricsBuilder
{
    internal static IReadOnlyList<WorkMetricsRunSnapshot> BuildRuns(
        IEnumerable<TranscriptThreadState> threads,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(threads);
        var runs = new List<WorkMetricsRunSnapshot>();
        foreach (var thread in threads.Where(thread => !thread.IsPlaceholderThread))
        {
            foreach (var turn in EnumerateTurns(thread))
                runs.Add(BuildRun(thread, turn, now));
        }

        return runs
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.AgentName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<TurnMetricsSource> EnumerateTurns(TranscriptThreadState thread)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var turn in thread.SavedTurns)
        {
            var key = BuildTurnKey(turn.StartedAt, turn.Prompt);
            if (!seen.Add(key)) continue;
            yield return new TurnMetricsSource(
                turn.StartedAt,
                turn.CompletedAt,
                turn.PlanId,
                turn.PlanWorkItemKind,
                turn.PlanWorkItemId,
                turn.Tools.Select(tool => new ToolMetricsSource(
                    tool.Descriptor.ToolName,
                    tool.StartedAt,
                    tool.FinishedAt,
                    tool.IsCompleted,
                    tool.Success,
                    tool.CapturedEdits)).ToArray());
        }

        if (thread.CurrentTurn is { } current)
        {
            var key = BuildTurnKey(current.StartedAt, current.Prompt);
            if (seen.Add(key))
            {
                yield return new TurnMetricsSource(
                    current.StartedAt,
                    thread.CompletedAt,
                    current.PlanId,
                    current.PlanWorkItemKind,
                    current.PlanWorkItemId,
                    current.ToolEntries.Select(tool => new ToolMetricsSource(
                        tool.Descriptor.ToolName,
                        tool.StartedAt,
                        tool.FinishedAt,
                        tool.IsCompleted,
                        tool.Success,
                        tool.CapturedEdits ?? ToolTranscriptFormatter.TryExtractCapturedEdits(
                            tool.Descriptor, tool.OutputText, tool.ArgsJson))).ToArray());
            }
        }
    }

    private static WorkMetricsRunSnapshot BuildRun(
        TranscriptThreadState thread,
        TurnMetricsSource turn,
        DateTimeOffset now)
    {
        var counts = Enum.GetValues<WorkToolCategory>()
            .ToDictionary(category => category, _ => 0);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var removed = 0;
        foreach (var tool in turn.Tools)
        {
            counts[WorkToolClassifier.Classify(tool.ToolName)]++;
            if (!tool.IsCompleted || !tool.Success || tool.CapturedEdits is not { Count: > 0 })
                continue;
            foreach (var edit in tool.CapturedEdits)
            {
                paths.Add(edit.Path);
                added += edit.AddedLineCount;
                removed += edit.RemovedLineCount;
            }
        }

        var completedAt = turn.CompletedAt ?? (thread.CompletedAt is { } threadCompleted &&
            threadCompleted >= turn.StartedAt ? threadCompleted : null);
        var isLive = thread.IsSteeringTurnActive && thread.CurrentTurn is { } current &&
                     current.StartedAt == turn.StartedAt;
        return new WorkMetricsRunSnapshot(
            $"{thread.ThreadId}:{turn.StartedAt.UtcTicks}",
            ResolveAgentName(thread),
            thread.OriginThread?.ThreadId ?? thread.OriginParentToolCallId,
            turn.PlanId ?? thread.PlanId,
            string.IsNullOrWhiteSpace(turn.PlanWorkItemKind ?? thread.PlanWorkItemKind)
                ? PlanWorkItemKinds.Unscoped
                : (turn.PlanWorkItemKind ?? thread.PlanWorkItemKind)!,
            turn.PlanWorkItemId ?? thread.PlanWorkItemId ?? thread.AssignedPlanTaskId,
            turn.StartedAt,
            completedAt,
            isLive,
            turn.Tools.Count,
            counts[WorkToolCategory.Read],
            counts[WorkToolCategory.Search],
            counts[WorkToolCategory.Write],
            counts[WorkToolCategory.Delegation],
            counts[WorkToolCategory.Shell],
            counts[WorkToolCategory.Other],
            paths.Count,
            added,
            removed);
    }

    private static string ResolveAgentName(TranscriptThreadState thread) =>
        FirstNonEmpty(thread.AgentDisplayName, thread.AgentName, thread.RequestedAgentHandle,
            thread.Title, "Agent");

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string BuildTurnKey(DateTimeOffset startedAt, string? prompt) =>
        startedAt.UtcTicks + "\n" + (prompt ?? string.Empty);

    private sealed record TurnMetricsSource(
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        string? PlanId,
        string? PlanWorkItemKind,
        string? PlanWorkItemId,
        IReadOnlyList<ToolMetricsSource> Tools);

    private sealed record ToolMetricsSource(
        string ToolName,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt,
        bool IsCompleted,
        bool Success,
        IReadOnlyList<CapturedFileEdit>? CapturedEdits);
}

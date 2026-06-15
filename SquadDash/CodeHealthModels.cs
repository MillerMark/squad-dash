using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

internal enum CodeHealthTaskOutcome { Completed, Skipped, Error, Interrupted }

internal sealed record CodeHealthTaskResult(
    string               Id,
    string               Title,
    CodeHealthTaskOutcome Outcome,
    TimeSpan             Duration,
    string?              BranchCreated      = null,
    IReadOnlyList<string>? FilesChanged    = null,
    string?              ErrorMessage       = null,
    string?              SafetyOverrideNote = null);

internal sealed class CodeHealthReport {
    public required IReadOnlyList<string>                RanTaskIds     { get; init; }
    public required IReadOnlyList<string>                SkippedTaskIds { get; init; }
    public required IReadOnlyList<CodeHealthTaskResult> TaskResults    { get; init; }
    public required DateTimeOffset                       StartedAt      { get; init; }
    public required DateTimeOffset                       CompletedAt    { get; init; }
    public          string?                              Summary        { get; init; }

    public TimeSpan Duration => CompletedAt - StartedAt;
}

/// <summary>
/// A single code health task stub entry serialised to the JSON sidecar file
/// (<c>.squad/code-health-reports/YYYYMMDD-HHmmss.json</c>) so stubs can be
/// re-rendered in the coordinator thread after an app restart.
/// </summary>
internal sealed class CodeHealthStubRecord {
    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; init; } = "";

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("anchorIndex")]
    public int AnchorIndex { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; init; }
}

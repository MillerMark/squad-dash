using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace SquadDash;

// ── Data records ─────────────────────────────────────────────────────────────
// These types live in the data layer so both the persistence layer
// (WorkspaceConversationStore) and the display layer (ToolTranscriptFormatter)
// can depend on them without creating an upward dependency.

internal sealed record ToolTranscriptDescriptor(
    string ToolName,
    string? Description = null,
    string? Command = null,
    string? Path = null,
    string? Intent = null,
    string? Skill = null,
    string? DisplayText = null);

internal sealed record ToolTranscriptDetail(
    ToolTranscriptDescriptor Descriptor,
    string? ArgsJson,
    string? FullOutput,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ProgressText,
    bool IsCompleted,
    bool Success);

internal sealed record ToolEditDiffSummary(
    string DisplayName,
    int AddedLineCount,
    int RemovedLineCount,
    bool IsNewFile,
    bool IsDeletedFile);

/// <summary>
/// Durable, per-file representation of one captured edit.  Unlike raw tool output this record
/// is deliberately small enough to retain for the lifetime of its transcript turn.
/// </summary>
internal sealed record CapturedFileEdit(
    string Path,
    string UnifiedDiff,
    int AddedLineCount,
    int RemovedLineCount,
    bool IsNewFile,
    bool IsDeletedFile) {

    [JsonIgnore]
    public string DisplayName {
        get {
            try {
                var name = System.IO.Path.GetFileName(Path);
                return string.IsNullOrWhiteSpace(name) ? Path : name;
            }
            catch (ArgumentException) {
                return Path;
            }
        }
    }
}

/// <summary>A captured edit tool call, including every file changed by that call.</summary>
internal sealed record CapturedEditCall(
    string ToolCallId,
    DateTimeOffset StartedAt,
    IReadOnlyList<CapturedFileEdit> Files);

internal static class ToolTranscriptOutputLimiter {
    internal const int DefaultLiveOutputCharLimit = 100_000;
    internal const int ViewToolLiveOutputCharLimit = 20_000;

    internal static string? TrimForLiveTranscript(
        ToolTranscriptDescriptor descriptor,
        string? text) {
        var limit = string.Equals(descriptor.ToolName, "view", StringComparison.OrdinalIgnoreCase)
            ? ViewToolLiveOutputCharLimit
            : DefaultLiveOutputCharLimit;

        return TrimForLiveTranscript(text, limit);
    }

    internal static string? TrimForLiveTranscript(string? text, int maxChars) {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        if (normalized.Length <= maxChars)
            return normalized;

        var omitted = normalized.Length - maxChars;
        return normalized[..maxChars].TrimEnd()
            + Environment.NewLine
            + Environment.NewLine
            + $"[SquadDash truncated {omitted:N0} characters from live tool output. Use a narrower view if you need the omitted text.]";
    }
}

// ── Detail content builder ────────────────────────────────────────────────────
// Builds the persisted detail-content string for a tool call.
// Located in the data layer so the persistence layer (WorkspaceConversationStore)
// can invoke it without taking a dependency on the display-layer formatter.
// ToolTranscriptFormatter.BuildDetailContent delegates here.

internal static class ToolTranscriptDetailContent {
    public static string Build(ToolTranscriptDetail detail) {
        var builder = new StringBuilder();

        builder.Append("[TOOL] ")
            .Append(detail.Descriptor.ToolName)
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(detail.ArgsJson)) {
            builder.AppendLine(detail.ArgsJson);
            builder.AppendLine();
        }

        builder.Append("Started: ")
            .AppendLine(FormatTimestamp(detail.StartedAt));

        builder.Append("Finished: ")
            .AppendLine(detail.FinishedAt is { } finishedAt
                ? FormatTimestamp(finishedAt)
                : "(still running)");

        builder.Append("Status: ")
            .AppendLine(!detail.IsCompleted
                ? "Running"
                : detail.Success
                    ? "Succeeded"
                    : "Failed");

        if (!string.IsNullOrWhiteSpace(detail.ProgressText)) {
            builder.AppendLine();
            builder.AppendLine("Latest Progress");
            builder.AppendLine(detail.ProgressText);
        }

        if (!string.IsNullOrWhiteSpace(detail.FullOutput)) {
            builder.AppendLine();
            builder.AppendLine(detail.Success ? "Output" : "Error Output");
            builder.AppendLine(detail.FullOutput);
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatTimestamp(DateTimeOffset value) {
        return value.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }
}

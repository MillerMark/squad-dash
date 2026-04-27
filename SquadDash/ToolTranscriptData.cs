using System;
using System.Text;

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

public sealed record InboxMessage
{
    private const string DurablePlanAttachmentType = "decompose-plan";

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("read")]
    public bool Read { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("attachments")]
    public IReadOnlyList<InboxAttachment> Attachments { get; init; } = [];

    [JsonPropertyName("actions")]
    public IReadOnlyList<InboxAction> Actions { get; init; } = [];

    /// <summary>Labels of actions the user has already clicked (persisted).</summary>
    [JsonPropertyName("usedActions")]
    public IReadOnlyList<string> UsedActions { get; init; } = [];

    /// <summary>
    /// HEAD git SHA at the time this message was saved. Used to compute how many commits
    /// have occurred since the message was written, so dispatched actions can include a
    /// staleness note when the codebase has moved on.
    /// Null for messages saved before this field was introduced.
    /// </summary>
    [JsonPropertyName("gitSha")]
    public string? GitSha { get; init; }

    /// <summary>
    /// Optional priority level for this message. Valid values: "low", "mid", "high", "critical".
    /// Defaults to "mid" if absent. Controls the unread indicator appearance in the inbox list.
    /// </summary>
    [JsonPropertyName("priority")]
    public string? Priority { get; init; }

    /// <summary>
    /// Resolves the durable plan group ID associated with this inbox message.
    /// Deterministic resolution order:
    /// 1) first "decompose-plan" attachment with a non-empty planGroupId,
    /// 2) first attachment with a non-empty planGroupId (legacy compatibility).
    /// Returns null when the message is not plan-linked.
    /// </summary>
    public string? ResolvePlanGroupId()
    {
        var attachment = ResolvePlanAttachment();
        return string.IsNullOrWhiteSpace(attachment?.PlanGroupId)
            ? null
            : attachment.PlanGroupId;
    }

    /// <summary>
    /// Resolves the canonical plan-linked attachment for this message.
    /// Prefers the host-owned decompose-plan attachment type and falls back to
    /// any attachment that still carries planGroupId for backward compatibility.
    /// </summary>
    public InboxAttachment? ResolvePlanAttachment()
    {
        var attachments = Attachments ?? [];
        var canonical = attachments.FirstOrDefault(attachment =>
            attachment is not null &&
            string.Equals(attachment.Type, DurablePlanAttachmentType, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(attachment.PlanGroupId));
        if (canonical is not null)
            return canonical;

        return attachments.FirstOrDefault(attachment =>
            attachment is not null &&
            !string.IsNullOrWhiteSpace(attachment.PlanGroupId));
    }
}

public sealed record InboxAttachment
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>Relative file path (for file/markdown attachment types).</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>URL (for url attachment type, and remote image attachments).</summary>
    [JsonPropertyName("href")]
    public string? Href { get; init; }

    /// <summary>Task ID (for task-ref attachment type).</summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    /// <summary>Inbox message ID (for inbox-message-ref attachment type).</summary>
    [JsonPropertyName("inboxMessageId")]
    public string? InboxMessageId { get; init; }

    /// <summary>Inline text or markdown content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Pending decompose-plan group referenced by a decompose-plan attachment.</summary>
    [JsonPropertyName("planGroupId")]
    public string? PlanGroupId { get; init; }

    /// <summary>Immutable revision referenced by a decompose-plan attachment.</summary>
    [JsonPropertyName("planRevision")]
    public string? PlanRevision { get; init; }
}

/// <summary>
/// A deferred action button embedded in an inbox message.
/// Clicking it injects <see cref="Prompt"/> into the queue and routes based on <see cref="RouteMode"/>.
/// The prompt must be fully self-contained — it will be dispatched in a future session
/// with no conversation history from when this message was written.
/// </summary>
public sealed record InboxAction
{
    internal const string HostCommandRouteMode = "host_command";

    /// <summary>Human-readable button label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Routing mode. Valid values: "start_named_agent", "start_coordinator", "host_command", "draft", "done",
    /// and host-owned "decompose" actions generated by SquadDash.
    /// Use "done" only when the label records a meaningful decision (e.g. "Mark resolved", "Already fixed").
    /// Never use "done" for acknowledgement-only actions that record nothing — omit those entirely.
    /// Use "draft" to pre-fill the user's input box with the prompt text without sending it.
    /// </summary>
    [JsonPropertyName("routeMode")]
    public string RouteMode { get; init; } = string.Empty;

    /// <summary>Agent handle (required when routeMode is "start_named_agent").</summary>
    [JsonPropertyName("targetAgent")]
    public string? TargetAgent { get; init; }

    /// <summary>
    /// Host command to validate and execute directly when <see cref="RouteMode"/> is
    /// <c>host_command</c>. The host permits only commands explicitly marked as Inbox-action safe.
    /// </summary>
    [JsonPropertyName("hostCommand")]
    public InboxHostCommand? HostCommand { get; init; }

    /// <summary>
    /// Fully self-contained prompt to inject. Not used when routeMode is "done" or "host_command".
    /// Must include all context the receiving agent needs — file paths, symptoms,
    /// discovered facts — because no session history will be available.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>Optional tooltip hint shown on the action button.</summary>
    [JsonPropertyName("hint")]
    public string? Hint { get; init; }
}

/// <summary>A structured host operation requested by an explicit Inbox action.</summary>
public sealed record InboxHostCommand
{
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }
}

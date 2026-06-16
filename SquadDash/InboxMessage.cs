using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

public sealed record InboxMessage
{
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

    /// <summary>Inline text or markdown content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>
/// A deferred action button embedded in an inbox message.
/// Clicking it injects <see cref="Prompt"/> into the queue and routes based on <see cref="RouteMode"/>.
/// The prompt must be fully self-contained — it will be dispatched in a future session
/// with no conversation history from when this message was written.
/// </summary>
public sealed record InboxAction
{
    /// <summary>Human-readable button label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Routing mode. Valid values: "start_named_agent", "start_coordinator", "draft", "done".
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
    /// Fully self-contained prompt to inject. Null only when routeMode is "done".
    /// Must include all context the receiving agent needs — file paths, symptoms,
    /// discovered facts — because no session history will be available.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>Optional tooltip hint shown on the action button.</summary>
    [JsonPropertyName("hint")]
    public string? Hint { get; init; }
}

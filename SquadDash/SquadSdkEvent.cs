using System.Text.Json;

namespace SquadDash;

public sealed class SquadSdkEvent {
    public string? Type { get; set; }
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }
    public bool? SessionResumed { get; set; }
    public string? SessionReuseKind { get; set; }
    public int? SessionAcquireDurationMs { get; set; }
    public int? SessionResumeDurationMs { get; set; }
    public int? SessionCreateDurationMs { get; set; }
    public string? SessionResumeFailureMessage { get; set; }
    public int? SessionAgeMs { get; set; }
    public int? SessionPromptCountBeforeCurrent { get; set; }
    public int? SessionPromptCountIncludingCurrent { get; set; }
    public string? DiagnosticPhase { get; set; }
    public string? DiagnosticEventType { get; set; }
    public string? SendMethod { get; set; }
    public string? DiagnosticAt { get; set; }
    public string? SendStartedAt { get; set; }
    public string? FirstSdkEventAt { get; set; }
    public string? FirstSdkEventType { get; set; }
    public string? FirstThinkingAt { get; set; }
    public string? FirstResponseAt { get; set; }
    public string? SendCompletedAt { get; set; }
    public int? MillisecondsSinceSendStart { get; set; }
    public int? TimeToFirstSdkEventMs { get; set; }
    public int? TimeToFirstThinkingMs { get; set; }
    public int? TimeToFirstResponseMs { get; set; }
    public int? BackgroundAgentCount { get; set; }
    public int? BackgroundShellCount { get; set; }
    public int? KnownSubagentCount { get; set; }
    public int? ActiveToolCount { get; set; }
    public int? CachedAssistantChars { get; set; }
    public string? RestoredContextSummary { get; set; }
    public string? ParentToolCallId { get; set; }
    public string? Text { get; set; }
    public string? ReasoningText { get; set; }
    public string? Speaker { get; set; }
    public string? Chunk { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public string? Description { get; set; }
    public string? Command { get; set; }
    public string? Path { get; set; }
    public string? Intent { get; set; }
    public string? Skill { get; set; }
    public string? ProgressMessage { get; set; }
    public string? PartialOutput { get; set; }
    public string? OutputText { get; set; }
    public bool? Success { get; set; }
    public JsonElement Args { get; set; }
    public string? Message { get; set; }
    public string? Summary { get; set; }
    public string? AgentId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentDisplayName { get; set; }
    public string? AgentDescription { get; set; }
    public string? Status { get; set; }
    public string? Prompt { get; set; }
    public string? LatestResponse { get; set; }
    public string? LatestIntent { get; set; }
    public string[]? RecentActivity { get; set; }
    public string? Model { get; set; }
    public int? TotalToolCalls { get; set; }
    public int? TotalInputTokens { get; set; }
    public int? TotalOutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? DurationMs { get; set; }
    public SquadBackgroundAgentInfo[]? BackgroundAgents { get; set; }
    public SquadBackgroundShellInfo[]? BackgroundShells { get; set; }
    // Watch lifecycle event fields
    public string? WatchCycleId { get; set; }
    public string? WatchPhase { get; set; }
    public int? WatchFleetSize { get; set; }
    public int? WatchWaveIndex { get; set; }
    public int? WatchWaveCount { get; set; }
    public int? WatchAgentCount { get; set; }
    public string? WatchRetroSummary { get; set; }
    public string? WatchNotificationChannel { get; set; }
    public bool? WatchNotificationSent { get; set; }
    public string? WatchNotificationRecipient { get; set; }
    // Loop lifecycle event fields
    public int? LoopIteration { get; set; }
    public string? LoopStatus { get; set; }
    public string? LoopMdPath { get; set; }
    public string? OutputLine { get; set; }
    // Remote bridge event fields
    public int? RcPort { get; set; }
    public string? RcToken { get; set; }
    public string? RcUrl { get; set; }
    public string? RcLanUrl { get; set; }
    // Remote audio event fields
    public string? ConnectionId { get; set; }
    public string? AudioData { get; set; }
}

public sealed class SquadBackgroundAgentInfo {
    public string? AgentId { get; set; }
    public string? ToolCallId { get; set; }
    public string? AgentType { get; set; }
    public string? Status { get; set; }
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public string? Error { get; set; }
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? LatestResponse { get; set; }
    public string? LatestIntent { get; set; }
    public string[]? RecentActivity { get; set; }
    public string? AgentName { get; set; }
    public string? AgentDisplayName { get; set; }
    public string? Model { get; set; }
    public int? TotalToolCalls { get; set; }
    public int? TotalInputTokens { get; set; }
    public int? TotalOutputTokens { get; set; }
}

public sealed class SquadBackgroundShellInfo {
    public string? ShellId { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Command { get; set; }
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? RecentOutput { get; set; }
    public int? Pid { get; set; }
}

namespace SquadDash;

internal sealed record FollowUpAttachment(
    string CommitSha,
    string Description,
    string? OriginalPrompt,
    string? TranscriptQuote = null);

namespace SquadDash;

internal sealed record FollowUpAttachment(
    string CommitSha,
    string Description,
    string? OriginalPrompt,
    string? TranscriptQuote = null);

/// <summary>DTO for persisting <see cref="FollowUpAttachment"/> items as JSON.</summary>
internal sealed class FollowUpAttachmentDto
{
    public FollowUpAttachmentDto() { }
    public FollowUpAttachmentDto(string? commitSha, string? description, string? originalPrompt, string? transcriptQuote)
    {
        CommitSha       = commitSha;
        Description     = description;
        OriginalPrompt  = originalPrompt;
        TranscriptQuote = transcriptQuote;
    }

    public string? CommitSha       { get; set; }
    public string? Description     { get; set; }
    public string? OriginalPrompt  { get; set; }
    public string? TranscriptQuote { get; set; }
}


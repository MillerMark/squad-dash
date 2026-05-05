namespace SquadDash;

internal sealed record FollowUpAttachment(
    string CommitSha,
    string Description,
    string? OriginalPrompt,
    string? TranscriptQuote = null,
    string? ContentBlock = null);

/// <summary>DTO for persisting <see cref="FollowUpAttachment"/> items as JSON.</summary>
internal sealed class FollowUpAttachmentDto
{
    public FollowUpAttachmentDto() { }
    public FollowUpAttachmentDto(string? commitSha, string? description, string? originalPrompt, string? transcriptQuote, string? contentBlock = null)
    {
        CommitSha       = commitSha;
        Description     = description;
        OriginalPrompt  = originalPrompt;
        TranscriptQuote = transcriptQuote;
        ContentBlock    = contentBlock;
    }

    public string? CommitSha       { get; set; }
    public string? Description     { get; set; }
    public string? OriginalPrompt  { get; set; }
    public string? TranscriptQuote { get; set; }
    public string? ContentBlock    { get; set; }
}


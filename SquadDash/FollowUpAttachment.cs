namespace SquadDash;

internal sealed record FollowUpAttachment(
    string CommitSha,
    string Description,
    string? OriginalPrompt,
    string? TranscriptQuote = null,
    string? ContentBlock = null,
    string? ImagePath = null,
    DateTime? ImageSubmittedAt = null,
    string? InboxMessageId = null,
    string? FileReferencePath = null,
    bool RequiresRemovalConfirmation = false,
    string? RemovalWarning = null,
    string? Notes = null)
{
    internal bool SupportsNotes => ImagePath is null &&
        System.IO.Path.GetExtension(FileReferencePath ?? "").ToLowerInvariant() is not
            (".png" or ".jpg" or ".jpeg" or ".jfif" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff" or ".ico" or ".svg");
}

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

    public string? CommitSha        { get; set; }
    public string? Description      { get; set; }
    public string? OriginalPrompt   { get; set; }
    public string? TranscriptQuote  { get; set; }
    public string? ContentBlock     { get; set; }
    public string? ImagePath        { get; set; }
    public string? ImageSubmittedAt { get; set; }
    public string? InboxMessageId      { get; set; }
    public string? FileReferencePath   { get; set; }
    public bool RequiresRemovalConfirmation { get; set; }
    public string? RemovalWarning { get; set; }
    public string? Notes { get; set; }
}


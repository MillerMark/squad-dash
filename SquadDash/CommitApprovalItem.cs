namespace SquadDash;

internal sealed record CommitApprovalItem(
    string         Id,              // Guid.NewGuid().ToString("N") — stable across sessions
    string         CommitSha,       // Short or full SHA as extracted by ExtractGitCommitSha
    string?        CommitUrl,       // Full GitHub URL, null when _workspaceGitHubUrl is not configured
    string         Description,     // ≤10 words from notifSummary; fallback = first-8-words of prompt
    DateTimeOffset TurnStartedAt,   // Turn's startedAt value — used as scroll-to-turn lookup key
    string?        TurnPromptHint,  // First ~60 chars of prompt for display only
    bool           IsApproved,      // false = Needs Approval, true = Approved
    string?        OriginalPrompt = null  // Full prompt text; null for entries from older versions
) {
    public static CommitApprovalItem Create(
        string         sha,
        string?        url,
        string         description,
        DateTimeOffset turnStartedAt,
        string?        turnPromptHint,
        string?        originalPrompt)
        => new(Guid.NewGuid().ToString("N"), sha, url, description,
               turnStartedAt, turnPromptHint, IsApproved: false, originalPrompt);
}

namespace SquadDash;

/// <summary>
/// Context for a single SHA fetch — carries the turn metadata that the service cannot derive from git.
/// The window builds one of these per <see cref="CommitApprovalItem"/> it wants stats for.
/// </summary>
internal record CommitStatRequest(
    string          Sha,
    string?         FeatureGroupId,    // null → "Uncategorized" at the rendering layer
    DateOnly        TurnDate,          // from CommitApprovalItem.TurnStartedAt, NOT a git timestamp
    DateTimeOffset? TurnStartedAt = null,  // precise turn start time for duration rendering
    DateTimeOffset? CommitTime    = null   // precise commit author time from git
);

/// <summary>
/// Resolved commit stats for a single SHA.  Immutable; cached for the lifetime of the service instance.
/// <para>
/// <see cref="TurnDate"/> is the squad-turn date (from <c>TurnStartedAt</c>), never the git author/commit
/// date.  The service stores whatever the caller supplied; it does not derive dates from git output.
/// </para>
/// </summary>
internal record CommitStatResult(
    string          Sha,
    string?         FeatureGroupId,
    DateOnly        TurnDate,
    int             FilesChanged,
    int             Insertions,
    int             Deletions,
    bool            IsFound,          // false = SHA not found in repo or git call failed
    DateTimeOffset? TurnStartedAt = null,
    DateTimeOffset? CommitTime    = null
);

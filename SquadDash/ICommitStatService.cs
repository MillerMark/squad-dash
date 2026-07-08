namespace SquadDash;

/// <summary>
/// Fetches and caches git commit stats (files changed, insertions, deletions) for a set of SHAs.
/// </summary>
/// <remarks>
/// Register as a <b>singleton</b> so the in-memory cache survives window close/reopen cycles.
/// The workspace folder path is fixed at construction — this product runs against a single workspace.
/// </remarks>
internal interface ICommitStatService
{
    /// <summary>
    /// Fetches stats for any <paramref name="requests"/> whose SHA is not already cached, then
    /// returns results for all requested SHAs (from cache + newly fetched).
    /// <para>
    /// <paramref name="progress"/> is invoked once per resolved git batch so the window can
    /// update hollow-dot loading states incrementally.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CommitStatResult>> GetStatsAsync(
        IEnumerable<CommitStatRequest>          requests,
        IProgress<IReadOnlyList<CommitStatResult>>? progress          = null,
        CancellationToken                           cancellationToken = default);

    /// <summary>
    /// Returns the cached result for <paramref name="sha"/>, or <c>null</c> if not yet fetched.
    /// Call this to filter out already-known SHAs before calling <see cref="GetStatsAsync"/>.
    /// </summary>
    CommitStatResult? TryGetCached(string sha);
}

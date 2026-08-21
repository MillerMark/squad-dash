using System.Security.Cryptography;
using System.Text;

namespace SquadDash;

internal sealed record PlanRecoveryRepositorySnapshot(
    string HeadCommit,
    string Fingerprint);

/// <summary>
/// Gives a recovery assessment a durable identity tied to repository content. Merely reopening
/// or restoring a recovery card never invalidates an assessment; a different commit, tracked
/// diff, or untracked-file content does.
/// </summary>
internal static class PlanRecoveryAssessmentSnapshotPolicy
{
    internal static string CreateFingerprint(
        string headCommit,
        string trackedDiff,
        IEnumerable<(string Path, string ContentHash)> untrackedFiles)
    {
        var payload = new StringBuilder();
        payload.AppendLine(headCommit.Trim());
        payload.AppendLine(trackedDiff.Replace("\r\n", "\n", StringComparison.Ordinal));
        foreach (var file in untrackedFiles
                     .OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            payload.Append(file.Path.Replace('\\', '/'))
                .Append('\0')
                .AppendLine(file.ContentHash);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    internal static bool IsCurrent(
        PlanRecoveryDecisionEvidence? assessment,
        PlanRecoveryRepositorySnapshot snapshot) =>
        assessment is not null &&
        !string.IsNullOrWhiteSpace(assessment.HeadCommit) &&
        !string.IsNullOrWhiteSpace(assessment.RepositoryFingerprint) &&
        string.Equals(assessment.HeadCommit, snapshot.HeadCommit, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            assessment.RepositoryFingerprint,
            snapshot.Fingerprint,
            StringComparison.OrdinalIgnoreCase);
}

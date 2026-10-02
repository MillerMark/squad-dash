using System.Security.Cryptography;
using System.Text;

namespace SquadDash;

internal sealed record PlanRecoveryRepositorySnapshot(
    string HeadCommit,
    string Fingerprint,
    string StatusSnapshot = "");

/// <summary>
/// Signals an intentional safety pause when task-owned source changed after a recovery
/// snapshot was accepted. This is not an application or loop crash.
/// </summary>
internal sealed class PlanRecoverySourceChangedException(string message)
    : InvalidOperationException(message);

internal enum PlanRecoveryContinuationDisposition
{
    Ready,
    WrongScope,
    SourceChanged,
}

/// <summary>
/// Pure transition policy for the assessment-to-dispatch recovery lease. Keeping this logic
/// outside WPF makes every state transition independently testable.
/// </summary>
internal static class PlanRecoveryContinuationPolicy
{
    internal static PlanRecoveryContinuationDisposition Evaluate(
        PlanRecoveryContinuationState continuation,
        string planId,
        string revision,
        string taskId,
        PlanRecoveryRepositorySnapshot currentSnapshot)
    {
        if (!string.Equals(continuation.PlanId, planId, StringComparison.Ordinal) ||
            !string.Equals(continuation.Revision, revision, StringComparison.Ordinal) ||
            !string.Equals(continuation.TaskId, taskId, StringComparison.Ordinal))
            return PlanRecoveryContinuationDisposition.WrongScope;

        return PlanRecoveryAssessmentSnapshotPolicy.Matches(
            continuation.RepositorySnapshot,
            currentSnapshot)
            ? PlanRecoveryContinuationDisposition.Ready
            : PlanRecoveryContinuationDisposition.SourceChanged;
    }

    internal static bool ShouldClearAfterPromptCompletion(
        PlanRecoveryContinuationState? continuation,
        string planId,
        string taskId,
        bool promptCompleted) =>
        continuation is not null &&
        promptCompleted &&
        string.Equals(continuation.PlanId, planId, StringComparison.Ordinal) &&
        string.Equals(continuation.TaskId, taskId, StringComparison.Ordinal);
}

/// <summary>
/// Gives a recovery assessment a durable identity tied to repository content. Merely reopening
/// or restoring a recovery card never invalidates an assessment; a different commit, tracked
/// diff, or untracked-file content does.
/// </summary>
internal static class PlanRecoveryAssessmentSnapshotPolicy
{
    internal static string BuildGitPathspec(IEnumerable<string> hostOwnedPaths)
    {
        var exclusions = hostOwnedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Replace('\\', '/'))
            .Select(path => path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path)
            .Select(path => path.TrimStart('/'))
            .Where(path => path.Length > 0)
            .Select(path => path.EndsWith("/", StringComparison.Ordinal)
                ? path + "**"
                : path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"\":(exclude){path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
        return "-- . " + string.Join(" ", exclusions);
    }

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

    internal static bool Matches(
        PlanRecoveryRepositorySnapshot expected,
        PlanRecoveryRepositorySnapshot actual) =>
        string.Equals(expected.HeadCommit, actual.HeadCommit, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.Fingerprint, actual.Fingerprint, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.StatusSnapshot, actual.StatusSnapshot, StringComparison.Ordinal);
}

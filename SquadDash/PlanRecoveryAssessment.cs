using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanRecoveryClassification
{
    internal const string Complete = "complete";
    internal const string Partial = "partial";
    internal const string NotStarted = "not_started";
    internal const string Inconclusive = "inconclusive";

    internal static bool IsValid(string value) =>
        value is Complete or Partial or NotStarted or Inconclusive;
}

internal static class PlanRecoveryCommitRelation
{
    internal const string Task = "task";
    internal const string Mixed = "mixed";
    internal const string Unrelated = "unrelated";
    internal const string Unknown = "unknown";

    internal static bool IsValid(string value) =>
        value is Task or Mixed or Unrelated or Unknown;
}

internal sealed record PlanRecoveryCommitAssessment(
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("relation")] string Relation,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// AI's semantic assessment of repository state after an interrupted plan task. The host treats
/// this as an advisory claim and validates every identity and git fact before changing plan state.
/// </summary>
internal sealed record PlanRecoveryAssessmentResponse(
    [property: JsonPropertyName("recoveryAssessmentId")] string RecoveryAssessmentId,
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("baselineCommit")] string BaselineCommit,
    [property: JsonPropertyName("assessedHead")] string AssessedHead,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("remainingWork")] IReadOnlyList<string>? RemainingWork,
    [property: JsonPropertyName("verification")] DecomposeStepVerification? Verification,
    [property: JsonPropertyName("commits")] IReadOnlyList<PlanRecoveryCommitAssessment> Commits);

internal static class PlanRecoveryAssessmentParser
{
    internal const string Marker = "PLAN_RECOVERY_ASSESSMENT_JSON:";

    internal static bool TryParse(
        string? text,
        out PlanRecoveryAssessmentResponse? response,
        out string? error)
    {
        response = null;
        error = null;
        if (!StructuredJsonBlockParser.TryExtractObject<PlanRecoveryAssessmentResponse>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        response = extraction.Payload;
        if (response is null ||
            string.IsNullOrWhiteSpace(response.RecoveryAssessmentId) ||
            string.IsNullOrWhiteSpace(response.PlanId) ||
            string.IsNullOrWhiteSpace(response.TaskId) ||
            string.IsNullOrWhiteSpace(response.Revision) ||
            string.IsNullOrWhiteSpace(response.BaselineCommit) ||
            string.IsNullOrWhiteSpace(response.AssessedHead) ||
            string.IsNullOrWhiteSpace(response.Summary) ||
            response.Commits is null)
        {
            error = "The recovery assessment omitted required identity, repository, summary, or commit fields.";
            return false;
        }

        if (!PlanRecoveryClassification.IsValid(response.Classification))
        {
            error = "The recovery classification must be complete, partial, not_started, or inconclusive.";
            return false;
        }

        if (response.Commits.Any(commit =>
                string.IsNullOrWhiteSpace(commit.Commit) ||
                string.IsNullOrWhiteSpace(commit.Reason) ||
                !PlanRecoveryCommitRelation.IsValid(commit.Relation)))
        {
            error = "Every assessed commit requires a commit, reason, and valid relation.";
            return false;
        }

        if (response.Classification == PlanRecoveryClassification.Complete &&
            !string.Equals(response.Verification?.Status, "passed", StringComparison.OrdinalIgnoreCase))
        {
            error = "A complete recovery assessment requires passed verification evidence.";
            return false;
        }

        if (response.Classification == PlanRecoveryClassification.Partial &&
            response.RemainingWork is not { Count: > 0 })
        {
            error = "A partial recovery assessment must describe the remaining work.";
            return false;
        }

        return true;
    }
}

internal static class PlanRecoveryAssessmentValidator
{
    internal static bool TryValidateAgainstPlanEvidence(
        Plan plan,
        PlanRecoveryAssessmentResponse response,
        out string? error)
    {
        error = null;
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, response.TaskId, StringComparison.Ordinal));
        if (task is null)
        {
            error = $"The assessed task {response.TaskId} is not present in the durable plan.";
            return false;
        }

        var latestScrutiny = task.ScrutinyHistory?.LastOrDefault();
        var unresolved = latestScrutiny is not null &&
                         !string.Equals(
                             latestScrutiny.Verdict,
                             PlanTaskScrutinyVerdict.Accepted,
                             StringComparison.Ordinal)
            ? latestScrutiny
            : null;
        if (response.Classification == PlanRecoveryClassification.Complete && unresolved is not null)
        {
            error = "AI classified the task as complete while independent scrutiny remains unresolved: " +
                    unresolved.Summary +
                    " Use partial when bounded corrective work remains, or inconclusive when human judgment is required.";
            return false;
        }

        return true;
    }

    internal static bool MatchesRequest(
        PlanRecoveryAssessmentResponse response,
        string assessmentId,
        string planId,
        string taskId,
        string revision,
        string baselineCommit,
        string assessedHead) =>
        string.Equals(response.RecoveryAssessmentId, assessmentId, StringComparison.Ordinal) &&
        string.Equals(response.PlanId, planId, StringComparison.Ordinal) &&
        string.Equals(response.TaskId, taskId, StringComparison.Ordinal) &&
        string.Equals(response.Revision, revision, StringComparison.Ordinal) &&
        string.Equals(response.BaselineCommit, baselineCommit, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(response.AssessedHead, assessedHead, StringComparison.OrdinalIgnoreCase);

    internal static bool TryValidateCommitCoverage(
        PlanRecoveryAssessmentResponse response,
        IReadOnlyList<string> actualCommits,
        out IReadOnlyList<string> attributedCommits,
        out string? error)
    {
        attributedCommits = [];
        error = null;
        var actualSet = new HashSet<string>(actualCommits, StringComparer.OrdinalIgnoreCase);
        var assessed = new Dictionary<string, PlanRecoveryCommitAssessment>(StringComparer.OrdinalIgnoreCase);
        foreach (var commit in response.Commits)
        {
            if (!actualSet.Contains(commit.Commit) || !assessed.TryAdd(commit.Commit, commit))
            {
                error = "The assessment omitted, duplicated, or referenced a commit outside the captured task range.";
                return false;
            }
        }
        if (assessed.Count != actualCommits.Count)
        {
            error = "The assessment did not classify every commit after the task baseline.";
            return false;
        }

        var attributed = actualCommits.Where(commit => assessed[commit].Relation is
            PlanRecoveryCommitRelation.Task or PlanRecoveryCommitRelation.Mixed).ToArray();
        if (response.Classification == PlanRecoveryClassification.Complete && attributed.Length == 0)
        {
            error = "AI classified the task as complete without identifying any commit that contains its work.";
            return false;
        }
        if (response.Classification == PlanRecoveryClassification.NotStarted && attributed.Length > 0)
        {
            error = "The assessment called the task not started while also attributing commits to it.";
            return false;
        }

        attributedCommits = attributed;
        return true;
    }
}

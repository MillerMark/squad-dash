using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    internal const string Superseded = "superseded";

    internal static bool IsValid(string value) =>
        value is Task or Mixed or Unrelated or Unknown or Superseded;
}

internal sealed record PlanRecoveryCommitAssessment(
    [property: JsonPropertyName("commitId")] string CommitId,
    [property: JsonPropertyName("relation")] string Relation,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record PlanRecoverySupportingCommitAssessment(
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("relation")] string Relation,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record PlanRecoveryCommitReference(
    string Id,
    string Commit);

/// <summary>
/// AI's semantic assessment of repository state after an interrupted plan task. The host treats
/// this as an advisory claim and validates every identity and git fact before changing plan state.
/// </summary>
internal sealed record PlanRecoveryAssessmentResponse(
    [property: JsonPropertyName("recoveryAssessmentId")] string RecoveryAssessmentId,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("remainingWork")] IReadOnlyList<string>? RemainingWork,
    [property: JsonPropertyName("verification")] DecomposeStepVerification? Verification,
    [property: JsonPropertyName("commits")] IReadOnlyList<PlanRecoveryCommitAssessment> Commits,
    [property: JsonPropertyName("supportingCommits")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<PlanRecoverySupportingCommitAssessment>? SupportingCommits = null);

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
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<PlanRecoveryAssessmentResponse>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        response = extraction.Payload;
        response = response with
        {
            Classification = response.Classification?.Trim().ToLowerInvariant() ?? string.Empty,
            RemainingWork = (response.RemainingWork ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
            Commits = (response.Commits ?? [])
                .Where(commit => commit is not null)
                .Select(commit =>
                {
                    var relation = commit.Relation?.Trim().ToLowerInvariant() ?? string.Empty;
                    var reason = string.IsNullOrWhiteSpace(commit.Reason)
                        ? relation == PlanRecoveryCommitRelation.Unrelated
                            ? "Classified as unrelated during recovery assessment."
                            : string.Empty
                        : commit.Reason.Trim();
                    return commit with { Relation = relation, Reason = reason };
                })
                .ToArray(),
            SupportingCommits = (response.SupportingCommits ?? [])
                .Where(commit => commit is not null)
                .Select(commit => commit with
                {
                    Relation = commit.Relation?.Trim().ToLowerInvariant() ?? string.Empty,
                })
                .ToArray(),
        };
        if (response is null)
        {
            error = "The recovery assessment payload was empty.";
            return false;
        }

        // Report every finding that can be determined from this payload. A one-shot repair
        // must not fix the first schema problem only to reveal a second independent problem.
        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(response.RecoveryAssessmentId) ||
            string.IsNullOrWhiteSpace(response.Summary))
            validationErrors.Add("The recovery assessment omitted its assessment identity or summary.");

        if (!PlanRecoveryClassification.IsValid(response.Classification))
            validationErrors.Add("The recovery classification must be complete, partial, not_started, or inconclusive.");

        if (response.Commits.Any(commit =>
                string.IsNullOrWhiteSpace(commit.CommitId) ||
                !PlanRecoveryCommitRelation.IsValid(commit.Relation) ||
                (commit.Relation != PlanRecoveryCommitRelation.Unrelated &&
                 string.IsNullOrWhiteSpace(commit.Reason))))
            validationErrors.Add("Every assessed commit requires a commitId and valid relation; non-unrelated commits also require a reason.");

        if ((response.SupportingCommits ?? []).Any(commit =>
                string.IsNullOrWhiteSpace(commit.Commit) ||
                string.IsNullOrWhiteSpace(commit.Reason) ||
                !PlanRecoveryCommitRelation.IsValid(commit.Relation)))
            validationErrors.Add("Every supporting commit requires a commit, reason, and valid relation.");

        if (response.Classification == PlanRecoveryClassification.Complete &&
            !string.Equals(response.Verification?.Status, "passed", StringComparison.OrdinalIgnoreCase))
            validationErrors.Add("A complete recovery assessment requires passed verification evidence.");

        if (response.Classification == PlanRecoveryClassification.Partial &&
            response.RemainingWork is not { Count: > 0 })
            validationErrors.Add("A partial recovery assessment must describe the remaining work.");

        if (validationErrors.Count > 0)
        {
            error = string.Join(" ", validationErrors);
            return false;
        }

        return true;
    }
}

/// <summary>
/// Reconstructs the candidate envelope needed to resume independent verification after the
/// live execution envelope was lost. The worker handoff remains candidate evidence; this policy
/// never marks the task complete or treats the worker's build as independent acceptance.
/// </summary>
internal static class PlanVerificationRecoveryPolicy
{
    internal static bool CanResume(Plan plan, string taskId)
    {
        if (plan.LifecycleStatus is not (PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked))
            return false;

        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        return task is not null &&
               task.Status is PlanTaskStatus.VerificationPending or PlanTaskStatus.Verifying &&
               task.Handoff is { } handoff &&
               IsSafeGitCommitIdentifier(handoff.Commit) &&
               string.Equals(handoff.Verification?.Status, "passed", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryCreateCandidate(
        Plan plan,
        string taskId,
        string resolvedCommit,
        out DecomposeStepResult? candidate,
        out string? error)
    {
        candidate = null;
        error = null;
        if (!CanResume(plan, taskId))
        {
            error = "The interrupted task does not have a verification-stage candidate handoff with passed build evidence.";
            return false;
        }
        if (!IsSafeGitCommitIdentifier(resolvedCommit))
        {
            error = "The stored candidate commit did not resolve to a valid Git commit identifier.";
            return false;
        }

        var task = plan.Tasks.Single(item =>
            string.Equals(item.TaskId, taskId, StringComparison.Ordinal));
        var handoff = task.Handoff!;
        var proofEvidence = (task.ProofEvidence ?? [])
            .Select(item => new DecomposeStepProofEvidence(
                item.RequirementId,
                item.ProofType,
                item.Summary,
                item.Artifacts))
            .ToArray();
        candidate = new DecomposeStepResult(
            plan.PlanId,
            taskId,
            plan.Revision,
            "complete",
            resolvedCommit,
            handoff.Summary,
            [],
            handoff.Verification,
            ProofEvidence: proofEvidence,
            DeferredWork: handoff.DeferredWork);
        return true;
    }

    private static bool IsSafeGitCommitIdentifier(string? value) =>
        value is { Length: >= 7 and <= 64 } &&
        Regex.IsMatch(value, "^[0-9a-fA-F]+$", RegexOptions.CultureInvariant);
}

internal static class PlanRecoveryAssessmentFallbackPolicy
{
    internal const string UnverifiedCompleteError =
        "A complete recovery assessment requires passed verification evidence.";

    /// <summary>
    /// Preserves useful semantic and commit-attribution evidence after the one repair is used,
    /// but converts an unverified completion claim into the existing human-review boundary.
    /// </summary>
    internal static bool TryDowngradeUnverifiedComplete(
        PlanRecoveryAssessmentResponse? response,
        string? error,
        out PlanRecoveryAssessmentResponse? downgraded)
    {
        downgraded = null;
        if (response?.Classification != PlanRecoveryClassification.Complete ||
            !string.Equals(error?.Trim(), UnverifiedCompleteError, StringComparison.Ordinal))
            return false;

        downgraded = response with
        {
            Classification = PlanRecoveryClassification.Inconclusive,
            Summary = "Implementation evidence was found, but independent verification was not passed. " +
                      response.Summary.Trim(),
            RemainingWork = [],
        };
        return true;
    }
}

internal static class PlanRecoveryAssessmentValidator
{
    internal static bool IsSafeGitCommitIdentifier(string? value) =>
        value is { Length: >= 7 and <= 40 } && value.All(Uri.IsHexDigit);

    internal static bool TryValidateAgainstPlanEvidence(
        Plan plan,
        string taskId,
        PlanRecoveryAssessmentResponse response,
        out string? error)
    {
        error = null;
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        if (task is null)
        {
            error = $"The assessed task {taskId} is not present in the durable plan.";
            return false;
        }

        var latestVerification = task.VerificationHistory?.LastOrDefault();
        var unresolved = latestVerification is not null &&
                         !string.Equals(
                             latestVerification.Verdict,
                             PlanTaskVerificationVerdict.Accepted,
                             StringComparison.Ordinal)
            ? latestVerification
            : null;
        if (response.Classification == PlanRecoveryClassification.Complete && unresolved is not null)
        {
            error = "AI classified the task as complete while independent verification remains unresolved: " +
                    unresolved.Summary +
                    " Use partial when bounded corrective work remains, or inconclusive when human judgment is required.";
            return false;
        }

        return true;
    }

    internal static bool MatchesRequest(
        PlanRecoveryAssessmentResponse response,
        string assessmentId) =>
        string.Equals(response.RecoveryAssessmentId, assessmentId, StringComparison.Ordinal);

    internal static IReadOnlyList<PlanRecoveryCommitReference> CreateCommitReferences(
        IReadOnlyList<string> commits) =>
        commits.Select((commit, index) =>
                new PlanRecoveryCommitReference($"c{index + 1:D3}", commit))
            .ToArray();

    internal static bool TryValidateCommitCoverage(
        PlanRecoveryAssessmentResponse response,
        IReadOnlyList<PlanRecoveryCommitReference> actualCommits,
        out IReadOnlyList<string> attributedCommits,
        out string? error)
    {
        attributedCommits = [];
        error = null;
        var actualById = actualCommits.ToDictionary(
            reference => reference.Id,
            reference => reference,
            StringComparer.OrdinalIgnoreCase);
        var assessed = new Dictionary<string, PlanRecoveryCommitAssessment>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();
        var unexpected = new List<string>();
        foreach (var commit in response.Commits)
        {
            if (!actualById.ContainsKey(commit.CommitId))
                unexpected.Add(commit.CommitId);
            else if (!assessed.TryAdd(commit.CommitId, commit))
                duplicates.Add(commit.CommitId);
        }
        var missing = actualCommits.Where(reference => !assessed.ContainsKey(reference.Id)).ToArray();
        var validationErrors = new List<string>();
        if (missing.Length > 0 || unexpected.Count > 0 || duplicates.Count > 0)
        {
            var details = new List<string>();
            if (missing.Length > 0)
                details.Add("Missing: " + string.Join(", ", missing.Select(reference => reference.Id)));
            if (unexpected.Count > 0)
                details.Add("Not in the captured range: " + string.Join(", ", unexpected.Distinct(StringComparer.OrdinalIgnoreCase)));
            if (duplicates.Count > 0)
                details.Add("Duplicated: " + string.Join(", ", duplicates.Distinct(StringComparer.OrdinalIgnoreCase)));
            validationErrors.Add(
                "The assessment did not classify every commit after the task baseline exactly once. " +
                string.Join(". ", details) + ".");
        }

        var attributed = actualCommits.Where(reference =>
                assessed.TryGetValue(reference.Id, out var assessment) &&
                assessment.Relation is PlanRecoveryCommitRelation.Task or PlanRecoveryCommitRelation.Mixed)
            .Select(reference => reference.Commit)
            .ToArray();
        if (response.Classification == PlanRecoveryClassification.Complete && attributed.Length == 0)
        {
            validationErrors.Add(
                "AI classified the task as complete without identifying any commit that contains its current work.");
        }
        if (response.Classification == PlanRecoveryClassification.NotStarted && attributed.Length > 0)
        {
            var attributedIds = actualCommits
                .Where(reference => attributed.Contains(reference.Commit, StringComparer.OrdinalIgnoreCase))
                .Select(reference => reference.Id);
            validationErrors.Add(
                "The assessment called the current task revision not started while also marking these commits as " +
                $"task or mixed: {string.Join(", ", attributedIds)}. Use relation superseded for an older implementation " +
                "that the current task revision replaced.");
        }

        attributedCommits = attributed;
        if (validationErrors.Count > 0)
        {
            error = string.Join(" ", validationErrors);
            return false;
        }

        return true;
    }
}

internal static class PlanRecoveryAssessmentRetryPolicy
{
    internal const int MaximumRepositoryChangeRetries = 1;

    internal static bool CanRetryRepositoryChange(int completedRetries) =>
        completedRetries < MaximumRepositoryChangeRetries;
}

internal static class PlanRecoveryAssessmentErrorReport
{
    internal const string LinkPrefix = "app://plan-recovery-assessment-error/";

    internal static string Build(
        string planId,
        string taskId,
        string assessmentId,
        string? error,
        string responseAttempt = "Initial assessment response")
    {
        var result = string.Equals(
            responseAttempt,
            "Initial assessment response",
            StringComparison.Ordinal)
            ? "SquadDash left the plan unchanged and requested one corrected structured response from AI."
            : "SquadDash left the plan unchanged because the corrected response still did not satisfy the recovery contract.";
        return $"""
            Recovery assessment validation report

            Plan: {planId}
            Task: {taskId}
            Assessment: {assessmentId}
            Response attempt: {responseAttempt}

            Validation findings
            {error?.Trim() ?? "The response did not satisfy the recovery assessment contract."}

            Result
            {result}
            """;
    }

    internal static string CreateLinkTarget(string report)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(report))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return LinkPrefix + encoded;
    }

    internal static bool TryDecodeLinkTarget(string? target, out string report)
    {
        report = string.Empty;
        if (string.IsNullOrWhiteSpace(target) ||
            !target.StartsWith(LinkPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = target[LinkPrefix.Length..];
        if (encoded.Length is 0 or > 32768)
            return false;

        encoded = encoded.Replace('-', '+').Replace('_', '/');
        encoded += (encoded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        try
        {
            report = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return !string.IsNullOrWhiteSpace(report);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PlanRevisionProposalPayload(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("baseRevision")] string BaseRevision,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("reopenTaskIds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ReopenTaskIds,
    [property: JsonPropertyName("revisedPlan")] DecomposedTaskGroup RevisedPlan);

internal sealed record PendingPlanRevisionProposal(
    [property: JsonPropertyName("proposalId")] string ProposalId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("payload")] PlanRevisionProposalPayload Payload);

internal static class PlanRevisionProposalParser
{
    internal const string Marker = "PLAN_REVISION_JSON:";

    internal static bool TryParse(
        string? text,
        out PlanRevisionProposalPayload? proposal,
        out string? error)
    {
        proposal = null;
        error = null;
        if (text?.Contains(Marker, StringComparison.Ordinal) != true)
            return false;
        if (CountOccurrences(text, Marker) != 1)
        {
            error = "Return exactly one PLAN_REVISION_JSON object.";
            return false;
        }

        if (!StructuredJsonBlockParser.TryExtractObject<PlanRevisionProposalPayload>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = "The PLAN_REVISION_JSON object was missing or was not valid JSON.";
            return false;
        }

        var payload = extraction.Payload;
        var reopenIds = payload.ReopenTaskIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(payload.PlanId) ||
            string.IsNullOrWhiteSpace(payload.BaseRevision) ||
            string.IsNullOrWhiteSpace(payload.Summary) ||
            payload.RevisedPlan is null)
        {
            error = "PlanId, baseRevision, summary, and revisedPlan are required.";
            return false;
        }
        if (!string.Equals(payload.PlanId, payload.RevisedPlan.GroupId, StringComparison.Ordinal))
        {
            error = "planId must match revisedPlan.groupId.";
            return false;
        }

        proposal = payload with
        {
            PlanId = payload.PlanId.Trim(),
            BaseRevision = payload.BaseRevision.Trim(),
            Summary = payload.Summary.Trim(),
            ReopenTaskIds = reopenIds,
        };
        return true;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    internal static string BuildRepairPrompt(string validationError, PlanRevisionProposalPayload? proposal = null)
    {
        var intent = proposal is null
            ? "Preserve the user's requested plan change."
            : $"Preserve this intended revision: {proposal.Summary}";
        return $$"""
            Your PLAN_REVISION_JSON response did not satisfy SquadDash's required schema.

            Validation errors:
            {{validationError}}

            {{intent}}
            Return exactly one corrected PLAN_REVISION_JSON block. Do not add commentary and do not make source changes.
            Read `.squad/instructions/decompose-planning.md` and use its complete TASKS_JSON task schema for `revisedPlan`.
            """;
    }
}

internal sealed class PendingPlanRevisionProposalStore(string squadFolderPath)
{
    private readonly string _folder = Path.Combine(squadFolderPath, "plans", "pending-revisions");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal PendingPlanRevisionProposal Save(PlanRevisionProposalPayload payload)
    {
        Directory.CreateDirectory(_folder);
        var proposal = new PendingPlanRevisionProposal(
            "plan-revision-" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            payload);
        JsonFileStorage.AtomicWrite(GetPath(payload.PlanId), proposal, Options);
        return proposal;
    }

    internal PendingPlanRevisionProposal? Load(string planId)
    {
        var path = GetPath(planId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<PendingPlanRevisionProposal>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal IReadOnlyList<PendingPlanRevisionProposal> LoadAll()
    {
        if (!Directory.Exists(_folder)) return [];
        var proposals = new List<PendingPlanRevisionProposal>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var proposal = JsonSerializer.Deserialize<PendingPlanRevisionProposal>(
                    File.ReadAllText(path), Options);
                if (proposal is not null) proposals.Add(proposal);
            }
            catch (JsonException) { }
        }
        return proposals.OrderBy(proposal => proposal.CreatedAt).ToArray();
    }

    internal void Delete(string planId)
    {
        var path = GetPath(planId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string GetPath(string planId) => Path.Combine(_folder, Sanitize(planId) + ".json");

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}

internal static class PlanRevisionPromptInjection
{
    internal static string Build(string squadFolderPath, bool includeExecutingPlan)
    {
        var eligible = new PlanStore(squadFolderPath).LoadAll()
            .Where(plan => IsEligible(plan, includeExecutingPlan))
            .OrderBy(plan => plan.Timestamps.CreatedAt)
            .ToArray();
        if (eligible.Length == 0) return string.Empty;

        var lines = eligible.Select(plan =>
        {
            var path = $".squad/plans/{plan.PlanId}.json";
            var reopenable = plan.Tasks
                .Where(task => task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial or
                    PlanTaskStatus.Failed or PlanTaskStatus.HumanReviewRequired)
                .Select(task => task.TaskId)
                .ToArray();
            return $"- planId={plan.PlanId}; baseRevision={plan.Revision}; status={plan.LifecycleStatus}; " +
                   $"definition={path}; reopenableTaskIds=[{string.Join(", ", reopenable)}]";
        });

        return $$"""
            ## Optional revision of an unfinished plan

            The plan executor is currently at a durable boundary. If, and only if, the user's request asks to change one of the unfinished plans below, do not edit source files in this turn. Read the exact durable plan file and `.squad/instructions/decompose-planning.md`, then propose the complete revised definition with exactly one `PLAN_REVISION_JSON:` object.

            {{string.Join("\n", lines)}}

            Schema:
            ```json
            {
              "planId": "exact eligible plan ID",
              "baseRevision": "exact current revision",
              "summary": "short user-facing description of the proposed change",
              "reopenTaskIds": ["completed or failed task IDs whose accepted specification must change"],
              "revisedPlan": { "the complete TASKS_JSON group object using the existing groupId": true }
            }
            ```

            `reopenTaskIds` may be omitted when only pending, unstarted work changes. Preserve completed task definitions unless their IDs are explicitly listed for reopening. Update downstream pending tasks and validations when the new contract affects them. The response creates a proposal only: a human must approve it before SquadDash changes the durable plan or resumes execution. If several plans are listed and the user did not identify one unambiguously, ask one concise question and do not emit PLAN_REVISION_JSON.
            """;
    }

    private static bool IsEligible(Plan plan, bool includeExecutingPlan) =>
        plan.Tasks.Any(task => task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded)) &&
        (plan.LifecycleStatus is PlanLifecycleStatus.AwaitingApproval or
             PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked ||
         includeExecutingPlan && plan.LifecycleStatus == PlanLifecycleStatus.Executing);
}

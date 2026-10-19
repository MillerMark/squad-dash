using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanTaskVerificationVerdict
{
    internal const string Accepted = "accepted";
    internal const string ReworkRequired = "rework-required";
    internal const string HumanReviewRequired = "human-review-required";
}

internal static class PlanTaskHumanReviewRecommendedAction
{
    internal const string AcceptStep = "accept-step";
    internal const string ContinueWorking = "continue-working";
    internal const string RevisePlan = "revise-plan";
    internal const string RetryVerification = "retry-verification";

    internal static bool IsValid(string? value) => value is
        AcceptStep or ContinueWorking or RevisePlan or RetryVerification;
}

/// <summary>
/// AI-authored operator guidance for a human-review verdict. The host validates the action token
/// and owns the actual transition; it does not synthesize explanatory prose from technical fields.
/// </summary>
internal sealed record PlanTaskHumanReviewBrief(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("situation")] string Situation,
    [property: JsonPropertyName("impact")] string Impact,
    [property: JsonPropertyName("recommendedAction")] string RecommendedAction,
    [property: JsonPropertyName("recommendation")] string Recommendation,
    [property: JsonPropertyName("alternativeGuidance")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AlternativeGuidance = null);

internal enum PlanTaskVerificationNextAction
{
    Accept,
    AutomaticRework,
    HumanReview,
}

internal static class PlanTaskVerificationRecoveryPolicy
{
    internal static PlanTaskVerificationNextAction Resolve(string verdict, int completedAutomaticReworks) =>
        verdict switch
        {
            PlanTaskVerificationVerdict.Accepted => PlanTaskVerificationNextAction.Accept,
            PlanTaskVerificationVerdict.ReworkRequired when completedAutomaticReworks < 1 =>
                PlanTaskVerificationNextAction.AutomaticRework,
            _ => PlanTaskVerificationNextAction.HumanReview,
        };
}

internal sealed record PlanTaskVerificationResult(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("evaluatedCommit")] string EvaluatedCommit,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("claimFindings")] IReadOnlyList<PlanTaskVerificationFinding> ClaimFindings,
    [property: JsonPropertyName("missingOrOverstatedWork")]
    [property: JsonConverter(typeof(VerificationNarrativeListJsonConverter))]
    IReadOnlyList<string> MissingOrOverstatedWork,
    [property: JsonPropertyName("testAssessment")] string TestAssessment,
    [property: JsonPropertyName("reworkInstructions")]
    [property: JsonConverter(typeof(VerificationNarrativeListJsonConverter))]
    IReadOnlyList<string> ReworkInstructions,
    [property: JsonPropertyName("humanReview")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PlanTaskHumanReviewBrief? HumanReview = null);

internal sealed class VerificationNarrativeListJsonConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected an array of strings or structured finding objects.");

        var items = new List<string>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var normalized = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Object => FormatObject(element),
                JsonValueKind.Null => null,
                _ => throw new JsonException(
                    "Expected each array item to be a string or structured finding object."),
            };

            if (!string.IsNullOrWhiteSpace(normalized))
                items.Add(normalized.Trim());
        }

        return items;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value ?? [])
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    private static string FormatObject(JsonElement element)
    {
        string? priority = null;
        string? subject = null;
        var details = new List<string>();

        foreach (var property in element.EnumerateObject())
        {
            var value = FormatValue(property.Value);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (string.Equals(property.Name, "priority", StringComparison.OrdinalIgnoreCase))
            {
                priority = value;
                continue;
            }

            if (subject is null && IsSubjectProperty(property.Name))
            {
                subject = value;
                continue;
            }

            details.Add($"{ToDisplayLabel(property.Name)}: {value}");
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(priority))
            builder.Append('[').Append(priority!.Trim().ToLowerInvariant()).Append("] ");
        if (!string.IsNullOrWhiteSpace(subject))
            builder.Append(subject!.Trim());
        if (details.Count > 0)
        {
            if (builder.Length > 0)
                builder.Append(": ");
            builder.AppendJoin("; ", details);
        }

        return builder.Length > 0 ? builder.ToString() : element.GetRawText();
    }

    private static string? FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
            .Select(FormatValue)
            .Where(item => !string.IsNullOrWhiteSpace(item))),
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };

    private static string ToDisplayLabel(string propertyName) => propertyName switch
    {
        "issue" => "Issue",
        "details" => "Details",
        "observation" => "Observation",
        "evidence" => "Evidence",
        "reason" => "Reason",
        "locations" => "Locations",
        _ => propertyName,
    };

    private static bool IsSubjectProperty(string propertyName) =>
        string.Equals(propertyName, "task", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "requirement", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "claim", StringComparison.OrdinalIgnoreCase);
}

internal static class PlanTaskVerificationResultParser
{
    internal const string Marker = "PLAN_TASK_VERIFICATION_JSON:";
    internal const string LegacyMarker = "PLAN_TASK_SCRUTINY_JSON:";
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    internal static bool TryParse(string? text, out PlanTaskVerificationResult? result, out string? error)
    {
        result = null;
        error = null;
        var extracted = StructuredJsonBlockParser.TryExtractObject<JsonElement>(
            text, Marker, out var extraction) ||
            StructuredJsonBlockParser.TryExtractObject<JsonElement>(
                text, LegacyMarker, out extraction) ||
            StructuredJsonBlockParser.TryExtractSingleObject<JsonElement>(text, out extraction);
        if (!extracted || extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        PlanTaskVerificationResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlanTaskVerificationResult>(
                extraction.JsonText,
                ParseOptions);
        }
        catch (JsonException exception)
        {
            var path = string.IsNullOrWhiteSpace(exception.Path)
                ? string.Empty
                : $" at {exception.Path}";
            error = $"The {Marker} payload could not be deserialized{path}: {exception.Message}";
            return false;
        }

        if (parsed is null)
        {
            error = $"The {Marker} payload deserialized to null.";
            return false;
        }

        // Normalize representation-only model variations at the protocol boundary. The verdict
        // consistency checks below still reject missing decision-critical evidence.
        parsed = parsed with
        {
            Verdict = parsed.Verdict?.Trim().ToLowerInvariant() ?? string.Empty,
            ClaimFindings = (parsed.ClaimFindings ?? [])
                .Where(finding => finding is not null)
                .Select(finding => finding with
                {
                    Disposition = NormalizeDisposition(finding.Disposition),
                })
                .ToArray(),
            MissingOrOverstatedWork = (parsed.MissingOrOverstatedWork ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
            ReworkInstructions = (parsed.ReworkInstructions ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
            HumanReview = parsed.HumanReview is null
                ? null
                : parsed.HumanReview with
                {
                    Headline = parsed.HumanReview.Headline?.Trim() ?? string.Empty,
                    Situation = parsed.HumanReview.Situation?.Trim() ?? string.Empty,
                    Impact = parsed.HumanReview.Impact?.Trim() ?? string.Empty,
                    RecommendedAction = parsed.HumanReview.RecommendedAction?.Trim().ToLowerInvariant() ?? string.Empty,
                    Recommendation = parsed.HumanReview.Recommendation?.Trim() ?? string.Empty,
                    AlternativeGuidance = string.IsNullOrWhiteSpace(parsed.HumanReview.AlternativeGuidance)
                        ? null
                        : parsed.HumanReview.AlternativeGuidance.Trim(),
                },
        };

        if (string.IsNullOrWhiteSpace(parsed.PlanId) || string.IsNullOrWhiteSpace(parsed.TaskId) ||
            string.IsNullOrWhiteSpace(parsed.Revision) || string.IsNullOrWhiteSpace(parsed.EvaluatedCommit) ||
            string.IsNullOrWhiteSpace(parsed.Summary) || string.IsNullOrWhiteSpace(parsed.TestAssessment))
        {
            error = "The verification result omitted its scope, evaluated commit, summary, or test assessment.";
            return false;
        }

        if (parsed.Verdict is not (PlanTaskVerificationVerdict.Accepted or
                                   PlanTaskVerificationVerdict.ReworkRequired or
                                   PlanTaskVerificationVerdict.HumanReviewRequired))
        {
            error = "The verification verdict must be accepted, rework-required, or human-review-required.";
            return false;
        }

        // A contradictory verdict is still a complete, useful verification result. Preserve all
        // findings and normalize toward the safer decision instead of spending another model call
        // asking for representation-only repair. Bounded instructions permit one automatic
        // rework; otherwise the human decides from the evidence already collected.
        var hasDefiniteFinding = parsed.ClaimFindings.Any(finding =>
            finding.Disposition is "missing" or "overstated") ||
            parsed.MissingOrOverstatedWork.Count > 0;
        var hasUnclearFinding = parsed.ClaimFindings.Any(finding =>
            finding.Disposition == "unclear");
        if (parsed.Verdict == PlanTaskVerificationVerdict.Accepted &&
            (hasDefiniteFinding || hasUnclearFinding))
        {
            parsed = parsed with
            {
                Verdict = hasDefiniteFinding && !hasUnclearFinding && parsed.ReworkInstructions.Count > 0
                    ? PlanTaskVerificationVerdict.ReworkRequired
                    : PlanTaskVerificationVerdict.HumanReviewRequired,
            };
        }

        if (parsed.Verdict == PlanTaskVerificationVerdict.ReworkRequired &&
            (parsed.ReworkInstructions.Count == 0 || hasUnclearFinding || !hasDefiniteFinding))
        {
            parsed = parsed with { Verdict = PlanTaskVerificationVerdict.HumanReviewRequired };
        }

        if (parsed.Verdict == PlanTaskVerificationVerdict.HumanReviewRequired &&
            (parsed.HumanReview is null ||
             string.IsNullOrWhiteSpace(parsed.HumanReview.Headline) ||
             string.IsNullOrWhiteSpace(parsed.HumanReview.Situation) ||
             string.IsNullOrWhiteSpace(parsed.HumanReview.Impact) ||
             string.IsNullOrWhiteSpace(parsed.HumanReview.Recommendation) ||
             !PlanTaskHumanReviewRecommendedAction.IsValid(parsed.HumanReview.RecommendedAction)))
        {
            error = "A human-review-required result must include a complete humanReview brief with a valid recommendedAction.";
            return false;
        }

        result = parsed;
        return true;
    }

    private static string NormalizeDisposition(string? disposition)
    {
        var normalized = disposition?.Trim().ToLowerInvariant();
        return normalized is "supported" or "missing" or "overstated" or "unclear"
            ? normalized
            : "unclear";
    }
}

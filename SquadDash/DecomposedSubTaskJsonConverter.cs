using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>
/// Writes the canonical taskId shape while retaining lossless compatibility with the historical
/// id property used by TASKS_JSON and plan-revision payloads.
/// </summary>
internal sealed class DecomposedSubTaskJsonConverter : JsonConverter<DecomposedSubTask>
{
    public override DecomposedSubTask Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<WireTask>(ref reader, options)
            ?? throw new JsonException("task must be a JSON object");

        var canonicalId = Normalize(wire.TaskId);
        var legacyId = Normalize(wire.LegacyId);
        if (canonicalId is not null && legacyId is not null &&
            !string.Equals(canonicalId, legacyId, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"task contains conflicting taskId '{canonicalId}' and legacy id '{legacyId}'; " +
                "keep taskId and make the values agree");
        }

        return new DecomposedSubTask(
            canonicalId ?? legacyId!,
            wire.Description!,
            wire.DependsOn!,
            wire.Priority!,
            wire.Title,
            wire.ParentTaskId,
            wire.AgentAssignments,
            wire.ParallelEligible,
            wire.AgentRoutingMode,
            wire.GenericAgentReason,
            wire.Outputs,
            wire.Inputs,
            wire.ProofRequirements,
            wire.AmendmentGateId,
            wire.ExecutionMode,
            wire.VerificationLevel,
            wire.ContractExamples);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DecomposedSubTask value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new WireTask(
            value.Id,
            null,
            value.Description,
            value.DependsOn,
            value.Priority,
            value.Title,
            value.ParentTaskId,
            value.AgentAssignments,
            value.ParallelEligible,
            value.AgentRoutingMode,
            value.GenericAgentReason,
            value.Outputs,
            value.Inputs,
            value.ProofRequirements,
            value.AmendmentGateId,
            value.ExecutionMode,
            value.VerificationLevel,
            value.ContractExamples), options);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record WireTask(
        [property: JsonPropertyName("taskId")] string? TaskId = null,
        [property: JsonPropertyName("id")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LegacyId = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("dependsOn")] IReadOnlyList<string>? DependsOn = null,
        [property: JsonPropertyName("priority")] string? Priority = null,
        [property: JsonPropertyName("title")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Title = null,
        [property: JsonPropertyName("parentTaskId")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ParentTaskId = null,
        [property: JsonPropertyName("agentAssignments")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DecomposedAgentAssignment>? AgentAssignments = null,
        [property: JsonPropertyName("parallelEligible")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        bool? ParallelEligible = null,
        [property: JsonPropertyName("agentRoutingMode")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? AgentRoutingMode = null,
        [property: JsonPropertyName("genericAgentReason")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? GenericAgentReason = null,
        [property: JsonPropertyName("outputs")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DecomposedTaskOutput>? Outputs = null,
        [property: JsonPropertyName("inputs")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Inputs = null,
        [property: JsonPropertyName("proofRequirements")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DecomposedTaskProofRequirement>? ProofRequirements = null,
        [property: JsonPropertyName("amendmentGateId")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? AmendmentGateId = null,
        [property: JsonPropertyName("executionMode")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ExecutionMode = null,
        [property: JsonPropertyName("verificationLevel")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? VerificationLevel = null,
        [property: JsonPropertyName("contractExamples")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DecomposedTaskContractExample>? ContractExamples = null);
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record DecomposedGate(
    [property: JsonPropertyName("gateId")]        string GateId,
    [property: JsonPropertyName("message")]       string Message,
    [property: JsonPropertyName("afterTaskIds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  IReadOnlyList<string>? AfterTaskIds  = null,
    [property: JsonPropertyName("beforeTaskIds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  IReadOnlyList<string>? BeforeTaskIds = null,
    [property: JsonPropertyName("proofRequirements")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                  IReadOnlyList<DecomposedTaskProofRequirement>? ProofRequirements = null);

/// <summary>
/// A first-class, non-mutating validation node in a decomposed plan. It becomes eligible after
/// its prerequisite tasks complete and blocks its downstream frontier until its assertions pass.
/// </summary>
internal sealed record DecomposedValidationNode(
    [property: JsonPropertyName("validationId")] string ValidationId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("afterTaskIds")] IReadOnlyList<string> AfterTaskIds,
    [property: JsonPropertyName("beforeTaskIds")] IReadOnlyList<string> BeforeTaskIds,
    [property: JsonPropertyName("assertions")] IReadOnlyList<string> Assertions,
    [property: JsonPropertyName("outputIds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<string>? OutputIds = null,
    [property: JsonPropertyName("mode")] string Mode = "evidence",
    [property: JsonPropertyName("commands")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                IReadOnlyList<string>? Commands = null,
    [property: JsonPropertyName("revalidateAtCompletion")]
                                                bool RevalidateAtCompletion = true);

internal sealed record DecomposedTaskOutput(
    [property: JsonPropertyName("outputId")] string OutputId,
    [property: JsonPropertyName("description")] string Description);

/// <summary>
/// Declarative evidence contract for a plan task. The host matches returned evidence by stable
/// identifier and proof type; it never infers a live demonstration from filenames or test names.
/// </summary>
internal sealed record DecomposedTaskProofRequirement(
    [property: JsonPropertyName("requirementId")] string RequirementId,
    [property: JsonPropertyName("proofType")] string ProofType,
    [property: JsonPropertyName("description")] string Description);

internal sealed record DecomposedTaskGroup(
    [property: JsonPropertyName("groupId")]    string GroupId,
    [property: JsonPropertyName("groupTitle")] string GroupTitle,
    [property: JsonPropertyName("branch")]     string Branch,
    [property: JsonPropertyName("summary")]    string Summary,
    [property: JsonPropertyName("tasks")]      IReadOnlyList<DecomposedSubTask> Tasks,
    [property: JsonPropertyName("delivery")]   string? Delivery = null,
    [property: JsonPropertyName("approvalGates")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                               IReadOnlyList<DecomposedGate>? ApprovalGates = null,
    [property: JsonPropertyName("validations")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                               IReadOnlyList<DecomposedValidationNode>? Validations = null,
    [property: JsonIgnore]                     string? HostRevision = null);

internal sealed record DecomposedAgentAssignment(
    [property: JsonPropertyName("agentHandle")] string AgentHandle,
    [property: JsonPropertyName("role")]        string Role,
    [property: JsonPropertyName("allowGenericChildren")]
                                                  bool AllowGenericChildren = true);

internal sealed record DecomposedSubTask(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dependsOn")]   IReadOnlyList<string> DependsOn,
    [property: JsonPropertyName("priority")]    string Priority,
    [property: JsonPropertyName("title")]       string? Title = null,
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
                                                string? AmendmentGateId = null);

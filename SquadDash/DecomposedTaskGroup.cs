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
                                                  IReadOnlyList<string>? BeforeTaskIds = null);

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
    [property: JsonIgnore]                     string? HostRevision = null);

internal sealed record DecomposedSubTask(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dependsOn")]   IReadOnlyList<string> DependsOn,
    [property: JsonPropertyName("priority")]    string Priority,
    [property: JsonPropertyName("title")]       string? Title = null,
    [property: JsonPropertyName("parentTaskId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? ParentTaskId = null);

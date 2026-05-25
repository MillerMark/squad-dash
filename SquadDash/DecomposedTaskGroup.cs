using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record DecomposedTaskGroup(
    [property: JsonPropertyName("groupId")]    string GroupId,
    [property: JsonPropertyName("groupTitle")] string GroupTitle,
    [property: JsonPropertyName("branch")]     string Branch,
    [property: JsonPropertyName("summary")]    string Summary,
    [property: JsonPropertyName("tasks")]      IReadOnlyList<DecomposedSubTask> Tasks);

internal sealed record DecomposedSubTask(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dependsOn")]   IReadOnlyList<string> DependsOn,
    [property: JsonPropertyName("priority")]    string Priority);

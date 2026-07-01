using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash.GuidedTours;

internal sealed class GuidedTourStep
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("markdownText")]
    public string MarkdownText { get; set; } = string.Empty;

    /// <summary>The x:Name of the WPF element this step points at.</summary>
    [JsonPropertyName("targetControlId")]
    public string TargetControlId { get; set; } = string.Empty;

    /// <summary>Callout placement: "North", "South", "East", "West", or "Auto".</summary>
    [JsonPropertyName("calloutPlacement")]
    public string CalloutPlacement { get; set; } = "Auto";

    /// <summary>Raw preAction string, e.g. "None", "SaveLayout", "LoadLayout:myLayout".</summary>
    [JsonPropertyName("preAction")]
    public string PreAction { get; set; } = "None";

    /// <summary>Name of the registered command to run before this step is shown, or empty.</summary>
    [JsonPropertyName("commandBefore")]
    public string CommandBefore { get; set; } = string.Empty;

    /// <summary>Name of the registered command to run after this step is left (navigate away or tour stop).</summary>
    [JsonPropertyName("commandAfter")]
    public string CommandAfter { get; set; } = string.Empty;

    /// <summary>Names of registered commands to run before this step is shown (multi-command form).</summary>
    [JsonPropertyName("commandsBefore")]
    public List<string>? CommandsBefore { get; set; }

    /// <summary>Names of registered commands to run after this step is left (multi-command form).</summary>
    [JsonPropertyName("commandsAfter")]
    public List<string>? CommandsAfter { get; set; }

    /// <summary>
    /// Effective list of commands to run before this step.
    /// Prefers <see cref="CommandsBefore"/> when non-empty; falls back to <see cref="CommandBefore"/>.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveCommandsBefore =>
        CommandsBefore is { Count: > 0 }
            ? CommandsBefore
            : string.IsNullOrWhiteSpace(CommandBefore)
                ? []
                : [CommandBefore];

    /// <summary>
    /// Effective list of commands to run after this step.
    /// Prefers <see cref="CommandsAfter"/> when non-empty; falls back to <see cref="CommandAfter"/>.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveCommandsAfter =>
        CommandsAfter is { Count: > 0 }
            ? CommandsAfter
            : string.IsNullOrWhiteSpace(CommandAfter)
                ? []
                : [CommandAfter];

    /// <summary>
    /// Optional trigger spec that auto-advances to the next step when the named event fires.
    /// Format: "TriggerType:Argument" (e.g. "MenuOpened:HelpMenuItem") or empty for manual-only.
    /// </summary>
    [JsonPropertyName("advanceTrigger")]
    public string AdvanceTrigger { get; set; } = string.Empty;

    /// <summary>
    /// Horizontal offset of the callout arrow attachment point within the target control,
    /// in 0.0–1.0 space where 0.5 = center (no shift).
    /// </summary>
    [JsonPropertyName("targetOffsetX")]
    public double TargetOffsetX { get; set; } = 0.5;

    /// <summary>
    /// Vertical offset of the callout arrow attachment point within the target control,
    /// in 0.0–1.0 space where 0.5 = center (no shift).
    /// </summary>
    [JsonPropertyName("targetOffsetY")]
    public double TargetOffsetY { get; set; } = 0.5;

    [JsonIgnore]
    public GuidedTourPreActionDescriptor ParsedPreAction =>
        GuidedTourPreActionDescriptor.Parse(PreAction);

    [JsonIgnore]
    public global::SquadDash.CalloutPlacement ParsedCalloutPlacement =>
        Enum.TryParse<global::SquadDash.CalloutPlacement>(CalloutPlacement, ignoreCase: true, out var r)
            ? r
            : global::SquadDash.CalloutPlacement.Auto;
}

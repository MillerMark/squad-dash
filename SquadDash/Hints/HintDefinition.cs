namespace SquadDash.Hints;

public enum HintTrigger { Idle, Action }

public class HintDefinition {
    public string      HintId          { get; set; } = "";
    public string      MarkdownText    { get; set; } = "";
    public string      TargetControlId { get; set; } = "";
    public int         Priority        { get; set; } = 100;
    public int         MaxShowCount    { get; set; } = 3;
    public HintTrigger Trigger         { get; set; } = HintTrigger.Idle;
    public string?     ActionId        { get; set; }
    public string?     ConditionId     { get; set; }
}

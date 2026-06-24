namespace SquadDash.Hints;

public class HintRecord {
    public string    HintId    { get; set; } = "";
    public DateTime  LastShown { get; set; }
    public DateTime? LastSeen  { get; set; }
    public int       SeenCount { get; set; }
}

namespace SquadDash;

using System;

internal sealed class MaintenancePanelViewModel {
    public MaintenanceMdConfig?   Config           { get; set; }
    public MaintenanceStateStore? StateStore       { get; set; }
    public bool                   RunnerActive     { get; set; }
    public string?                RunningTaskTitle { get; set; }
    public string                 FilterText       { get; set; } = string.Empty;
    public DateTimeOffset         NextMaintenanceAt { get; set; } = DateTimeOffset.MaxValue;
}

using System.Collections.Generic;

namespace SquadDash;

/// <summary>Parsed global configuration from a maintenance.md file.</summary>
internal sealed record MaintenanceMdConfig(
    bool                           Configured         = false,
    bool                           EnabledOnIdle      = false,
    double                         IdleTimeout        = 15,
    int                            MaxTasksPerSession = 5,
    string                         Safety             = "branch",
    IReadOnlyList<MaintenanceTask>? Tasks             = null);

/// <summary>A single task entry parsed from the tasks block in maintenance.md.</summary>
internal sealed record MaintenanceTask(
    string                   Id,
    bool                     Enabled,
    string                   Frequency,
    string                   Safety,
    string                   Title,
    string                   Instructions,
    IReadOnlyList<MaintenanceOption>? Options = null,
    string                   SourceFilePath = "");

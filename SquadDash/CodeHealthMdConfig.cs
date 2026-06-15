using System.Collections.Generic;

namespace SquadDash;

/// <summary>Parsed global configuration from a code-health.md file.</summary>
internal sealed record CodeHealthMdConfig(
    bool                           Configured         = false,
    bool                           EnabledOnIdle      = false,
    double                         IdleTimeout        = 15,
    int                            MaxTasksPerSession = 5,
    string                         Safety             = "branch",
    IReadOnlyList<CodeHealthTask>? Tasks             = null);

/// <summary>A single task entry parsed from the tasks block in code-health.md.</summary>
internal sealed record CodeHealthTask(
    string                   Id,
    bool                     Enabled,
    string                   Frequency,
    string                   Safety,
    string                   Title,
    string                   Instructions,
    IReadOnlyList<CodeHealthOption>? Options = null,
    string                   SourceFilePath = "");


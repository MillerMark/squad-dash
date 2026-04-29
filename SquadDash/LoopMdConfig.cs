using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Parsed configuration from a loop.md frontmatter block.
/// </summary>
internal sealed record LoopMdConfig(
    double IntervalMinutes,
    double TimeoutMinutes,
    string Description,
    string Instructions,
    IReadOnlyList<string>? Commands = null);

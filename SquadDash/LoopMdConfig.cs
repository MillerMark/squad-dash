using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// A single typed option defined in the <c>options:</c> block of a loop file's frontmatter.
/// </summary>
internal sealed record LoopOption(
    string Key,
    string RawValue,
    string Type,
    string? Label,
    string? Hint,
    IReadOnlyList<string>? Choices);

/// <summary>
/// Parsed configuration from a loop.md frontmatter block.
/// </summary>
internal sealed record LoopMdConfig(
    double IntervalMinutes,
    double TimeoutMinutes,
    string Description,
    string Instructions,
    IReadOnlyList<string>? Commands = null,
    IReadOnlyList<LoopOption>? Options = null);

using System.Collections.Generic;

namespace SquadDash;

internal sealed record CliModelMetadata(
    string ModelId,
    string? CliCategory,
    decimal? InputPricePerMillionTokens,
    decimal? OutputPricePerMillionTokens,
    string? ReleaseStatus,
    IReadOnlyList<string> SupportedReasoningEfforts);

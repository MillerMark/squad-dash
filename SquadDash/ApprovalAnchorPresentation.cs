using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Immutable record representing which approval control is the primary visual anchor
/// (full opacity) and which are equivalent (half-opacity).
/// </summary>
internal sealed record ApprovalAnchorPresentation(
    /// <summary>The gate ID of the primary controller (rendered at full opacity).</summary>
    string PrimaryGateId,
    /// <summary>The inferred anchor string for the primary controller.</summary>
    string PrimaryAnchor,
    /// <summary>Gate IDs of equivalent controls (rendered at half-opacity).</summary>
    IReadOnlyList<string> EquivalentGateIds,
    /// <summary>The "Human approval requirements" sentence text derived from the primary anchor.</summary>
    string RequirementsSentence,
    /// <summary>Summary items — one per logical gate.</summary>
    IReadOnlyList<ApprovalAnchorSummaryItem> SummaryItems);

/// <summary>One summary item per logical gate in the plan.</summary>
internal sealed record ApprovalAnchorSummaryItem(
    string GateId,
    string Anchor,
    string Description);

/// <summary>
/// Font sizing metadata for approval anchor presentation in the Plan Viewer.
/// Separates environmental DPI/font concerns from logic.
/// </summary>
internal sealed record ApprovalAnchorFontMetrics(
    double BaseFontSize,
    double FontSizeFactor,
    double EffectiveFontSize);

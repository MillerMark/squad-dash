using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal enum ModelProfileResolutionReason {
    Override,
    CategoryAssignment,
    Default,
    Unavailable
}

internal sealed record ModelProfileResolutionResult(
    ModelProfile? Profile,
    ModelProfileResolutionReason Reason,
    string? ExplicitOverrideProfileId,
    ModelProfile? ExplicitOverrideProfile);

/// <summary>
/// Pure resolver that determines the effective <see cref="ModelProfile"/> for an agent context
/// using deterministic precedence: per-agent override → category assignment → default profile.
/// </summary>
internal static class ModelProfileResolver {

    /// <summary>
    /// Resolves the effective profile using three-level precedence:
    /// 1. Explicit per-agent override (by profile ID)
    /// 2. Agent-category assignment (category → profileId from assignments map)
    /// 3. Default profile (IsDefault flag, or first profile if none marked default)
    /// </summary>
    internal static ModelProfile? Resolve(
        IReadOnlyList<ModelProfile>? profiles,
        IReadOnlyDictionary<string, string>? categoryAssignments,
        string? perAgentOverrideProfileId,
        string? agentCategory) {

        return ResolveWithReason(profiles, categoryAssignments, perAgentOverrideProfileId, agentCategory).Profile;
    }

    internal static ModelProfile? Resolve(
        IReadOnlyList<ModelProfile>? profiles,
        IReadOnlyDictionary<string, string>? categoryAssignments,
        string? agentHandle,
        string? agentCategory,
        IReadOnlyDictionary<string, string>? agentOverrides) {

        return ResolveWithReason(profiles, categoryAssignments, agentHandle, agentCategory, agentOverrides).Profile;
    }

    internal static ModelProfileResolutionResult ResolveWithReason(
        IReadOnlyList<ModelProfile>? profiles,
        IReadOnlyDictionary<string, string>? categoryAssignments,
        string? perAgentOverrideProfileId,
        string? agentCategory) {

        if (profiles is null || profiles.Count == 0)
            return new ModelProfileResolutionResult(null, ModelProfileResolutionReason.Unavailable, null, null);

        // 1. Per-agent override
        ModelProfile? overrideProfile = null;
        if (!string.IsNullOrEmpty(perAgentOverrideProfileId)) {
            overrideProfile = FindById(profiles, perAgentOverrideProfileId);
            if (overrideProfile is not null && overrideProfile.IsEnabled)
                return new ModelProfileResolutionResult(overrideProfile, ModelProfileResolutionReason.Override, perAgentOverrideProfileId, overrideProfile);
        }

        // 2. Category assignment
        if (!string.IsNullOrEmpty(agentCategory) && categoryAssignments is not null) {
            foreach (var kvp in categoryAssignments) {
                if (string.Equals(kvp.Key, agentCategory, StringComparison.OrdinalIgnoreCase)) {
                    var categoryProfile = FindById(profiles, kvp.Value);
                    if (categoryProfile is not null && categoryProfile.IsEnabled)
                        return new ModelProfileResolutionResult(categoryProfile, ModelProfileResolutionReason.CategoryAssignment, perAgentOverrideProfileId, overrideProfile);
                    break;
                }
            }
        }

        // 3. Default profile (prefer enabled default, then any enabled profile)
        var fallback = profiles.FirstOrDefault(p => p.IsDefault && p.IsEnabled)
            ?? profiles.FirstOrDefault(p => p.IsEnabled);
        return new ModelProfileResolutionResult(fallback ?? profiles[0], ModelProfileResolutionReason.Default, perAgentOverrideProfileId, overrideProfile);
    }

    internal static ModelProfileResolutionResult ResolveWithReason(
        IReadOnlyList<ModelProfile>? profiles,
        IReadOnlyDictionary<string, string>? categoryAssignments,
        string? agentHandle,
        string? agentCategory,
        IReadOnlyDictionary<string, string>? agentOverrides) {

        if (profiles is null || profiles.Count == 0)
            return new ModelProfileResolutionResult(null, ModelProfileResolutionReason.Unavailable, null, null);

        string? overrideProfileId = null;
        ModelProfile? overrideProfile = null;
        if (!string.IsNullOrWhiteSpace(agentHandle) && agentOverrides is not null) {
            foreach (var kvp in agentOverrides) {
                if (string.Equals(kvp.Key, agentHandle, StringComparison.OrdinalIgnoreCase)) {
                    overrideProfileId = kvp.Value;
                    overrideProfile = FindById(profiles, kvp.Value);
                    if (overrideProfile is not null && overrideProfile.IsEnabled)
                        return new ModelProfileResolutionResult(overrideProfile, ModelProfileResolutionReason.Override, overrideProfileId, overrideProfile);
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(agentCategory) && categoryAssignments is not null) {
            foreach (var kvp in categoryAssignments) {
                if (string.Equals(kvp.Key, agentCategory, StringComparison.OrdinalIgnoreCase)) {
                    var categoryProfile = FindById(profiles, kvp.Value);
                    if (categoryProfile is not null && categoryProfile.IsEnabled)
                        return new ModelProfileResolutionResult(categoryProfile, ModelProfileResolutionReason.CategoryAssignment, overrideProfileId, overrideProfile);
                    break;
                }
            }
        }

        // 3. Default profile (prefer enabled default, then any enabled profile)
        var fallback = profiles.FirstOrDefault(p => p.IsDefault && p.IsEnabled)
            ?? profiles.FirstOrDefault(p => p.IsEnabled);
        return new ModelProfileResolutionResult(fallback ?? profiles[0], ModelProfileResolutionReason.Default, overrideProfileId, overrideProfile);
    }

    private static ModelProfile? FindById(IReadOnlyList<ModelProfile> profiles, string id) {
        foreach (var p in profiles) {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
}

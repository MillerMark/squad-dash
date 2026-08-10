using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

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

        if (profiles is null || profiles.Count == 0)
            return null;

        // 1. Per-agent override
        if (!string.IsNullOrEmpty(perAgentOverrideProfileId)) {
            var overrideProfile = FindById(profiles, perAgentOverrideProfileId);
            if (overrideProfile is not null)
                return overrideProfile;
        }

        // 2. Category assignment
        if (!string.IsNullOrEmpty(agentCategory) && categoryAssignments is not null) {
            foreach (var kvp in categoryAssignments) {
                if (string.Equals(kvp.Key, agentCategory, StringComparison.OrdinalIgnoreCase)) {
                    var categoryProfile = FindById(profiles, kvp.Value);
                    if (categoryProfile is not null)
                        return categoryProfile;
                    break;
                }
            }
        }

        // 3. Default profile
        return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
    }

    private static ModelProfile? FindById(IReadOnlyList<ModelProfile> profiles, string id) {
        foreach (var p in profiles) {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
}

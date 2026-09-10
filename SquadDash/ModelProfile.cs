using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// A named model configuration that maps to a specific LLM provider and model.
/// Multiple profiles can coexist so different agent categories use different models.
/// ReasoningEffort supports "auto" (treated as unset), "low", "medium", "high", and "max".
/// </summary>
internal sealed record ModelProfile(
    string Id,
    string Alias,
    string ProviderType,
    string? ProviderUrl,
    string? Model,
    string? ApiKey,
    bool OfflineMode = false,
    bool IsDefault = false,
    bool IsEnabled = true,
    string? WireApi = null,
    string? ReasoningEffort = null) {
    
    /// <summary>
    /// Returns a copy with the Alias updated. Used for profile rename.
    /// </summary>
    internal ModelProfile WithAlias(string newAlias) => this with { Alias = newAlias };

    /// <summary>
    /// Returns a profile copy with a new ID and a duplicate-friendly alias.
    /// </summary>
    internal static ModelProfile CreateDuplicate(
        ModelProfile source,
        IReadOnlyList<ModelProfile> existingProfiles,
        Func<string>? idFactory = null) {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (existingProfiles is null) throw new ArgumentNullException(nameof(existingProfiles));

        var duplicateId = idFactory?.Invoke();
        if (string.IsNullOrWhiteSpace(duplicateId))
            duplicateId = Guid.NewGuid().ToString("N")[..8];

        var duplicateAlias = BuildDuplicateAlias(
            source.Alias,
            existingProfiles.Select(profile => profile.Alias));

        return source with {
            Id = duplicateId,
            Alias = duplicateAlias,
            IsDefault = false
        };
    }

    internal static string BuildDuplicateAlias(string sourceAlias, IEnumerable<string?> existingAliases) {
        if (existingAliases is null) throw new ArgumentNullException(nameof(existingAliases));

        var baseAlias = string.IsNullOrWhiteSpace(sourceAlias) ? "Profile" : sourceAlias.Trim();
        var copyAlias = $"{baseAlias} - Copy";
        var usedAliases = new HashSet<string>(
            existingAliases
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (!usedAliases.Contains(copyAlias))
            return copyAlias;

        var index = 2;
        while (true) {
            var disambiguated = $"{baseAlias} - Copy {index}";
            if (!usedAliases.Contains(disambiguated))
                return disambiguated;

            index++;
        }
    }
};

using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Persistence layer for <see cref="ModelProfile"/> CRUD and per-category assignments.
/// Profiles and assignments are stored inside the existing <see cref="ApplicationSettingsSnapshot"/>
/// JSON file alongside all other settings.
/// </summary>
internal sealed class ModelProfileStore {
    private readonly ApplicationSettingsStore _settingsStore;

    internal ModelProfileStore(ApplicationSettingsStore settingsStore) {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    /// <summary>
    /// Returns all model profiles, performing legacy migration if the list is empty.
    /// </summary>
    internal IReadOnlyList<ModelProfile> GetProfiles() {
        var snapshot = _settingsStore.Load();
        var profiles = snapshot.ModelProfiles;
        if (profiles is not null && profiles.Count > 0)
            return profiles;

        var migrated = MigrateLegacyProfile(snapshot);
        _settingsStore.SaveModelProfiles(migrated);
        return migrated;
    }

    /// <summary>
    /// Returns the default profile, or the first profile if none is marked default.
    /// </summary>
    internal ModelProfile? GetDefaultProfile() {
        var profiles = GetProfiles();
        return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
    }

    /// <summary>
    /// Replaces the full profile list.
    /// </summary>
    internal void SaveProfiles(IReadOnlyList<ModelProfile> profiles) {
        _settingsStore.SaveModelProfiles(profiles);
    }

    /// <summary>
    /// Returns category → profileId assignments.
    /// </summary>
    internal IReadOnlyDictionary<string, string> GetCategoryAssignments() {
        return _settingsStore.Load().CategoryAssignments
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the full category assignment map.
    /// </summary>
    internal void SaveCategoryAssignments(Dictionary<string, string> assignments) {
        _settingsStore.SaveCategoryAssignments(assignments);
    }

    /// <summary>
    /// Builds a single default <see cref="ModelProfile"/> from legacy single-provider fields.
    /// </summary>
    internal static IReadOnlyList<ModelProfile> MigrateLegacyProfile(ApplicationSettingsSnapshot snapshot) {
        string providerType;
        string? providerUrl;
        string? model;
        string? apiKey;
        bool offlineMode;

        if (snapshot.ModelProvider == ModelProvider.Custom &&
            !string.IsNullOrWhiteSpace(snapshot.ByokProviderUrl)) {
            providerType = snapshot.ByokProviderType ?? "openai";
            providerUrl = snapshot.ByokProviderUrl;
            model = snapshot.ByokModel;
            apiKey = snapshot.ByokApiKey;
            offlineMode = snapshot.ByokOfflineMode;
        }
        else {
            providerType = "copilot";
            providerUrl = null;
            model = snapshot.CopilotDefaultModel ?? ApplicationSettingsSnapshot.DefaultCopilotModel;
            apiKey = null;
            offlineMode = false;
        }

        var profile = new ModelProfile(
            Id: "default",
            Alias: "Default",
            ProviderType: providerType,
            ProviderUrl: providerUrl,
            Model: model,
            ApiKey: apiKey,
            OfflineMode: offlineMode,
            IsDefault: true);

        return new[] { profile };
    }
}

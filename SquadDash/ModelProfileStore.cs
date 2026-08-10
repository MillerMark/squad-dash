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
        
        // Initialize default category assignments if none exist
        EnsureCategoryAssignmentsInitialized(migrated);
        
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
    /// Returns category → profileId assignments, initializing defaults if missing.
    /// </summary>
    internal IReadOnlyDictionary<string, string> GetCategoryAssignments() {
        var snapshot = _settingsStore.Load();
        var assignments = snapshot.CategoryAssignments;
        
        // Initialize if missing or empty
        if (assignments is null || assignments.Count == 0) {
            var profiles = GetProfiles();
            EnsureCategoryAssignmentsInitialized(profiles);
            // Reload after initialization
            assignments = _settingsStore.Load().CategoryAssignments;
        }
        
        return assignments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        string alias;

        if (snapshot.ModelProvider == ModelProvider.Custom &&
            !string.IsNullOrWhiteSpace(snapshot.ByokProviderUrl)) {
            providerType = snapshot.ByokProviderType ?? "openai";
            providerUrl = snapshot.ByokProviderUrl;
            model = snapshot.ByokModel;
            apiKey = snapshot.ByokApiKey;
            offlineMode = snapshot.ByokOfflineMode;
            alias = "Custom Provider";
        }
        else {
            providerType = "copilot";
            providerUrl = null;
            model = snapshot.CopilotDefaultModel ?? ApplicationSettingsSnapshot.DefaultCopilotModel;
            apiKey = null;
            offlineMode = false;
            alias = "GitHub Copilot";
        }

        var profile = new ModelProfile(
            Id: "default",
            Alias: alias,
            ProviderType: providerType,
            ProviderUrl: providerUrl,
            Model: model,
            ApiKey: apiKey,
            OfflineMode: offlineMode,
            IsDefault: true);

        return new[] { profile };
    }

    /// <summary>
    /// Returns default category assignments for a fresh migration.
    /// All categories use the default profile initially.
    /// </summary>
    internal static Dictionary<string, string> GetDefaultCategoryAssignments(string defaultProfileId) {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [ModelProfileCategory.Coordinator] = defaultProfileId,
            [ModelProfileCategory.SpawnedNamedAgents] = defaultProfileId,
            [ModelProfileCategory.TemporaryAgents] = defaultProfileId,
            [ModelProfileCategory.RAI] = defaultProfileId,
            [ModelProfileCategory.Scribe] = defaultProfileId,
            [ModelProfileCategory.Ralph] = defaultProfileId,
            [ModelProfileCategory.FactChecker] = defaultProfileId
        };
    }

    /// <summary>
    /// Ensures category assignments are initialized and persisted if missing.
    /// </summary>
    private void EnsureCategoryAssignmentsInitialized(IReadOnlyList<ModelProfile> profiles) {
        var snapshot = _settingsStore.Load();
        if (snapshot.CategoryAssignments is not null && snapshot.CategoryAssignments.Count > 0)
            return;

        var defaultProfile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
        if (defaultProfile is null)
            return;

        var defaultAssignments = GetDefaultCategoryAssignments(defaultProfile.Id);
        _settingsStore.SaveCategoryAssignments(defaultAssignments);
    }
}

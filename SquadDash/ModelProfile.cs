namespace SquadDash;

/// <summary>
/// A named model configuration that maps to a specific LLM provider and model.
/// Multiple profiles can coexist so different agent categories use different models.
/// </summary>
internal sealed record ModelProfile(
    string Id,
    string Alias,
    string ProviderType,
    string? ProviderUrl,
    string? Model,
    string? ApiKey,
    bool OfflineMode = false,
    bool IsDefault = false) {
    
    /// <summary>
    /// Returns a copy with the Alias updated. Used for profile rename.
    /// </summary>
    internal ModelProfile WithAlias(string newAlias) => this with { Alias = newAlias };
};

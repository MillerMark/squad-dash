namespace SquadDash;

/// <summary>
/// Bring Your Own Key (BYOK) provider configuration for the Copilot CLI bridge process.
/// When <see cref="ProviderUrl"/> is set, GitHub auth is bypassed and the custom provider is used.
/// </summary>
internal sealed record ByokProviderSettings(
    string ProviderUrl,
    string? Model,
    string? ProviderType,
    string? ApiKey);

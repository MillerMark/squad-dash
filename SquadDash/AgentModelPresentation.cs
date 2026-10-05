using System.Collections.Generic;

namespace SquadDash;

internal static class AgentModelPresentation {
    internal static IReadOnlyList<string> BuildDetails(
        string? runtimeProfileAlias,
        string? runtimeModel,
        ModelProfileResolutionResult nextLaunch) {
        var runtime = string.IsNullOrWhiteSpace(runtimeProfileAlias)
            ? runtimeModel
            : string.IsNullOrWhiteSpace(runtimeModel)
                ? runtimeProfileAlias
                : $"{runtimeProfileAlias} ({runtimeModel})";
        var profile = nextLaunch.Profile;
        var configured = profile is null
            ? "Unavailable"
            : string.IsNullOrWhiteSpace(profile.Model)
                ? profile.Alias
                : $"{profile.Alias} ({profile.Model})";
        var reason = nextLaunch.Reason == ModelProfileResolutionReason.Override ? " (override)" : string.Empty;
        return [
            $"This run: {(string.IsNullOrWhiteSpace(runtime) ? "model not reported" : runtime)}",
            $"Next launch: {configured}{reason}"
        ];
    }
}

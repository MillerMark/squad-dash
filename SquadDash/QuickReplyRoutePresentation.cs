using System;
using System.Linq;
using System.Collections.Generic;

namespace SquadDash;

internal static class QuickReplyRoutePresentation {
    internal readonly record struct RouteInfo(
        string? RouteMode,
        string? AgentLabel,
        string? Reason);

    public static string? BuildCaption(IReadOnlyList<RouteInfo> routes) {
        var normalized = routes
            .Select(route => new RouteInfo(
                Normalize(route.RouteMode),
                Normalize(route.AgentLabel),
                Normalize(route.Reason)))
            .ToArray();

        // Draft-mode buttons pre-fill the input without sending — no routing caption applies.
        if (normalized.Any(route => string.Equals(route.RouteMode, "draft", StringComparison.OrdinalIgnoreCase)))
            return null;

        var nonCoordinatorRoutes = normalized
            .Where(route => !string.IsNullOrWhiteSpace(route.AgentLabel))
            .ToArray();

        if (nonCoordinatorRoutes.Length == 0)
            return "Next step will stay with the Coordinator.";

        var distinctLabels = nonCoordinatorRoutes
            .Select(route => route.AgentLabel!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasCoordinatorRoute = normalized.Any(route => string.IsNullOrWhiteSpace(route.AgentLabel));
        if (hasCoordinatorRoute || distinctLabels.Length != 1)
            return null;

        var routeModes = nonCoordinatorRoutes
            .Select(route => route.RouteMode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (routeModes.Length != 1)
            return null;

        var routeMode = routeModes[0];
        if (string.Equals(routeMode, "start_named_agent", StringComparison.OrdinalIgnoreCase))
            return $"Next step will go to {distinctLabels[0]}.";

        return $"Next step will continue with {distinctLabels[0]}.";
    }

    public static string BuildButtonToolTip(RouteInfo route) {
        var normalizedLabel = Normalize(route.AgentLabel);
        var normalizedMode = Normalize(route.RouteMode);

        if (string.Equals(normalizedMode, "draft", StringComparison.OrdinalIgnoreCase))
            return "Pre-fill draft (won't send)";

        return string.IsNullOrWhiteSpace(normalizedLabel)
            ? "Handled by Coordinator"
            : string.Equals(normalizedMode, "start_named_agent", StringComparison.OrdinalIgnoreCase)
                ? $"Start with {normalizedLabel}"
                : $"Continue with {normalizedLabel}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}

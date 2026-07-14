using System;

namespace SquadDash.GuidedTours;

internal static class GuidedTourWorkspacePathResolver
{
    internal static string? Resolve(string? capturedPath, Func<string?>? currentPathProvider)
    {
        if (!string.IsNullOrWhiteSpace(capturedPath))
            return capturedPath;

        var currentPath = currentPathProvider?.Invoke();
        return string.IsNullOrWhiteSpace(currentPath) ? null : currentPath;
    }
}

using SquadDash.Screenshots;

namespace SquadDash;

internal static class WorkspaceStartupRoutingPolicy {
    public static bool ShouldBypassSingleInstanceRouting(ScreenshotRefreshOptions refreshOptions) {
        return refreshOptions.Mode != ScreenshotRefreshMode.None;
    }
}

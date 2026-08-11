namespace SquadDash;

internal static class QueueRestorePolicy
{
    internal static int? NormalizeActiveTabIndex(int? savedIndex, int itemCount) =>
        savedIndex is >= 0 && savedIndex < itemCount
            ? savedIndex
            : null;
}

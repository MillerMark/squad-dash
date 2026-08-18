using System;
using System.Collections.Generic;
using System.IO;

namespace SquadDash;

internal static class RuntimeSlotDeploymentPolicy {
    public static string GetInactiveSlot(string? activeSlot) {
        return RuntimeSlotNames.Toggle(activeSlot);
    }

    public static bool IsSlotOccupied(
        RuntimeSlotStateStore slotStore,
        string slotName,
        IEnumerable<string?> runningExecutablePaths) {
        ArgumentNullException.ThrowIfNull(slotStore);
        ArgumentNullException.ThrowIfNull(runningExecutablePaths);

        var payloadPath = NormalizePath(slotStore.GetPayloadPath(slotName));
        foreach (var executablePath in runningExecutablePaths) {
            if (string.IsNullOrWhiteSpace(executablePath))
                continue;

            if (string.Equals(NormalizePath(executablePath), payloadPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

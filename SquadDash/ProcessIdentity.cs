using System;
using System.Diagnostics;

namespace SquadDash;

internal static class ProcessIdentity {
    public static long GetCurrentProcessStartedAtUtcTicks() {
        try {
            using var process = Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch {
            return DateTimeOffset.UtcNow.Ticks;
        }
    }
}

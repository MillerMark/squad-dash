using System;
using System.Text.Json;

namespace SquadDash;

// SafeWrite lives in a separate file so that projects sharing JsonFileStorage.cs
// (e.g. SquadDashLauncher) are not forced to also include SquadDashTrace.
internal static partial class JsonFileStorage
{
    /// <summary>
    /// Calls <see cref="AtomicWrite{T}"/> and logs any exception to
    /// <see cref="SquadDashTrace"/> instead of propagating it.
    /// </summary>
    public static void SafeWrite<T>(string path, T payload,
        string traceCategory, string operationName,
        JsonSerializerOptions? options = null) {
        try {
            AtomicWrite(path, payload, options);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(traceCategory, $"{operationName} failed: {ex.Message}");
        }
    }
}

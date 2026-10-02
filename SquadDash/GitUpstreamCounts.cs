using System.Globalization;

namespace SquadDash;

internal readonly record struct GitUpstreamCounts(int Behind, int Ahead)
{
    public static bool TryParse(string? output, out GitUpstreamCounts counts)
    {
        counts = default;
        var parts = output?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var behind)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ahead))
            return false;

        counts = new(behind, ahead);
        return true;
    }
}

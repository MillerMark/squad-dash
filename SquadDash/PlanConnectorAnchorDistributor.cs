namespace SquadDash;

/// <summary>
/// Distributes connector endpoints along a task edge. Equal destination coordinates still
/// represent distinct connectors, so each occurrence receives its own slot.
/// </summary>
internal sealed class PlanConnectorAnchorDistributor
{
    private readonly Dictionary<string, List<double>> _coordinates =
        new(StringComparer.Ordinal);
    private readonly Dictionary<(string TaskId, double Coordinate), int> _resolvedOccurrences = [];

    internal void Register(string taskId, double otherCoordinate)
    {
        if (!_coordinates.TryGetValue(taskId, out var values))
            _coordinates[taskId] = values = [];
        values.Add(otherCoordinate);
    }

    internal void Sort()
    {
        foreach (var values in _coordinates.Values)
            values.Sort();
        _resolvedOccurrences.Clear();
    }

    internal double ResolveY(string taskId, double otherCoordinate, double taskTop, double taskHeight)
    {
        if (!_coordinates.TryGetValue(taskId, out var values) || values.Count <= 1)
            return taskTop + taskHeight / 2.0;

        var firstIndex = values.BinarySearch(otherCoordinate);
        if (firstIndex < 0)
            firstIndex = ~firstIndex;
        else
            while (firstIndex > 0 && values[firstIndex - 1].Equals(otherCoordinate))
                firstIndex--;

        var key = (taskId, otherCoordinate);
        _resolvedOccurrences.TryGetValue(key, out var occurrence);
        _resolvedOccurrences[key] = occurrence + 1;

        var slotIndex = Math.Min(firstIndex + occurrence, values.Count - 1);
        return taskTop + taskHeight * (slotIndex + 1.0) / (values.Count + 1.0);
    }
}

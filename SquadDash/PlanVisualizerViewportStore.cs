using System;
using System.IO;
using System.Text.Json;

namespace SquadDash;

internal sealed record PlanVisualizerViewportState(double Zoom, double HorizontalOffset, double VerticalOffset);

internal sealed class PlanVisualizerViewportStore
{
    internal const double MinimumZoom = 0.25;
    internal const double MaximumZoom = 4.0;

    private const string DefaultZoomMutexName = @"Local\SquadDash.PlanVisualizerZoom";
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true
    };

    private readonly string? _folder;
    private readonly string _defaultZoomPath;

    internal PlanVisualizerViewportStore(string? squadFolderPath, string? appDataPath = null)
    {
        if (squadFolderPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(squadFolderPath);
            _folder = Path.Combine(squadFolderPath, "tmp", "decompose", "layout");
        }

        appDataPath ??= SquadDashPaths.AppData;
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataPath);
        _defaultZoomPath = Path.Combine(appDataPath, "plan-visualizer-zoom.json");
    }

    internal PlanVisualizerViewportState? Load(string groupId)
    {
        ValidateGroupId(groupId);
        if (_folder is null)
            return null;

        lock (Gate)
        {
            return LoadFile<PlanVisualizerViewportState>(
                Path.Combine(_folder, groupId + ".viewport.json"), IsValidState);
        }
    }

    internal void Save(string groupId, PlanVisualizerViewportState state)
    {
        ValidateGroupId(groupId);
        ArgumentNullException.ThrowIfNull(state);
        if (!IsValidState(state))
            throw new ArgumentOutOfRangeException(nameof(state), "Viewport zoom or offsets are invalid.");
        if (_folder is null)
            throw new InvalidOperationException("A squad folder is required to save a plan viewport.");

        lock (Gate)
        {
            Directory.CreateDirectory(_folder);
            JsonFileStorage.AtomicWrite(Path.Combine(_folder, groupId + ".viewport.json"), state, Options);
        }
    }

    internal double LoadDefaultZoom()
    {
        lock (Gate)
        {
            using var mutex = MutexLease.Acquire(DefaultZoomMutexName);
            return LoadFile<DefaultZoomState>(_defaultZoomPath, state => IsValidZoom(state.Zoom))?.Zoom ?? 1.0;
        }
    }

    internal void SaveDefaultZoom(double zoom)
    {
        if (!IsValidZoom(zoom))
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be finite and between 0.25 and 4.0.");

        lock (Gate)
        {
            using var mutex = MutexLease.Acquire(DefaultZoomMutexName);
            Directory.CreateDirectory(Path.GetDirectoryName(_defaultZoomPath)!);
            JsonFileStorage.AtomicWrite(_defaultZoomPath, new DefaultZoomState(zoom), Options);
        }
    }

    private static T? LoadFile<T>(string path, Func<T, bool> isValid) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            if (state is not null && isValid(state))
                return state;

            SquadDashTrace.Write("PlanViewport", $"Invalid viewport data in {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SquadDashTrace.Write("PlanViewport", $"Could not load viewport data from {path}: {ex.Message}");
        }

        return null;
    }

    private static bool IsValidState(PlanVisualizerViewportState state) =>
        IsValidZoom(state.Zoom) &&
        double.IsFinite(state.HorizontalOffset) && state.HorizontalOffset >= 0 &&
        double.IsFinite(state.VerticalOffset) && state.VerticalOffset >= 0;

    private static bool IsValidZoom(double zoom) =>
        double.IsFinite(zoom) && zoom >= MinimumZoom && zoom <= MaximumZoom;

    private static void ValidateGroupId(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (groupId is "." or ".." || groupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Group ID must be a single file name.", nameof(groupId));
    }

    private sealed record DefaultZoomState(double Zoom);
}

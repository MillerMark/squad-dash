using System;
using System.IO;
using System.Text.Json;

namespace SquadDash;

internal sealed class CommitViewerCodeFontStore
{
    internal const double MinimumFontSize = 8;
    internal const double MaximumFontSize = 32;

    private const string MutexName = @"Local\SquadDash.CommitViewerCodeFont";
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true
    };

    private readonly string _path;

    internal CommitViewerCodeFontStore(string? appDataPath = null)
    {
        appDataPath ??= SquadDashPaths.AppData;
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataPath);
        _path = Path.Combine(appDataPath, "commit-viewer-code-font.json");
    }

    internal static double Clamp(double fontSize)
    {
        if (!double.IsFinite(fontSize))
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be finite.");

        return Math.Clamp(fontSize, MinimumFontSize, MaximumFontSize);
    }

    internal double Load(double defaultFontSize)
    {
        lock (Gate)
        {
            using var mutex = MutexLease.Acquire(MutexName);
            if (!File.Exists(_path))
                return defaultFontSize;

            try
            {
                var state = JsonSerializer.Deserialize<CodeFontState>(File.ReadAllText(_path), Options);
                if (state is not null && double.IsFinite(state.FontSize))
                {
                    var fontSize = Clamp(state.FontSize);
                    if (fontSize != state.FontSize)
                        SquadDashTrace.Write("CommitViewerCodeFont",
                            $"Adjusted font size from {state.FontSize} to {fontSize} in {_path}.");

                    return fontSize;
                }

                SquadDashTrace.Write("CommitViewerCodeFont", $"Invalid font size data in {_path}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SquadDashTrace.Write("CommitViewerCodeFont",
                    $"Could not load font size data from {_path}: {ex.Message}");
            }

            return defaultFontSize;
        }
    }

    internal void Save(double fontSize)
    {
        fontSize = Clamp(fontSize);
        lock (Gate)
        {
            using var mutex = MutexLease.Acquire(MutexName);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            JsonFileStorage.AtomicWrite(_path, new CodeFontState(fontSize), Options);
        }
    }

    private sealed record CodeFontState(double FontSize);
}

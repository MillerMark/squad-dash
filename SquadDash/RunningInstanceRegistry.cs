using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SquadDash;

internal sealed class RunningInstanceRegistry {
    private readonly string _stateDirectory;

    public RunningInstanceRegistry()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadDash")) {
    }

    internal RunningInstanceRegistry(string stateDirectory) {
        if (string.IsNullOrWhiteSpace(stateDirectory))
            throw new ArgumentException("State directory cannot be empty.", nameof(stateDirectory));

        _stateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(_stateDirectory);
    }

    public void Upsert(RunningInstanceRecord record) {
        var normalized = Normalize(record);
        using var mutex = AcquireMutex(normalized.ApplicationRoot);

        var records = LoadCore(normalized.ApplicationRoot)
            .Where(existing => !IsSameInstance(existing, normalized))
            .Append(normalized)
            .Where(IsProcessAlive)
            .ToArray();

        SaveCore(normalized.ApplicationRoot, records);
    }

    public void Remove(string applicationRoot, int processId, long processStartedAtUtcTicks) {
        var normalizedRoot = NormalizePath(applicationRoot);
        using var mutex = AcquireMutex(normalizedRoot);

        var records = LoadCore(normalizedRoot)
            .Where(existing =>
                existing.ProcessId != processId ||
                existing.ProcessStartedAtUtcTicks != processStartedAtUtcTicks)
            .Where(IsProcessAlive)
            .ToArray();

        SaveCore(normalizedRoot, records);
    }

    public IReadOnlyList<RunningInstanceRecord> LoadLiveInstances(string applicationRoot) {
        var normalizedRoot = NormalizePath(applicationRoot);
        using var mutex = AcquireMutex(normalizedRoot);

        var records = LoadCore(normalizedRoot)
            .Where(IsProcessAlive)
            .OrderBy(record => record.RegisteredAtUtcTicks)
            .ToArray();

        SaveCore(normalizedRoot, records);
        return records;
    }

    private RunningInstanceRecord[] LoadCore(string applicationRoot) {
        var path = GetRegistryPath(applicationRoot);
        if (!File.Exists(path))
            return Array.Empty<RunningInstanceRecord>();

        try {
            var json = File.ReadAllText(path);
            var records = JsonSerializer.Deserialize<RunningInstanceRecord[]>(json);
            return records?
                .Select(Normalize)
                .ToArray()
                ?? Array.Empty<RunningInstanceRecord>();
        }
        catch {
            return Array.Empty<RunningInstanceRecord>();
        }
    }

    private void SaveCore(string applicationRoot, IReadOnlyList<RunningInstanceRecord> records) {
        var normalizedRoot = NormalizePath(applicationRoot);
        var path = GetRegistryPath(normalizedRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (records.Count == 0) {
            if (File.Exists(path))
                File.Delete(path);

            return;
        }

        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions {
            WriteIndented = true
        });

        File.WriteAllText(tempPath, json);

        if (File.Exists(path)) {
            File.Copy(tempPath, path, overwrite: true);
            File.Delete(tempPath);
        }
        else {
            File.Move(tempPath, path);
        }
    }

    private string GetRegistryPath(string applicationRoot) {
        return Path.Combine(
            _stateDirectory,
            $"instances-{ComputeHash(applicationRoot)[..16]}.json");
    }

    private static bool IsSameInstance(RunningInstanceRecord left, RunningInstanceRecord right) {
        return left.ProcessId == right.ProcessId &&
               left.ProcessStartedAtUtcTicks == right.ProcessStartedAtUtcTicks &&
               string.Equals(left.ApplicationRoot, right.ApplicationRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static RunningInstanceRecord Normalize(RunningInstanceRecord record) {
        return new RunningInstanceRecord(
            NormalizePath(record.ApplicationRoot),
            NormalizePath(record.WorkspaceFolder),
            record.ProcessId,
            record.ProcessStartedAtUtcTicks,
            record.RegisteredAtUtcTicks) {
            ActiveWorkspaceFolder = string.IsNullOrWhiteSpace(record.ActiveWorkspaceFolder)
                ? null
                : NormalizePath(record.ActiveWorkspaceFolder)
        };
    }

    private static string NormalizePath(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsProcessAlive(RunningInstanceRecord record) {
        try {
            using var process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited)
                return false;

            var startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            return startTimeUtcTicks == record.ProcessStartedAtUtcTicks;
        }
        catch {
            return false;
        }
    }

    private static MutexLease AcquireMutex(string applicationRoot) {
        var hash = ComputeHash(applicationRoot);
        return MutexLease.Acquire($@"Local\SquadDash.RunningInstances.{hash[..24]}");
    }

    private static string ComputeHash(string value) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var valueByte in bytes)
            builder.Append(valueByte.ToString("x2"));

        return builder.ToString();
    }
}

internal sealed record RunningInstanceRecord(
    string ApplicationRoot,
    string WorkspaceFolder,
    int ProcessId,
    long ProcessStartedAtUtcTicks,
    long RegisteredAtUtcTicks) {
    public string? ActiveWorkspaceFolder { get; init; } = WorkspaceFolder;
}

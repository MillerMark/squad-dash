using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SquadDash;

internal sealed class RestartCoordinatorStateStore {
    private readonly string _stateDirectory;

    public RestartCoordinatorStateStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadDash")) {
    }

    internal RestartCoordinatorStateStore(string stateDirectory) {
        if (string.IsNullOrWhiteSpace(stateDirectory))
            throw new ArgumentException("State directory cannot be empty.", nameof(stateDirectory));

        _stateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(_stateDirectory);
    }

    public RestartRequestState? LoadRequest(string applicationRoot) {
        var normalizedRoot = NormalizePath(applicationRoot);
        using var mutex = AcquireMutex(normalizedRoot);

        var path = GetRequestPath(normalizedRoot);
        if (!File.Exists(path))
            return null;

        try {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<RestartRequestState>(json);
            return NormalizeRequest(state);
        }
        catch {
            return null;
        }
    }

    public void SaveRequest(RestartRequestState state) {
        var normalized = NormalizeRequest(state)
            ?? throw new ArgumentException("Restart request state is invalid.", nameof(state));
        using var mutex = AcquireMutex(normalized.ApplicationRoot);
        SaveJson(GetRequestPath(normalized.ApplicationRoot), normalized);
    }

    public void ClearRequest(string applicationRoot) {
        var normalizedRoot = NormalizePath(applicationRoot);
        using var mutex = AcquireMutex(normalizedRoot);
        DeleteIfExists(GetRequestPath(normalizedRoot));
    }

    public void SavePlan(RestartPlanState state) {
        var normalized = NormalizePlan(state)
            ?? throw new ArgumentException("Restart plan state is invalid.", nameof(state));
        using var mutex = AcquireMutex(normalized.ApplicationRoot);
        SaveJson(GetPlanPath(normalized.ApplicationRoot, normalized.RequestId), normalized);
    }

    public RestartPlanState? LoadPlan(string applicationRoot, string requestId) {
        var normalizedRoot = NormalizePath(applicationRoot);
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId is null)
            return null;

        using var mutex = AcquireMutex(normalizedRoot);
        var path = GetPlanPath(normalizedRoot, normalizedRequestId);
        if (!File.Exists(path))
            return null;

        try {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<RestartPlanState>(json);
            return NormalizePlan(state);
        }
        catch {
            return null;
        }
    }

    public void ClearPlan(string applicationRoot, string requestId) {
        var normalizedRoot = NormalizePath(applicationRoot);
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId is null)
            return;

        using var mutex = AcquireMutex(normalizedRoot);
        DeleteIfExists(GetPlanPath(normalizedRoot, normalizedRequestId));
    }

    public string GetRequestPathForWatcher(string applicationRoot) {
        return GetRequestPath(NormalizePath(applicationRoot));
    }

    private static RestartRequestState? NormalizeRequest(RestartRequestState? state) {
        if (state is null)
            return null;

        var applicationRoot = NormalizePath(state.ApplicationRoot);
        var requestId = NormalizeRequestId(state.RequestId);
        if (requestId is null)
            return null;

        return new RestartRequestState(
            applicationRoot,
            requestId,
            state.RequestedAt.ToUniversalTime());
    }

    private static RestartPlanState? NormalizePlan(RestartPlanState? state) {
        if (state is null)
            return null;

        var applicationRoot = NormalizePath(state.ApplicationRoot);
        var requestId = NormalizeRequestId(state.RequestId);
        if (requestId is null)
            return null;

        var instances = state.Instances
            .Select(record => new RunningInstanceRecord(
                NormalizePath(record.ApplicationRoot),
                NormalizePath(record.WorkspaceFolder),
                record.ProcessId,
                record.ProcessStartedAtUtcTicks,
                record.RegisteredAtUtcTicks) {
                ActiveWorkspaceFolder = string.IsNullOrWhiteSpace(record.ActiveWorkspaceFolder)
                    ? null
                    : NormalizePath(record.ActiveWorkspaceFolder)
            })
            .Where(record => string.Equals(record.ApplicationRoot, applicationRoot, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new RestartPlanState(
            applicationRoot,
            requestId,
            state.CreatedAt.ToUniversalTime(),
            instances);
    }

    private void SaveJson<T>(string path, T payload) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonFileStorage.AtomicWrite(path, payload);
    }

    private static void DeleteIfExists(string path) {
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetRequestPath(string applicationRoot) {
        return Path.Combine(_stateDirectory, $"restart-{ComputeHash(applicationRoot)[..16]}.json");
    }

    private string GetPlanPath(string applicationRoot, string requestId) {
        return Path.Combine(
            _stateDirectory,
            $"restart-plan-{ComputeHash(applicationRoot)[..16]}-{requestId}.json");
    }

    private static string NormalizePath(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? NormalizeRequestId(string? requestId) {
        return string.IsNullOrWhiteSpace(requestId)
            ? null
            : requestId.Trim();
    }

    private static MutexLease AcquireMutex(string applicationRoot) {
        var hash = ComputeHash(applicationRoot);
        return MutexLease.Acquire($@"Local\SquadDash.Restart.{hash[..24]}");
    }

    private static string ComputeHash(string value) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var valueByte in bytes)
            builder.Append(valueByte.ToString("x2"));

        return builder.ToString();
    }
}

internal sealed record RestartRequestState(
    string ApplicationRoot,
    string RequestId,
    DateTimeOffset RequestedAt);

internal sealed record RestartPlanState(
    string ApplicationRoot,
    string RequestId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RunningInstanceRecord> Instances);

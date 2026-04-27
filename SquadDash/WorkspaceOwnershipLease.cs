using System;
using System.Text;
using System.Threading;
using System.Security.Cryptography;

namespace SquadDash;

internal sealed class WorkspaceOwnershipLease : IDisposable {
    private MutexLease? _mutexLease;

    private WorkspaceOwnershipLease(
        string applicationRoot,
        string workspaceFolder,
        MutexLease mutexLease) {
        ApplicationRoot = NormalizePath(applicationRoot);
        WorkspaceFolder = NormalizePath(workspaceFolder);
        _mutexLease = mutexLease;
    }

    public string ApplicationRoot { get; }

    public string WorkspaceFolder { get; }

    public bool Matches(string applicationRoot, string workspaceFolder) {
        return string.Equals(ApplicationRoot, NormalizePath(applicationRoot), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(WorkspaceFolder, NormalizePath(workspaceFolder), StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryAcquire(
        string applicationRoot,
        string workspaceFolder,
        out WorkspaceOwnershipLease? lease) {
        var normalizedRoot = NormalizePath(applicationRoot);
        var normalizedWorkspace = NormalizePath(workspaceFolder);

        lease = null;
        if (!MutexLease.TryAcquire(GetMutexName(normalizedRoot, normalizedWorkspace), out var mutexLease) ||
            mutexLease is null) {
            return false;
        }

        lease = new WorkspaceOwnershipLease(normalizedRoot, normalizedWorkspace, mutexLease);
        return true;
    }

    public void Dispose() {
        Interlocked.Exchange(ref _mutexLease, null)?.Dispose();
    }

    public static string NormalizePath(string path) {
        return StartupWorkspaceResolver.NormalizePath(path);
    }

    private static string GetMutexName(string applicationRoot, string workspaceFolder) {
        var hash = ComputeHash(applicationRoot + "\n" + workspaceFolder);
        return $@"Local\SquadDash.Workspace.{hash[..24]}";
    }

    private static string ComputeHash(string value) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var valueByte in bytes)
            builder.Append(valueByte.ToString("x2"));

        return builder.ToString();
    }
}

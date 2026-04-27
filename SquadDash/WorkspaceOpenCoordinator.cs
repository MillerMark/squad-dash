using System;
using System.Linq;
using System.Threading;

namespace SquadDash;

internal enum WorkspaceOpenDisposition {
    OpenHere,
    AlreadyOpenHere,
    ActivatedExisting,
    Blocked
}

internal sealed record WorkspaceOpenDecision(
    WorkspaceOpenDisposition Disposition,
    WorkspaceOwnershipLease? Lease,
    RunningInstanceRecord? ExistingOwner);

internal sealed class WorkspaceOpenCoordinator {
    private static readonly TimeSpan InitialActivationWait = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan LeaseContentionActivationWait = TimeSpan.FromSeconds(2);

    private readonly RunningInstanceRegistry _registry;
    private readonly Func<string, RunningInstanceRecord, TimeSpan, bool> _activationRequester;

    public WorkspaceOpenCoordinator(
        RunningInstanceRegistry? registry = null,
        Func<string, RunningInstanceRecord, TimeSpan, bool>? activationRequester = null) {
        _registry = registry ?? new RunningInstanceRegistry();
        _activationRequester = activationRequester ?? InstanceActivationChannel.TryRequestActivation;
    }

    public WorkspaceOpenDecision ReserveOrActivate(
        string applicationRoot,
        string workspaceFolder,
        int currentProcessId,
        long currentProcessStartedAtUtcTicks,
        WorkspaceOwnershipLease? currentLease = null) {
        var normalizedRoot = WorkspaceOwnershipLease.NormalizePath(applicationRoot);
        var normalizedWorkspace = WorkspaceOwnershipLease.NormalizePath(workspaceFolder);

        if (currentLease?.Matches(normalizedRoot, normalizedWorkspace) == true) {
            return new WorkspaceOpenDecision(
                WorkspaceOpenDisposition.AlreadyOpenHere,
                Lease: null,
                ExistingOwner: null);
        }

        var seenOwner = TryActivateExistingOwner(
            normalizedRoot,
            normalizedWorkspace,
            currentProcessId,
            currentProcessStartedAtUtcTicks,
            InitialActivationWait,
            out var owner);
        if (seenOwner.Activated) {
            return new WorkspaceOpenDecision(
                WorkspaceOpenDisposition.ActivatedExisting,
                Lease: null,
                owner);
        }

        if (seenOwner.SeenOwner) {
            return new WorkspaceOpenDecision(
                WorkspaceOpenDisposition.Blocked,
                Lease: null,
                owner);
        }

        if (WorkspaceOwnershipLease.TryAcquire(normalizedRoot, normalizedWorkspace, out var lease)) {
            return new WorkspaceOpenDecision(
                WorkspaceOpenDisposition.OpenHere,
                lease,
                ExistingOwner: null);
        }

        var contendedOwner = TryActivateExistingOwner(
            normalizedRoot,
            normalizedWorkspace,
            currentProcessId,
            currentProcessStartedAtUtcTicks,
            LeaseContentionActivationWait,
            out owner);
        if (contendedOwner.Activated) {
            return new WorkspaceOpenDecision(
                WorkspaceOpenDisposition.ActivatedExisting,
                Lease: null,
                owner);
        }

        return new WorkspaceOpenDecision(
            WorkspaceOpenDisposition.Blocked,
            Lease: null,
            owner);
    }

    private (bool SeenOwner, bool Activated) TryActivateExistingOwner(
        string applicationRoot,
        string workspaceFolder,
        int currentProcessId,
        long currentProcessStartedAtUtcTicks,
        TimeSpan timeout,
        out RunningInstanceRecord? owner) {
        owner = null;
        var deadline = DateTime.UtcNow + timeout;
        var sawOwner = false;

        do {
            owner = FindExistingOwner(
                applicationRoot,
                workspaceFolder,
                currentProcessId,
                currentProcessStartedAtUtcTicks);

            if (owner is not null) {
                sawOwner = true;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    remaining = TimeSpan.FromMilliseconds(100);

                var attemptTimeout = remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250);
                if (_activationRequester(applicationRoot, owner, attemptTimeout))
                    return (SeenOwner: true, Activated: true);
            }

            if (DateTime.UtcNow >= deadline)
                break;

            Thread.Sleep(50);
        }
        while (true);

        return (SeenOwner: sawOwner, Activated: false);
    }

    private RunningInstanceRecord? FindExistingOwner(
        string applicationRoot,
        string workspaceFolder,
        int currentProcessId,
        long currentProcessStartedAtUtcTicks) {
        return _registry.LoadLiveInstances(applicationRoot)
            .Where(record =>
                record.ProcessId != currentProcessId ||
                record.ProcessStartedAtUtcTicks != currentProcessStartedAtUtcTicks)
            .Where(record =>
                !string.IsNullOrWhiteSpace(record.ActiveWorkspaceFolder) &&
                string.Equals(record.ActiveWorkspaceFolder, workspaceFolder, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.RegisteredAtUtcTicks)
            .FirstOrDefault();
    }
}

using System.Diagnostics;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class RunningInstanceRegistryTests {
    [Test]
    public void Upsert_ThenLoadLiveInstances_ReturnsCurrentProcessRecord() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = Path.Combine(workspace.RootPath, "AppRoot");
        var workspaceFolder = Path.Combine(workspace.RootPath, "Workspace");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(workspaceFolder);

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            workspaceFolder,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks));

        var records = registry.LoadLiveInstances(appRoot);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].WorkspaceFolder, Is.EqualTo(workspaceFolder));
    }

    [Test]
    public void Remove_DeletesMatchingRecord() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = Path.Combine(workspace.RootPath, "AppRoot");
        var workspaceFolder = Path.Combine(workspace.RootPath, "Workspace");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(workspaceFolder);
        var startedAtTicks = process.StartTime.ToUniversalTime().Ticks;

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            workspaceFolder,
            process.Id,
            startedAtTicks,
            DateTimeOffset.UtcNow.Ticks));

        registry.Remove(appRoot, process.Id, startedAtTicks);
        var records = registry.LoadLiveInstances(appRoot);

        Assert.That(records, Is.Empty);
    }

    [Test]
    public void LoadLiveInstances_PrunesRecordsForDeadProcesses() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        var appRoot = Path.Combine(workspace.RootPath, "AppRoot");
        var workspaceFolder = Path.Combine(workspace.RootPath, "Workspace");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(workspaceFolder);

        // int.MaxValue is not a valid system PID and Process.GetProcessById will throw,
        // causing IsProcessAlive to return false.
        const int deadPid = int.MaxValue;

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            workspaceFolder,
            deadPid,
            DateTimeOffset.UtcNow.AddHours(-1).Ticks,
            DateTimeOffset.UtcNow.Ticks));

        var records = registry.LoadLiveInstances(appRoot);

        Assert.That(records, Is.Empty);
    }

    [Test]
    public void Upsert_ReplacesExistingRecordForSameProcess() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = Path.Combine(workspace.RootPath, "AppRoot");
        var originalFolder = Path.Combine(workspace.RootPath, "Workspace1");
        var updatedFolder = Path.Combine(workspace.RootPath, "Workspace2");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(originalFolder);
        Directory.CreateDirectory(updatedFolder);
        var startedAtTicks = process.StartTime.ToUniversalTime().Ticks;

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            originalFolder,
            process.Id,
            startedAtTicks,
            DateTimeOffset.UtcNow.Ticks));

        var updated = new RunningInstanceRecord(
            appRoot,
            updatedFolder,
            process.Id,
            startedAtTicks,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = updatedFolder
        };
        registry.Upsert(updated);

        var records = registry.LoadLiveInstances(appRoot);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].ActiveWorkspaceFolder, Is.EqualTo(Path.GetFullPath(updatedFolder).TrimEnd(Path.DirectorySeparatorChar)));
    }

    [Test]
    public void LoadLiveInstances_ReturnsAllLiveInstancesForSameApplicationRoot() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = Path.Combine(workspace.RootPath, "AppRoot");
        var workspace1 = Path.Combine(workspace.RootPath, "Workspace1");
        var workspace2 = Path.Combine(workspace.RootPath, "Workspace2");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(workspace1);
        Directory.CreateDirectory(workspace2);

        // Use the current process's parent as the second live instance.
        // If we can't resolve the parent, the test is skipped.
        using var helper = TryGetParentProcess();
        Assume.That(helper, Is.Not.Null, "Parent process not accessible; skipping multi-instance test.");

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            workspace1,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks));

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            workspace2,
            helper!.Id,
            helper.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks));

        var records = registry.LoadLiveInstances(appRoot);

        Assert.That(records, Has.Count.EqualTo(2));
    }

    private static Process? TryGetParentProcess() {
        try {
            using var current = Process.GetCurrentProcess();
            // Walk the process list for a process whose Id is different from ours,
            // is alive, and has a readable StartTime. The test runner's parent
            // (usually dotnet.exe or vstest.console.exe) is a reliable choice.
            foreach (var candidate in Process.GetProcesses()) {
                if (candidate.Id == current.Id) {
                    candidate.Dispose();
                    continue;
                }

                try {
                    if (candidate.HasExited) {
                        candidate.Dispose();
                        continue;
                    }

                    _ = candidate.StartTime; // ensure readable before returning
                    return candidate;
                }
                catch {
                    candidate.Dispose();
                }
            }

            return null;
        }
        catch {
            return null;
        }
    }
}

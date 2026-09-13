using System.IO;
using System.Text.RegularExpressions;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryGitWiringTests
{
    [TestCase("AppendDecomposeRecoveryActions")]
    [TestCase("RefreshRecoveryInboxMessage")]
    [TestCase("SaveDecomposeRecoveryInboxReminder")]
    [TestCase("ReconcileDecomposeRecoveryInboxMessages")]
    [TestCase("TryPrepareRecoveryInboxMessageForUse")]
    public void RecoveryRenderPaths_ReadCachedHeadAndAncestryWithoutSynchronousGit(string methodName)
    {
        var method = ReadMember(ReadSource("MainWindow.xaml.cs"), methodName);

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ReadCachedRecoveryHeadCommit()"));
            Assert.That(method, Does.Contain("IsCachedRecoveryAncestor("));
            Assert.That(method, Does.Not.Contain("TryReadCurrentHeadCommit("));
            Assert.That(method, Does.Not.Contain("IsCurrentWorkspaceAncestor("));
            Assert.That(method, Does.Not.Contain("RunGitAsync("));
            Assert.That(method, Does.Not.Contain(".GetAwaiter().GetResult()"));
            Assert.That(method, Does.Not.Match(@"\.WaitForExit\s*\("));
            Assert.That(method, Does.Not.Match(@"\.Wait\s*\("));
            Assert.That(method, Does.Not.Match(@"\.Result\b"));
        });
    }

    [Test]
    public void CachedRecoveryAccessors_OnlyReadWorkspaceScopedSnapshot()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var head = ReadMember(source, "ReadCachedRecoveryHeadCommit");
        var ancestry = ReadMember(source, "IsCachedRecoveryAncestor");

        Assert.Multiple(() =>
        {
            Assert.That(head, Does.Contain("RecoveryPresentationSnapshot?.Head"));
            Assert.That(ancestry, Does.Contain("RecoveryPresentationSnapshot?.IsAncestor("));
            Assert.That(head + ancestry, Does.Not.Contain("PlanRecoveryGit."));
            Assert.That(head + ancestry, Does.Not.Contain("RunGit"));
            Assert.That(source, Does.Not.Contain("private string? TryReadCurrentHeadCommit("));
            Assert.That(source, Does.Not.Contain("private bool IsCurrentWorkspaceAncestor("));
            Assert.That(source, Does.Match(
                @"private readonly PlanRecoveryGitPresentationCache\s+_recoveryGitPresentationCache\s*="));
            Assert.That(source, Does.Match(
                @"RecoveryPresentationSnapshot\s*=>\s*string\.Equals\(\s*_recoveryGitPresentationCache\.Current\?\.Workspace,\s*_currentWorkspace\?\.FolderPath,\s*StringComparison\.OrdinalIgnoreCase\)\s*\?\s*_recoveryGitPresentationCache\.Current\s*:\s*null"));
        });
    }

    [Test]
    public void SnapshotRefresh_AwaitsBoundedQueryAndSelectsWorkspaceScopedPresentation()
    {
        var method = ReadMember(ReadSource("MainWindow.xaml.cs"), "RefreshRecoveryGitSnapshotAsync");
        var begin = method.IndexOf("_recoveryGitPresentationCache.BeginRefresh(workspace, refreshSurfaces)", StringComparison.Ordinal);
        var query = method.IndexOf("await PlanRecoveryGit.ReadSnapshotAsync(", StringComparison.Ordinal);
        var guard = method.IndexOf("if (!ReferenceEquals(store, _planStore)", StringComparison.Ordinal);
        var apply = method.IndexOf("_recoveryGitPresentationCache.TryApply(version, snapshot, out var shouldRefreshSurfaces)", StringComparison.Ordinal);
        var render = method.IndexOf("RefreshHumanReviewSurfaces(plan)", StringComparison.Ordinal);
        var sync = method.IndexOf("SyncPlansPanel()", StringComparison.Ordinal);
        var mark = method.IndexOf("_recoveryGitPresentationCache.MarkRendered()", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(begin, Is.GreaterThanOrEqualTo(0));
            Assert.That(query, Is.GreaterThan(begin));
            Assert.That(guard, Is.GreaterThan(query));
            Assert.That(apply, Is.GreaterThan(guard));
            Assert.That(method, Does.Match(
                @"if \(!ReferenceEquals\(store, _planStore\) \|\|\s*!string\.Equals\(workspace, _currentWorkspace\?\.FolderPath, StringComparison\.OrdinalIgnoreCase\)\)\s*return;"));
            Assert.That(method, Does.Match(
                @"if \(!_recoveryGitPresentationCache\.TryApply\(version, snapshot, out var shouldRefreshSurfaces\)\)\s*return;"));
            Assert.That(render, Is.GreaterThan(apply));
            Assert.That(sync, Is.GreaterThan(render));
            Assert.That(mark, Is.GreaterThan(sync));
            Assert.That(Regex.Matches(method, @"\.MarkRendered\(\)"), Has.Count.EqualTo(1));
            Assert.That(method, Does.Match(
                @"if \(shouldRefreshSurfaces\)\s*\{[^{}]*RefreshHumanReviewSurfaces\(plan\);[^{}]*SyncPlansPanel\(\);[^{}]*_recoveryGitPresentationCache\.MarkRendered\(\);\s*\}"));
            Assert.That(method, Does.Contain("CancellationTokenSource"));
            Assert.That(method, Does.Contain("budget.Token"));
            Assert.That(method, Does.Not.Contain(".GetAwaiter().GetResult()"));
            Assert.That(method, Does.Not.Match(@"\.Result\b"));
        });
    }

    [Test]
    public void WatcherAndActivation_RequestAsyncSurfaceRefreshEvenWhenBranchIsUnchanged()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var activation = ReadMember(source, "MainWindow_Activated");
        var refresh = ReadMember(source, "RefreshBranchIfChangedAsync");
        var query = refresh.IndexOf(
            "await RefreshRecoveryGitSnapshotAsync(refreshSurfaces: true)", StringComparison.Ordinal);
        var branchGuard = refresh.IndexOf("if (branch != _currentBranch)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Match(
                @"onGitHeadChanged:\s*\(\)\s*=>\s*_\s*=\s*RefreshBranchIfChangedAsync\(\)"));
            Assert.That(activation, Does.Contain("_ = RefreshBranchIfChangedAsync();"));
            Assert.That(query, Is.GreaterThanOrEqualTo(0));
            Assert.That(branchGuard, Is.GreaterThan(query));
            Assert.That(refresh, Does.Not.Contain(".GetAwaiter().GetResult()"));
        });
    }

    [Test]
    public void SaveRecoveryInboxReminder_RequestsAsyncSurfaceRefresh()
    {
        var method = ReadMember(ReadSource("MainWindow.xaml.cs"), "SaveDecomposeRecoveryInboxReminder");

        Assert.That(method, Does.Contain("_ = RefreshRecoveryGitSnapshotAsync(refreshSurfaces: true);"));
    }

    [Test]
    public void ApprovalClick_ValidatesFreshHeadAndDurablePlanBeforePreparingTaskAcceptance()
    {
        var method = ReadMember(ReadSource("MainWindow.xaml.cs"), "HandleApprovalClickAsync");
        var headReads = Regex.Matches(method, @"await\s+PlanRecoveryGit\.TryReadHeadAsync\s*\(");
        Assert.That(headReads, Has.Count.EqualTo(2), "Read HEAD before review and again before accepting it.");
        var validation = method.IndexOf("await ValidateHumanReviewStepAcceptanceAsync(", StringComparison.Ordinal);
        var apply = Regex.Match(method,
            @"PlanHumanReviewStepAcceptance\.ApplyTasks\s*\([^;]*\bcurrentHead\b[^;]*\breviewedTaskCommits\b",
            RegexOptions.Singleline);
        var durableReload = method.IndexOf("store.Load(plan.PlanId)", headReads[1].Index, StringComparison.Ordinal);
        var durableComparison = method.IndexOf("JsonSerializer.Serialize(latest)", StringComparison.Ordinal);
        var approval = method.IndexOf("_planApprovalRuntime.ApproveAsync(", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(validation, Is.GreaterThan(headReads[0].Index));
            Assert.That(headReads[1].Index, Is.GreaterThan(validation));
            Assert.That(method, Does.Match(@"string\.Equals\s*\(\s*currentHead\s*,\s*finalHead\s*,"));
            Assert.That(durableReload, Is.GreaterThan(headReads[1].Index));
            Assert.That(durableComparison, Is.GreaterThan(durableReload));
            Assert.That(method, Does.Contain("JsonSerializer.Serialize(plan)"));
            Assert.That(apply.Success, Is.True);
            Assert.That(apply.Index, Is.GreaterThan(durableComparison));
            Assert.That(approval, Is.GreaterThan(apply.Index));
            Assert.That(method, Does.Contain("prepareResolution: prepareResolution"));
            Assert.That(method, Does.Contain("CancellationTokenSource"));
            Assert.That(method, Does.Not.Contain("RunGitAsync("));
            Assert.That(method, Does.Not.Contain("rev-parse"));
        });
    }

    [Test]
    public void HumanReviewValidation_UsesBoundedCancelableAncestryQuery()
    {
        var method = ReadMember(ReadSource("MainWindow.xaml.cs"), "ValidateHumanReviewStepAcceptanceAsync");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Match(
                @"await\s+PlanRecoveryGit\.IsAncestorAsync\s*\(\s*workspace\s*,\s*reviewTarget\s*,\s*currentHead\s*,\s*cancellationToken\s*\)"));
            Assert.That(method, Does.Not.Contain("RunGitAsync("));
            Assert.That(method, Does.Not.Contain("merge-base"));
            Assert.That(method, Does.Not.Contain(".GetAwaiter().GetResult()"));
        });
    }

    [Test]
    public void RecoveryProcessImplementation_AsynchronouslyDrainsBothStreamsAndKillsTreeOnCancellation()
    {
        var source = ReadSource("PlanRecoveryGitQuery.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("StandardOutput.ReadToEndAsync("));
            Assert.That(source, Does.Contain("StandardError.ReadToEndAsync("));
            Assert.That(source, Does.Contain("WaitForExitAsync(cancellationToken)"));
            Assert.That(source, Does.Contain("Kill(entireProcessTree: true)"));
            Assert.That(source, Does.Not.Match(@"\.WaitForExit\s*\("));
            Assert.That(source, Does.Not.Match(@"\.ReadToEnd\s*\("));
            Assert.That(source, Does.Not.Contain(".GetAwaiter().GetResult()"));
        });
    }

    private static string ReadMember(string source, string name)
    {
        var declaration = Regex.Match(
            source, @"^    private [^\r\n]*\b" + Regex.Escape(name) + @"\(", RegexOptions.Multiline);
        Assert.That(declaration.Success, Is.True, $"Production member {name} was not found.");
        var next = Regex.Match(source[(declaration.Index + declaration.Length)..],
            @"^    private ", RegexOptions.Multiline);
        return next.Success
            ? source.Substring(declaration.Index, declaration.Length + next.Index)
            : source[declaration.Index..];
    }

    private static string ReadSource(string name)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SquadDash", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException($"Could not find production source {name}.");
    }
}

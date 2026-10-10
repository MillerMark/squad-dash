using System.IO;
using System.Text.RegularExpressions;

namespace SquadDash.Tests;

[TestFixture]
public class CollapsibleMarkdownDetailApprovalKitTests
{
    private const string TaskId = "DETAIL-20261009-006";
    private static readonly string[] ProofIds =
    [
        "detail-inbox-toggle", "detail-first-view-collapsed", "detail-plan-toggle",
        "detail-expanded-restart", "detail-collapsed-restart",
        "detail-copy-exclusion", "detail-find-exclusion",
    ];

    [Test]
    public void SupportedInboxArtifactPassesManualTestKitApprovalBinding()
    {
        var root = RepositoryRoot();
        var reference = new AgentArtifactReference
        {
            Path = Path.Combine(".squad", "tmp", "agent-artifacts", "detail-approval-kit.json"),
        };
        Assert.That(AgentArtifactStore.TryMaterialize(root, reference, AgentArtifactStore.DefaultMaxInboxBytes,
            archive: false, out var artifact, out var error), Is.True, error);
        Assert.That(InboxMessageParser.TryParseJsonObject(artifact!.Content, out var message), Is.True);
        Assert.That(message!.Subject, Is.EqualTo("Manual test kit: Collapsible Markdown Detail"));
        Assert.That(message.Attachments, Is.Empty, "The host, not the source author, binds the approval gate.");
        Assert.That(message.Actions, Is.Empty);
        Assert.That(ManualTestKitApprovalBinding.ValidateSourceBody(BuildPlan(), TaskId, message.Body), Is.Null);

        var lines = message.Body.ReplaceLineEndings("\n").Split('\n');
        Assert.That(message.Body, Does.Not.Contain(":::detail"), "Required actions stay outside details.");
        foreach (var id in ProofIds)
            Assert.That(lines.Count(line => line == "{{approval-check:" + id + "}}"), Is.EqualTo(1), id);
        Assert.That(Regex.Matches(message.Body, @"\{\{approval-check:").Count, Is.EqualTo(7));
        var firstView = message.Body.IndexOf("{{approval-check:detail-first-view-collapsed}}", StringComparison.Ordinal);
        var copy = message.Body.IndexOf("{{approval-check:detail-copy-exclusion}}", StringComparison.Ordinal);
        var find = message.Body.IndexOf("{{approval-check:detail-find-exclusion}}", StringComparison.Ordinal);
        var toggle = message.Body.IndexOf("{{approval-check:detail-inbox-toggle}}", StringComparison.Ordinal);
        var restart = message.Body.IndexOf("## Restart once", StringComparison.Ordinal);
        Assert.That(firstView, Is.LessThan(copy));
        Assert.That(copy, Is.LessThan(find));
        Assert.That(find, Is.LessThan(toggle));
        Assert.That(toggle, Is.LessThan(restart));
    }

    [Test]
    public void ProcedureLinksResolveFromWorkspaceAndCommandsNamePreparedLauncher()
    {
        var root = RepositoryRoot();
        var payload = File.ReadAllText(Path.Combine(root, ".squad", "tmp", "agent-artifacts", "detail-approval-kit.json"));
        Assert.That(InboxMessageParser.TryParseJsonObject(payload, out var message), Is.True);
        var links = Regex.Matches(message!.Body, @"\[([^\]]+)\]\(app://open-path:([^)]+)\)");
        Assert.That(links, Has.Count.EqualTo(2));
        foreach (Match link in links)
        {
            Assert.That(link.Groups[1].Value, Is.Not.Empty);
            var target = Path.GetFullPath(link.Groups[2].Value, root);
            Assert.That(File.Exists(target) || Directory.Exists(target), Is.True, target);
            Assert.That(target, Does.StartWith(root));
        }
        var commands = Regex.Matches(message.Body, "```text\\n([^`]+)\\n```");
        Assert.That(commands, Has.Count.EqualTo(2));
        Assert.That(commands[0].Groups[1].Value, Is.EqualTo(commands[1].Groups[1].Value));
        Assert.That(commands[0].Groups[1].Value, Is.EqualTo(
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"D:\\Drive\\Source\\SquadDash-public\\tools\\Open-DetailReviewBuild.ps1\""));
        var launcher = File.ReadAllText(Path.Combine(root, "tools", "Open-DetailReviewBuild.ps1"));
        Assert.That(launcher, Does.Contain(@"SquadDash\bin\Debug\net10.0-windows\SquadDash.App.exe"));
        Assert.That(launcher, Does.Contain("--app-root").And.Contain("--workspace"));
        Assert.That(launcher, Does.Contain("WaitForInputIdle").And.Contain("MainModule.FileName").And.Contain("Workspace."));
    }

    [Test]
    public void HostFixtureRoutingPreservesNormalInboxRefreshAndDoesNotPersistFixtureWindowsOrPanelVisibility()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "SquadDash", "MainWindow.xaml.cs"));
        var routingStart = host.IndexOf("private void OpenOrFocusInboxMessage(InboxMessage message", StringComparison.Ordinal);
        var routingEnd = host.IndexOf("private void OpenOrFocusDetailFixtureInboxMessage(", routingStart, StringComparison.Ordinal);
        var routing = host[routingStart..routingEnd];
        Assert.That(routing, Does.Contain("OpenOrFocusDetailFixtureInboxMessage(message);")
            .And.Contain("OpenOrFocusInboxMessage(message.Id, onMarkedRead);")
            .And.Not.Contain("ShowInboxMessageWindow("));
        var openStart = host.IndexOf("private async Task OpenDetailFixturesAsync(", StringComparison.Ordinal);
        var openEnd = host.IndexOf("private void FindInDetailFixtures()", openStart, StringComparison.Ordinal);
        var open = host[openStart..openEnd];
        Assert.That(open, Does.Contain("EnsurePlansPanelCreated();")
            .And.Not.Contain(".Show(").And.Not.Contain("_plansPanelVisible ="));
        Assert.That(host, Does.Contain(".Where(id => !string.IsNullOrEmpty(id) && !HasActiveDetailFixtureMessage(id))"));
        Assert.That(host, Does.Match(
            @"var openPlanGroupIds = _openPlanViewerWindows\s+\.Select\(entry => entry.GroupId\)\s+\.Where\(id => !string.IsNullOrEmpty\(id\) &&\s+!string.Equals\(id, SimulationPlanFixtureBuilder.DetailReviewPlanId, StringComparison.Ordinal\)\)"));
    }

    private static Plan BuildPlan() => new(
        "detail-approval-source", "review", PlanSource.DecomposeDecision, PlanLifecycleStatus.AwaitingApproval,
        "Collapsible Markdown Detail", "feature/collapsible-markdown-detail", "Review fixture source.",
        [new PlanTask(TaskId, "Prepare fixtures", "Prepare review material", [], "high", PlanTaskStatus.Complete)],
        [new PlanApprovalGate("detail-human-gate", "Review", [TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements: ProofIds.Select(id => new PlanTaskProofRequirement(
                id, "live-ui-observation", id, "Does the required behavior match?")).ToArray())],
        new PlanProgress(1, 1), new PlanTimestamps(DateTimeOffset.UtcNow));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "squad-dash.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

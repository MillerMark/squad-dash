using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public class CollapsibleMarkdownDetailFixtureTests
{
    [Test]
    public async Task FixtureStateAndCopyTextPersistAcrossFreshManagerReopen()
    {
        using var workspace = new TestWorkspace();
        var storePath = workspace.GetPath("prefs", "markdown-detail-state.json");
        var store = new MarkdownDetailStateStore(storePath);
        var firstManager = CreateManager(workspace.RootPath);
        var secondManager = CreateManager(workspace.RootPath);
        var firstSession = new DetailReviewFixtureSession(workspace.RootPath, firstManager, store);
        var secondSession = new DetailReviewFixtureSession(workspace.RootPath, secondManager, new MarkdownDetailStateStore(storePath));
        var sectionKeys = firstSession.GetFixtureSectionKeySet();

        try
        {
            var firstOpen = await firstSession.OpenAsync(resetDetailState: false);
            Assert.That(File.Exists(storePath), Is.False, "Unseen fixture reopening must not seed preferences.");
            using (var docs = BuildFixtureDocuments(firstOpen, workspace.RootPath, store))
            {
                SetExpandedState(docs.InboxDocument, sectionKeys.InboxLabelled, expanded: true);
                SetExpandedState(docs.PlanDocument, sectionKeys.PlanLabelled, expanded: true);
                SetExpandedState(docs.TaskDocument, sectionKeys.TaskLabelled, expanded: true);

                SetExpandedState(docs.InboxDocument, sectionKeys.InboxUnlabelled, expanded: true);
                SetExpandedState(docs.PlanDocument, sectionKeys.PlanUnlabelled, expanded: true);
                SetExpandedState(docs.TaskDocument, sectionKeys.TaskUnlabelled, expanded: true);

                SetExpandedState(docs.InboxDocument, sectionKeys.InboxUnlabelled, expanded: false);
                SetExpandedState(docs.PlanDocument, sectionKeys.PlanUnlabelled, expanded: false);
                SetExpandedState(docs.TaskDocument, sectionKeys.TaskUnlabelled, expanded: false);

                var inboxCopy = InboxMessageWindow.BuildMessageClipboardText(firstOpen.InboxMessage, docs.InboxDocument);
                var planCopy = PlanDescriptionDocumentSession.GetCopyText(docs.PlanDocument);
                var taskCopy = PlanDescriptionDocumentSession.GetCopyText(docs.TaskDocument);

                Assert.That(inboxCopy, Does.Contain(SimulationInboxFixtureBuilder.DetailReviewExpandedBodyToken));
                Assert.That(inboxCopy, Does.Not.Contain(SimulationInboxFixtureBuilder.DetailReviewCollapsedBodyToken));
                Assert.That(planCopy, Does.Contain(SimulationPlanFixtureBuilder.DetailReviewPlanExpandedBodyToken));
                Assert.That(planCopy, Does.Not.Contain(SimulationPlanFixtureBuilder.DetailReviewPlanCollapsedBodyToken));
                Assert.That(taskCopy, Does.Contain(SimulationPlanFixtureBuilder.DetailReviewTaskExpandedBodyToken));
                Assert.That(taskCopy, Does.Not.Contain(SimulationPlanFixtureBuilder.DetailReviewTaskCollapsedBodyToken));
            }

            Assert.That(store.Load(sectionKeys.InboxLabelled).IsExpanded, Is.True);
            Assert.That(store.Load(sectionKeys.InboxUnlabelled).IsExpanded, Is.False);
            Assert.That(store.Load(sectionKeys.PlanLabelled).IsExpanded, Is.True);
            Assert.That(store.Load(sectionKeys.PlanUnlabelled).IsExpanded, Is.False);
            Assert.That(store.Load(sectionKeys.TaskLabelled).IsExpanded, Is.True);
            Assert.That(store.Load(sectionKeys.TaskUnlabelled).IsExpanded, Is.False);

            var beforeReopen = File.ReadAllBytes(storePath);
            using (var json = JsonDocument.Parse(beforeReopen))
            {
                foreach (var key in new[] { sectionKeys.InboxUnlabelled, sectionKeys.PlanUnlabelled, sectionKeys.TaskUnlabelled })
                    Assert.That(json.RootElement.GetProperty("Entries").GetProperty(key).GetBoolean(), Is.False);
            }
            await firstSession.CleanupAsync(removeState: false);
            var reopened = await secondSession.ReopenAsync();
            using var reopenedDocs = BuildFixtureDocuments(reopened, workspace.RootPath, new MarkdownDetailStateStore(storePath));
            Assert.That(reopened.SessionId, Is.Not.EqualTo(firstOpen.SessionId));
            Assert.That(reopened.InboxMessage.Id, Is.EqualTo(firstOpen.InboxMessage.Id));
            Assert.That(reopened.InboxMessage.Body, Is.EqualTo(firstOpen.InboxMessage.Body));
            Assert.That(reopened.Plan.PlanId, Is.EqualTo(firstOpen.Plan.PlanId));
            Assert.That(reopened.Plan.Summary, Is.EqualTo(firstOpen.Plan.Summary));
            Assert.That(reopened.Plan.Tasks.Single().TaskId, Is.EqualTo(firstOpen.Plan.Tasks.Single().TaskId));
            Assert.That(reopened.Plan.Tasks.Single().Description, Is.EqualTo(firstOpen.Plan.Tasks.Single().Description));
            Assert.That(secondSession.GetFixtureSectionKeys(), Is.EqualTo(firstSession.GetFixtureSectionKeys()));
            Assert.That(File.ReadAllBytes(storePath), Is.EqualTo(beforeReopen), "Reopen/render must not write state.");

            Assert.That(GetExpandedState(reopenedDocs.InboxDocument, sectionKeys.InboxLabelled), Is.True);
            Assert.That(GetExpandedState(reopenedDocs.InboxDocument, sectionKeys.InboxUnlabelled), Is.False);
            Assert.That(GetExpandedState(reopenedDocs.PlanDocument, sectionKeys.PlanLabelled), Is.True);
            Assert.That(GetExpandedState(reopenedDocs.PlanDocument, sectionKeys.PlanUnlabelled), Is.False);
            Assert.That(GetExpandedState(reopenedDocs.TaskDocument, sectionKeys.TaskLabelled), Is.True);
            Assert.That(GetExpandedState(reopenedDocs.TaskDocument, sectionKeys.TaskUnlabelled), Is.False);
        }
        finally
        {
            await firstSession.CleanupAsync(removeState: false);
            await secondSession.CleanupAsync(removeState: false);
        }
    }

    [Test]
    public async Task CleanupStateRemovesOnlyFixtureKeysForCurrentWorkspace()
    {
        using var workspace = new TestWorkspace();
        var storePath = workspace.GetPath("prefs", "markdown-detail-state.json");
        var store = new MarkdownDetailStateStore(storePath);
        var manager = CreateManager(workspace.RootPath);
        var session = new DetailReviewFixtureSession(workspace.RootPath, manager, store);

        var unrelatedInboxKey = MarkdownDetailIdentity
            .ForInboxMessage(workspace.RootPath, "other-message")
            .GetSectionKey("Other section", "Other message content");
        var unrelatedPlanKey = MarkdownDetailIdentity
            .ForPlanDescription(workspace.RootPath, "other-plan")
            .GetSectionKey("Other detail", "Other plan content");
        store.Save(unrelatedInboxKey, true);
        store.Save(unrelatedPlanKey, true);

        foreach (var key in session.GetFixtureSectionKeys())
        {
            store.Save(key, true);
        }

        await session.CleanupAsync(removeState: true);

        Assert.That(store.Load(unrelatedInboxKey).IsExpanded, Is.True);
        Assert.That(store.Load(unrelatedPlanKey).IsExpanded, Is.True);
        foreach (var key in session.GetFixtureSectionKeys())
        {
            Assert.That(store.Load(key).IsExpanded, Is.False);
        }
    }

    [Test]
    public async Task CleanupStateIsScopedWhenOnlyCurrentWorkspaceSessionExists()
    {
        using var workspaceA = new TestWorkspace();
        using var workspaceB = new TestWorkspace();

        var sharedStorePath = workspaceA.GetPath("prefs-shared", "markdown-detail-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sharedStorePath)!);
        var store = new MarkdownDetailStateStore(sharedStorePath);
        var manager = CreateManager(workspaceA.RootPath);
        var sessionA = new DetailReviewFixtureSession(workspaceA.RootPath, manager, store);
        var sessionB = new DetailReviewFixtureSession(workspaceB.RootPath, manager, store);

        foreach (var key in sessionA.GetFixtureSectionKeys())
        {
            store.Save(key, true);
        }

        foreach (var key in sessionB.GetFixtureSectionKeys())
        {
            store.Save(key, true);
        }

        await sessionB.CleanupAsync(removeState: true);

        foreach (var key in sessionA.GetFixtureSectionKeys())
        {
            Assert.That(store.Load(key).IsExpanded, Is.True);
        }

        foreach (var key in sessionB.GetFixtureSectionKeys())
        {
            Assert.That(store.Load(key).IsExpanded, Is.False);
        }
    }

    [Test]
    public void CollapsedFixturesUseProductionCopyAndSearchPaths()
    {
        using var workspace = new TestWorkspace();
        var storePath = workspace.GetPath("prefs", "markdown-detail-state.json");
        var payload = new DetailReviewFixtureOpenResult(
            SessionId: "fixture-test",
            InboxMessage: SimulationInboxFixtureBuilder.BuildDetailReviewMessage(),
            Plan: SimulationPlanFixtureBuilder.BuildDetailReviewPlan());
        using var docs = BuildFixtureDocuments(payload, workspace.RootPath, new MarkdownDetailStateStore(storePath));

        foreach (var (document, prefix, inbox) in new[]
                 {
                     (docs.InboxDocument, "DETAIL-INBOX", true),
                     (docs.PlanDocument, "DETAIL-PLAN", false),
                     (docs.TaskDocument, "DETAIL-TASK", false),
                 })
        {
            var details = MarkdownVisibleContent.Details(document).ToArray();
            Assert.That(details.Select(detail => detail.Label), Is.EqualTo(new[] { "Expanded restart probe", "More detail" }));
            Assert.That(details.All(detail => !detail.Expander.IsExpanded), Is.True);
            var copy = inbox ? InboxMessageWindow.BuildMessageClipboardText(payload.InboxMessage, document)
                : PlanDescriptionDocumentSession.GetCopyText(document);
            Assert.That(copy, Does.Contain(prefix + "-VISIBLE-BEFORE").And.Contain(prefix + "-VISIBLE-AFTER"));
            foreach (var suffix in new[] { "-BODY", "-COLLAPSED" })
            {
                Assert.That(copy, Does.Not.Contain(prefix + suffix));
                Assert.That(InboxMessageWindow.FindTextInRange(document, prefix + suffix), Is.Null);
                Assert.That(PlanDescriptionDocumentSession.FindAll(document, prefix + suffix), Is.Empty);
            }
            Assert.That(InboxMessageWindow.FindTextInRange(document, prefix + "-VISIBLE-BEFORE"), Is.Not.Null);
            Assert.That(PlanDescriptionDocumentSession.FindAll(document, prefix + "-VISIBLE-BEFORE"), Has.Count.EqualTo(1));
            details[0].Expander.IsExpanded = true;
            Assert.That(InboxMessageWindow.FindTextInRange(document, prefix + "-BODY"), Is.Not.Null);
            Assert.That(PlanDescriptionDocumentSession.FindAll(document, prefix + "-BODY"), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void DefaultFixtureStoreUsesRealGlobalPathWithoutWriting()
    {
        var session = new DetailReviewFixtureSession(@"D:\review-workspace", new SimulationSessionManager());
        Assert.That(session.DetailStatePath,
            Is.EqualTo(Path.Combine(SquadDashPaths.AppData, "markdown-detail-state.json")));
    }

    [Test]
    public async Task ResetAndCleanupPreserveOtherSessionsContentAndPreferences()
    {
        using var workspace = new TestWorkspace();
        var store = new MarkdownDetailStateStore(workspace.GetPath("prefs", "detail.json"));
        var adapter = new TrackingSimulationAdapter(SimulationSurfaceKind.Inbox);
        var planAdapter = new TrackingSimulationAdapter(SimulationSurfaceKind.Plan);
        var manager = new SimulationSessionManager();
        manager.RegisterAdapter(adapter);
        manager.RegisterAdapter(planAdapter);
        var other = manager.CreateSession("Other fixture", "test");
        await manager.OverlayArtifactAsync(other.SessionId, SimulationSurfaceKind.Inbox, "other-inbox", "Other", new object());
        await manager.OverlayArtifactAsync(other.SessionId, SimulationSurfaceKind.Plan, "other-plan", "Other", new object());
        var unrelated = MarkdownDetailIdentity.ForInboxMessage(workspace.RootPath, "real-inbox")
            .GetSectionKey("Real detail", "Untouched");
        Assert.That(store.Save(unrelated, true).Succeeded, Is.True);
        var session = new DetailReviewFixtureSession(workspace.RootPath, manager, store);
        foreach (var key in session.GetFixtureSectionKeys())
            Assert.That(store.Save(key, true).Succeeded, Is.True);

        var result = await session.OpenAsync(resetDetailState: true);
        Assert.That(manager.GetArtifactsForSession(result.SessionId), Has.Count.EqualTo(2));
        Assert.That(adapter.Contains("sim-inbox-detail-fixture"), Is.True);
        Assert.That(planAdapter.Contains("sim-plan-detail-fixture"), Is.True);
        using (var json = JsonDocument.Parse(File.ReadAllBytes(store.FilePath)))
            Assert.That(json.RootElement.GetProperty("Entries").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { unrelated }));
        foreach (var key in session.GetFixtureSectionKeys())
            Assert.That(store.Save(key, false).Succeeded, Is.True);
        await session.CleanupAsync(removeState: true);
        Assert.That(manager.GetArtifactsForSession(result.SessionId), Is.Empty);
        Assert.That(adapter.Contains("sim-inbox-detail-fixture"), Is.False);
        Assert.That(planAdapter.Contains("sim-plan-detail-fixture"), Is.False);
        Assert.That(adapter.Contains("other-inbox"), Is.True);
        Assert.That(planAdapter.Contains("other-plan"), Is.True);
        Assert.That(manager.GetArtifactsForSession(other.SessionId), Has.Count.EqualTo(2));
        using (var json = JsonDocument.Parse(File.ReadAllBytes(store.FilePath)))
            Assert.That(json.RootElement.GetProperty("Entries").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { unrelated }));
        await manager.EndSessionAsync(other.SessionId);
    }

    [Test]
    public async Task RealPlanOverlayIsOpenOnlyAndSurvivesDurableRefresh()
    {
        var active = new StackPanel();
        PlanSimulationSurfaceAdapter? adapter = null;
        Plan? opened = null;
        void RejectMutation(Plan _) => Assert.Fail("A display-only fixture exposed a mutation.");
        var controller = new PlansPanelController(
            active, new StackPanel(), new Border(), new StackPanel(), new Border(),
            openPlan: plan => opened = plan,
            startPlan: RejectMutation, archivePlan: RejectMutation, revisePlan: RejectMutation,
            attachFollowUp: RejectMutation, addToNewChat: RejectMutation,
            renamePlan: (plan, _) => throw new AssertionException("Fixture rename exposed."),
            getPlanFilePath: _ => throw new AssertionException("Fixture has no durable path."),
            isSimulationOverlayPlanId: id => adapter?.IsSimulatedPlan(id) == true);
        adapter = new PlanSimulationSurfaceAdapter(controller, Dispatcher.CurrentDispatcher);
        var manager = new SimulationSessionManager();
        manager.RegisterAdapter(adapter);
        manager.RegisterAdapter(new TrackingSimulationAdapter(SimulationSurfaceKind.Inbox));
        using var workspace = new TestWorkspace();
        var session = new DetailReviewFixtureSession(workspace.RootPath, manager,
            new MarkdownDetailStateStore(workspace.GetPath("detail.json")));
        await session.ReopenAsync();
        controller.Refresh([]);
        var row = active.Children.OfType<Border>().Single(b => Equals(b.Tag, SimulationPlanFixtureBuilder.DetailReviewPlanId));
        var items = row.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Assert.That(items.Select(item => item.Header), Is.EqualTo(new[] { "Open Plan" }));
        items.Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(opened?.PlanId, Is.EqualTo(SimulationPlanFixtureBuilder.DetailReviewPlanId));
        await session.CleanupAsync(removeState: true);
        Assert.That(active.Children.OfType<Border>().Any(b => Equals(b.Tag, SimulationPlanFixtureBuilder.DetailReviewPlanId)), Is.False);
    }

    [Test]
    public void TaskPreviewAndInspectorShareProductionState()
    {
        using var workspace = new TestWorkspace();
        var plan = SimulationPlanFixtureBuilder.BuildDetailReviewPlan();
        var task = plan.Tasks.Single();
        var store = new MarkdownDetailStateStore(workspace.GetPath("detail.json"));
        var presentation = new PlanDescriptionPresentation(workspace.RootPath, plan.PlanId, plan.Tasks, 12, store: store);
        var preview = presentation.BuildTaskDescription(task.TaskId, task.Description);
        var inspector = new FlowDocument();
        presentation.AppendTaskDescription(inspector, task.TaskId, task.Description);
        try
        {
            var key = MarkdownVisibleContent.Details(inspector).First().Key!;
            SetExpandedState(inspector, key, true);
            Assert.That(PlanDescriptionDocumentSession.GetCopyText(preview), Does.Contain("DETAIL-TASK-BODY"));
            SetExpandedState(inspector, key, false);
            Assert.That(PlanDescriptionDocumentSession.GetCopyText(preview), Does.Not.Contain("DETAIL-TASK-BODY"));
        }
        finally
        {
            MarkdownVisibleContent.Detach(preview);
            MarkdownVisibleContent.Detach(inspector);
        }
    }

    private static SimulationSessionManager CreateManager(string workspaceRoot)
    {
        _ = workspaceRoot;
        var manager = new SimulationSessionManager();
        manager.RegisterAdapter(new TrackingSimulationAdapter(SimulationSurfaceKind.Inbox));
        manager.RegisterAdapter(new TrackingSimulationAdapter(SimulationSurfaceKind.Plan));
        return manager;
    }

    private static FixtureDocuments BuildFixtureDocuments(
        DetailReviewFixtureOpenResult payload,
        string workspaceRoot,
        MarkdownDetailStateStore store)
    {
        var inboxDetailState = new MarkdownDetailStateContext(
            MarkdownDetailIdentity.ForInboxMessage(workspaceRoot, payload.InboxMessage.Id),
            store);
        var task = payload.Plan.Tasks.Single(candidate => string.Equals(
            candidate.TaskId,
            SimulationPlanFixtureBuilder.DetailReviewTaskId,
            StringComparison.Ordinal));

        var inboxDocument = MarkdownFlowDocumentBuilder.BuildInbox(payload.InboxMessage.Body, detailState: inboxDetailState);
        var presentation = new PlanDescriptionPresentation(workspaceRoot, payload.Plan.PlanId, payload.Plan.Tasks, 12, store: store);
        var planDocument = new FlowDocument();
        presentation.AppendPlanDescription(planDocument, payload.Plan.Summary);
        var taskDocument = new FlowDocument();
        presentation.AppendTaskDescription(taskDocument, task.TaskId, task.Description);

        return new FixtureDocuments(
            inboxDocument,
            planDocument,
            taskDocument);
    }

    private static void SetExpandedState(FlowDocument document, string key, bool expanded)
    {
        var detail = MarkdownVisibleContent.Details(document)
            .Single(d => string.Equals(d.Key, key, StringComparison.Ordinal));
        detail.Expander.IsExpanded = expanded;
    }

    private static bool GetExpandedState(FlowDocument document, string key)
    {
        return MarkdownVisibleContent.Details(document)
            .Single(d => string.Equals(d.Key, key, StringComparison.Ordinal))
            .Expander.IsExpanded;
    }

    private sealed class FixtureDocuments : IDisposable
    {
        public FixtureDocuments(
            FlowDocument inboxDocument,
            FlowDocument planDocument,
            FlowDocument taskDocument)
        {
            InboxDocument = inboxDocument;
            PlanDocument = planDocument;
            TaskDocument = taskDocument;
        }

        public FlowDocument InboxDocument { get; }

        public FlowDocument PlanDocument { get; }

        public FlowDocument TaskDocument { get; }

        public void Dispose()
        {
            MarkdownVisibleContent.Detach(TaskDocument);
            MarkdownVisibleContent.Detach(PlanDocument);
            MarkdownVisibleContent.Detach(InboxDocument);
        }
    }

    private sealed class TrackingSimulationAdapter : ISimulationSurfaceAdapter
    {
        private readonly System.Collections.Generic.Dictionary<string, SimulationArtifact> _artifacts = new();

        public TrackingSimulationAdapter(SimulationSurfaceKind supportedSurface)
        {
            SupportedSurface = supportedSurface;
        }

        public SimulationSurfaceKind SupportedSurface { get; }

        public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
        {
            _artifacts.Add(artifact.ArtifactId, artifact);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(SimulationArtifact artifact)
        {
            _artifacts.Remove(artifact.ArtifactId);
            return Task.CompletedTask;
        }

        public Task RemoveAllForSessionAsync(string sessionId)
        {
            foreach (var artifact in _artifacts.Values.Where(a => a.SessionId == sessionId).ToArray())
                _artifacts.Remove(artifact.ArtifactId);
            return Task.CompletedTask;
        }

        public bool Contains(string artifactId)
        {
            return _artifacts.ContainsKey(artifactId);
        }
    }
}

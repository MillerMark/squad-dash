using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class DetailReviewFixtureSession
{
    private readonly string _workspaceFolder;
    private readonly SimulationSessionManager _simulationSessionManager;
    private readonly MarkdownDetailStateStore _detailStateStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private string? _sessionId;

    internal DetailReviewFixtureSession(
        string workspaceFolder,
        SimulationSessionManager simulationSessionManager,
        MarkdownDetailStateStore? detailStateStore = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder is required.", nameof(workspaceFolder));
        _workspaceFolder = workspaceFolder;
        _simulationSessionManager = simulationSessionManager ??
                                    throw new ArgumentNullException(nameof(simulationSessionManager));
        _detailStateStore = detailStateStore ?? new MarkdownDetailStateStore();
    }

    internal string WorkspaceFolder => _workspaceFolder;
    internal string DetailStatePath => _detailStateStore.FilePath;
    internal string? SessionId => _sessionId;

    internal Task<DetailReviewFixtureOpenResult> ReopenAsync() =>
        OpenAsync(resetDetailState: false);

    internal async Task<DetailReviewFixtureOpenResult> OpenAsync(bool resetDetailState)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EndSessionCoreAsync().ConfigureAwait(false);
            if (resetDetailState)
                RemoveStateCore();

            var inboxMessage = SimulationInboxFixtureBuilder.BuildDetailReviewMessage();
            var plan = SimulationPlanFixtureBuilder.BuildDetailReviewPlan();
            var session = _simulationSessionManager.CreateSession("Markdown detail fixtures", "developer-menu");
            _sessionId = session.SessionId;

            try
            {
                await _simulationSessionManager.OverlayArtifactAsync(
                    session.SessionId,
                    SimulationSurfaceKind.Inbox,
                    "sim-inbox-detail-fixture",
                    inboxMessage.Subject,
                    inboxMessage).ConfigureAwait(false);
                await _simulationSessionManager.OverlayArtifactAsync(
                    session.SessionId,
                    SimulationSurfaceKind.Plan,
                    "sim-plan-detail-fixture",
                    plan.Title,
                    plan).ConfigureAwait(false);
            }
            catch
            {
                await EndSessionCoreAsync().ConfigureAwait(false);
                throw;
            }

            return new DetailReviewFixtureOpenResult(
                session.SessionId,
                inboxMessage,
                plan);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task CleanupAsync(bool removeState)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EndSessionCoreAsync().ConfigureAwait(false);
            if (removeState)
                RemoveStateCore();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal DetailReviewFixtureSectionKeys GetFixtureSectionKeySet()
    {
        var inbox = SimulationInboxFixtureBuilder.BuildDetailReviewMessage();
        var plan = SimulationPlanFixtureBuilder.BuildDetailReviewPlan();
        var task = plan.Tasks.Single(candidate => string.Equals(
            candidate.TaskId,
            SimulationPlanFixtureBuilder.DetailReviewTaskId,
            StringComparison.Ordinal));

        var inboxIdentity = MarkdownDetailIdentity.ForInboxMessage(
            _workspaceFolder,
            SimulationInboxFixtureBuilder.DetailReviewMessageId);
        var planIdentity = MarkdownDetailIdentity.ForPlanDescription(
            _workspaceFolder,
            SimulationPlanFixtureBuilder.DetailReviewPlanId);
        var taskIdentity = MarkdownDetailIdentity.ForTaskDescription(
            _workspaceFolder,
            SimulationPlanFixtureBuilder.DetailReviewPlanId,
            SimulationPlanFixtureBuilder.DetailReviewTaskId);

        return new DetailReviewFixtureSectionKeys(
            InboxLabelled: ResolveFixtureSectionKeyByBody(
                inboxIdentity,
                inbox.Body,
                SimulationInboxFixtureBuilder.DetailReviewExpandedBodyToken),
            InboxUnlabelled: ResolveFixtureSectionKeyByBody(
                inboxIdentity,
                inbox.Body,
                SimulationInboxFixtureBuilder.DetailReviewCollapsedBodyToken),
            PlanLabelled: ResolveFixtureSectionKeyByBody(
                planIdentity,
                plan.Summary,
                SimulationPlanFixtureBuilder.DetailReviewPlanExpandedBodyToken),
            PlanUnlabelled: ResolveFixtureSectionKeyByBody(
                planIdentity,
                plan.Summary,
                SimulationPlanFixtureBuilder.DetailReviewPlanCollapsedBodyToken),
            TaskLabelled: ResolveFixtureSectionKeyByBody(
                taskIdentity,
                task.Description,
                SimulationPlanFixtureBuilder.DetailReviewTaskExpandedBodyToken),
            TaskUnlabelled: ResolveFixtureSectionKeyByBody(
                taskIdentity,
                task.Description,
                SimulationPlanFixtureBuilder.DetailReviewTaskCollapsedBodyToken));
    }

    internal IReadOnlyList<string> GetFixtureSectionKeys()
    {
        var keys = GetFixtureSectionKeySet();
        return
        [
            keys.InboxLabelled,
            keys.InboxUnlabelled,
            keys.PlanLabelled,
            keys.PlanUnlabelled,
            keys.TaskLabelled,
            keys.TaskUnlabelled,
        ];
    }

    private void RemoveStateCore()
    {
        foreach (var key in GetFixtureSectionKeys())
        {
            var result = _detailStateStore.Remove(key);
            if (!result.Succeeded)
                throw new InvalidOperationException("Could not remove the fixture detail preference.", result.Error);
        }
    }

    private async Task EndSessionCoreAsync()
    {
        if (_sessionId is null)
            return;
        var sessionId = _sessionId;
        _sessionId = null;
        await _simulationSessionManager.EndSessionAsync(sessionId).ConfigureAwait(false);
    }

    private static IEnumerable<MarkdownDetailSection> ParseDetailSections(string markdown) =>
        MarkdownDetailParser.Parse(markdown.ReplaceLineEndings("\n").Split('\n'))
            .Where(region => region.Diagnostic is null)
            .Select(region => new MarkdownDetailSection(region.Label, region.Body));

    private static string ResolveFixtureSectionKeyByBody(
        MarkdownDetailIdentity identity,
        string markdown,
        string bodyToken)
    {
        var section = ParseDetailSections(markdown).Single(candidate =>
            candidate.Body.Contains(bodyToken, StringComparison.Ordinal));
        return identity.GetSectionKey(section.Label, section.Body);
    }
}

internal sealed record DetailReviewFixtureOpenResult(
    string SessionId,
    InboxMessage InboxMessage,
    Plan Plan);

internal sealed record DetailReviewFixtureSectionKeys(
    string InboxLabelled,
    string InboxUnlabelled,
    string PlanLabelled,
    string PlanUnlabelled,
    string TaskLabelled,
    string TaskUnlabelled);

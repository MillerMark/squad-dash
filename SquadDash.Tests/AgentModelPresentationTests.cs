using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentModelPresentationTests {
    private static readonly ModelProfile Astra = new("astra", "GPT 6 Astra", "copilot", null, "gpt-6-astra", null);

    [TestCase(" @LyRa-MoRn ", "lyra-morn", "lyra-morn")]
    [TestCase(null, " LyRa-MoRn ", "lyra-morn")]
    [TestCase(null, null, "LyRa-MoRn")]
    [Apartment(ApartmentState.STA)]
    public void DisplayedTranscriptHandleResolvesItsOverride_NotCoordinatorOrDisplayName(
        string? requestedHandle, string? cardKey, string cardHandle) {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var store = new ModelProfileStore(new ApplicationSettingsStore(settingsPath));
        var coordinator = new ModelProfile("default", "Copilot", "copilot", null, "coordinator-model", null, IsDefault: true);
        store.SaveProfiles([coordinator, Astra]);
        store.SaveAgentOverride("lyra-morn", Astra.Id);
        store.SaveAgentOverride("coordinator", coordinator.Id);
        var reloaded = new ModelProfileStore(new ApplicationSettingsStore(settingsPath));
        var thread = new TranscriptThreadState("lyra-history", TranscriptThreadKind.Agent, "Lyra Morn", DateTimeOffset.Now) {
            RequestedAgentHandle = requestedHandle,
            AgentCardKey = cardKey,
            AgentName = "Lyra Morn",
            AgentDisplayName = "Lyra Morn"
        };

        var handle = AgentModelPresentation.GetOverrideHandle(thread, cardHandle);
        var resolution = ModelProfileResolver.ResolveWithReason(
            reloaded.GetProfiles(), reloaded.GetCategoryAssignments(), handle,
            ModelProfileCategory.SpawnedNamedAgents, reloaded.GetAgentOverrides());

        Assert.Multiple(() => {
            Assert.That(handle, Is.EqualTo("lyra-morn"));
            Assert.That(resolution.Profile?.Alias, Is.EqualTo(Astra.Alias));
            Assert.That(resolution.Profile?.Model, Is.EqualTo(Astra.Model));
            Assert.That(resolution.Reason, Is.EqualTo(ModelProfileResolutionReason.Override));
            Assert.That(reloaded.GetDefaultProfile()?.Alias, Is.EqualTo("Copilot"));
        });
    }

    [TestCase(true, ModelProfileResolutionReason.CategoryAssignment, "Named agents")]
    [TestCase(false, ModelProfileResolutionReason.Default, "Copilot")]
    [Apartment(ApartmentState.STA)]
    public void DisplayedTranscriptWithoutOverrideUsesCategoryThenDefault(
        bool assignCategory, ModelProfileResolutionReason expectedReason, string expectedAlias) {
        var coordinator = new ModelProfile("default", "Copilot", "copilot", null, null, null, IsDefault: true);
        var category = Astra with { Id = "named", Alias = "Named agents" };
        var thread = new TranscriptThreadState("lyra-placeholder", TranscriptThreadKind.Agent, "Lyra Morn", DateTimeOffset.Now) {
            AgentCardKey = "lyra-morn",
            IsPlaceholderThread = true
        };
        var assignments = new Dictionary<string, string>();
        if (assignCategory)
            assignments[ModelProfileCategory.SpawnedNamedAgents] = category.Id;

        var handle = AgentModelPresentation.GetOverrideHandle(thread, null);
        var resolution = ModelProfileResolver.ResolveWithReason(
            [coordinator, category], assignments, handle, ModelProfileCategory.SpawnedNamedAgents,
            new Dictionary<string, string> { ["coordinator"] = coordinator.Id });

        Assert.Multiple(() => {
            Assert.That(handle, Is.EqualTo("lyra-morn"));
            Assert.That(resolution.Profile?.Alias, Is.EqualTo(expectedAlias));
            Assert.That(resolution.Reason, Is.EqualTo(expectedReason));
        });
    }

    [TestCase("Squad")]
    [TestCase("@lyra-morn")]
    [Apartment(ApartmentState.STA)]
    public void GetOverrideHandle_LeadAgentWithNullThread_ResolvesCoordinator(string cardHandle) {
        var handle = AgentModelPresentation.GetOverrideHandle(thread: null, agentCardHandle: cardHandle, isLeadAgent: true);
        Assert.That(handle, Is.EqualTo("coordinator"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void GetOverrideHandle_LeadAgentWithAgentThread_ResolvesCoordinator() {
        var thread = new TranscriptThreadState("lead-thread", TranscriptThreadKind.Agent, "Squad", DateTimeOffset.Now) {
            RequestedAgentHandle = "squad",
            AgentCardKey = "Squad"
        };
        Assert.That(AgentModelPresentation.GetOverrideHandle(thread, "Squad", isLeadAgent: true),
            Is.EqualTo("coordinator"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void GetOverrideHandle_CoordinatorThreadIgnoresStaleIdentity_ResolvesCoordinator() {
        var thread = new TranscriptThreadState("coord-thread", TranscriptThreadKind.Coordinator, "Stale Name", DateTimeOffset.Now) {
            RequestedAgentHandle = "@lyra-morn",
            AgentCardKey = "lyra-morn",
            AgentName = "Lyra Morn"
        };

        var handle = AgentModelPresentation.GetOverrideHandle(thread, agentCardHandle: "rai");
        Assert.That(handle, Is.EqualTo("coordinator"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void GetOverrideHandle_NamedCardWithNullThread_UsesNormalizedCardHandle() {
        var handle = AgentModelPresentation.GetOverrideHandle(thread: null, agentCardHandle: " @LyRa-MoRn ");
        Assert.That(handle, Is.EqualTo("lyra-morn"));
    }

    [TestCase(" @CoOrDiNaToR ", ModelProfileCategory.Coordinator)]
    [TestCase(" RAI ", ModelProfileCategory.RAI)]
    [TestCase(" Scribe ", ModelProfileCategory.Scribe)]
    [TestCase(" RaLpH ", ModelProfileCategory.Ralph)]
    [TestCase(" Fact-Checker ", ModelProfileCategory.FactChecker)]
    [TestCase(" @LyRa-MoRn ", ModelProfileCategory.SpawnedNamedAgents)]
    [Apartment(ApartmentState.STA)]
    public void GetProfileCategory_NormalizesCoordinatorBuiltinsAndNamedHandles(string handle, string expectedCategory) {
        var category = AgentModelPresentation.GetProfileCategory(handle);
        Assert.That(category, Is.EqualTo(expectedCategory));
    }

    [Test]
    public void MainAgentTitleUsesAgentTooltip_NotCoordinatorDefault() {
        var source = ReadSource("MainWindow.xaml.cs");
        var start = source.IndexOf("private void UpdateTranscriptThreadBadge(", StringComparison.Ordinal);
        var end = source.IndexOf("private void UpdateTransientTranscriptFooters(", start, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Multiple(() => {
            Assert.That(method, Does.Not.Contain("BuildCoordinatorTooltipText("));
            Assert.That(method.Split("BuildSecondaryTranscriptInfoTooltip("), Has.Length.EqualTo(3),
                "Both the pre-layout and normal-layout paths must use the shared agent tooltip.");
            Assert.That(method.Split("FindAgentCardForThread(thread), thread, baseText, relativeTime"), Has.Length.EqualTo(3),
                "Resolve the card from the displayed thread, not from the coordinator or another selection.");
            Assert.That(method, Does.Contain("thread.Kind == TranscriptThreadKind.Coordinator"));
            Assert.That(method, Does.Contain("ScanAndUpdateCoordinatorIntent();"));
        });

        var tooltipStart = source.IndexOf("private string BuildSecondaryTranscriptInfoTooltipText(", StringComparison.Ordinal);
        var tooltipEnd = source.IndexOf("private string BuildCoordinatorTooltipText(", tooltipStart, StringComparison.Ordinal);
        var tooltip = source[tooltipStart..tooltipEnd];
        Assert.Multiple(() => {
            Assert.That(tooltip, Does.Contain("GetThreadModelOverrideHandle(thread, agent)"));
            Assert.That(tooltip, Does.Contain("ResolveThreadModelProfile(thread, handle, category)"));
            Assert.That(tooltip, Does.Contain("resolution.Profile?.Alias"));
            Assert.That(tooltip, Does.Not.Contain("This run:").And.Not.Contain("Next launch:"));
            Assert.That(source, Does.Contain("AgentModelPresentation.GetOverrideHandle(thread, agent?.AccentStorageKey, agent?.IsLeadAgent == true)"));
        });
    }

    [Test]
    public void PrimaryTitleModelOverrideMenu_TargetsDisplayedThreadThroughSharedFactory() {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var title = XDocument.Parse(ReadSource("MainWindow.xaml")).Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == "TranscriptTitleTextBlock");
        var source = ReadSource("MainWindow.xaml.cs");
        var handler = SourceRegion(source,
            "private void TranscriptTitleTextBlock_PreviewMouseRightButtonUp(",
            "private ContextMenu? CreateAgentModelOverrideContextMenu(");
        var factory = SourceRegion(source,
            "private ContextMenu? CreateAgentModelOverrideContextMenu(", "private void OpenAgentModelOverrideDialog(");
        var secondaryHandler = SourceRegion(source,
            "titleBlock.PreviewMouseRightButtonUp +=", "var navUp = CreateSecondaryNavButton(");
        var accentMenu = SourceRegion(source,
            "private ContextMenu CreateAgentAccentContextMenu(", "// Model Override");
        var accentOverride = SourceRegion(source,
            "// Model Override", "if (primaryThread is not null &&");

        Assert.Multiple(() => {
            Assert.That((string?)title.Attribute("PreviewMouseRightButtonUp"),
                Is.EqualTo("TranscriptTitleTextBlock_PreviewMouseRightButtonUp"));
            Assert.That(handler, Does.Contain("_pendingPrimaryTranscriptVisualThread ?? _selectedTranscriptThread ?? CoordinatorThread"));
            Assert.That(handler, Does.Contain("thread.Kind == TranscriptThreadKind.Coordinator ? _leadAgent : FindAgentCardForThread(thread)"));
            Assert.That(handler, Does.Contain("CreateAgentModelOverrideContextMenu(agent, thread)"));
            Assert.That(handler, Does.Contain("menu.PlacementTarget = TranscriptTitleTextBlock"));
            Assert.That(handler, Does.Contain("menu.IsOpen = true"));
            Assert.That(secondaryHandler, Does.Contain("CreateAgentModelOverrideContextMenu(agent, thread)"));
            Assert.That(secondaryHandler, Does.Contain("menu.PlacementTarget = titleBlock"));
            Assert.That(secondaryHandler, Does.Contain("menu.IsOpen = true"));
            Assert.That(factory, Does.Contain("GetThreadModelOverrideHandle(thread, agent)"));
            Assert.That(factory, Does.Contain("MakeItem(\"Model Override\\u2026\")"));
            Assert.That(factory, Does.Contain("OpenAgentModelOverrideDialog(thread, handle, agent)"));
            Assert.That(accentMenu, Does.Contain("GetPrimaryThread(agentCard)"));
            Assert.That(accentOverride, Does.Contain("GetThreadModelOverrideHandle(primaryThread, agentCard)"));
            Assert.That(accentOverride, Does.Contain("OpenAgentModelOverrideDialog(primaryThread, overrideHandle, agentCard)"));
        });
    }

    [TestCase(
        "private ContextMenu? CreateAgentModelOverrideContextMenu(",
        "private void OpenAgentModelOverrideDialog(", "item", "thread")]
    [TestCase(
        "private ContextMenu CreateAgentAccentContextMenu(",
        "if (primaryThread is not null &&", "modelOverrideItem", "metricsThread")]
    public void WorkMetricsMenuItem_FollowsModelOverrideAndLaunchesThreadCurrentTurn(
        string startMarker, string endMarker, string overrideItem, string threadVariable) {
        var menu = SourceRegion(ReadSource("MainWindow.xaml.cs"), startMarker, endMarker);
        var additions = Regex.Matches(menu, @"menu\.Items\.Add\(([^;]+)\);")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Multiple(() => {
            Assert.That(menu, Does.Contain("MakeItem(\"Model Override\\u2026\")"));
            Assert.That(menu, Does.Contain("var workMetricsItem = MakeItem(\"Work Metrics\u2026\");"));
            Assert.That(additions.TakeLast(2), Is.EqualTo(new[] { overrideItem, "workMetricsItem" }),
                "Work Metrics must be the next menu item after Model Override, without a separator.");
            Assert.That(menu, Does.Contain(
                $"workMetricsItem.Click += (_, _) => OpenWorkMetricsForThread({threadVariable}, {threadVariable}.CurrentTurn?.StartedAt);"),
                "Resolve the current turn at click time; null keeps the existing all-thread-runs scope.");
        });
    }

    [Test]
    public void AccentWorkMetrics_PrefersPrimaryThreadThenSpinnerFallbackAndOmitsUnresolvedScope() {
        var source = ReadSource("MainWindow.xaml.cs");
        var menu = SourceRegion(source,
            "private ContextMenu CreateAgentAccentContextMenu(", "if (primaryThread is not null &&");
        var fallback = SourceRegion(menu, "var metricsThread =", "if (metricsThread is not null)");
        var guardedItem = SourceRegion(menu, "if (metricsThread is not null)", "menu.Items.Add(workMetricsItem);");

        Assert.Multiple(() => {
            Assert.That(menu, Does.Contain("var primaryThread = GetPrimaryThread(agentCard);"));
            Assert.That(fallback, Does.Match(
                @"primaryThread \?\? \(agentCard\.IsLeadAgent\s*\? CoordinatorThread\s*: agentCard\.Threads"));
            Assert.That(fallback, Does.Match(
                @"\.OrderByDescending\(candidate => candidate\.IsCurrentBackgroundRun\)\s*" +
                @"\.ThenByDescending\(candidate => candidate\.LastObservedActivityAt \?\? candidate\.StartedAt\)\s*" +
                @"\.FirstOrDefault\(\)"));
            Assert.That(guardedItem, Does.Match(
                "if \\(metricsThread is not null\\)\\s*\\{\\s*var workMetricsItem = MakeItem\\(\"Work Metrics\u2026\"\\);"));
            Assert.That(guardedItem, Does.Contain(
                "OpenWorkMetricsForThread(metricsThread, metricsThread.CurrentTurn?.StartedAt)"));
        });
    }

    [Test]
    public void CoordinatorDialogTooltipAndSdk_UseCoordinatorCategoryAndPersistedOverride() {
        var source = ReadSource("MainWindow.xaml.cs");
        var dialog = SourceRegion(source,
            "private void OpenAgentModelOverrideDialog(", "private string? ResolveEffectiveProfileAliasWithoutOverride(");
        var effective = SourceRegion(source,
            "private string? ResolveEffectiveProfileAliasWithoutOverride(", "private string? ResolveRouteProfileAlias(");
        var category = SourceRegion(source,
            "private static string ResolveNamedAgentProfileCategory(", "private static string? StripUrlScheme(");
        var tooltip = SourceRegion(source,
            "private string BuildCoordinatorTooltipText(", "private void TranscriptTitleTextBlock_PreviewMouseRightButtonUp(");
        var active = SourceRegion(source,
            "private ModelProfile? ResolveActiveProfile(", "private Plan? ResolveLatestPendingRevisionPreview(");
        var apply = SourceRegion(source,
            "private bool ApplyBridgeModelSettings(", "private void RestartBridgeForSettingsWhenIdle(");
        var request = SourceRegion(ReadSource("SquadSdkProcess.cs"),
            "private async Task RunPromptOnceAsync(", "private async Task RunBridgeRequestOnceAsync");

        Assert.Multiple(() => {
            Assert.That(dialog, Does.Contain("ResolveEffectiveProfileAliasWithoutOverride(agentHandle)"));
            Assert.That(dialog, Does.Contain("if (agentHandle == \"coordinator\")"));
            Assert.That(dialog, Does.Contain("ApplyBridgeModelSettings(_settingsStore.Load(), \"coordinator-model-override\")"));
            Assert.That(dialog, Does.Contain("else").And.Contain("RefreshBridgeNamedAgentRoutes()"));
            Assert.That(effective, Does.Contain("ResolveNamedAgentProfileCategory(agentHandle)"));
            Assert.That(effective, Does.Contain("ModelProfileResolver.ResolveWithReason(profiles, assignments, null, category)"));
            Assert.That(category, Does.Contain("AgentModelPresentation.GetProfileCategory(handle)"));
            Assert.That(tooltip, Does.Contain("ResolveThreadModelProfile(CoordinatorThread, \"coordinator\", ModelProfileCategory.Coordinator)"));
            Assert.That(tooltip, Does.Not.Contain("GetDefaultProfile()"));
            Assert.That(active, Does.Contain("agentHandle: \"coordinator\""));
            Assert.That(active, Does.Contain("agentCategory: ModelProfileCategory.Coordinator"));
            Assert.That(active, Does.Contain("agentOverrides: profileStore.GetAgentOverrides()"));
            Assert.That(apply, Does.Contain("currentActiveProfile = ResolveActiveProfile(snapshot)"));
            Assert.That(apply, Does.Contain("_bridge.ActiveProfile = currentActiveProfile"));
            Assert.That(apply, Does.Contain("DetachCurrentSessionForModelSettingsChange(reason)"));
            Assert.That(apply, Does.Contain("RestartBridgeForSettingsWhenIdle(reason)"));
            Assert.That(request, Does.Contain("Model: ResolveRequestModel(ActiveProfile?.Model ?? CopilotDefaultModel)"));
            Assert.That(request, Does.Contain("ProfileId: ActiveProfile?.Id"));
        });
    }

    private static string SourceRegion(string source, string startMarker, string endMarker) {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source[start..end];
    }

    private static string ReadSource(string name) {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SquadDash", name)))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        return File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", name));
    }
}

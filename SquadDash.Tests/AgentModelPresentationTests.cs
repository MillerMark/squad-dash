using System.IO;
using System.Threading;

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

    [Test]
    public void MainAgentTitleUsesAgentTooltip_NotCoordinatorDefault() {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "MainWindow.xaml.cs")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "MainWindow.xaml.cs"));
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
            Assert.That(source, Does.Contain("AgentModelPresentation.GetOverrideHandle(thread, agent?.AccentStorageKey)"));
        });
    }
}

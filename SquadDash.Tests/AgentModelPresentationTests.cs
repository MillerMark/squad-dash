using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentModelPresentationTests {
    private static readonly ModelProfile Astra = new("astra", "GPT 6 Astra", "copilot", null, "gpt-6-astra", null);

    [Test]
    public void OverrideDoesNotRelabelAnExistingRun() {
        var resolution = new ModelProfileResolutionResult(Astra, ModelProfileResolutionReason.Override, Astra.Id, Astra);

        var lines = AgentModelPresentation.BuildDetails("Copilot", "old-model", resolution);

        Assert.That(lines, Is.EqualTo(new[] {
            "This run: Copilot (old-model)",
            "Next launch: GPT 6 Astra (gpt-6-astra) (override)"
        }));
    }

    [Test]
    public void RuntimeModelIsShownEvenWhenProfileAliasWasNotReported() {
        var resolution = new ModelProfileResolutionResult(Astra, ModelProfileResolutionReason.Override, Astra.Id, Astra);

        var lines = AgentModelPresentation.BuildDetails(null, "gpt-6-astra", resolution);

        Assert.That(lines[0], Is.EqualTo("This run: gpt-6-astra"));
    }

    [Test]
    public void UnknownRuntimeDoesNotPretendToUseConfiguredProfile() {
        var resolution = new ModelProfileResolutionResult(Astra, ModelProfileResolutionReason.CategoryAssignment, null, null);

        var lines = AgentModelPresentation.BuildDetails(null, null, resolution);

        Assert.That(lines, Is.EqualTo(new[] {
            "This run: model not reported",
            "Next launch: GPT 6 Astra (gpt-6-astra)"
        }));
    }

    [Test]
    public void DisabledOverrideDoesNotLabelCategoryFallbackAsOverride() {
        var disabled = Astra with { IsEnabled = false };
        var fallback = new ModelProfile("default", "Copilot", "copilot", null, null, null, IsDefault: true);
        var resolution = ModelProfileResolver.ResolveWithReason(
            [disabled, fallback], null, disabled.Id, ModelProfileCategory.SpawnedNamedAgents);

        var lines = AgentModelPresentation.BuildDetails(null, null, resolution);

        Assert.That(lines[1], Is.EqualTo("Next launch: Copilot"));
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
        });
    }
}

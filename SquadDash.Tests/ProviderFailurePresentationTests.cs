using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Xml.Linq;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ProviderFailurePresentationTests
{
    [Test]
    public void Analyze_Azure404_ExplainsDeploymentSetting()
    {
        var result = ProviderFailurePresentation.Analyze(
            "Resource not found on provider at https://squaddash-resource.services.ai.azure.com/openai/v1 (HTTP 404).",
            new ProviderFailureContext(
                Model: "gpt-5.4-mini",
                ProfileAlias: "Profile 2",
                ProviderBaseUrl: "https://squaddash-resource.services.ai.azure.com/openai/v1",
                ProviderType: "openai",
                WireApi: "responses"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.DeploymentOrModel));
            Assert.That(result.Title, Is.EqualTo("Azure deployment not found"));
            Assert.That(result.Explanation, Does.Contain("Profile 2"));
            Assert.That(result.Explanation, Does.Contain("gpt-5.4-mini"));
            Assert.That(result.Guidance, Does.Contain("exact Azure deployment name"));
            Assert.That(result.ContextLine, Does.Contain("Protocol: responses"));
            Assert.That(result.RawError, Does.Contain("HTTP 404"));
        });
    }

    [TestCase("HTTP 401 Unauthorized: invalid API key", ProviderFailureCategory.Authentication)]
    [TestCase("HTTP 429 Too Many Requests: quota exceeded", ProviderFailureCategory.RateLimitOrQuota)]
    [TestCase("HTTP 404 Cannot POST /v1/responses", ProviderFailureCategory.EndpointOrProtocol)]
    [TestCase("Request timed out after 30 seconds", ProviderFailureCategory.Timeout)]
    [TestCase("getaddrinfo ENOTFOUND provider.example", ProviderFailureCategory.Network)]
    [TestCase("HTTP 400 invalid_request_error: unsupported parameter", ProviderFailureCategory.InvalidRequest)]
    [TestCase("ResponsibleAIPolicyViolation: content_filter", ProviderFailureCategory.ContentSafety)]
    [TestCase("HTTP 503 Service Unavailable", ProviderFailureCategory.ProviderService)]
    public void Analyze_ClassifiesCommonProviderFailures(string message, ProviderFailureCategory expected)
    {
        var result = ProviderFailurePresentation.Analyze(message);

        Assert.That(result.Category, Is.EqualTo(expected));
        Assert.That(result.Guidance, Is.Not.Empty);
        Assert.That(result.RawError, Is.EqualTo(message));
    }

    [Test]
    public void Analyze_UnknownFailure_PreservesRawErrorAndRedactsSecrets()
    {
        const string secret = "sk-abcdefghijklmnopqrstuvwxyz123456";
        var result = ProviderFailurePresentation.Analyze(
            $"Strange provider failure api-key={secret} detail=opaque");

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.Unknown));
            Assert.That(result.RawError, Does.Contain("Strange provider failure"));
            Assert.That(result.RawError, Does.Contain("[REDACTED]"));
            Assert.That(result.RawError, Does.Not.Contain(secret));
            Assert.That(result.Guidance, Does.Contain("complete raw error"));
        });
    }

    [Test]
    public void Analyze_ExplicitDeploymentNotFound_WorksWithoutEndpointContext()
    {
        var result = ProviderFailurePresentation.Analyze(
            "{\"error\":{\"code\":\"DeploymentNotFound\",\"message\":\"No deployment\"}}");

        Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.DeploymentOrModel));
        Assert.That(result.Title, Is.EqualTo("Azure deployment not found"));
    }

    [TestCase(ProviderFailureCategory.Authentication, true)]
    [TestCase(ProviderFailureCategory.DeploymentOrModel, true)]
    [TestCase(ProviderFailureCategory.EndpointOrProtocol, true)]
    [TestCase(ProviderFailureCategory.RateLimitOrQuota, false)]
    [TestCase(ProviderFailureCategory.Timeout, false)]
    [TestCase(ProviderFailureCategory.Network, false)]
    [TestCase(ProviderFailureCategory.ProviderService, false)]
    [TestCase(ProviderFailureCategory.Unknown, false)]
    public void RequiredPlanAgentFailure_OnlyHardConfigurationFailuresInterrupt(
        ProviderFailureCategory category,
        bool expected)
    {
        var result = ProviderFailureContinuationPolicy.ShouldInterruptRequiredPlanWork(
            category,
            coordinatorPromptRunning: true,
            planExecutionActive: true,
            assignedPlanTaskId: "MODELPROF-005",
            rosterIdentityVerified: true);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void RequiredPlanAgentFailure_DoesNotInterruptOptionalOrUnverifiedWork()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProviderFailureContinuationPolicy.ShouldInterruptRequiredPlanWork(
                ProviderFailureCategory.DeploymentOrModel, true, true, null, true), Is.False);
            Assert.That(ProviderFailureContinuationPolicy.ShouldInterruptRequiredPlanWork(
                ProviderFailureCategory.DeploymentOrModel, true, true, "MODELPROF-005", false), Is.False);
            Assert.That(ProviderFailureContinuationPolicy.ShouldInterruptRequiredPlanWork(
                ProviderFailureCategory.DeploymentOrModel, false, true, "MODELPROF-005", true), Is.False);
            Assert.That(ProviderFailureContinuationPolicy.ShouldInterruptRequiredPlanWork(
                ProviderFailureCategory.DeploymentOrModel, true, false, "MODELPROF-005", true), Is.False);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TranscriptBlock_RendersRawErrorAsThemedReadOnlyCodeBlock()
    {
        var failure = ProviderFailurePresentation.Analyze("HTTP 401 Unauthorized");
        var container = ProviderFailureTranscriptBlockFactory.Create(failure, "Lyra Morn");
        var border = (Border)container.Child;
        var panel = (StackPanel)border.Child;
        var codeBlock = panel.Children.OfType<TextBox>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(codeBlock.Text, Is.EqualTo(failure.RawError));
            Assert.That(codeBlock.IsReadOnly, Is.True);
            Assert.That(codeBlock.FontFamily.Source, Is.EqualTo("Consolas"));
            Assert.That(codeBlock.Tag, Is.EqualTo(ProviderFailureTranscriptBlockFactory.CodeBlockTag));
            Assert.That(border.ReadLocalValue(Border.BackgroundProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(border.ReadLocalValue(Border.BorderBrushProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(codeBlock.ReadLocalValue(Control.ForegroundProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Append_DoesNotReplaceOrMutateActiveStreamingTurn()
    {
        var thread = new TranscriptThreadState(
            "agent-1",
            TranscriptThreadKind.Agent,
            "Lyra Morn",
            DateTimeOffset.Now);
        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        var turn = new TranscriptTurnView(thread, "prompt", DateTimeOffset.Now, narrative, [narrative]);
        thread.CurrentTurn = turn;

        ProviderFailureTranscriptBlockFactory.Append(
            thread,
            ProviderFailurePresentation.Analyze("HTTP 503 Service Unavailable"),
            "Lyra Morn");

        Assert.Multiple(() =>
        {
            Assert.That(thread.CurrentTurn, Is.SameAs(turn));
            Assert.That(thread.CurrentTurn.ResponseTextBuilder.Length, Is.Zero);
            Assert.That(narrative.Blocks.Count, Is.Zero);
            Assert.That(thread.Document.Blocks.Count, Is.EqualTo(2));
            Assert.That(thread.Document.Blocks.LastBlock, Is.TypeOf<BlockUIContainer>());
        });
    }

    [Test]
    public void ErrorTheme_UsesDarkRedInLightThemeAndLightRedInDarkThemeWithHighContrast()
    {
        var lightText = ReadThemeColor("Light.xaml", ProviderFailureTranscriptBlockFactory.ForegroundResourceKey);
        var lightBackground = ReadThemeColor("Light.xaml", ProviderFailureTranscriptBlockFactory.BackgroundResourceKey);
        var darkText = ReadThemeColor("Dark.xaml", ProviderFailureTranscriptBlockFactory.ForegroundResourceKey);
        var darkBackground = ReadThemeColor("Dark.xaml", ProviderFailureTranscriptBlockFactory.BackgroundResourceKey);

        Assert.Multiple(() =>
        {
            Assert.That(lightText, Is.EqualTo("#420B0B"));
            Assert.That(darkText, Is.EqualTo("#FEA1A1"));
            Assert.That(ContrastRatio(lightText, lightBackground), Is.GreaterThanOrEqualTo(7.0));
            Assert.That(ContrastRatio(darkText, darkBackground), Is.GreaterThanOrEqualTo(7.0));
        });
    }

    private static string ReadThemeColor(string fileName, string resourceKey)
    {
        var themePath = FindRepositoryFile(Path.Combine("SquadDash", "Themes", fileName));
        var document = XDocument.Load(themePath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document
            .Descendants()
            .Single(element => string.Equals((string?)element.Attribute(x + "Key"), resourceKey, StringComparison.Ordinal))
            .Attribute("Color")!
            .Value;
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private static double ContrastRatio(string foreground, string background)
    {
        static double Luminance(string color)
        {
            var rgb = new[] { color[1..3], color[3..5], color[5..7] }
                .Select(component => int.Parse(component, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0)
                .Select(value => value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4))
                .ToArray();
            return (0.2126 * rgb[0]) + (0.7152 * rgb[1]) + (0.0722 * rgb[2]);
        }

        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}

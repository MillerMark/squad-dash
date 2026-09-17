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
    public void Analyze_DistinguishesRetryExhaustionFromQuotaThatRequiresAction()
    {
        var throttled = ProviderFailurePresentation.Analyze(
            "HTTP 429 tokens per minute rate limit exceeded");
        var quota = ProviderFailurePresentation.Analyze(
            "HTTP 429 insufficient_quota: billing credits exhausted");

        Assert.Multiple(() =>
        {
            Assert.That(throttled.Category, Is.EqualTo(ProviderFailureCategory.RateLimitOrQuota));
            Assert.That(throttled.Title, Does.Contain("persisted after retries"));
            Assert.That(throttled.Guidance, Does.Contain("rate window"));
            Assert.That(quota.Category, Is.EqualTo(ProviderFailureCategory.RateLimitOrQuota));
            Assert.That(quota.Title, Does.Contain("requires action"));
            Assert.That(quota.Explanation, Does.Contain("Waiting alone is not expected"));
        });
    }

    [Test]
    public void Analyze_Timeout_DoesNotMisdiagnoseTokenOrBudgetExhaustion()
    {
        var result = ProviderFailurePresentation.Analyze(
            "Timeout after 3600000ms waiting for session.idle");

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.Timeout));
            Assert.That(result.Title, Is.EqualTo("Provider request timed out"));
            Assert.That(result.Explanation, Does.Contain("does not indicate token or budget exhaustion"));
            Assert.That(result.Guidance, Does.Contain("preserved work"));
        });
    }

    [TestCase(ProviderFailureCategory.Timeout, "Interrupted")]
    [TestCase(ProviderFailureCategory.Authentication, "Failed")]
    [TestCase(ProviderFailureCategory.RateLimitOrQuota, "Failed")]
    public void RecoveryPolicy_MarksOnlyTimeoutAsRecoverableInterruption(
        ProviderFailureCategory category,
        string expectedStatus)
    {
        Assert.That(ProviderFailureRecoveryPolicy.BuildThreadStatus(category), Is.EqualTo(expectedStatus));
    }

    [Test]
    public void Analyze_CliStartupExit_DoesNotMisdiagnoseStderrWarningAsProfileFailure()
    {
        const string message = "Failed to connect to Copilot CLI: CLI server exited with code 1\n" +
                               "stderr: ExperimentalWarning: SQLite is an experimental feature";

        var result = ProviderFailurePresentation.Analyze(message);

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.AgentRuntime));
            Assert.That(result.Title, Does.Contain("runtime stopped"));
            Assert.That(result.Explanation, Does.Contain("Warning text").IgnoreCase);
            Assert.That(result.Guidance, Does.Contain("Retry once"));
            Assert.That(result.Guidance, Does.Contain("Squad Doctor"));
            Assert.That(result.Guidance, Does.Not.Contain("endpoint"));
            Assert.That(result.Guidance, Does.Not.Contain("credential"));
        });
    }

    [Test]
    public void Analyze_CopilotAuthenticationLookup503_IsAServiceFailureNotBadCredentials()
    {
        var result = ProviderFailurePresentation.Analyze(
            "GitHub Copilot authentication lookup failed with HTTP 503. GitHub could not complete credential verification.");

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.ProviderService));
            Assert.That(result.Title, Does.Contain("temporarily unavailable"));
            Assert.That(result.Guidance, Does.Contain("do not sign in again").IgnoreCase);
            Assert.That(result.Guidance, Does.Contain("do not").And.Contain("restart SquadDash").IgnoreCase);
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

    [Test]
    public void Analyze_UnavailableSessionModel_ShowsActualAndRequestedModels()
    {
        var result = ProviderFailurePresentation.Analyze(
            "Execution failed: Error: Model \"claude-sonnet-4.6\" is not available.",
            new ProviderFailureContext(
                Model: "claude-sonnet-4.6",
                RequestedModel: "auto"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Category, Is.EqualTo(ProviderFailureCategory.DeploymentOrModel));
            Assert.That(result.Explanation, Does.Contain("claude-sonnet-4.6"));
            Assert.That(result.ContextLine, Does.Contain("Model/deployment: claude-sonnet-4.6"));
            Assert.That(result.ContextLine, Does.Contain("Requested model: auto"));
        });
    }

    [TestCase(ProviderFailureCategory.Authentication, true)]
    [TestCase(ProviderFailureCategory.DeploymentOrModel, true)]
    [TestCase(ProviderFailureCategory.EndpointOrProtocol, true)]
    [TestCase(ProviderFailureCategory.RateLimitOrQuota, false)]
    [TestCase(ProviderFailureCategory.Timeout, false)]
    [TestCase(ProviderFailureCategory.Network, false)]
    [TestCase(ProviderFailureCategory.ProviderService, false)]
    [TestCase(ProviderFailureCategory.Unknown, false)]
    public void RequiredPlanAgentFailure_OnlyTerminalConfigurationFailuresInterrupt(
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

    [TestCase(ProviderFailureCategory.RateLimitOrQuota, true)]
    [TestCase(ProviderFailureCategory.Authentication, false)]
    [TestCase(ProviderFailureCategory.Timeout, false)]
    public void RequiredPlanAgentFailure_OnlyRateOrQuotaExhaustionPausesGracefully(
        ProviderFailureCategory category,
        bool expected)
    {
        var result = ProviderFailureContinuationPolicy.ShouldPauseRequiredPlanWork(
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
            Assert.That(ProviderFailureContinuationPolicy.ShouldPauseRequiredPlanWork(
                ProviderFailureCategory.RateLimitOrQuota, true, true, null, true), Is.False);
            Assert.That(ProviderFailureContinuationPolicy.ShouldPauseRequiredPlanWork(
                ProviderFailureCategory.RateLimitOrQuota, true, true, "MODELPROF-005", false), Is.False);
            Assert.That(ProviderFailureContinuationPolicy.ShouldPauseRequiredPlanWork(
                ProviderFailureCategory.RateLimitOrQuota, false, true, "MODELPROF-005", true), Is.False);
            Assert.That(ProviderFailureContinuationPolicy.ShouldPauseRequiredPlanWork(
                ProviderFailureCategory.RateLimitOrQuota, true, false, "MODELPROF-005", true), Is.False);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TranscriptBlock_RendersRawErrorAsThemedReadOnlyCodeBlock()
    {
        var failure = ProviderFailurePresentation.Analyze("HTTP 401 Unauthorized");
        var container = ProviderFailureTranscriptBlockFactory.Create(failure, "Lyra Morn", 14);
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
            "Lyra Morn",
            14);

        Assert.Multiple(() =>
        {
            Assert.That(thread.CurrentTurn, Is.SameAs(turn));
            Assert.That(thread.CurrentTurn.ResponseTextBuilder.Length, Is.Zero);
            Assert.That(narrative.Blocks.Count, Is.Zero);
            Assert.That(thread.Document.Blocks.Count, Is.EqualTo(2));
            Assert.That(thread.Document.Blocks.LastBlock, Is.TypeOf<BlockUIContainer>());
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Append_RepeatedFailureMovesOneUpdatedCardToTranscriptBottom()
    {
        var thread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Coordinator",
            DateTimeOffset.Now);
        var first = ProviderFailurePresentation.Analyze(
            "HTTP 429 rate limit exceeded. Request-ID first",
            new ProviderFailureContext(Model: "gpt-test", ProfileAlias: "Profile A"));
        var second = ProviderFailurePresentation.Analyze(
            "HTTP 429 rate limit exceeded. Request-ID second",
            new ProviderFailureContext(Model: "gpt-test", ProfileAlias: "Profile A"));

        ProviderFailureTranscriptBlockFactory.Append(thread, first, "Arjun Sen", 14);
        thread.Document.Blocks.Add(new Paragraph(new Run("newer transcript work")));
        ProviderFailureTranscriptBlockFactory.Append(thread, second, "Arjun Sen", 14);

        var failureCards = thread.Document.Blocks
            .Cast<Block>()
            .Where(ProviderFailureTranscriptBlockFactory.IsProviderFailureContainer)
            .ToArray();
        var latest = (ProviderFailureTranscriptBlockFactory.ProviderFailureTranscriptTag)
            ((BlockUIContainer)failureCards.Single()).Tag;
        Assert.Multiple(() =>
        {
            Assert.That(failureCards, Has.Length.EqualTo(1));
            Assert.That(thread.Document.Blocks.LastBlock, Is.SameAs(failureCards[0]));
            Assert.That(latest.Failure.RawError, Does.Contain("Request-ID second"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AppendAggregated_RelatedFailuresCreateOneNavigableCopyAllCard()
    {
        var thread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Coordinator",
            DateTimeOffset.Now);
        var first = ProviderFailurePresentation.Analyze(
            "HTTP 429 rate limit exceeded. Request-ID first",
            new ProviderFailureContext(Model: "gpt-test", ProfileAlias: "Profile A"));
        var second = ProviderFailurePresentation.Analyze(
            "HTTP 429 rate limit exceeded. Request-ID second",
            new ProviderFailureContext(Model: "gpt-test", ProfileAlias: "Profile A"));
        var startedAt = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

        ProviderFailureTranscriptBlockFactory.AppendAggregated(
            thread, first, "Temporary Agent (review-1)", 14, "review-operation", startedAt, "failure-1");
        ProviderFailureTranscriptBlockFactory.AppendAggregated(
            thread, second, "Temporary Agent (review-2)", 14, "review-operation", startedAt.AddSeconds(10), "failure-2");

        var failureCards = thread.Document.Blocks
            .Cast<Block>()
            .Where(ProviderFailureTranscriptBlockFactory.IsProviderFailureContainer)
            .Cast<BlockUIContainer>()
            .ToArray();
        var tag = (ProviderFailureTranscriptBlockFactory.ProviderFailureAggregateTranscriptTag)
            failureCards.Single().Tag;
        var buttons = ((StackPanel)((Border)failureCards.Single().Child).Child)
            .Children.OfType<StackPanel>()
            .Single(panel => panel.Orientation == Orientation.Horizontal)
            .Children.OfType<Button>()
            .ToArray();
        var copied = tag.GetCopyText();

        Assert.Multiple(() =>
        {
            Assert.That(failureCards, Has.Length.EqualTo(1));
            Assert.That(tag.Occurrences, Has.Count.EqualTo(2));
            Assert.That(tag.SelectedIndex, Is.EqualTo(1));
            Assert.That(copied, Does.Contain("Temporary Agent (review-1)"));
            Assert.That(copied, Does.Contain("Temporary Agent (review-2)"));
            Assert.That(copied, Does.Contain("Request-ID first"));
            Assert.That(copied, Does.Contain("Request-ID second"));
            Assert.That(buttons, Has.Length.EqualTo(2));
            Assert.That(buttons.All(button =>
                button.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue), Is.True);
        });

        tag.Move(-1);
        Assert.That(tag.SelectedIndex, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AppendAggregated_DifferentOperationScopesRemainSeparate()
    {
        var thread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Coordinator",
            DateTimeOffset.Now);
        var failure = ProviderFailurePresentation.Analyze(
            "HTTP 429 rate limit exceeded",
            new ProviderFailureContext(Model: "gpt-test", ProfileAlias: "Profile A"));

        ProviderFailureTranscriptBlockFactory.AppendAggregated(
            thread, failure, "Temporary Agent (one)", 14, "operation-1", DateTimeOffset.Now, "failure-1");
        ProviderFailureTranscriptBlockFactory.AppendAggregated(
            thread, failure, "Temporary Agent (two)", 14, "operation-2", DateTimeOffset.Now, "failure-2");

        Assert.That(thread.Document.Blocks
            .Cast<Block>()
            .Count(ProviderFailureTranscriptBlockFactory.IsProviderFailureContainer), Is.EqualTo(2));
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

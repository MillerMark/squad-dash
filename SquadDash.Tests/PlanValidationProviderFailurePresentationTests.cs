using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanValidationProviderFailurePresentationTests
{
    private const string CopilotServiceFailure =
        "GitHub Copilot authentication lookup failed with HTTP 503. " +
        "GitHub could not complete credential verification. " +
        "The saved Copilot sign-in may still be valid; wait briefly and retry.";

    [Test]
    public void BuildAttemptMessage_ExplainsServiceFailureAndPreservedWork()
    {
        var message = PlanValidationProviderFailurePresentation.BuildAttemptMessage(
            "Gate question and consolidation enforced",
            CopilotServiceFailure,
            willRetry: false);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("temporarily unavailable"));
            Assert.That(message, Does.Contain("GitHub could not verify"));
            Assert.That(message, Does.Contain("paused this validation"));
            Assert.That(message, Does.Contain("will not be repeated"));
            Assert.That(message, Does.Not.Contain("sign-in is required").IgnoreCase);
        });
    }

    [Test]
    public void BuildRecoveryReason_PreservesCauseAndSafeAction()
    {
        var reason = PlanValidationProviderFailurePresentation.BuildRecoveryReason(
            CopilotServiceFailure,
            attemptCount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("temporarily unavailable"));
            Assert.That(reason, Does.Contain("stopped 2 times"));
            Assert.That(reason, Does.Contain("Wait briefly and retry"));
            Assert.That(reason, Does.Contain("do not sign in again").IgnoreCase);
            Assert.That(reason, Does.Contain("do not").And.Contain("restart SquadDash").IgnoreCase);
        });
    }
}

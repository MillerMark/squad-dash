using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalOperatorGuidePolicyTests
{
    [TestCase("Follow the delivered Runbook.md Setup instructions to install the package.")]
    [TestCase("Follow the Inbox mixed-edit Open/Act steps, then invoke the feature.")]
    [TestCase("Restore the fixture through the kit's scoped cleanup instructions.")]
    [TestCase("Compare the result with the linked expected-after file.")]
    [TestCase("Save evidence in the declared capture location.")]
    public void Validate_RejectsDelegatedOrVagueRequiredProcedure(string text)
    {
        Assert.That(ApprovalOperatorGuidePolicy.Validate(text), Is.Not.Empty);
    }

    [Test]
    public void Validate_AllowsDirectActionsWithContextualArtifactLinks()
    {
        const string body = """
            1. Open [the sample solution](app://open-path:samples/App.sln) in Visual Studio.
            2. Invoke AiGenFromPrompt and enter `Update the counter`.
            3. Compare the editor with [the expected file](app://open-path:expected/Counter.razor).
            4. Restore [the baseline file](app://open-path:baseline/Counter.razor).
            """;

        Assert.That(ApprovalOperatorGuidePolicy.Validate(body), Is.Empty);
    }
}

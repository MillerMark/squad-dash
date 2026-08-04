namespace SquadDash.Tests;

[TestFixture]
internal sealed class UiExceptionChatPromptBuilderTests
{
    [Test]
    public void Build_RequiresEvidenceBeforeFrameworkBugConclusion()
    {
        var prompt = UiExceptionChatPromptBuilder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("diagnostic context"));
            Assert.That(prompt, Does.Contain("trace/code"));
            Assert.That(prompt, Does.Contain("Do not classify it as a harmless framework bug"));
            Assert.That(prompt, Does.Contain("input route"));
            Assert.That(prompt, Does.Contain("window-state transition"));
            Assert.That(prompt, Does.Contain("SquadDash event handlers"));
        });
    }
}

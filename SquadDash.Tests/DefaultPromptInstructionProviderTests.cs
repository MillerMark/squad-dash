namespace SquadDash.Tests;

[TestFixture]
internal sealed class DefaultPromptInstructionProviderTests
{
    [Test]
    public void InboxInstruction_SaysActionsAreDeferred_NotImmediateDelegation()
    {
        var instruction = new DefaultPromptInstructionProvider().Get().InboxMessage;

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("Inbox actions are deferred user choices"));
            Assert.That(instruction, Does.Contain("launch that agent with the native delegation/tool path"));
            Assert.That(instruction, Does.Not.Contain("Strongly encouraged"));
        });
    }

    [Test]
    public void CommitReportingInstruction_RequiresBareShortHash()
    {
        var instruction = new DefaultPromptInstructionProvider().Get().CommitReporting;

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("bare short commit hash"));
            Assert.That(instruction, Does.Contain("7 chars"));
            Assert.That(instruction, Does.Contain("git rev-parse --short HEAD"));
            Assert.That(instruction, Does.Contain("Do not construct a markdown hyperlink"));
        });
    }
}

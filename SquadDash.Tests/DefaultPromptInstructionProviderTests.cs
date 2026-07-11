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
    public void ArtifactInstruction_ProvidesOptionalFileEscapeHatch()
    {
        var instructions = new DefaultPromptInstructionProvider().Get();

        Assert.Multiple(() =>
        {
            Assert.That(instructions.ArtifactFiles, Does.Contain("SQUADDASH_ARTIFACT_JSON"));
            Assert.That(instructions.ArtifactFiles, Does.Contain(".squad/tmp/agent-artifacts/"));
            Assert.That(instructions.ArtifactFiles, Does.Contain("INBOX_MESSAGE_JSON_FILE"));
            Assert.That(instructions.InboxMessage, Does.Contain("Keep normal INBOX_MESSAGE_JSON as the default"));
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
            Assert.That(instruction, Does.Contain("APPROVAL_GROUP_JSON"));
            Assert.That(instruction, Does.Contain("feature group"));
            Assert.That(instruction, Does.Contain("{\"sha\":\"<7-char-hash>\",\"group\":\"<feature-group>\"}"));
            Assert.That(instruction, Does.Not.Contain("\\\"sha\\\""));
        });
    }

    [Test]
    public void SubAgentApprovalGroupInstruction_UsesReadableJsonExample()
    {
        var instruction = new DefaultPromptInstructionProvider().Get().SubAgentApprovalGroup;

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("APPROVAL_GROUP_JSON"));
            Assert.That(instruction, Does.Contain("{\"sha\":\"<7-char-hash>\",\"group\":\"<feature-group>\"}"));
            Assert.That(instruction, Does.Not.Contain("\\\"sha\\\""));
        });
    }
}

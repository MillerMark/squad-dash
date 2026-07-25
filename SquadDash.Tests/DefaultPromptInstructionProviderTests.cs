namespace SquadDash.Tests;

[TestFixture]
internal sealed class DefaultPromptInstructionProviderTests
{
    [Test]
    public void DecomposePlanningSpecification_ContainsCompleteSchemas()
    {
        var instruction = DecomposePlanningInstructions.LoadSpecification();
        Assert.That(instruction, Does.Contain("TASKS_JSON:"));
        Assert.That(instruction, Does.Contain("DECOMPOSE_DECISION_JSON:"));
        Assert.That(instruction, Does.Contain("DECOMPOSE_RECOVERY_JSON:"));
        Assert.That(instruction, Does.Contain("DECOMPOSE_STEP_RESULT_JSON"));
        Assert.That(instruction, Does.Contain("SEARCH-20260725-003"));
        Assert.That(instruction, Does.Contain("schema-version: 3"));
        Assert.That(instruction, Does.Contain("`tasks[].title`"));
        Assert.That(instruction, Does.Contain("\"title\": \"Introduce the search index abstraction\""));
    }

    [Test]
    public void EnsureMaterialized_UsesProvidedConfiguredSquadFolder()
    {
        var configuredSquadFolder = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "external-squad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = DecomposePlanningInstructions.EnsureMaterialized(configuredSquadFolder);
            Assert.That(path, Is.EqualTo(Path.Combine(configuredSquadFolder, "instructions", "decompose-planning.md")));
            Assert.That(File.ReadAllText(path), Does.Contain("schema-version: 3"));
            Assert.That(DecomposePlanningInstructions.BuildOrdinaryPromptPointer(path), Does.Contain(path));
        }
        finally
        {
            if (Directory.Exists(configuredSquadFolder)) Directory.Delete(configuredSquadFolder, recursive: true);
        }
    }

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

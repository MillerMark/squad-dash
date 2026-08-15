namespace SquadDash.Tests;

[TestFixture]
internal sealed class DefaultPromptInstructionProviderTests
{
    [Test]
    public void QuickReplyInstructions_DescribeRiskTonesWithoutMakingThemDecorative()
    {
        var instruction = new DefaultPromptInstructionProvider().Get().QuickReply;

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("optional `tone` field"));
            Assert.That(instruction, Does.Contain("\"warning\""));
            Assert.That(instruction, Does.Contain("\"destructive\""));
            Assert.That(instruction, Does.Contain("Never use tone merely to attract attention"));
        });
    }

    [Test]
    public void DecomposePlanningSpecification_ContainsCompleteSchemas()
    {
        var instruction = DecomposePlanningInstructions.LoadSpecification();
        Assert.That(instruction, Does.Contain("TASKS_JSON:"));
        Assert.That(instruction, Does.Contain("DECOMPOSE_DECISION_JSON:"));
        Assert.That(instruction, Does.Contain("DECOMPOSE_RECOVERY_JSON:"));
        Assert.That(instruction, Does.Contain("DECOMPOSE_STEP_RESULT_JSON"));
        Assert.That(instruction, Does.Contain("SEARCH-20260725-003"));
        Assert.That(instruction, Does.Contain("schema-version: 8"));
        Assert.That(instruction, Does.Contain("1–25 task objects"));
        Assert.That(instruction, Does.Contain("TASKS_JSON_FILE:"));
        Assert.That(instruction, Does.Contain("Never use `SQUADDASH_ARTIFACT_JSON` for a plan"));
        Assert.That(instruction, Does.Contain("`approvalGates[].gateId`"));
        Assert.That(instruction, Does.Contain("`approvalGates[].message`"));
        Assert.That(instruction, Does.Contain("`approvalGates[].afterTaskIds`"));
        Assert.That(instruction, Does.Contain("`approvalGates[].beforeTaskIds`"));
        Assert.That(instruction, Does.Contain("`tasks[].agentAssignments[].agentHandle`"));
        Assert.That(instruction, Does.Contain("`tasks[].agentAssignments[].role`"));
        Assert.That(instruction, Does.Contain("`tasks[].agentAssignments[].allowGenericChildren`"));
        Assert.That(instruction, Does.Contain("`tasks[].proofRequirements`"));
        Assert.That(instruction, Does.Contain("final `audit` validation"));
        Assert.That(instruction, Does.Contain("`tasks[].title`"));
        Assert.That(instruction, Does.Contain("\"title\": \"Introduce the search index abstraction\""));
        Assert.That(instruction, Does.Contain("Never claim that a plan was staged"));
        Assert.That(instruction, Does.Contain("Submitting the plan proposal now."));
        Assert.That(instruction, Does.Contain("## STAGED_PLAN_REVISION_JSON schema"));
        Assert.That(instruction, Does.Contain("one compact delta instead of repeating the complete task graph"));
        Assert.That(instruction, Does.Contain("Allowed operations are `updatePlan`, `editPlanText`, `updateTask`, `editTaskText`"));
        Assert.That(instruction, Does.Contain("`/assertions/2`"));
        Assert.That(instruction, Does.Contain("oldText` that occurs exactly once"));
        Assert.That(instruction, Does.Contain("short revision-local `targetRef`"));
        Assert.That(instruction, Does.Contain("outline's `revisionStrategy`"));
        Assert.That(instruction, Does.Contain("Array-valued fields in a patch replace"));
        Assert.That(instruction, Does.Contain("rejects a stale `baseRevision`"));
        Assert.That(instruction, Does.Contain("`PLAN_REVISION_JSON` for a staged proposal"));
    }

    [Test]
    public void PendingPlanContext_UsesCompactDelegationEnvelopeAndHostOwnedProtocolPointer()
    {
        var squadFolder = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "pending-context-" + Guid.NewGuid().ToString("N"));
        try
        {
            var group = new DecomposedTaskGroup(
                "CONTEXT-20260814", "Context plan", "feature/context-plan", "Context summary",
                [new DecomposedSubTask("CONTEXT-20260814-001", "First task", [], "high", "First task")]);
            var pending = new PendingDecomposePlanStore(squadFolder).Save(group);

            var context = DecomposePlanningInstructions.BuildPendingPlanContext(squadFolder);

            Assert.Multiple(() =>
            {
                Assert.That(context, Does.Contain("STAGED_PLAN_REVISION_REQUEST_JSON:"));
                Assert.That(context, Does.Contain(pending.Revision));
                Assert.That(context, Does.Contain(".squad/tmp/decompose/CONTEXT-20260814.json"));
                Assert.That(context, Does.Contain(".squad/instructions/decompose-planning.md"));
                Assert.That(context, Does.Contain("The envelope is not an\noutput"));
                Assert.That(context, Does.Contain("must not appear in the final response"));
                Assert.That(context, Does.Contain("collected and applied directly by SquadDash"));
                Assert.That(context, Does.Contain("do not extract or re-emit Rory's JSON"));
                Assert.That(context, Does.Contain("`staged_plan_revision_request` argument"));
                Assert.That(context, Does.Contain("first-class tool metadata"));
                Assert.That(context, Does.Contain("small mechanical change"));
                Assert.That(context, Does.Contain("The user may refer conversationally"));
                Assert.That(context, Does.Contain("Never require the user to provide a groupId"));
                Assert.That(context, Does.Contain("T1`, `T2`, `G1`, and `V1"));
                Assert.That(context, Does.Contain("sizeClass=small"));
                Assert.That(context, Does.Contain("outline=.squad/tmp/decompose/outlines/CONTEXT-20260814.json"));
                Assert.That(context, Does.Not.Contain("\"operations\""));
            });
        }
        finally
        {
            if (Directory.Exists(squadFolder)) Directory.Delete(squadFolder, recursive: true);
        }
    }

    [Test]
    public void PendingPlanOutline_UsesShortReferencesAndCompactDependencyBoundaries()
    {
        var squadFolder = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "pending-outline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var group = new DecomposedTaskGroup(
                "OUTLINE-20260814", "Outline plan", "feature/outline", "Keep revision context compact",
                [
                    new DecomposedSubTask("OUTLINE-20260814-001", "First detailed contract", [], "high", "Build contracts",
                        Outputs: [new DecomposedTaskOutput("contracts", "Reusable contracts")]),
                    new DecomposedSubTask("OUTLINE-20260814-002", "Second detailed implementation", ["OUTLINE-20260814-001"], "high", "Implement feature",
                        Inputs: ["contracts"]),
                    new DecomposedSubTask("OUTLINE-20260814-003", "Third detailed verification", ["OUTLINE-20260814-002"], "high", "Verify feature"),
                ],
                ApprovalGates:
                [
                    new DecomposedGate("review-contracts", "Review contracts", ["OUTLINE-20260814-001"], ["OUTLINE-20260814-002"])
                ],
                Validations:
                [
                    new DecomposedValidationNode("OUTLINE-20260814-VAL-001", "Feature boundary", "Validate feature",
                        ["OUTLINE-20260814-002"], ["OUTLINE-20260814-003"], ["The feature boundary holds"])
                ]);
            var pending = new PendingDecomposePlanStore(squadFolder).Save(group);

            var outline = StagedPlanRevisionOutlineStore.Build(pending);
            var serialized = System.Text.Json.JsonSerializer.Serialize(outline);

            Assert.Multiple(() =>
            {
                Assert.That(outline.Tasks.Select(task => task.Ref), Is.EqualTo(new[] { "T1", "T2", "T3" }));
                Assert.That(outline.Tasks[0].Downstream, Is.EqualTo(new[] { "T2" }));
                Assert.That(outline.Tasks[1].DependsOn, Is.EqualTo(new[] { "T1" }));
                Assert.That(outline.Tasks[0].RelatedGates, Is.EqualTo(new[] { "G1" }));
                Assert.That(outline.Tasks[1].RelatedValidations, Is.EqualTo(new[] { "V1" }));
                Assert.That(outline.ApprovalGates.Single().After, Is.EqualTo(new[] { "T1" }));
                Assert.That(outline.Validations.Single().Before, Is.EqualTo(new[] { "T3" }));
                Assert.That(outline.Tasks[1].Source, Is.EqualTo("Group.tasks[1]"));
                Assert.That(serialized, Does.Not.Contain("OUTLINE-20260814-VAL-001"));
                Assert.That(serialized, Does.Not.Contain("OUTLINE-20260814-002"));
            });
        }
        finally
        {
            if (Directory.Exists(squadFolder)) Directory.Delete(squadFolder, recursive: true);
        }
    }

    [Test]
    public void PendingPlanOutline_ClassifiesLargePlanWithoutCopyingItsDescription()
    {
        var group = new DecomposedTaskGroup(
            "LARGEPLAN-20260814", "Large plan", "feature/large-plan", "Large revision",
            [new DecomposedSubTask("LARGEPLAN-20260814-001", new string('x', 40_000), [], "high", "Large task")]);
        var pending = new PendingDecomposePlan(PendingDecomposePlanStore.ComputeRevision(group), group);

        var outline = StagedPlanRevisionOutlineStore.Build(pending);
        var serialized = System.Text.Json.JsonSerializer.Serialize(outline);

        Assert.Multiple(() =>
        {
            Assert.That(outline.SizeClass, Is.EqualTo("large"));
            Assert.That(outline.RevisionStrategy, Does.Contain("near-total rewrite"));
            Assert.That(outline.Tasks.Single().DescriptionChars, Is.EqualTo(40_000));
            Assert.That(serialized.Length, Is.LessThan(3_000));
            Assert.That(serialized, Does.Not.Contain(new string('x', 500)));
        });
    }

    [Test]
    public void PendingPlanOutline_UsesAdaptiveRevisionStrategyForMediumPlan()
    {
        var group = new DecomposedTaskGroup(
            "MEDIUMPLAN-20260814", "Medium plan", "feature/medium-plan", "Medium revision",
            [new DecomposedSubTask("MEDIUMPLAN-20260814-001", new string('m', 16_000), [], "high", "Medium task")]);
        var pending = new PendingDecomposePlan(PendingDecomposePlanStore.ComputeRevision(group), group);

        var outline = StagedPlanRevisionOutlineStore.Build(pending);

        Assert.Multiple(() =>
        {
            Assert.That(outline.SizeClass, Is.EqualTo("medium"));
            Assert.That(outline.RevisionStrategy, Does.Contain("Strongly prefer a delta"));
            Assert.That(outline.RevisionStrategy, Does.Contain("sweeping graph rewrite"));
        });
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
            Assert.That(File.ReadAllText(path), Does.Contain("schema-version: 8"));
            var pointer = DecomposePlanningInstructions.BuildOrdinaryPromptPointer(path);
            Assert.That(pointer, Does.Contain(path));
            Assert.That(pointer, Does.Contain("Never claim that it was staged"));
            Assert.That(pointer, Does.Contain("complete replacement TASKS_JSON with the same groupId"));
            Assert.That(pointer, Does.Contain("PLAN_REVISION_JSON is only for an accepted durable plan"));
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

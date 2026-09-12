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
        Assert.That(instruction, Does.Contain("schema-version: 9"));
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
        Assert.That(instruction, Does.Contain("Each task has exactly one accountable primary agent"));
        Assert.That(instruction, Does.Contain("create a separate dependent task assigned"));
        Assert.That(instruction, Does.Contain("Do not encode required execution only through prose"));
        Assert.That(instruction, Does.Contain("subordinate read-only research helpers"));
        Assert.That(instruction, Does.Contain("`tasks[].proofRequirements`"));
        Assert.That(instruction, Does.Contain("`tasks[].contractExamples`"));
        Assert.That(instruction, Does.Contain("minimal normative example"));
        Assert.That(instruction, Does.Contain("Use `contractExamples` proactively"));
        Assert.That(instruction, Does.Contain("by all means author a grounded example"));
        Assert.That(instruction, Does.Contain("plausible wrong interpretation"));
        Assert.That(instruction, Does.Contain("high-value candidates for focused automated test coverage"));
        Assert.That(instruction, Does.Contain("not an additional validation node, approval gate"));
        Assert.That(instruction.ReplaceLineEndings(" "),
            Does.Contain("Never refer to content that is nonexistent or otherwise unavailable"));
        Assert.That(instruction, Does.Contain("prefer one focused clarifying question"));
        Assert.That(instruction, Does.Contain("absence of examples is never itself a validation error"));
        Assert.That(instruction, Does.Contain("at most seven human checks"));
        Assert.That(instruction, Does.Contain("An `audit` is optional"));
        Assert.That(instruction, Does.Contain("does **not** need a separate final"));
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
        Assert.That(instruction, Does.Contain("never run `squad upgrade --help`"));
    }

    [Test]
    public void DecomposePlanningSpecification_PreventsHumanOnlyTaskShellsBeforeValidation()
    {
        var instruction = DecomposePlanningInstructions.LoadSpecification()
            .ReplaceLineEndings(" ");

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("invalid human-only task shell"));
            Assert.That(instruction, Does.Match("do not ask an\\s+AI worker to perform, simulate, or claim those observations"));
            Assert.That(instruction, Does.Contain("Bad: `Perform the live Visual Studio smoke test`"));
            Assert.That(instruction, Does.Contain("Good: `Prepare the Visual Studio smoke-test kit`"));
            Assert.That(instruction, Does.Contain("plain-language, step-by-step instructions"));
            Assert.That(instruction, Does.Match("SquadDash\\s+Inbox"));
            Assert.That(instruction, Does.Contain("**AI-reachable:**"));
            Assert.That(instruction, Does.Contain("**Human-only:**"));
            Assert.That(instruction, Does.Contain("**Mixed:**"));
            Assert.That(instruction, Does.Contain("manual-test-kit preparation task is complete only when"));
            Assert.That(instruction, Does.Contain("full usable instructions in the Inbox body"));
            Assert.That(instruction, Does.Contain("[Open fixture folder](app://open-path:"));
            Assert.That(instruction, Does.Contain("app://open-path:"));
            Assert.That(instruction, Does.Contain("do not replace links embedded beside the relevant steps"));
            Assert.That(instruction, Does.Contain("same proof requirement IDs and fixture outputs"));
        });
    }

    [Test]
    public void DecomposePlanningSpecification_DocumentsEverySerializedPlanProperty()
    {
        var instruction = DecomposePlanningInstructions.LoadSpecification();

        AssertSerializedProperties<DecomposedTaskGroup>(instruction, string.Empty, nameof(DecomposedTaskGroup.HostRevision));
        AssertSerializedProperties<DecomposedSubTask>(instruction, "tasks[]");
        AssertSerializedProperties<DecomposedAgentAssignment>(instruction, "tasks[].agentAssignments[]");
        AssertSerializedProperties<DecomposedTaskOutput>(instruction, "tasks[].outputs[]");
        AssertSerializedProperties<DecomposedTaskContractExample>(instruction, "tasks[].contractExamples[]");
        AssertSerializedProperties<DecomposedTaskProofRequirement>(instruction, "tasks[].proofRequirements[]");
        AssertSerializedProperties<HumanVerificationRecipe>(
            instruction,
            "tasks[].proofRequirements[].verificationRecipe");
        AssertSerializedProperties<DecomposedGate>(instruction, "approvalGates[]");
        AssertSerializedProperties<DecomposedValidationNode>(instruction, "validations[]");
    }

    private static void AssertSerializedProperties<T>(
        string instruction,
        string prefix,
        params string[] excludedProperties)
    {
        var excluded = excludedProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var property in typeof(T).GetProperties().Where(value => !excluded.Contains(value.Name)))
        {
            var attribute = property.GetCustomAttributes(
                    typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), inherit: false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                .Single();
            var path = string.IsNullOrEmpty(prefix)
                ? attribute.Name
                : $"{prefix}.{attribute.Name}";
            Assert.That(instruction, Does.Contain($"`{path}`"),
                $"The live planning specification must document serialized property {typeof(T).Name}.{property.Name} as {path}.");
        }
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
                Assert.That(context, Does.Contain("tmp\\host-instructions\\decompose-planning"));
                Assert.That(context, Does.Contain("decompose-planning.md"));
                Assert.That(context, Does.Contain("The envelope is not an"));
                Assert.That(context, Does.Contain("output protocol"));
                Assert.That(context, Does.Contain("must not appear in the final response"));
                Assert.That(context, Does.Contain("collected and applied directly by SquadDash"));
                Assert.That(context, Does.Contain("do not extract or re-emit Rory's JSON"));
                Assert.That(context, Does.Contain("`staged_plan_revision_request` argument"));
                Assert.That(context, Does.Contain("first-class tool metadata"));
                Assert.That(context, Does.Contain("small mechanical change"));
                Assert.That(context, Does.Contain("The user may refer conversationally"));
                Assert.That(context, Does.Contain("brand-new, independent, fresh, from-scratch"));
                Assert.That(context, Does.Contain("do not inspect or reuse a"));
                Assert.That(context, Does.Contain("derive the new proposal only from the current request"));
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
        var centralRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "central-instructions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = DecomposePlanningInstructions.EnsureMaterialized(configuredSquadFolder, centralRoot);
            Assert.That(path, Does.StartWith(Path.Combine(
                configuredSquadFolder,
                "tmp",
                "host-instructions",
                "decompose-planning")));
            Assert.That(File.ReadAllText(path), Does.Contain("schema-version: 9"));
            Assert.That(
                File.ReadAllText(Path.Combine(configuredSquadFolder, "instructions", "decompose-planning.md")),
                Does.Contain("schema-version: 9"));
            var decisionsPath = Path.Combine(configuredSquadFolder, "decisions.md");
            var pointer = DecomposePlanningInstructions.BuildOrdinaryPromptPointer(
                path,
                decisionsPath: decisionsPath);
            Assert.That(pointer, Does.Contain(path));
            Assert.That(pointer, Does.Contain(decisionsPath));
            Assert.That(pointer, Does.Contain("do not assume it is under the repository root"));
            Assert.That(pointer, Does.Contain("Never claim that it was staged"));
            Assert.That(pointer, Does.Contain("complete replacement TASKS_JSON with the same groupId"));
            Assert.That(pointer, Does.Contain("PLAN_REVISION_JSON is only for an accepted durable plan"));
        }
        finally
        {
            if (Directory.Exists(configuredSquadFolder)) Directory.Delete(configuredSquadFolder, recursive: true);
            if (Directory.Exists(centralRoot)) Directory.Delete(centralRoot, recursive: true);
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
    public void InboxInstruction_DefinesExistingProtocolAsManualTestKitDelivery()
    {
        var instruction = new DefaultPromptInstructionProvider().Get().InboxMessage;

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("manual test kit"));
            Assert.That(instruction, Does.Contain("Inbox delivery is required"));
            Assert.That(instruction, Does.Contain("numbered Setup, Open, and Act steps"));
            Assert.That(instruction, Does.Contain("stable human-proof requirement ID"));
            Assert.That(instruction, Does.Contain("{{approval-check:requirement-id}}"));
            Assert.That(instruction, Does.Contain("live checkbox shared with the approval Inbox item and transcript card"));
            Assert.That(instruction, Does.Contain("exact reproducible target identity"));
            Assert.That(instruction, Does.Contain("established regression checks may use the user's familiar existing files"));
            Assert.That(instruction, Does.Contain("At the exact step"));
            Assert.That(instruction, Does.Contain("app://open-path:path/from/workspace/root"));
            Assert.That(instruction, Does.Contain("active SquadDash workspace directory"));
            Assert.That(instruction, Does.Contain("not from the Git repository root"));
            Assert.That(instruction, Does.Contain("resolve and verify every linked file or folder"));
            Assert.That(instruction, Does.Contain("use an absolute path"));
            Assert.That(instruction, Does.Contain("file-or-folder"));
            Assert.That(instruction, Does.Contain("INBOX_MESSAGE_JSON_FILE"));
        });
    }

    [Test]
    public void PlanExecutionInstruction_AllowsRequiredInboxDeliverableWithoutWeakeningResultProtocol()
    {
        var instruction = SquadInstallerService.LoadEmbeddedLoopMarkdownPublic("loop-executing-plan.md");

        Assert.That(instruction, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("approved task explicitly requires Inbox delivery"));
            Assert.That(instruction, Does.Contain("not a second result payload"));
            Assert.That(instruction, Does.Contain("Do not claim the task complete"));
            Assert.That(instruction, Does.Contain("stable proof IDs"));
            Assert.That(instruction, Does.Contain("active SquadDash workspace directory"));
            Assert.That(instruction, Does.Contain("verify every target exists"));
            Assert.That(instruction, Does.Contain("absolute path"));
            Assert.That(instruction, Does.Contain("{{approval-check:requirement-id}}"));
            Assert.That(instruction, Does.Contain("contract examples"));
            Assert.That(instruction, Does.Contain("single assigned primary owns every worker-produced output and proof"));
            Assert.That(instruction, Does.Contain("do not claim that unlaunched agent performed it"));
            Assert.That(instruction.ReplaceLineEndings(" "),
                Does.Contain("strong candidates for focused regression tests"));
            Assert.That(instruction, Does.Contain("Do not create a ceremonial"));
            Assert.That(instruction, Does.Contain("DECOMPOSE_STEP_RESULT_JSON:"));
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

using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanProofContractParserTests
{
    private static DecomposedTaskProofRequirement[] HumanProofs(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new DecomposedTaskProofRequirement(
                $"human-check-{index}",
                "human-observation",
                $"Observe acceptance outcome {index}.",
                $"Does acceptance outcome {index} pass?"))
            .ToArray();

    private static DecomposedTaskGroup MakeGroup(bool includeAudit)
    {
        var task = new DecomposedSubTask(
            "PROOF-20260803-001",
            "Run a rendered UI scenario and observe the result.",
            [], "high", "Run live scenario",
            AgentRoutingMode: "generic",
            GenericAgentReason: "Test fixture.",
            ProofRequirements:
            [
                new DecomposedTaskProofRequirement(
                    "live-ui", "live-ui-observation", "Observe the rendered UI."),
            ]);
        var audits = includeAudit
            ? new[]
            {
                new DecomposedValidationNode(
                    "PROOF-20260803-VAL-001", "Audit completion", "Compare actual proof to the contract.",
                    [task.Id], [], ["The live UI was genuinely observed."], Mode: "audit"),
            }
            : null;
        return new DecomposedTaskGroup(
            "PROOF-20260803", "Proof Contract", "feature/proof-contract",
            "Require typed proof with an optional audit.", [task], Validations: audits);
    }

    [Test]
    public void ProofBearingPlan_WithoutFinalAudit_IsAccepted()
    {
        var text = "TASKS_JSON:\n" + JsonSerializer.Serialize(MakeGroup(false));
        Assert.That(TasksJsonParser.TryParse(text, out var parsed), Is.True);
        Assert.That(parsed?.Validations, Is.Null);
    }

    [Test]
    public void ProofBearingPlan_WithLeafCoveringAudit_IsAccepted()
    {
        var text = "TASKS_JSON:\n" + JsonSerializer.Serialize(MakeGroup(true));
        Assert.That(TasksJsonParser.TryParse(text, out var parsed), Is.True);
        Assert.That(parsed?.Validations?.Single().Mode, Is.EqualTo("audit"));
    }

    [Test]
    public void RoutedTerminalHumanProof_WithoutFinalAudit_ReparsesForCollection()
    {
        var original = MakeGroup(false);
        var humanProof = new DecomposedTaskProofRequirement(
            "live-ui", "human-observation", "Observe the rendered UI.",
            "Does the rendered UI show the expected result?");
        var authored = original with
        {
            Tasks =
            [
                original.Tasks[0] with
                {
                    ExecutionMode = PlanTaskExecutionMode.Verification,
                    ProofRequirements = [humanProof],
                },
            ],
        };

        Assert.That(TasksJsonParser.TryParse(
            "TASKS_JSON:\n" + JsonSerializer.Serialize(authored), out var routed, out var firstDiagnostic),
            Is.True, firstDiagnostic?.Message);
        Assert.That(routed!.Tasks.Single().ProofRequirements, Is.Null);
        Assert.That(routed.ApprovalGates?.Single().ProofRequirements?.Single().RequirementId,
            Is.EqualTo(humanProof.RequirementId));

        var reparsed = TasksJsonParser.TryParse(
            "TASKS_JSON:\n" + JsonSerializer.Serialize(routed), out _, out var secondDiagnostic);

        Assert.That(reparsed, Is.True, secondDiagnostic?.Message);
    }

    [Test]
    public void HumanProofCheckpoint_WithSevenChecks_IsAccepted()
    {
        var original = MakeGroup(false);
        var authored = original with
        {
            Tasks = [original.Tasks[0] with { ProofRequirements = HumanProofs(7) }],
        };

        var parsed = TasksJsonParser.TryParse(
            "TASKS_JSON:\n" + JsonSerializer.Serialize(authored),
            out var routed,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, diagnostic?.Message);
            Assert.That(routed?.ApprovalGates?.Single().ProofRequirements, Has.Count.EqualTo(7));
        });
    }

    [Test]
    public void TaskHumanProofCheckpoint_WithEightChecks_IsRejected()
    {
        var original = MakeGroup(false);
        var authored = original with
        {
            Tasks = [original.Tasks[0] with { ProofRequirements = HumanProofs(8) }],
        };

        var parsed = TasksJsonParser.TryParse(
            "TASKS_JSON:\n" + JsonSerializer.Serialize(authored),
            out _,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(diagnostic?.Code, Is.EqualTo("too-many-human-proof-requirements"));
            Assert.That(diagnostic?.Message, Does.Contain("at most 7"));
        });
    }

    [Test]
    public void ExplicitHumanProofGate_WithEightChecks_IsRejected()
    {
        var original = MakeGroup(false);
        var task = original.Tasks[0] with { ProofRequirements = null };
        var authored = original with
        {
            Tasks = [task],
            ApprovalGates =
            [
                new DecomposedGate(
                    "PROOF-20260803-GATE-001",
                    "Review the acceptance outcomes.",
                    [task.Id],
                    [],
                    HumanProofs(8),
                    "Do all listed acceptance outcomes pass?"),
            ],
        };

        var parsed = TasksJsonParser.TryParse(
            "TASKS_JSON:\n" + JsonSerializer.Serialize(authored),
            out _,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(diagnostic?.Code, Is.EqualTo("too-many-human-proof-requirements"));
            Assert.That(diagnostic?.Message, Does.Contain("at most 7"));
        });
    }

    [Test]
    public void OptionalAudit_WithWrongBoundary_ReturnsExpectedAndActualLeafIds()
    {
        var original = MakeGroup(true);
        var finalTask = new DecomposedSubTask(
            "PROOF-20260803-002",
            "Consume the live observation and finish the proof.",
            [original.Tasks[0].Id], "high", "Finish proof",
            AgentRoutingMode: "generic",
            GenericAgentReason: "Test fixture.");
        var invalid = original with { Tasks = [original.Tasks[0], finalTask] };
        var text = "TASKS_JSON:\n" + JsonSerializer.Serialize(invalid);

        var parsed = TasksJsonParser.TryParse(text, out _, out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(diagnostic?.Code, Is.EqualTo("invalid-proof-completion-audit"));
            Assert.That(diagnostic?.Message, Does.Contain("Expected leaf task IDs: [PROOF-20260803-002]"));
            Assert.That(diagnostic?.Message, Does.Contain("actual afterTaskIds: [PROOF-20260803-001]"));
        });
    }

    [Test]
    public void ProofBearingRevision_FinalAuditCoversReplacementLeaf_NotSupersededParent()
    {
        var original = MakeGroup(true);
        var parent = original.Tasks[0];
        var firstReplacement = new DecomposedSubTask(
            "PROOF-20260803-002",
            "Capture the live observation using the approved production path.",
            [], "high", "Capture observation",
            ParentTaskId: parent.Id,
            AgentRoutingMode: "generic",
            GenericAgentReason: "Test fixture.");
        var terminalReplacement = new DecomposedSubTask(
            "PROOF-20260803-003",
            "Package the observation evidence for the final independent audit.",
            [firstReplacement.Id], "high", "Package evidence",
            ParentTaskId: parent.Id,
            AgentRoutingMode: "generic",
            GenericAgentReason: "Test fixture.");
        var audit = original.Validations!.Single() with
        {
            AfterTaskIds = [terminalReplacement.Id],
        };
        var revision = original with
        {
            Tasks = [parent, firstReplacement, terminalReplacement],
            Validations = [audit],
        };
        var text = "TASKS_JSON:\n" + JsonSerializer.Serialize(revision);

        var parsed = TasksJsonParser.TryParse(text, out var accepted, out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, diagnostic?.Message);
            Assert.That(accepted?.Validations?.Single().AfterTaskIds,
                Is.EqualTo(new[] { terminalReplacement.Id }));
        });
    }

    [Test]
    public void ProofBearingRevision_AuditIncludingSupersededParent_IsRejectedWithEffectiveLeaf()
    {
        var original = MakeGroup(true);
        var parent = original.Tasks[0];
        var firstReplacement = new DecomposedSubTask(
            "PROOF-20260803-002", "Capture the observation.", [], "high", "Capture observation",
            ParentTaskId: parent.Id, AgentRoutingMode: "generic", GenericAgentReason: "Test fixture.");
        var terminalReplacement = new DecomposedSubTask(
            "PROOF-20260803-003", "Package the evidence.", [firstReplacement.Id], "high", "Package evidence",
            ParentTaskId: parent.Id, AgentRoutingMode: "generic", GenericAgentReason: "Test fixture.");
        var audit = original.Validations!.Single() with
        {
            AfterTaskIds = [parent.Id, terminalReplacement.Id],
        };
        var revision = original with
        {
            Tasks = [parent, firstReplacement, terminalReplacement],
            Validations = [audit],
        };

        var parsed = TasksJsonParser.TryParse(
            "TASKS_JSON:\n" + JsonSerializer.Serialize(revision), out _, out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(diagnostic?.Code, Is.EqualTo("invalid-proof-completion-audit"));
            Assert.That(diagnostic?.Message,
                Does.Contain("Expected leaf task IDs: [PROOF-20260803-003]"));
        });
    }
}

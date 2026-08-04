namespace SquadDash.Tests;

[TestFixture]
internal sealed class ProofProvenancePresenterTests
{
    // ── Evidence source classification ────────────────────────────────────────

    [TestCase("ai-assessed", EvidenceSourceKind.AiAssessed)]
    [TestCase("host-recorded", EvidenceSourceKind.HostRecorded)]
    [TestCase("automated-test", EvidenceSourceKind.Automated)]
    [TestCase("live-ui-observation", EvidenceSourceKind.LiveUi)]
    [TestCase("restart-observation", EvidenceSourceKind.Restart)]
    [TestCase("human-observation", EvidenceSourceKind.HumanObservation)]
    [TestCase(null, EvidenceSourceKind.AiAssessed)]
    [TestCase("unknown-type", EvidenceSourceKind.AiAssessed)]
    public void ClassifyProofType_ReturnsExpectedKind(string? proofType, EvidenceSourceKind expected)
    {
        Assert.That(ProofProvenancePresenter.ClassifyProofType(proofType), Is.EqualTo(expected));
    }

    // ── Commit formatting ─────────────────────────────────────────────────────

    [Test]
    public void FormatShortSha_FullSha_ReturnsSeven()
    {
        Assert.That(ProofProvenancePresenter.FormatShortSha("abc1234def5678"), Is.EqualTo("abc1234"));
    }

    [Test]
    public void FormatShortSha_ShortInput_ReturnsAsIs()
    {
        Assert.That(ProofProvenancePresenter.FormatShortSha("abc"), Is.EqualTo("abc"));
    }

    [Test]
    public void FormatShortSha_Null_ReturnsNull()
    {
        Assert.That(ProofProvenancePresenter.FormatShortSha(null), Is.Null);
    }

    [Test]
    public void FormatShortSha_Whitespace_ReturnsNull()
    {
        Assert.That(ProofProvenancePresenter.FormatShortSha("   "), Is.Null);
    }

    // ── BuildForTask ──────────────────────────────────────────────────────────

    [Test]
    public void BuildForTask_NullTask_ReturnsNull()
    {
        Assert.That(ProofProvenancePresenter.BuildForTask(null), Is.Null);
    }

    [Test]
    public void BuildForTask_NoProofRequirements_ReturnsNull()
    {
        var task = MakeTask(proofRequirements: null);
        Assert.That(ProofProvenancePresenter.BuildForTask(task), Is.Null);
    }

    [Test]
    public void BuildForTask_EmptyProofRequirements_ReturnsNull()
    {
        var task = MakeTask(proofRequirements: []);
        Assert.That(ProofProvenancePresenter.BuildForTask(task), Is.Null);
    }

    [Test]
    public void BuildForTask_WithEvidence_ClassifiesFromEvidence()
    {
        var task = MakeTask(
            proofRequirements: [new PlanTaskProofRequirement("r1", "live-ui-observation", "Observe UI")],
            proofEvidence: [new PlanTaskProofEvidence("r1", "live-ui-observation", "Saw the UI update",
                ["trace:screenshot-001"])],
            commit: "abc1234567890def");

        var result = ProofProvenancePresenter.BuildForTask(task)!;

        Assert.That(result.SourceKind, Is.EqualTo(EvidenceSourceKind.LiveUi));
        Assert.That(result.SourceLabel, Is.EqualTo("Live UI observation"));
        Assert.That(result.CommitShortSha, Is.EqualTo("abc1234"));
        Assert.That(result.CommitFullSha, Is.EqualTo("abc1234567890def"));
        Assert.That(result.DeclaredRequirements, Is.EqualTo(new[] { "Observe UI" }));
        Assert.That(result.ReturnedSummaries, Is.EqualTo(new[] { "Saw the UI update" }));
        Assert.That(result.Artifacts, Is.EqualTo(new[] { "trace:screenshot-001" }));
    }

    [Test]
    public void BuildForTask_WithoutEvidence_ClassifiesFromRequirement()
    {
        var task = MakeTask(
            proofRequirements: [new PlanTaskProofRequirement("r1", "human-observation", "Human checks")],
            proofEvidence: null,
            commit: null);

        var result = ProofProvenancePresenter.BuildForTask(task)!;

        Assert.That(result.SourceKind, Is.EqualTo(EvidenceSourceKind.HumanObservation));
        Assert.That(result.CommitShortSha, Is.Null);
        Assert.That(result.CommitFullSha, Is.Null);
        Assert.That(result.ReturnedSummaries, Is.Empty);
        Assert.That(result.Artifacts, Is.Empty);
    }

    [Test]
    public void BuildForTask_DistinguishesRequirementsFromEvidence()
    {
        var task = MakeTask(
            proofRequirements: [new PlanTaskProofRequirement("r1", "automated-test", "Tests must pass")],
            proofEvidence: [new PlanTaskProofEvidence("r1", "automated-test", "All 47 tests passed")],
            commit: "deadbeef12345");

        var result = ProofProvenancePresenter.BuildForTask(task)!;

        // Declared requirements are descriptions from proof contract
        Assert.That(result.DeclaredRequirements[0], Is.EqualTo("Tests must pass"));
        // Returned summaries are from evidence
        Assert.That(result.ReturnedSummaries[0], Is.EqualTo("All 47 tests passed"));
        // They are never conflated
        Assert.That(result.DeclaredRequirements, Is.Not.EqualTo(result.ReturnedSummaries));
    }

    [Test]
    public void BuildForTask_AccessibleDescription_ContainsAllParts()
    {
        var task = MakeTask(
            proofRequirements: [new PlanTaskProofRequirement("r1", "automated-test", "Tests pass")],
            proofEvidence: [new PlanTaskProofEvidence("r1", "automated-test", "47 tests passed")],
            commit: "abc1234");

        var result = ProofProvenancePresenter.BuildForTask(task)!;

        Assert.That(result.AccessibleDescription, Does.Contain("Evidence source:"));
        Assert.That(result.AccessibleDescription, Does.Contain("Validated commit: abc1234"));
        Assert.That(result.AccessibleDescription, Does.Contain("Declared requirements: Tests pass"));
        Assert.That(result.AccessibleDescription, Does.Contain("Returned evidence: 47 tests passed"));
    }

    // ── BuildForValidation ────────────────────────────────────────────────────

    [Test]
    public void BuildForValidation_NullNode_ReturnsNull()
    {
        Assert.That(ProofProvenancePresenter.BuildForValidation(null), Is.Null);
    }

    [Test]
    public void BuildForValidation_NoAssertions_ReturnsNull()
    {
        var node = MakeValidation(assertions: []);
        Assert.That(ProofProvenancePresenter.BuildForValidation(node), Is.Null);
    }

    [Test]
    public void BuildForValidation_WithCommands_ClassifiedAsAutomated()
    {
        var node = MakeValidation(
            assertions: ["Build passes", "Tests pass"],
            commands: ["dotnet build", "dotnet test"],
            validatedCommit: "feedface12345678",
            summary: "All checks green");

        var result = ProofProvenancePresenter.BuildForValidation(node)!;

        Assert.That(result.SourceKind, Is.EqualTo(EvidenceSourceKind.Automated));
        Assert.That(result.SourceLabel, Is.EqualTo("Automated test evidence"));
        Assert.That(result.CommitShortSha, Is.EqualTo("feedfac"));
        Assert.That(result.CommitFullSha, Is.EqualTo("feedface12345678"));
        Assert.That(result.DeclaredRequirements, Is.EqualTo(new[] { "Build passes", "Tests pass" }));
        Assert.That(result.ReturnedSummaries, Does.Contain("All checks green"));
    }

    [Test]
    public void BuildForValidation_WithoutCommands_ClassifiedAsAiAssessed()
    {
        var node = MakeValidation(
            assertions: ["Code quality acceptable"],
            commands: null,
            validatedCommit: "abc1234",
            summary: "Reviewed and satisfactory");

        var result = ProofProvenancePresenter.BuildForValidation(node)!;

        Assert.That(result.SourceKind, Is.EqualTo(EvidenceSourceKind.AiAssessed));
        Assert.That(result.SourceLabel, Is.EqualTo("AI-assessed validation"));
    }

    [Test]
    public void BuildForValidation_NullCommit_GracefulDegradation()
    {
        var node = MakeValidation(
            assertions: ["Something holds"],
            commands: null,
            validatedCommit: null,
            summary: null);

        var result = ProofProvenancePresenter.BuildForValidation(node)!;

        Assert.That(result.CommitShortSha, Is.Null);
        Assert.That(result.CommitFullSha, Is.Null);
        Assert.That(result.ReturnedSummaries, Is.Empty);
    }

    // ── Source label formatting ───────────────────────────────────────────────

    [Test]
    public void FormatSourceLabel_AllKinds_ReturnsNonEmpty()
    {
        foreach (EvidenceSourceKind kind in Enum.GetValues(typeof(EvidenceSourceKind)))
        {
            var label = ProofProvenancePresenter.FormatSourceLabel(kind);
            Assert.That(label, Is.Not.Null.And.Not.Empty,
                $"FormatSourceLabel returned empty for {kind}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PlanTask MakeTask(
        IReadOnlyList<PlanTaskProofRequirement>? proofRequirements = null,
        IReadOnlyList<PlanTaskProofEvidence>? proofEvidence = null,
        string? commit = null) => new(
        TaskId: "T1",
        Title: "Test task",
        Description: "A test task",
        DependsOn: [],
        Priority: "high",
        Status: PlanTaskStatus.Complete,
        Commit: commit,
        ProofRequirements: proofRequirements,
        ProofEvidence: proofEvidence);

    private static PlanValidationNode MakeValidation(
        IReadOnlyList<string> assertions,
        IReadOnlyList<string>? commands = null,
        string? validatedCommit = null,
        string? summary = null) => new(
        ValidationId: "V1",
        Title: "Test validation",
        Description: "Validate something",
        AfterTaskIds: ["T1"],
        BeforeTaskIds: [],
        Assertions: assertions,
        OutputIds: null,
        Mode: "ai",
        Commands: commands,
        RevalidateAtCompletion: false,
        Status: PlanValidationStatus.Passed,
        ValidatedCommit: validatedCommit,
        Summary: summary,
        Evidence: null);
}

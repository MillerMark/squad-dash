namespace SquadDash.Tests;

[TestFixture]
internal sealed class RoutingIssueWorkflowTests {
    [Test]
    public void BuildSystemEntry_IncludesQuickRepliesAndIssueDetails() {
        var assessment = new SquadRoutingDocumentAssessment(
            @"C:\Repo",
            @"C:\Repo\.squad\routing.md",
            SquadRoutingDocumentStatus.UnfilledSeed,
            Array.Empty<SquadTeamMember>(),
            ExistingContent: null,
            IssueFingerprint: "ABC123",
            DiagnosticMessage: "routing.md still contains placeholders.");

        var entry = RoutingIssueWorkflow.BuildSystemEntry(assessment);

        Assert.Multiple(() => {
            Assert.That(entry, Does.Contain("[Repair Routing.md] [Ignore for now]"));
            Assert.That(entry, Does.Contain("placeholder template content"));
        });
    }

    [Test]
    public void BuildRepairInstruction_TellsCoordinatorNotToDelegate() {
        var instruction = RoutingIssueWorkflow.BuildRepairInstruction();

        Assert.Multiple(() => {
            Assert.That(instruction, Does.Contain("Do not delegate this task."));
            Assert.That(instruction, Does.Contain(".squad/team.md"));
            Assert.That(instruction, Does.Contain("charter.md"));
        });
    }

    [Test]
    public void BuildRepairQueuedMessage_WithBackupPath_IncludesBackupPath() {
        var message = RoutingIssueWorkflow.BuildRepairQueuedMessage(@"C:\Repo\.squad\routing.pre-repair.backup.md");

        Assert.Multiple(() => {
            Assert.That(message, Does.Contain("[info]"));
            Assert.That(message, Does.Contain(@"C:\Repo\.squad\routing.pre-repair.backup.md"));
        });
    }

    [Test]
    public void BuildRepairQueuedMessage_WithNullBackupPath_OmitsBackupReference() {
        var message = RoutingIssueWorkflow.BuildRepairQueuedMessage(null);

        Assert.Multiple(() => {
            Assert.That(message, Does.Contain("[info]"));
            Assert.That(message, Does.Not.Contain("backup"));
        });
    }

    [Test]
    public void BuildIgnoredMessage_ContainsMeaningfulContent() {
        var message = RoutingIssueWorkflow.BuildIgnoredMessage();

        Assert.Multiple(() => {
            Assert.That(message, Is.Not.Null.And.Not.Empty);
            Assert.That(message, Does.Contain("[info]"));
            Assert.That(message, Does.Contain("routing.md"));
        });
    }

    [Test]
    public void BuildRepairBlockedMessage_ContainsMeaningfulContent() {
        var message = RoutingIssueWorkflow.BuildRepairBlockedMessage();

        Assert.Multiple(() => {
            Assert.That(message, Is.Not.Null.And.Not.Empty);
            Assert.That(message, Does.Contain("[info]"));
            Assert.That(message, Does.Contain("routing.md"));
        });
    }

    [Test]
    public void BuildSystemEntry_WithMissingStatus_DescribesMissingFile() {
        var assessment = new SquadRoutingDocumentAssessment(
            @"C:\Repo",
            @"C:\Repo\.squad\routing.md",
            SquadRoutingDocumentStatus.Missing,
            Array.Empty<SquadTeamMember>(),
            ExistingContent: null,
            IssueFingerprint: "XYZ",
            DiagnosticMessage: null);

        var entry = RoutingIssueWorkflow.BuildSystemEntry(assessment);

        Assert.That(entry, Does.Contain("is missing"));
    }

    [Test]
    public void BuildSystemEntry_WithInvalidCustomStatus_DescribesParseFailure() {
        var assessment = new SquadRoutingDocumentAssessment(
            @"C:\Repo",
            @"C:\Repo\.squad\routing.md",
            SquadRoutingDocumentStatus.InvalidCustom,
            Array.Empty<SquadTeamMember>(),
            ExistingContent: null,
            IssueFingerprint: "XYZ",
            DiagnosticMessage: null);

        var entry = RoutingIssueWorkflow.BuildSystemEntry(assessment);

        Assert.That(entry, Does.Contain("parseable routing table"));
    }

    [Test]
    public void BuildSystemEntry_WithUnknownStatusAndDiagnosticMessage_UsesDiagnosticMessage() {
        const string diagnosticMessage = "Custom diagnostic detail from caller.";

        var assessment = new SquadRoutingDocumentAssessment(
            @"C:\Repo",
            @"C:\Repo\.squad\routing.md",
            SquadRoutingDocumentStatus.HealthyCustom,
            Array.Empty<SquadTeamMember>(),
            ExistingContent: null,
            IssueFingerprint: "XYZ",
            DiagnosticMessage: diagnosticMessage);

        var entry = RoutingIssueWorkflow.BuildSystemEntry(assessment);

        Assert.That(entry, Does.Contain(diagnosticMessage));
    }

    [Test]
    public void BuildSystemEntry_WithUnknownStatusAndNoDiagnosticMessage_UsesDefaultBullet() {
        var assessment = new SquadRoutingDocumentAssessment(
            @"C:\Repo",
            @"C:\Repo\.squad\routing.md",
            SquadRoutingDocumentStatus.HealthyCustom,
            Array.Empty<SquadTeamMember>(),
            ExistingContent: null,
            IssueFingerprint: "XYZ",
            DiagnosticMessage: null);

        var entry = RoutingIssueWorkflow.BuildSystemEntry(assessment);

        Assert.That(entry, Does.Contain("needs attention"));
    }
}

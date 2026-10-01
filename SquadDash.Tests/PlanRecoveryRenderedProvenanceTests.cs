using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PlanRecoveryRenderedProvenanceTests
{
    private const string TaskId = "TASK-1";
    private const string OldCommit = "ddc9136669";
    private const string NewCommit = "f8f3dd89b9";
    private const string OldCriticism = "The footer still omits model attribution.";
    private const string NewConclusion = "The footer now displays model attribution.";
    private const string Question = "Does the completed footer identify the selected model profile?";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-13T11:00:00Z");

    [Test]
    public void RefreshedEvidence_RenderedNarrativeSelectsLatestMatchingReportNotLastHistoricalReport()
    {
        var plan = MakePlan([
            Report(NewCommit, "An earlier check of the refreshed footer."),
            Report(NewCommit, NewConclusion),
            Report(OldCommit, OldCriticism),
        ]);

        var rendered = RenderSharedContent(plan);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Does.Contain("Review Required"));
            Assert.That(rendered.Text, Does.Contain(Question));
            Assert.That(rendered.Text, Does.Contain(NewConclusion));
            Assert.That(rendered.Text, Does.Not.Contain(OldCriticism));
            Assert.That(rendered.Text, Does.Not.Contain("An earlier check"));
            Assert.That(rendered.Text, Does.Not.Contain("Review below reflects"));
            Assert.That(rendered.Text, Does.Contain("Commits: f8f3dd8"));
            Assert.That(rendered.Text, Does.Contain("Tests: 98/98 tests passed."));
            Assert.That(PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(plan, TaskId), Is.Null);
        });
    }

    [Test]
    public void RefreshedEvidence_WithoutMatchingReportSeparatesHistoricalNarrativeFromCurrentEvidence()
    {
        var plan = MakePlan([Report(OldCommit, OldCriticism)]);
        var warning = $"A previous review reflects commit {OldCommit}; current evidence is commit {NewCommit}. " +
                      "The earlier conclusions are kept separate because they do not verify the current commit.";

        var rendered = RenderSharedContent(plan);
        var sharedNotice = PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(plan, TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Presentation.AnalysisBullets, Is.Empty);
            Assert.That(rendered.Presentation.PreviousReview, Is.Not.Null);
            Assert.That(rendered.Presentation.PreviousReview!.AnalysisBullets.Select(item => item.Text),
                Does.Contain(OldCriticism));
            Assert.That(rendered.Text, Does.Not.Contain(OldCriticism),
                "Historical conclusions must not appear in the current review narrative.");
            Assert.That(rendered.Text, Does.Contain("Commits: f8f3dd8"));
            Assert.That(rendered.Text, Does.Contain("Tests: 98/98 tests passed."));
            Assert.That(sharedNotice, Is.EqualTo(warning),
                "The recovery Inbox must receive exactly the same provenance notice as the card.");
        });

        var inboxReason = new Paragraph();
        MarkdownFlowDocumentBuilder.AddInboxInlineText(inboxReason.Inlines, sharedNotice!);
        MarkdownFlowDocumentBuilder.AddInboxInlineText(inboxReason.Inlines, "\n" + OldCriticism);
        Assert.That(ReadText(inboxReason), Does.StartWith(warning));
    }

    [TestCase("")]
    [TestCase(" ")]
    public void UnknownEvaluatedCommit_IsExplicitlyUnknownInRenderedWarning(string evaluatedCommit)
    {
        var plan = MakePlan([Report(evaluatedCommit, OldCriticism)]);

        var rendered = RenderSharedContent(plan);
        var notice = PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(plan, TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(notice, Does.Contain("unknown").IgnoreCase);
            Assert.That(notice, Does.Contain($"current evidence is commit {NewCommit}"));
            Assert.That(rendered.Presentation.PreviousReview, Is.Not.Null);
            Assert.That(rendered.Text, Does.Not.Contain(OldCriticism));
            Assert.That(rendered.Text, Does.Contain("Commits: f8f3dd8"));
        });
    }

    [Test]
    public void MatchingAcceptedReport_ReplacesHistoricalCriticismWhileHumanCheckpointRemains()
    {
        var plan = MakePlan([
            Report(OldCommit, OldCriticism),
            Report(NewCommit, NewConclusion) with { Verdict = PlanTaskVerificationVerdict.Accepted },
        ]);

        var rendered = RenderSharedContent(plan);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Does.Contain(Question));
            Assert.That(rendered.Text, Does.Contain(NewConclusion));
            Assert.That(rendered.Text, Does.Not.Contain(OldCriticism));
            Assert.That(rendered.Text, Does.Not.Contain("not been reverified"));
            Assert.That(rendered.Text, Does.Contain("Commits: f8f3dd8"));
        });
    }

    [Test]
    public void RefreshedEvidence_MatchingReportComparisonIsCaseInsensitive()
    {
        var plan = MakePlan([Report(NewCommit.ToUpperInvariant(), NewConclusion), Report(OldCommit, OldCriticism)]);

        var rendered = RenderSharedContent(plan);

        Assert.That(rendered.Text, Does.Contain(NewConclusion));
        Assert.That(rendered.Text, Does.Not.Contain(OldCriticism));
        Assert.That(rendered.Text, Does.Not.Contain("not been reverified"));
    }

    [Test]
    public void SiblingEvidenceAndHistory_CannotReplaceDisplayedTasksOwnNarrativeOrFooter()
    {
        var plan = MakePlan([Report(OldCommit, "This task's reviewed implementation is supported.")]);
        var task = plan.Tasks[0] with { Handoff = plan.Tasks[0].Handoff! with { Commit = OldCommit } };
        var sibling = task with
        {
            TaskId = "TASK-2",
            Handoff = task.Handoff! with { Commit = NewCommit },
            VerificationHistory = [Report(NewCommit, "Sibling-only review conclusion.")],
        };
        plan = plan with
        {
            Tasks = [task, sibling],
            InterruptionData = plan.InterruptionData! with
            {
                InterruptedTaskId = sibling.TaskId,
                TaskCommitEvidence = plan.InterruptionData.TaskCommitEvidence! with { TaskId = sibling.TaskId },
            },
        };

        var rendered = RenderSharedContent(plan);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Does.Contain("This task's reviewed implementation is supported."));
            Assert.That(rendered.Text, Does.Not.Contain("Sibling-only"));
            Assert.That(rendered.Text, Does.Not.Contain(NewCommit));
            Assert.That(rendered.Text, Does.Not.Contain("Commits: f8f3dd8"));
            Assert.That(rendered.Text, Does.Not.Contain("Review below reflects"));
            Assert.That(PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(plan, TaskId), Is.Null);
        });
    }

    [Test]
    public void OwnTaskEvidence_OverridesOlderHandoffForNarrativeAndFooterTogether()
    {
        var plan = MakePlan([Report(OldCommit, OldCriticism), Report(NewCommit, NewConclusion)]);
        plan = plan with
        {
            Tasks = [plan.Tasks[0] with { Handoff = plan.Tasks[0].Handoff! with { Commit = OldCommit } }],
        };

        var rendered = RenderSharedContent(plan);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Does.Contain(NewConclusion));
            Assert.That(rendered.Text, Does.Not.Contain(OldCriticism));
            Assert.That(rendered.Text, Does.Contain("Commits: f8f3dd8"));
            Assert.That(rendered.Text, Does.Contain("98/98"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveryInbox_ActualRenderedBodyAndFooterShareProvenance(bool hasMatchingReport)
    {
        var reports = new List<PlanTaskVerificationReport> { Report(OldCommit, OldCriticism) };
        if (hasMatchingReport) reports.Add(Report(NewCommit, NewConclusion));
        var plan = MakePlan(reports);
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            PendingDecomposePlanAdapter.FromPlan(plan), TaskId, OldCriticism, Now,
            taskCommitEvidence: plan.InterruptionData!.TaskCommitEvidence,
            durablePlan: plan, currentHead: NewCommit);
        var document = MarkdownFlowDocumentBuilder.Build(message.Body);
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Human Verification Is Required"));
            Assert.That(text, Does.Contain("Commits: f8f3dd8"));
            Assert.That(text, Does.Contain("Tests passed: 98"));
            if (hasMatchingReport)
            {
                Assert.That(text, Does.Contain("Automated review"));
                Assert.That(text, Does.Contain(NewConclusion));
                Assert.That(text, Does.Not.Contain(OldCriticism),
                    "The old interruption reason must not reintroduce obsolete conclusions.");
                Assert.That(text, Does.Not.Contain("not been reverified"));
            }
            else
            {
                Assert.That(text, Does.Contain("Previous review"));
                var warning = PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(plan, TaskId)!;
                Assert.That(text, Does.Contain(warning));
                Assert.That(text, Does.Not.Contain(OldCriticism));
            }
        });
    }

    // MainWindow is not linked into the test assembly. Exercise its production presentation
    // and inline-rendering seams; this adapter does not test window layout or action wiring.
    private static RenderedContent RenderSharedContent(Plan plan)
    {
        var presentation = PlanRecoveryPresentationBuilder.BuildHumanReviewCard(plan, TaskId);
        Assert.That(presentation, Is.Not.Null);
        var document = new FlowDocument();
        AddParagraph(presentation!.Title);
        if (presentation.Question is not null) AddParagraph(presentation.Question);
        AddParagraph("Automated Review Status:");
        var analysis = new System.Windows.Documents.List { MarkerStyle = TextMarkerStyle.Disc };
        foreach (var bullet in presentation.AnalysisBullets)
        {
            var paragraph = new Paragraph();
            MarkdownFlowDocumentBuilder.AddInboxInlineText(paragraph.Inlines, bullet.Text);
            analysis.ListItems.Add(new ListItem(paragraph));
        }
        document.Blocks.Add(analysis);

        var recovery = PlanRecoveryPresentationBuilder.Build(plan, TaskId, hasPreservedWork: false);
        if (recovery.CommitEvidence is { } evidence)
        {
            var commit = new Paragraph(new Run("Commits: "));
            commit.Inlines.Add(new Hyperlink(new Run(evidence.Commit[..Math.Min(7, evidence.Commit.Length)])));
            document.Blocks.Add(commit);
            var tests = PlanRecoveryPresentationBuilder.BuildCompactTestSummary(evidence.Verification);
            if (tests is not null) AddParagraph(tests);
        }

        return new RenderedContent(
            new TextRange(document.ContentStart, document.ContentEnd).Text, presentation, analysis);

        void AddParagraph(string text)
        {
            var paragraph = new Paragraph();
            MarkdownFlowDocumentBuilder.AddInboxInlineText(paragraph.Inlines, text);
            document.Blocks.Add(paragraph);
        }
    }

    private static string ReadText(TextElement element) =>
        new TextRange(element.ContentStart, element.ContentEnd).Text.Trim();

    private sealed record RenderedContent(
        string Text, PlanHumanReviewCardPresentation Presentation, System.Windows.Documents.List Analysis);

    private static PlanTaskVerificationReport Report(string commit, string summary) => new(
        PlanTaskVerificationVerdict.HumanReviewRequired, summary, [], [],
        "Runtime rendering requires human observation.", [], commit, Now);

    private static Plan MakePlan(IReadOnlyList<PlanTaskVerificationReport> reports)
    {
        var verification = new DecomposeStepVerification(
            "passed",
            "dotnet test",
            "98/98 tests passed",
            new DecomposeStepTestResults(98, 98, 0, 0));
        var task = new PlanTask(
            TaskId, "Footer", "Show selected model profile.", [], "high", PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(NewCommit, "Refreshed implementation.", ["Footer.cs"], verification, Now),
            VerificationHistory: reports);
        var gate = new PlanApprovalGate(
            "GATE-1", "Observe footer", [TaskId], [], PlanGateStatus.AwaitingApproval, Question: Question);
        return new Plan(
            "PLAN-1", "rev", PlanSource.Manual, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/plan", "Summary", [task], [gate],
            new PlanProgress(0, 1), new PlanTimestamps(Now),
            new PlanInterruptionData(
                OldCriticism, PlanRecoveryState.PendingRecovery, 1, InterruptedTaskId: TaskId,
                TaskCommitEvidence: new PlanTaskCommitEvidence(
                    TaskId, "attempt-2", "baseline", NewCommit, "Refreshed implementation.", verification)));
    }
}

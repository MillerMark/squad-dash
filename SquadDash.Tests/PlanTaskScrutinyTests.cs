using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskScrutinyTests
{
    private static Plan MakePlan()
    {
        var parent = new PlanTask(
            "P-1", "Build source", "Create the production source.", [], "high", PlanTaskStatus.Complete,
            Commit: "1111111",
            CompletionSummary: "Created source",
            Handoff: new PlanTaskHandoff(
                "1111111", "Created the production source and exposed its contract.",
                ["src/Source.cs"], new DecomposeStepVerification("passed", "dotnet test", "green"),
                DateTimeOffset.UtcNow.AddMinutes(-2)));
        var child = new PlanTask(
            "P-2", "Wire consumer", "Use the source from the running application.", ["P-1"], "high",
            PlanTaskStatus.Executing);
        return new Plan(
            "P", "rev", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "Connected feature", "feature/test", "Deliver one cohesive user-visible feature.",
            [parent, child], [], new PlanProgress(1, 2, "P-2"),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    [Test]
    public void Context_IncludesGuidingIntentAndAcceptedAncestorHandoff()
    {
        var plan = MakePlan();
        var text = PlanExecutionContextBuilder.Build(plan, plan.Tasks[1]);

        Assert.That(text, Does.Contain("Deliver one cohesive user-visible feature"));
        Assert.That(text, Does.Contain("Created the production source and exposed its contract"));
        Assert.That(text, Does.Contain("src/Source.cs"));
        Assert.That(text, Does.Contain("Wire consumer"));
    }

    [Test]
    public void Context_PreservesFullDistantAncestryWithoutCompression()
    {
        var longSummary = "I established the original production contract: " + new string('x', 500);
        var root = MakePlan().Tasks[0] with
        {
            TaskId = "P-0",
            Handoff = MakePlan().Tasks[0].Handoff! with
            {
                Summary = longSummary,
                ChangedFiles = ["src/RootContract.cs"],
            },
        };
        var middle = MakePlan().Tasks[0] with
        {
            TaskId = "P-1",
            Title = "Middle",
            DependsOn = ["P-0"],
        };
        var current = MakePlan().Tasks[1] with { DependsOn = ["P-1"] };
        var plan = MakePlan() with { Tasks = [root, middle, current], Progress = new PlanProgress(2, 3, current.TaskId) };

        var text = PlanExecutionContextBuilder.Build(plan, current);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(longSummary));
            Assert.That(text, Does.Contain("src/RootContract.cs"));
            Assert.That(text, Does.Not.Contain("…"));
            Assert.That(text.IndexOf("P-0", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("P-1", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ExecutionJournal_PersistsExactSentAndReturnedPayloadsOutsidePlanState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SquadDashJournalTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = PlanExecutionJournal.Append(
                directory, "P", "P-2", "task-context-sent", "exact upstream context",
                new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
            PlanExecutionJournal.Append(
                directory, "P", "P-2", "scrutiny-result-returned", "exact scrutiny result",
                new DateTimeOffset(2026, 8, 4, 12, 1, 0, TimeSpan.Zero));

            var journal = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(journal, Does.Contain("task-context-sent"));
                Assert.That(journal, Does.Contain("exact upstream context"));
                Assert.That(journal, Does.Contain("scrutiny-result-returned"));
                Assert.That(journal, Does.Contain("exact scrutiny result"));
            });
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Parser_RequiresMissingOrOverstatedWorkArray()
    {
        var json = """
            PLAN_TASK_SCRUTINY_JSON:
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"accepted","summary":"ok","claimFindings":[],
             "testAssessment":"adequate","reworkInstructions":[]}
            """;

        Assert.That(PlanTaskScrutinyResultParser.TryParse(json, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("missingOrOverstatedWork"));
    }

    [Test]
    public void EnvelopeRepairPrompt_RequestsOnlyStructuredResultWithoutMoreWork()
    {
        var plan = MakePlan();
        var task = plan.Tasks[1];
        var candidate = new DecomposeStepResult(
            "P", "P-2", "rev", "complete", "abcdef1", "Wired consumer", null,
            new DecomposeStepVerification("passed", "dotnet test", "green"));

        var prompt = PlanTaskScrutinyPromptBuilder.BuildEnvelopeRepair(plan, task, candidate);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(PlanTaskScrutinyResultParser.Marker));
            Assert.That(prompt, Does.Contain("Do not inspect files again"));
            Assert.That(prompt, Does.Contain("Do not add prose before or after it"));
            Assert.That(prompt, Does.Contain("\"evaluatedCommit\": \"abcdef1\""));
        });
    }

    [Test]
    public void Parser_RejectsAcceptedVerdictWithDiscrepancies()
    {
        var json = """
            PLAN_TASK_SCRUTINY_JSON:
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"accepted","summary":"not actually complete","claimFindings":[],
             "missingOrOverstatedWork":["consumer is not wired"],
             "testAssessment":"tests cover only helper","reworkInstructions":[]}
            """;

        Assert.That(PlanTaskScrutinyResultParser.TryParse(json, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("accepted scrutiny result"));
    }

    [Test]
    public void StoreUpdater_DoesNotMarkCandidateCompleteBeforeScrutiny()
    {
        var plan = MakePlan();
        var candidate = new DecomposeStepResult(
            "P", "P-2", "rev", "complete", "abcdef1", "Wired consumer", null,
            new DecomposeStepVerification("passed", "dotnet test", "green"));

        var scrutinizing = PlanStoreUpdater.ApplyTaskScrutinyStarted(
            plan, "P-2", candidate, ["src/Consumer.cs"]);

        Assert.That(scrutinizing.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Scrutinizing));
        Assert.That(scrutinizing.Progress.CompletedCount, Is.EqualTo(1));
        Assert.That(scrutinizing.Tasks[1].Handoff?.ChangedFiles, Does.Contain("src/Consumer.cs"));
    }

    [Test]
    public void StoreUpdater_SecondFailureRequiresHumanReviewAndPreservesHistory()
    {
        var plan = MakePlan();
        var result = new PlanTaskScrutinyResult(
            "P", "P-2", "rev", "abcdef1", PlanTaskScrutinyVerdict.ReworkRequired,
            "Production consumer is absent.", [], ["No consumer wiring"],
            "Tests exercise only a helper.", ["Wire the production consumer."]);

        var updated = PlanStoreUpdater.ApplyTaskScrutinyResult(
            plan, "P-2", result, automaticReworkAvailable: false);

        Assert.That(updated.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        Assert.That(updated.Tasks[1].ScrutinyHistory, Has.Count.EqualTo(1));
    }

    [Test]
    public void RecoveryPolicy_AllowsExactlyOneAutomaticRework()
    {
        Assert.That(
            PlanTaskScrutinyRecoveryPolicy.Resolve(PlanTaskScrutinyVerdict.ReworkRequired, 0),
            Is.EqualTo(PlanTaskScrutinyNextAction.AutomaticRework));
        Assert.That(
            PlanTaskScrutinyRecoveryPolicy.Resolve(PlanTaskScrutinyVerdict.ReworkRequired, 1),
            Is.EqualTo(PlanTaskScrutinyNextAction.HumanReview));
        Assert.That(
            PlanTaskScrutinyRecoveryPolicy.Resolve(PlanTaskScrutinyVerdict.HumanReviewRequired, 0),
            Is.EqualTo(PlanTaskScrutinyNextAction.HumanReview));
    }

    [Test]
    public void DurablePlan_RoundTripsHandoffAndScrutiny()
    {
        var plan = MakePlan();
        var json = JsonSerializer.Serialize(plan);
        var roundTrip = JsonSerializer.Deserialize<Plan>(json);

        Assert.That(roundTrip?.Tasks[0].Handoff?.Commit, Is.EqualTo("1111111"));
    }

    [Test]
    public void ExecutionProjection_ContainsPlanOnlyIntentAndHandoffs()
    {
        var markdown = PlanExecutionProjectionWriter.Build(MakePlan());

        Assert.That(markdown, Does.Contain("Generated execution projection"));
        Assert.That(markdown, Does.Contain("Guiding intent"));
        Assert.That(markdown, Does.Contain("Created the production source"));
        Assert.That(markdown, Does.Not.Contain("tasks.md"));
    }

    [Test]
    public void CompletionSummary_IncludesHandoffsScrutinyValidationsApprovalsAndUtcMarkers()
    {
        var plan = MakePlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            Timestamps = new PlanTimestamps(
                DateTimeOffset.UtcNow.AddMinutes(-10),
                StartedAt: DateTimeOffset.UtcNow.AddMinutes(-9),
                CompletedAt: DateTimeOffset.UtcNow),
            Tasks =
            [
                MakePlan().Tasks[0] with
                {
                    ScrutinyHistory =
                    [
                        new PlanTaskScrutinyReport(
                            PlanTaskScrutinyVerdict.Accepted, "Claims supported", [], [],
                            "Tests exercise production", [], "1111111", DateTimeOffset.UtcNow),
                    ],
                },
            ],
            Progress = new PlanProgress(1, 1),
            Validations =
            [
                new PlanValidationNode(
                    "V", "Integration holds", "Checks integration", ["P-1"], [], ["Connected"], null,
                    "evidence", null, false, PlanValidationStatus.Passed,
                    CompletedAt: DateTimeOffset.UtcNow, ValidatedCommit: "1111111", Summary: "Connected"),
            ],
            ApprovalGates =
            [
                new PlanApprovalGate(
                    "G", "Review integration", ["P-1"], [], PlanGateStatus.Approved,
                    ResolvedAt: DateTimeOffset.UtcNow, ResolvedBy: "Mark",
                    ProofRequirements:
                    [
                        new PlanTaskProofRequirement(
                            "visible", "human-observation", "Observe the connected behavior."),
                    ],
                    ProofEvidence:
                    [
                        new PlanTaskProofEvidence(
                            "visible", "human-observation", "Mark observed the connected behavior.",
                            ["squaddash://approval/P/G"]),
                    ]),
            ],
        };

        var message = PlanCompletionSummaryBuilder.Build(plan);

        Assert.That(message.Subject, Does.StartWith("Plan completed:"));
        Assert.That(message.Body, Does.Contain("Created the production source"));
        Assert.That(message.Body, Does.Contain("Claims supported"));
        Assert.That(message.Body, Does.Contain("Integration holds"));
        Assert.That(message.Body, Does.Contain("Approved by: Mark"));
        Assert.That(message.Body, Does.Contain("Human proof `visible`"));
        Assert.That(message.Body, Does.Contain("squaddash://approval/P/G"));
        Assert.That(message.Body, Does.Contain("{{utc-time:"));
        Assert.That(message.Attachments.Single().PlanGroupId, Is.EqualTo("P"));
    }
}

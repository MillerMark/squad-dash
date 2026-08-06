using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskVerificationTests
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
        Assert.That(text.IndexOf("Created the production source and exposed its contract", StringComparison.Ordinal),
            Is.LessThan(text.IndexOf("### Current task contract", StringComparison.Ordinal)));
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
                directory, "P", "P-2", "verification-result-returned", "exact verification result",
                new DateTimeOffset(2026, 8, 4, 12, 1, 0, TimeSpan.Zero));

            var journal = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(journal, Does.Contain("task-context-sent"));
                Assert.That(journal, Does.Contain("exact upstream context"));
                Assert.That(journal, Does.Contain("verification-result-returned"));
                Assert.That(journal, Does.Contain("exact verification result"));
            });
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Parser_NormalizesOmittedNonCriticalArraysForAcceptedVerdict()
    {
        var json = """
            PLAN_TASK_VERIFICATION_JSON:
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"accepted","summary":"ok","claimFindings":null,
             "testAssessment":"adequate","reworkInstructions":null}
            """;

        var parsed = PlanTaskVerificationResultParser.TryParse(json, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(result!.ClaimFindings, Is.Empty);
            Assert.That(result.MissingOrOverstatedWork, Is.Empty);
            Assert.That(result.ReworkInstructions, Is.Empty);
        });
    }

    [Test]
    public void Parser_StillRequiresInstructionsForReworkVerdict()
    {
        var json = """
            PLAN_TASK_VERIFICATION_JSON:
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"rework-required","summary":"missing wiring","claimFindings":[],
             "missingOrOverstatedWork":["missing wiring"],"testAssessment":"inadequate",
             "reworkInstructions":null}
            """;

        Assert.That(PlanTaskVerificationResultParser.TryParse(json, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("actionable rework instructions"));
    }

    [Test]
    public void EnvelopeRepairPrompt_RequestsOnlyStructuredResultWithoutMoreWork()
    {
        var plan = MakePlan();
        var task = plan.Tasks[1];
        var candidate = new DecomposeStepResult(
            "P", "P-2", "rev", "complete", "abcdef1", "Wired consumer", null,
            new DecomposeStepVerification("passed", "dotnet test", "green"));

        var prompt = PlanTaskVerificationPromptBuilder.BuildEnvelopeRepair(
            plan, task, candidate, "The production approval actions are still enabled.");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(PlanTaskVerificationResultParser.Marker));
            Assert.That(prompt, Does.Contain("Do not inspect files again"));
            Assert.That(prompt, Does.Contain("Do not add prose before or after it"));
            Assert.That(prompt, Does.Contain("\"evaluatedCommit\": \"abcdef1\""));
            Assert.That(prompt, Does.Contain("production approval actions are still enabled"));
        });
    }

    [Test]
    public void VerificationPrompt_BindsExplicitRequirementsAndOnlyAllowsDeclaredDownstreamDeferrals()
    {
        var plan = MakePlan();
        var task = plan.Tasks[0] with { Status = PlanTaskStatus.Executing };
        plan = plan with { Tasks = [task, plan.Tasks[1]], Progress = new PlanProgress(0, 2, task.TaskId) };
        var candidate = new DecomposeStepResult(
            "P", "P-1", "rev", "complete", "abcdef1", "Built source", [],
            new DecomposeStepVerification("passed", "dotnet test", "green"),
            DeferredWork:
            [
                new PlanTaskDeferredWork(
                    "Wire the running consumer", "Owned by the approved consumer task", ["P-2"]),
            ]);

        var prompt = PlanTaskVerificationPromptBuilder.Build(
            plan, task, candidate, "1111111", ["src/Source.cs"], "1 file changed");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("Treat every explicit current-task requirement as binding"));
            Assert.That(prompt, Does.Contain("candidate handoff declares it in `deferredWork`"));
            Assert.That(prompt, Does.Contain("`P-2` — Wire consumer"));
            Assert.That(prompt, Does.Contain("Use the source from the running application"));
        });
    }

    [Test]
    public void Parser_AcceptsSingleBareObjectWhenMarkerIsOmitted()
    {
        var json = """
            ```json
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"rework-required","summary":"approval actions remain enabled",
             "claimFindings":[],"missingOrOverstatedWork":["missing action guard"],
             "testAssessment":"missing production action test",
             "reworkInstructions":["guard approve and reject actions"]}
            ```
            """;

        var parsed = PlanTaskVerificationResultParser.TryParse(json, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(result?.Verdict, Is.EqualTo(PlanTaskVerificationVerdict.ReworkRequired));
        });
    }

    [Test]
    public void Parser_AcceptsLegacyScrutinyMarkerForExistingResponses()
    {
        var json = """
            PLAN_TASK_SCRUTINY_JSON:
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"accepted","summary":"all claims supported","claimFindings":[],
             "missingOrOverstatedWork":[],"testAssessment":"adequate","reworkInstructions":[]}
            """;

        var parsed = PlanTaskVerificationResultParser.TryParse(json, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(result?.Verdict, Is.EqualTo(PlanTaskVerificationVerdict.Accepted));
        });
    }

    [Test]
    public void Parser_DoesNotInferResultFromProseContainingAnEmbeddedObject()
    {
        var response = "Review complete. {\"planId\":\"P\"}";

        Assert.That(PlanTaskVerificationResultParser.TryParse(response, out _, out _), Is.False);
    }

    [Test]
    public void Parser_RejectsAcceptedVerdictWithDiscrepancies()
    {
        var json = """
            PLAN_TASK_VERIFICATION_JSON:
            {"planId":"P","taskId":"P-2","revision":"rev","evaluatedCommit":"abcdef1",
             "verdict":"accepted","summary":"not actually complete","claimFindings":[],
             "missingOrOverstatedWork":["consumer is not wired"],
             "testAssessment":"tests cover only helper","reworkInstructions":[]}
            """;

        Assert.That(PlanTaskVerificationResultParser.TryParse(json, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("accepted verification result"));
    }

    [Test]
    public void StoreUpdater_DistinguishesPendingVerificationFromActiveVerification()
    {
        var plan = MakePlan();
        var candidate = new DecomposeStepResult(
            "P", "P-2", "rev", "complete", "abcdef1", "Wired consumer", null,
            new DecomposeStepVerification("passed", "dotnet test", "green"));

        var pending = PlanStoreUpdater.ApplyTaskVerificationPending(
            plan, "P-2", candidate, ["src/Consumer.cs"]);
        var verifying = PlanStoreUpdater.ApplyTaskVerificationStarted(pending, "P-2");

        Assert.That(pending.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.VerificationPending));
        Assert.That(pending.Progress.CompletedCount, Is.EqualTo(1));
        Assert.That(pending.Tasks[1].Handoff?.ChangedFiles, Does.Contain("src/Consumer.cs"));
        Assert.That(verifying.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Verifying));
        Assert.That(verifying.Progress.CompletedCount, Is.EqualTo(1));
        Assert.That(verifying.Tasks[1].Handoff, Is.SameAs(pending.Tasks[1].Handoff));
    }

    [Test]
    public void StoreUpdater_PersistsDeclaredDeferralsInCandidateHandoff()
    {
        var plan = MakePlan();
        var candidate = new DecomposeStepResult(
            "P", "P-2", "rev", "complete", "abcdef1", "Wired consumer", [],
            new DecomposeStepVerification("passed", "dotnet test", "green"),
            DeferredWork:
            [
                new PlanTaskDeferredWork("Polish labels", "Owned by later UX task", ["P-3"]),
            ]);

        var verifying = PlanStoreUpdater.ApplyTaskVerificationPending(
            plan, "P-2", candidate, ["src/Consumer.cs"]);

        Assert.That(verifying.Tasks[1].Handoff!.DeferredWork!.Single().OwnerTaskIds,
            Is.EqualTo(new[] { "P-3" }));
    }

    [Test]
    public void StoreUpdater_SecondFailureRequiresHumanReviewAndPreservesHistory()
    {
        var plan = MakePlan();
        var result = new PlanTaskVerificationResult(
            "P", "P-2", "rev", "abcdef1", PlanTaskVerificationVerdict.ReworkRequired,
            "Production consumer is absent.", [], ["No consumer wiring"],
            "Tests exercise only a helper.", ["Wire the production consumer."]);

        var updated = PlanStoreUpdater.ApplyTaskVerificationResult(
            plan, "P-2", result, automaticReworkAvailable: false);

        Assert.That(updated.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        Assert.That(updated.Tasks[1].VerificationHistory, Has.Count.EqualTo(1));
    }

    [Test]
    public void RecoveryPolicy_AllowsExactlyOneAutomaticRework()
    {
        Assert.That(
            PlanTaskVerificationRecoveryPolicy.Resolve(PlanTaskVerificationVerdict.ReworkRequired, 0),
            Is.EqualTo(PlanTaskVerificationNextAction.AutomaticRework));
        Assert.That(
            PlanTaskVerificationRecoveryPolicy.Resolve(PlanTaskVerificationVerdict.ReworkRequired, 1),
            Is.EqualTo(PlanTaskVerificationNextAction.HumanReview));
        Assert.That(
            PlanTaskVerificationRecoveryPolicy.Resolve(PlanTaskVerificationVerdict.HumanReviewRequired, 0),
            Is.EqualTo(PlanTaskVerificationNextAction.HumanReview));
    }

    [Test]
    public void DurablePlan_RoundTripsHandoffAndVerification()
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
    public void CompletionSummary_IncludesHandoffsVerificationValidationsApprovalsAndUtcMarkers()
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
                    VerificationHistory =
                    [
                        new PlanTaskVerificationReport(
                            PlanTaskVerificationVerdict.Accepted, "Claims supported", [], [],
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

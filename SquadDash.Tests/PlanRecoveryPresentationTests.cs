namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryPresentationTests
{
    [Test]
    public void AssessmentCard_PartialWorkRecommendsContinuingOnlyRemainingWork()
    {
        var assessment = new PlanRecoveryDecisionEvidence(
            "The service migration is present, but registration is missing.",
            [],
            DateTimeOffset.UtcNow,
            Classification: PlanRecoveryClassification.Partial,
            RemainingWork: ["Register the service."]);

        var presentation = PlanRecoveryAssessmentCardPresentationBuilder.Build(
            assessment,
            "Step 5");

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Finding, Does.Contain("partial work"));
            Assert.That(presentation.Recommendation, Does.Contain("continue working"));
        });
    }

    [Test]
    public void AssessmentCard_VerifiedNoOpExplainsAlreadySatisfiedResult()
    {
        var assessment = new PlanRecoveryDecisionEvidence(
            "The production route already forwards the configured value.",
            [],
            DateTimeOffset.UtcNow,
            PlanRecoveryClassification.Complete,
            CompletionKind: DecomposeStepCompletionKind.VerifiedNoOp,
            EvaluatedCommit: "abcdef1",
            NoOpEvidence:
            [
                new PlanTaskNoOpEvidence("Forward max reasoning", "The existing route forwards it.", ["src/App.cs"]),
            ]);

        var presentation = PlanRecoveryAssessmentCardPresentationBuilder.Build(assessment, "Step 3");

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Finding, Does.Contain("already satisfied at the task baseline"));
            Assert.That(presentation.Recommendation, Does.Contain("review the no-op evidence"));
        });
    }

    [Test]
    public void AssessedHumanReview_SuppressesDuplicatedGenericReason()
    {
        var assessment = new PlanRecoveryDecisionEvidence(
            "The evidence needs human review.",
            [new PlanEvidenceCommit("abcdef12", PlanRecoveryCommitRelation.Unknown, "Attribution is uncertain.")],
            DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryPresentationBuilder.ShouldShowGenericReason(assessment), Is.False);
            Assert.That(PlanRecoveryPresentationBuilder.AssessmentStoppedMessage,
                Is.EqualTo("Assessment finished — plan still stopped."));
        });
    }

    [Test]
    public void OrdinaryInterruption_KeepsGenericReason()
    {
        Assert.That(PlanRecoveryPresentationBuilder.ShouldShowGenericReason(null), Is.True);
    }

    [TestCase("5", "Task title", "Step 5")]
    [TestCase(" 5 ", "Task title", "Step 5")]
    [TestCase("Step 5", "Task title", "Step 5")]
    [TestCase(null, "Add profile grid", "Add profile grid")]
    public void FormatStepLabel_AddsStepPrefixToNumericLabels(
        string? displayLabel,
        string fallbackTitle,
        string expected)
    {
        Assert.That(
            PlanRecoveryPresentationBuilder.FormatStepLabel(displayLabel, fallbackTitle),
            Is.EqualTo(expected));
    }

    [Test]
    public void FormatStepLabel_MissingDisplayLabelAndFallback_UsesSafeGenericLabel()
    {
        Assert.That(
            PlanRecoveryPresentationBuilder.FormatStepLabel(null, null),
            Is.EqualTo("Step"));
    }

    [Test]
    public void ResolveTaskEvidence_UsesRecoveryCommitsForActiveIncompleteStep()
    {
        var task = new PlanTask(
            "PLAN-001", "Active step", "Work", [], "high", PlanTaskStatus.Executing,
            DisplayStepLabel: "5");
        var commits = new[]
        {
            new PlanEvidenceCommit("commit-1", PlanRecoveryCommitRelation.Task, "Feature work."),
            new PlanEvidenceCommit("commit-2", PlanRecoveryCommitRelation.Unknown, "Attribution uncertain."),
        };
        var plan = TestPlan(null) with
        {
            Tasks = [task],
            InterruptionData = TestPlan(null).InterruptionData! with
            {
                RecoveryAssessment = new PlanRecoveryDecisionEvidence(
                    "Needs review.", commits, DateTimeOffset.UtcNow),
            },
        };

        var resolved = PlanRecoveryPresentationBuilder.ResolveTaskEvidence(plan, task);

        Assert.Multiple(() =>
        {
            Assert.That(task.Commit, Is.Null, "The active step must remain incomplete.");
            Assert.That(resolved.Select(commit => commit.Commit),
                Is.EqualTo(new[] { "commit-1", "commit-2" }));
        });
    }

    [Test]
    public void BuildStatusMessage_WithCommitEvidence_LeadsWithCommittedWorkState()
    {
        Assert.That(
            PlanRecoveryPresentationBuilder.BuildStatusMessage(hasCommittedWork: true),
            Is.EqualTo("Plan execution stopped unexpectedly after producing committed work. Recovery is available."));
    }

    [Test]
    public void SummarizeReason_MissingVerificationEnvelope_ProducesConcisePrimaryReason()
    {
        var reason =
            "Independent verification requires human review. Verification summary: Independent verification could not produce a trustworthy structured verdict after one envelope repair. " +
            "Missing or overstated work: - Missing verification result. Test assessment: Test adequacy could not be independently classified.";

        Assert.That(
            PlanRecoveryPresentationBuilder.SummarizeReason(reason),
            Is.EqualTo("Independent verification did not return the required structured result after two attempts. " +
                       "Test adequacy could not be independently classified."));
    }

    [Test]
    public void SummarizeReason_LegacyAdditionalGenericWorker_ExplainsCorrectedPolicy()
    {
        var summary = PlanRecoveryPresentationBuilder.SummarizeReason(
            "Task PLAN-001 launched more than one generic primary worker.");

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("earlier SquadDash build"));
            Assert.That(summary, Does.Contain("preserved"));
            Assert.That(summary, Does.Contain("advisory rather than fatal"));
        });
    }

    [Test]
    public void BuildCompactTestSummary_OmitsCommandLine()
    {
        var verification = new DecomposeStepVerification(
            "passed",
            "dotnet test SquadDash.Tests --filter FullyQualifiedName~SimulationSessionManager",
            "11 tests passed");

        Assert.That(
            PlanRecoveryPresentationBuilder.BuildCompactTestSummary(verification),
            Is.EqualTo("Focused tests: 11 tests passed."));
    }

    [Test]
    public void BuildHumanReviewCard_PresentsQuestionAndStructuredAiAnalysis()
    {
        var report = new PlanTaskVerificationReport(
            PlanTaskVerificationVerdict.HumanReviewRequired,
            "All code-verifiable claims are supported by the diff and current file state. " +
            "The build passes with 0 errors.",
            [],
            ["Runtime rendering still needs human observation."],
            "The build passes with 0 errors. AppendModelAttributionLine was removed.",
            [],
            "commit-1",
            DateTimeOffset.UtcNow);
        var task = new PlanTask(
            "PLAN-007",
            "Show model profile in completion footer",
            "Show the selected profile.",
            [],
            "high",
            PlanTaskStatus.HumanReviewRequired,
            VerificationHistory: [report],
            DisplayStepLabel: "7");
        var gate = new PlanApprovalGate(
            "GATE-007",
            "Confirm the completion footer.",
            [task.TaskId],
            [],
            PlanGateStatus.AwaitingApproval,
            Question: "Does the completed footer identify the selected model profile?");
        var plan = TestPlan(null) with
        {
            Tasks = [task],
            ApprovalGates = [gate],
        };

        var presentation = PlanRecoveryPresentationBuilder.BuildHumanReviewCard(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(presentation, Is.Not.Null);
            Assert.That(presentation!.Title, Is.EqualTo("Review Required"));
            Assert.That(presentation.Question,
                Is.EqualTo("Does the completed footer identify the selected model profile?"));
            Assert.That(presentation.AnalysisBullets.Select(item => item.Text), Does.Contain(
                "All code-verifiable claims are supported by the diff and current file state."));
            Assert.That(presentation.AnalysisBullets.Select(item => item.Text), Does.Contain(
                "Runtime rendering still needs human observation."));
            Assert.That(presentation.AnalysisBullets.Select(item => item.Text), Does.Contain(
                "AppendModelAttributionLine was removed."));
            Assert.That(presentation.AnalysisBullets.Count(bullet =>
                bullet.Text.StartsWith("The build passes", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(presentation.AnalysisBullets.Single(item =>
                item.Text == "AppendModelAttributionLine was removed.").Severity,
                Is.EqualTo(PlanHumanReviewAnalysisSeverity.Warning));
        });
    }

    [Test]
    public void HumanReviewGatePolicy_CombinesSingleDirectHumanProofGate()
    {
        var reviewed = new PlanTask(
            "PLAN-007", "Review me", "Work", [], "high",
            PlanTaskStatus.HumanReviewRequired);
        var downstream = new PlanTask(
            "PLAN-008", "Continue", "Next", [reviewed.TaskId], "high",
            PlanTaskStatus.Pending);
        var gate = new PlanApprovalGate(
            "GATE-007",
            "Observe the completed behavior.",
            [reviewed.TaskId],
            [downstream.TaskId],
            PlanGateStatus.Pending,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "proof-007", "live-ui-observation", "Observe the footer."),
            ],
            Question: "Does the footer show the alias?");
        var plan = TestPlan(null) with
        {
            Tasks = [reviewed, downstream],
            ApprovalGates = [gate],
        };

        Assert.That(
            PlanHumanReviewGatePolicy.FindCombinableGate(plan, reviewed.TaskId),
            Is.SameAs(gate));
    }

    [Test]
    public void HumanReviewGatePolicy_DoesNotCombineAutomatedOrAmbiguousGate()
    {
        var reviewed = new PlanTask(
            "PLAN-007", "Review me", "Work", [], "high",
            PlanTaskStatus.HumanReviewRequired);
        var automatedGate = new PlanApprovalGate(
            "GATE-007",
            "Automated proof.",
            [reviewed.TaskId],
            [],
            PlanGateStatus.Pending,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "proof-007", "build", "Build the project."),
            ]);
        var plan = TestPlan(null) with
        {
            Tasks = [reviewed],
            ApprovalGates = [automatedGate],
        };

        Assert.That(
            PlanHumanReviewGatePolicy.FindCombinableGate(plan, reviewed.TaskId),
            Is.Null);
    }

    [Test]
    public void HumanReviewGatePolicy_DoesNotAbsorbExplicitReworkState()
    {
        var reworking = new PlanTask(
            "PLAN-007", "Needs changes", "Work", [], "high",
            PlanTaskStatus.Reworking);
        var gate = new PlanApprovalGate(
            "GATE-007",
            "Observe the completed behavior.",
            [reworking.TaskId],
            [],
            PlanGateStatus.Pending,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "proof-007", "live-ui-observation", "Observe the footer."),
            ]);
        var plan = TestPlan(null) with
        {
            Tasks = [reworking],
            ApprovalGates = [gate],
        };

        Assert.That(
            PlanHumanReviewGatePolicy.FindCombinableGate(plan, reworking.TaskId),
            Is.Null);
    }

    [Test]
    public void RecoveryAction_WithHostRecordedCandidateOffersDirectStepCompletion()
    {
        const string commit = "abcdef1234567890";
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-007",
            "attempt-1",
            "baseline",
            commit,
            "Implemented the task.",
            new DecomposeStepVerification("passed", "dotnet test", "12 tests passed"));
        var task = new PlanTask(
            "PLAN-007", "Review me", "Work", [], "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit, "Implemented the task.", ["Feature.cs"], evidence.Verification,
                DateTimeOffset.UtcNow),
            DisplayStepLabel: "7");
        var plan = TestPlan(evidence) with { Tasks = [task] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptStep));
            Assert.That(action.Label, Is.EqualTo("Step 7 Is Complete & Continue"));
            Assert.That(action.ProtocolAction, Is.EqualTo("accept-step-complete"));
            Assert.That(action.Hint, Does.Contain("No additional AI assessment"));
        });
    }

    [Test]
    public void RecoveryAction_AmendmentForHumanProofGate_AcceptsTaskWithoutApprovingChecklist()
    {
        const string commit = "abcdef1234567890";
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-AMD-001", "attempt-1", "baseline", commit, "Added fixture.",
            new DecomposeStepVerification("passed", "dotnet test", "12 tests passed"));
        var task = new PlanTask(
            "PLAN-AMD-001", "Add review fixture", "Work", [], "high",
            PlanTaskStatus.Executing,
            AmendmentGateId: "GATE-HUMAN",
            DisplayStepLabel: "5A");
        var gate = new PlanApprovalGate(
            "GATE-HUMAN", "Perform the live checks.", [task.TaskId], [],
            PlanGateStatus.Pending,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "live-check", "live-ui-observation", "Observe the simulated flow."),
            ]);
        var plan = TestPlan(evidence) with { Tasks = [task], ApprovalGates = [gate] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanAmendmentGateAcceptancePolicy.CanCombineWithTaskAcceptance(plan, task),
                Is.False);
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptStep));
            Assert.That(action.Label, Is.EqualTo("5A Is Complete & Continue"));
            Assert.That(action.Hint, Does.Not.Contain("approve its checkpoint"));
        });
    }

    [Test]
    public void RecoveryAction_AmendmentForOrdinaryGate_OffersCombinedApproval()
    {
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-AMD-001", "attempt-1", "baseline", "abcdef1234567890", "Amended.", null);
        var task = new PlanTask(
            "PLAN-AMD-001", "Amendment", "Work", [], "high", PlanTaskStatus.Executing,
            AmendmentGateId: "GATE-PLAIN");
        var gate = new PlanApprovalGate(
            "GATE-PLAIN", "Approve the amendment.", [task.TaskId], [], PlanGateStatus.Pending);
        var plan = TestPlan(evidence) with { Tasks = [task], ApprovalGates = [gate] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanAmendmentGateAcceptancePolicy.CanCombineWithTaskAcceptance(plan, task),
                Is.True);
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.ApproveAmendment));
            Assert.That(action.Label, Is.EqualTo("Approve Amendment & Continue"));
        });
    }

    [Test]
    public void RecoveryAction_WhenHumanExplicitlyPaused_OmitsRedundantStayPausedSuffix()
    {
        const string commit = "abcdef1234567890";
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-007", "attempt-1", "baseline", commit, "Implemented the task.",
            new DecomposeStepVerification("passed", "dotnet test", "12 tests passed"));
        var task = new PlanTask(
            "PLAN-007", "Review me", "Work", [], "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit, "Implemented the task.", ["Feature.cs"], evidence.Verification,
                DateTimeOffset.UtcNow),
            DisplayStepLabel: "7");
        var plan = TestPlan(evidence) with { Tasks = [task], IsPaused = true };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(action.Label, Is.EqualTo("Step 7 Is Complete"));
            Assert.That(action.Label, Does.Not.Contain("Stay Paused"));
            Assert.That(action.Hint, Does.Contain("keep the plan paused"));
        });
    }

    [Test]
    public void AcceptancePreflight_OnlyUnpausedContinueRequiresCleanWorktree()
    {
        var interrupted = TestPlan(null);

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanRecoveryAcceptancePolicy.RequiresCleanWorktreeBeforeAcceptance(interrupted),
                Is.True,
                "An operational interruption should continue through the normal safety preflight.");
            Assert.That(
                PlanRecoveryAcceptancePolicy.RequiresCleanWorktreeBeforeAcceptance(
                    interrupted with { IsPaused = true }),
                Is.False,
                "An explicit human hold accepts the reviewed commit now and defers safety checks to Resume.");
            Assert.That(
                PlanRecoveryAcceptancePolicy.ShouldAdvanceAfterAcceptance(interrupted),
                Is.True,
                "An operational interruption continues after the human resolves it.");
            Assert.That(
                PlanRecoveryAcceptancePolicy.ShouldAdvanceAfterAcceptance(
                    interrupted with { IsPaused = true }),
                Is.False,
                "An explicit human hold must not start a step or open the next boundary.");
        });
    }

    [Test]
    public void RecoveryAction_WithoutHostRecordedCandidateOffersExplicitHumanCompletion()
    {
        var task = new PlanTask(
            "PLAN-007", "Review me", "Work", [], "high",
            PlanTaskStatus.Failed,
            DisplayStepLabel: "7");
        var plan = TestPlan(null) with { Tasks = [task] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptUnattributedStep));
            Assert.That(action.Label, Is.EqualTo("Step 7 Is Complete & Continue"));
            Assert.That(action.ProtocolAction, Is.EqualTo("accept-step-complete"));
            Assert.That(action.Hint, Does.Contain("reviewing the repository changes"));
            Assert.That(action.Hint, Does.Contain("attribution remains marked as uncertain"));
        });
    }

    [Test]
    public void RecoveryAction_WithSavedVerificationCandidateNamesDirectVerification()
    {
        var task = new PlanTask(
            "PLAN-001", "Verify me", "Work", [], "high",
            PlanTaskStatus.Verifying,
            Handoff: new PlanTaskHandoff(
                "abcdef1",
                "Implemented the task.",
                ["Feature.cs"],
                new DecomposeStepVerification("passed", "dotnet build", "Build passed."),
                DateTimeOffset.UtcNow),
            DisplayStepLabel: "1");
        var plan = TestPlan(null) with { Tasks = [task] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.ResumeVerification));
            Assert.That(action.Label, Is.EqualTo("Resume Verification for Step 1"));
            Assert.That(action.ProtocolAction, Is.EqualTo("resume-verification"));
            Assert.That(action.Hint, Does.Contain("directly to independent verification"));
            Assert.That(action.Hint, Does.Contain("will not be repeated"));
        });
    }

    [Test]
    public void SplitAnalysisSentences_PreservesCodeReferencesWhileSeparatingClaims()
    {
        var bullets = PlanRecoveryPresentationBuilder.SplitAnalysisSentences(
            "MainWindow.xaml.cs contains the handler. ResponseTextBuilder has no remaining attribution write.");

        Assert.That(bullets, Is.EqualTo(new[]
        {
            "MainWindow.xaml.cs contains the handler.",
            "ResponseTextBuilder has no remaining attribution write.",
        }));
    }

    [Test]
    public void Build_WithTaskScopedCommitEvidence_RecommendsReviewAndWarnsOnRetry()
    {
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-001",
            "attempt-1",
            "baseline123",
            "commit456",
            "Implemented durable replay.",
            new DecomposeStepVerification("passed", "dotnet test", "14 tests passed"));
        var plan = TestPlan(evidence);

        var presentation = PlanRecoveryPresentationBuilder.Build(
            plan, "PLAN-001", hasPreservedWork: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.CommitEvidence, Is.SameAs(evidence));
            Assert.That(presentation.Heading, Does.Contain("Committed Work"));
            Assert.That(presentation.RetryLabel, Is.EqualTo("Retry Task Anyway…"));
            Assert.That(presentation.RetryIsWarning, Is.True);
        });
    }

    [Test]
    public void Build_WithEvidenceForDifferentTask_DoesNotMisattributeCommit()
    {
        var plan = TestPlan(new PlanTaskCommitEvidence(
            "PLAN-OTHER", null, "baseline123", "commit456", "Other task", null));

        var presentation = PlanRecoveryPresentationBuilder.Build(
            plan, "PLAN-001", hasPreservedWork: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.CommitEvidence, Is.Null);
            Assert.That(presentation.Heading, Is.EqualTo("SquadDash Could Not Confirm Whether This Task Finished"));
            Assert.That(presentation.Explanation, Does.Contain("may include unrelated work"));
        });
    }

    [Test]
    public void Build_WithAmendmentEvidence_RecommendsOneCombinedReviewAndApproval()
    {
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-001", "attempt-1", "baseline123", "commit456", "Amended.", null);
        var plan = TestPlan(evidence) with
        {
            Tasks =
            [
                new PlanTask("PLAN-001", "Amendment", "Amendment", [], "high",
                    PlanTaskStatus.Executing, AmendmentGateId: "PLAN-GATE-001"),
            ],
            ApprovalGates =
            [
                new PlanApprovalGate(
                    "PLAN-GATE-001", "Approve amendment", ["PLAN-001"], [],
                    PlanGateStatus.Pending),
            ],
        };

        var presentation = PlanRecoveryPresentationBuilder.Build(
            plan, "PLAN-001", hasPreservedWork: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Heading, Does.StartWith("Amendment"));
            Assert.That(presentation.Recommendation, Does.Contain("Approve the amendment"));
        });
    }

    [Test]
    public void Build_WithPreservedFiles_ExplainsUncommittedResumeAsWarning()
    {
        var presentation = PlanRecoveryPresentationBuilder.Build(
            TestPlan(null), "PLAN-001", hasPreservedWork: true);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.RetryLabel, Is.EqualTo("Resume With Uncommitted Files…"));
            Assert.That(presentation.RetryIsWarning, Is.True);
        });
    }

    [Test]
    public void HumanChecklist_SplitsLegacyBundledQuestionsIntoAtomicItems()
    {
        var task = new PlanTask(
            "PLAN-007", "Review", "Review", [], "high", PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff("abcdef123456", "Done", [], null, DateTimeOffset.UtcNow));
        var gate = new PlanApprovalGate(
            "GATE-007", "Observe", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "visual", "live-ui-observation", "Observe the UI",
                    "Does the footer show the alias? Does it survive restart? Does search exclude it?"),
            ]);
        var plan = TestPlan(null) with { Tasks = [task], ApprovalGates = [gate] };

        var items = PlanHumanReviewChecklist.Build(plan, [gate]);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => item.ItemId),
                Is.EqualTo(new[] { "visual#1", "visual#2", "visual#3" }));
            Assert.That(items.All(item => item.CandidateCommit == "abcdef123456"), Is.True);
            Assert.That(items.All(item => !item.IsChecked), Is.True);
        });
    }

    [Test]
    public void HumanChecklist_NewCandidateRequiresFreshChecksButShowsPriorVerification()
    {
        var task = new PlanTask(
            "PLAN-007", "Review", "Review", [], "high", PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff("newcommit123", "Updated", [], null, DateTimeOffset.UtcNow));
        var gate = new PlanApprovalGate(
            "GATE-007", "Observe", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement("visual", "live-ui-observation", "Observe", "Does it work?")],
            HumanReviewSelections:
            [new PlanHumanReviewSelection("visual", true, "oldcommit456", DateTimeOffset.UtcNow.AddMinutes(-5))]);
        var plan = TestPlan(null) with { Tasks = [task], ApprovalGates = [gate] };

        var item = PlanHumanReviewChecklist.Build(plan, [gate]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.IsChecked, Is.False);
            Assert.That(item.WasPreviouslyChecked, Is.True);
            Assert.That(item.PreviouslyCheckedCommit, Is.EqualTo("oldcommit456"));
        });
    }

    [Test]
    public void ApplyGateHumanReviewSelection_PreservesEarlierCandidateHistory()
    {
        var gate = new PlanApprovalGate(
            "GATE-007", "Observe", ["PLAN-007"], [], PlanGateStatus.AwaitingApproval);
        var plan = TestPlan(null) with { ApprovalGates = [gate] };

        var oldCandidate = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            plan, gate.GateId, "visual", true, "oldcommit");
        var newCandidate = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            oldCandidate, gate.GateId, "visual", false, "newcommit");

        Assert.That(newCandidate.ApprovalGates.Single().HumanReviewSelections,
            Has.Count.EqualTo(2));
    }

    private static Plan TestPlan(PlanTaskCommitEvidence? evidence) => new(
        PlanId: "PLAN",
        Revision: "revision",
        Source: PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Interrupted,
        Title: "Plan",
        Branch: "feature/plan",
        Summary: "Summary",
        Tasks: [],
        ApprovalGates: [],
        Progress: new PlanProgress(0, 1, null),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow),
        InterruptionData: new PlanInterruptionData(
            "Stopped", PlanRecoveryState.PendingRecovery, 1,
            InterruptedTaskId: "PLAN-001",
            TaskCommitEvidence: evidence));
}

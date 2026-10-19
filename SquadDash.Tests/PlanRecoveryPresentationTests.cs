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
            Assert.That(presentation.Finding, Does.Contain("already satisfied at the inspected snapshot"));
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
    [TestCase("5A", "Task title", "Step 5A")]
    [TestCase("15 Repair 1", "Task title", "Step 15 Repair 1")]
    [TestCase("Step 5", "Task title", "Step 5")]
    [TestCase(null, "Add profile grid", "Add profile grid")]
    public void FormatStepLabel_AddsStepPrefixToNumericLeadingLabels(
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
            PlanHumanReviewGatePolicy.FindCombinableGates(plan, reviewed.TaskId),
            Is.EqualTo(new[] { gate }));
    }

    [Test]
    public void HumanReviewWorkflow_MultipleSiblingGatesAreOneCompleteCurrentPhase()
    {
        var reviewed = ReviewTask();
        var downstream = new PlanTask(
            "PLAN-008", "Continue", "Next", [reviewed.TaskId], "high",
            PlanTaskStatus.Pending);
        var observeGate = ReviewGate(
            "GATE-OBSERVE", reviewed.TaskId, downstream.TaskId,
            "observe-motion", "Did the servo visibly respond?");
        var authorizeGate = ReviewGate(
            "GATE-AUTHORIZE", reviewed.TaskId, downstream.TaskId,
            "authorize-motion", "Are the commissioning safety conditions satisfied?");
        var plan = TestPlan(null) with
        {
            Tasks = [reviewed, downstream],
            ApprovalGates = [observeGate, authorizeGate],
        };

        var workflow = PlanHumanReviewWorkflowResolver.Resolve(plan, reviewed.TaskId);
        var attention = PlanReviewAttentionPresentationResolver.Resolve(plan, reviewed.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(workflow, Is.Not.Null);
            Assert.That(workflow!.CurrentGates.Select(gate => gate.GateId),
                Is.EqualTo(new[] { observeGate.GateId, authorizeGate.GateId }));
            Assert.That(workflow.Checklist, Has.Count.EqualTo(2));
            Assert.That(workflow.IsFinalPhase, Is.True);
            Assert.That(workflow.HasActionableInstructions, Is.True);
            Assert.That(PlanHumanReviewGatePolicy.FindCombinableGates(plan, reviewed.TaskId),
                Has.Count.EqualTo(2));
            Assert.That(attention, Is.Not.Null);
            Assert.That(attention!.Gates, Has.Count.EqualTo(2));
            Assert.That(attention.Checklist, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void HumanReviewWorkflow_DependsOnGateIdsCreatesOrderedPhases()
    {
        var reviewed = ReviewTask();
        var downstream = new PlanTask(
            "PLAN-008", "Continue", "Next", [reviewed.TaskId], "high",
            PlanTaskStatus.Pending);
        var authorizeGate = ReviewGate(
            "GATE-AUTHORIZE", reviewed.TaskId, downstream.TaskId,
            "authorize-motion", "Are the commissioning safety conditions satisfied?");
        var observeGate = ReviewGate(
            "GATE-OBSERVE", reviewed.TaskId, downstream.TaskId,
            "observe-motion", "Did the servo visibly respond?") with
        {
            DependsOnGateIds = [authorizeGate.GateId],
        };
        var plan = TestPlan(null) with
        {
            Tasks = [reviewed, downstream],
            ApprovalGates = [authorizeGate, observeGate],
        };

        var first = PlanHumanReviewWorkflowResolver.Resolve(plan, reviewed.TaskId);
        var afterAuthorization = plan with
        {
            ApprovalGates =
            [
                authorizeGate with { Status = PlanGateStatus.Approved },
                observeGate,
            ],
        };
        var second = PlanHumanReviewWorkflowResolver.Resolve(afterAuthorization, reviewed.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(first!.CurrentGates.Single().GateId, Is.EqualTo(authorizeGate.GateId));
            Assert.That(first.PhaseNumber, Is.EqualTo(1));
            Assert.That(first.TotalPhases, Is.EqualTo(2));
            Assert.That(first.IsFinalPhase, Is.False);
            Assert.That(PlanReviewAttentionPresentationResolver.Resolve(plan, reviewed.TaskId), Is.Null,
                "The first checkpoint must not complete the reviewed task while a later phase remains.");
            Assert.That(second!.CurrentGates.Single().GateId, Is.EqualTo(observeGate.GateId));
            Assert.That(second.PhaseNumber, Is.EqualTo(2));
            Assert.That(second.IsFinalPhase, Is.True);
            Assert.That(PlanReviewAttentionPresentationResolver.Resolve(
                afterAuthorization, reviewed.TaskId), Is.Not.Null);
        });
    }

    [Test]
    public void HumanReviewWorkflow_MissingProcedureOffersRepairInsteadOfAcceptance()
    {
        var reviewed = ReviewTask();
        var gate = ReviewGate(
            "GATE-REPAIR", reviewed.TaskId, "PLAN-008",
            "missing-procedure", "Can you confirm the result?") with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "missing-procedure",
                    "human-observation",
                    "Confirm the result.",
                    Question: "Can you confirm the result?",
                    AccessMode: HumanVerificationAccessMode.Recipe),
            ],
        };
        var plan = TestPlan(null) with
        {
            Tasks =
            [
                reviewed,
                new PlanTask("PLAN-008", "Continue", "Next", [reviewed.TaskId], "high", PlanTaskStatus.Pending),
            ],
            ApprovalGates = [gate],
        };

        var workflow = PlanHumanReviewWorkflowResolver.Resolve(plan, reviewed.TaskId);
        var actions = PlanRecoveryActionSetBuilder.Build(plan, reviewed.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(workflow!.HasActionableInstructions, Is.False);
            Assert.That(actions.Actions.Select(action => action.Kind),
                Does.Contain(PlanRecoverySurfaceActionKind.RepairReviewInstructions));
            Assert.That(actions.Actions.Select(action => action.Kind),
                Does.Not.Contain(PlanRecoverySurfaceActionKind.AcceptStep));
        });
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
            PlanHumanReviewGatePolicy.FindCombinableGates(plan, reviewed.TaskId),
            Is.Empty);
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
            PlanHumanReviewGatePolicy.FindCombinableGates(plan, reworking.TaskId),
            Is.Empty);
    }

    [Test]
    public void RecoveryAction_WithHostRecordedRepairCandidatePrefixesDisplayLabelWithStep()
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
            DisplayStepLabel: "15 Repair 1");
        var plan = TestPlan(evidence) with { Tasks = [task] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId, currentHead: commit);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptStep));
            Assert.That(action.Label, Is.EqualTo("Step 15 Repair 1 Is Complete & Continue"));
            Assert.That(action.ProtocolAction, Is.EqualTo("accept-step-complete"));
            Assert.That(action.Hint, Does.Contain("No additional AI assessment"));
        });
    }

    [Test]
    public void CardContentReflectsLatestTaskEvidenceAfterRefresh()
    {
        const string latestCommit = "f8f3dd89b9";
        var latestEvidence = new PlanTaskCommitEvidence(
            "PLAN-001",
            "attempt-2",
            "baseline",
            latestCommit,
            "Retry completed with refreshed evidence.",
            new DecomposeStepVerification(
                "passed",
                "dotnet test",
                "98/98 tests passed",
                new DecomposeStepTestResults(98, 98, 0, 0)));
        var plan = TestPlan(latestEvidence) with
        {
            Tasks =
            [
                new PlanTask("PLAN-001", "Footer", "Work", [], "high", PlanTaskStatus.HumanReviewRequired),
            ],
        };

        var recovery = PlanRecoveryPresentationBuilder.Build(plan, "PLAN-001", hasPreservedWork: false);
        var completedWorkReview = CompletedWorkReviewPresentationBuilder.Build(plan, "PLAN-001");

        Assert.Multiple(() =>
        {
            Assert.That(recovery.CommitEvidence, Is.Not.Null);
            Assert.That(recovery.CommitEvidence!.Commit, Is.EqualTo(latestCommit));
            Assert.That(recovery.CommitEvidence.Commit, Is.Not.EqualTo("ddc9136669"));
            Assert.That(recovery.CommitEvidence.Verification!.Summary, Does.Contain("98/98"));
            Assert.That(recovery.CommitEvidence.Verification!.Summary, Does.Not.Contain("93/93"));
            Assert.That(completedWorkReview, Is.Not.Null);
            Assert.That(completedWorkReview!.Commit!.Sha, Is.EqualTo(latestCommit));
            Assert.That(completedWorkReview.Commit.ShortSha, Is.EqualTo("f8f3dd8"));
            Assert.That(completedWorkReview.TestSummary, Is.EqualTo("Tests passed: 98"));
        });
    }

    [Test]
    public void RecoveryAction_WithStaleEvidenceAndAdvancedHead_RequiresUnattributedAcceptance()
    {
        const string recordedCommit = "ddc9136669";
        const string currentHead = "f8f3dd89b9";
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-001",
            "attempt-1",
            "baseline",
            recordedCommit,
            "Older evidence.",
            new DecomposeStepVerification("passed", "dotnet test", "93/93 tests passed"));
        var task = new PlanTask(
            "PLAN-001",
            "Review me",
            "Work",
            [],
            "high",
            PlanTaskStatus.HumanReviewRequired,
            DisplayStepLabel: "1");
        var plan = TestPlan(evidence) with { Tasks = [task] };

        var action = PlanRecoveryActionPresentationBuilder.Build(
            plan,
            task.TaskId,
            currentHead,
            (ancestor, descendant) =>
                string.Equals(ancestor, recordedCommit, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(descendant, currentHead, StringComparison.OrdinalIgnoreCase));

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptUnattributedStep));
            Assert.That(action.Kind, Is.Not.EqualTo(PlanRecoveryPrimaryActionKind.AcceptStep));
            Assert.That(action.Hint, Does.Contain("Newer commits are on this branch"));
            Assert.That(action.Hint, Does.Not.Contain("Refresh the card before accepting"));
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

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId, currentHead: commit);

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanAmendmentGateAcceptancePolicy.CanCombineWithTaskAcceptance(plan, task),
                Is.False);
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptStep));
            Assert.That(action.Label, Is.EqualTo("Step 5A Is Complete & Continue"));
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

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId, currentHead: evidence.Commit);

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

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId, currentHead: commit);

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
    public void RecoveryAction_RejectedVerifiedNoOpOffersExplicitHumanAcceptanceAndContinue()
    {
        var task = new PlanTask(
            "PLAN-001", "Verify contracts", "Run non-mutating checks", [], "high",
            PlanTaskStatus.Pending,
            DisplayStepLabel: "11",
            ExecutionMode: PlanTaskExecutionMode.Verification);
        var plan = TestPlan(null) with
        {
            Tasks = [task],
            InterruptionData = TestPlan(null).InterruptionData! with
            {
                Reason = "SquadDash rejected the step result after repair attempt: A verified-no-op result was rejected.",
            },
        };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptRejectedNoOp));
            Assert.That(action.Label, Is.EqualTo("Accept Step 11 as Complete & Continue"));
            Assert.That(action.ProtocolAction, Is.EqualTo("accept-step-complete"));
            Assert.That(action.Hint, Does.Contain("Intervening commits remain unattributed"));
        });
    }

    [Test]
    public void RecoveryAction_OrdinaryVerificationFailureStillOffersRetry()
    {
        var task = new PlanTask(
            "PLAN-001", "Verify contracts", "Run non-mutating checks", [], "high",
            PlanTaskStatus.Pending,
            DisplayStepLabel: "11",
            ExecutionMode: PlanTaskExecutionMode.Verification);
        var plan = TestPlan(null) with { Tasks = [task] };

        var action = PlanRecoveryActionPresentationBuilder.Build(plan, task.TaskId);

        Assert.That(action.Kind, Is.EqualTo(PlanRecoveryPrimaryActionKind.RetryVerification));
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
    public void ReviewProvenance_WithoutReviewReport_DoesNotInventEarlierReview()
    {
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-001",
            "attempt-1",
            "baseline123",
            "commit456",
            "Implemented durable replay.",
            new DecomposeStepVerification("passed", "dotnet test", "14 tests passed"));
        var plan = TestPlan(evidence);

        var notice = PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(
            plan, "PLAN-001");

        Assert.That(notice, Is.Null);
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
            Assert.That(presentation.Recommendation, Is.EqualTo(
                "Recommended: review the current work, then decide whether to mark PLAN-001 complete or continue working on it."));
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
    public void HumanChecklist_SingleProofUsesGateQuestionWhenProofHasNoQuestion()
    {
        var gate = new PlanApprovalGate(
            "GATE-007", "Review", [], [], PlanGateStatus.AwaitingApproval,
            Question: "Does the clarified behavior work?",
            ProofRequirements:
            [new PlanTaskProofRequirement("visual", "live-ui-observation", "Inspect the UI")]);
        var plan = TestPlan(null) with { ApprovalGates = [gate] };

        var item = PlanHumanReviewChecklist.Build(plan, [gate]).Single();

        Assert.That(item.Text, Is.EqualTo("Does the clarified behavior work?"));
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
            Assert.That(item.PreviousAnswer, Is.EqualTo(PlanHumanReviewAnswer.Yes));
        });
    }

    [Test]
    public void HumanChecklist_NewCandidateShowsPriorExplicitNoForReconfirmation()
    {
        var task = new PlanTask(
            "PLAN-007", "Review", "Review", [], "high", PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff("newcommit123", "Updated", [], null, DateTimeOffset.UtcNow));
        var gate = new PlanApprovalGate(
            "GATE-007", "Observe", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement("visual", "live-ui-observation", "Observe", "Does it work?")],
            HumanReviewSelections:
            [new PlanHumanReviewSelection(
                "visual",
                false,
                "oldcommit456",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                Answer: PlanHumanReviewAnswer.No)]);
        var plan = TestPlan(null) with { Tasks = [task], ApprovalGates = [gate] };

        var item = PlanHumanReviewChecklist.Build(plan, [gate]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.IsChecked, Is.False);
            Assert.That(item.WasPreviouslyChecked, Is.False);
            Assert.That(item.WasPreviouslyAnsweredNo, Is.True);
            Assert.That(item.PreviousAnswer, Is.EqualTo(PlanHumanReviewAnswer.No));
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

    [Test]
    public void HumanChecklist_PartialApprovalFollowedByCorrection_BecomesUncertainUntilReconfirmed()
    {
        var confirmedAt = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        var correctedAt = confirmedAt.AddMinutes(5);
        var task = new PlanTask(
            "PLAN-007", "Review", "Review", [], "high", PlanTaskStatus.Complete,
            Commit: "abcdef123456");
        var gate = new PlanApprovalGate(
            "GATE-007", "Observe", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "first", "live-ui-observation", "Observe first", "Does the first result work?"),
                new PlanTaskProofRequirement(
                    "second", "live-ui-observation", "Observe second", "Does the second result work?"),
            ],
            HumanReviewSelections:
            [new PlanHumanReviewSelection("first", true, task.Commit!, confirmedAt)]);
        var plan = TestPlan(null) with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = [task],
            ApprovalGates = [gate],
        };

        var corrected = PlanStoreUpdater.ApplyManualReviewActivity(
            plan, [task.TaskId], "Rebuilt the reviewed package after a relevant fix.", correctedAt);
        var afterCorrection = PlanHumanReviewChecklist.Build(corrected, [corrected.ApprovalGates.Single()]);

        Assert.Multiple(() =>
        {
            Assert.That(afterCorrection.Single(item => item.ItemId == "first").IsChecked, Is.False);
            Assert.That(afterCorrection.Single(item => item.ItemId == "first").WasPreviouslyChecked, Is.True);
            Assert.That(afterCorrection.Single(item => item.ItemId == "second").IsChecked, Is.False);
            Assert.That(afterCorrection.Single(item => item.ItemId == "second").WasPreviouslyChecked, Is.False);
        });

        var uncertain = afterCorrection.Single(item => item.ItemId == "first");
        var reconfirmed = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            corrected,
            gate.GateId,
            uncertain.ItemId,
            true,
            uncertain.CandidateCommit,
            uncertain.ReviewTarget,
            correctedAt.AddMinutes(1));
        var current = PlanHumanReviewChecklist.Build(reconfirmed, [reconfirmed.ApprovalGates.Single()]);

        Assert.Multiple(() =>
        {
            Assert.That(current.Single(item => item.ItemId == "first").IsChecked, Is.True);
            Assert.That(current.Single(item => item.ItemId == "first").WasPreviouslyChecked, Is.False);
            Assert.That(reconfirmed.ApprovalGates.Single().HumanReviewSelections, Has.Count.EqualTo(2),
                "The earlier confirmation remains as history while the new one applies to the current target.");
        });
    }

    [Test]
    public void HumanChecklist_ExplicitlyClearedCurrentSelection_DoesNotResurfaceHistoricalQuestionMark()
    {
        var task = new PlanTask(
            "PLAN-007", "Review", "Review", [], "high", PlanTaskStatus.Complete,
            Commit: "abcdef123456");
        var gate = new PlanApprovalGate(
            "GATE-007", "Observe", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement("visual", "live-ui-observation", "Observe", "Does it work?")]);
        var plan = TestPlan(null) with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = [task],
            ApprovalGates = [gate],
        };
        var target = PlanHumanReviewChecklist.Build(plan, [gate]).Single();
        var checkedPlan = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            plan, gate.GateId, target.ItemId, true, target.CandidateCommit, target.ReviewTarget);
        var clearedPlan = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            checkedPlan, gate.GateId, target.ItemId, false, target.CandidateCommit, target.ReviewTarget);

        var cleared = PlanHumanReviewChecklist.Build(clearedPlan, [clearedPlan.ApprovalGates.Single()]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(cleared.IsChecked, Is.False);
            Assert.That(cleared.WasPreviouslyChecked, Is.False);
        });
    }

    [TestCase(PlanAttentionCategory.AutomaticWorkExhausted, "Automatic Work Is Exhausted", false)]
    [TestCase(PlanAttentionCategory.HumanInputRequired, "Human Input Is Required", false)]
    [TestCase(PlanAttentionCategory.UnexpectedState, "Unexpected State Encountered", true)]
    public void AttentionCard_UsesPersistedCategoryWithoutParsingReason(
        string category,
        string expectedTitle,
        bool expectedDiagnostics)
    {
        var plan = TestPlan(null) with
        {
            InterruptionData = TestPlan(null).InterruptionData! with
            {
                Reason = "opaque recorded reason",
                AttentionCategory = category,
                AttentionSummary = "Recorded evidence summary.",
            }
        };

        var presentation = PlanAttentionCardPresentationBuilder.Build(
            plan, "PLAN-001", humanReview: null, hasHumanChecklist: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Category, Is.EqualTo(category));
            Assert.That(presentation.Title, Is.EqualTo(expectedTitle));
            Assert.That(presentation.RecordedReason, Is.EqualTo("Recorded evidence summary."));
            Assert.That(presentation.ShowDiagnosticsActions, Is.EqualTo(expectedDiagnostics));
        });
    }

    [Test]
    public void AttentionCard_HumanVerificationRequiresActionableChecklistNotQuestionAlone()
    {
        var plan = TestPlan(null) with
        {
            InterruptionData = TestPlan(null).InterruptionData! with
            {
                AttentionCategory = PlanAttentionCategory.HumanVerificationRequired,
            }
        };
        var missingQuestion = new PlanHumanReviewCardPresentation("Review Required", null, []);
        var concreteQuestion = missingQuestion with { Question = "Does the interaction remain readable?" };

        var rejected = PlanAttentionCardPresentationBuilder.Build(
            plan, "PLAN-001", missingQuestion, hasHumanChecklist: false);
        var questionOnly = PlanAttentionCardPresentationBuilder.Build(
            plan, "PLAN-001", concreteQuestion, hasHumanChecklist: false);
        var accepted = PlanAttentionCardPresentationBuilder.Build(
            plan, "PLAN-001", concreteQuestion, hasHumanChecklist: true);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.Category, Is.EqualTo(PlanAttentionCategory.UnexpectedState));
            Assert.That(rejected.Title, Is.EqualTo("Unexpected State Encountered"));
            Assert.That(questionOnly.Category, Is.EqualTo(PlanAttentionCategory.UnexpectedState));
            Assert.That(accepted.Category, Is.EqualTo(PlanAttentionCategory.HumanVerificationRequired));
            Assert.That(accepted.Title, Is.EqualTo("Human Verification Is Required"));
            Assert.That(accepted.RecordedReason, Is.EqualTo("Does the interaction remain readable?"));
        });
    }

    [Test]
    public void HumanizePlanText_ReplacesInternalTaskIdAndPrefixesRepairStepLabel()
    {
        var task = new PlanTask(
            "PLAN-AMD-001",
            "Stabilize tests",
            "Stabilize tests",
            [],
            "high",
            PlanTaskStatus.Pending,
            DisplayStepLabel: "15 Repair 1");
        var plan = TestPlan(null) with { Tasks = [task] };

        var result = PlanRecoveryPresentationBuilder.HumanizePlanText(
            plan,
            "Task PLAN-AMD-001 failed. 15 Repair 1 needs verification.");

        Assert.That(result, Is.EqualTo(
            "Step 15 Repair 1 failed. Step 15 Repair 1 needs verification."));
    }

    [Test]
    public void AttentionCard_WithPreservedCommitAndNoCurrentReview_UsesPlainVerificationMessage()
    {
        const string commit = "ba178610112bf6d7cbc8237eabbaad21465bdfd3";
        var task = new PlanTask(
            "PLAN-AMD-001",
            "Stabilize tests",
            "Stabilize tests",
            [],
            "high",
            PlanTaskStatus.Pending,
            DisplayStepLabel: "15 Repair 1");
        var evidence = new PlanTaskCommitEvidence(
            task.TaskId,
            "attempt-1",
            "300baebabd377af8f47d10fa02c35512323b53ca",
            commit,
            "Preserved work.",
            new DecomposeStepVerification("passed", "dotnet build", "Build passed"));
        var plan = TestPlan(evidence) with
        {
            Tasks = [task],
            InterruptionData = TestPlan(evidence).InterruptionData! with
            {
                AttentionCategory = PlanAttentionCategory.AutomaticWorkExhausted,
                AttentionSummary = $"Task {task.TaskId} did not complete.",
            }
        };

        var presentation = PlanAttentionCardPresentationBuilder.Build(
            plan, task.TaskId, humanReview: null, hasHumanChecklist: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Title, Is.EqualTo("Step Needs Verification"));
            Assert.That(presentation.RecordedReason, Does.Contain("ba17861"));
            Assert.That(presentation.RecordedReason, Does.Not.Contain(commit));
            Assert.That(presentation.RecordedReason, Does.Not.Contain(task.TaskId));
            Assert.That(presentation.RecordedReason, Does.Contain("No independent review is recorded"));
        });
    }

    [Test]
    public void RecoveryActionSet_WithPreservedCommitAndNoCurrentReview_OffersAcceptanceEverywhere()
    {
        const string commit = "ba178610112bf6d7cbc8237eabbaad21465bdfd3";
        var task = new PlanTask(
            "PLAN-AMD-001", "Stabilize tests", "Stabilize tests", [], "high",
            PlanTaskStatus.Pending, DisplayStepLabel: "14a");
        var evidence = new PlanTaskCommitEvidence(
            task.TaskId, "attempt-1", "300baebabd377af8f47d10fa02c35512323b53ca",
            commit, "Preserved work.",
            new DecomposeStepVerification("passed", "dotnet build", "Build passed"));
        var plan = TestPlan(evidence) with
        {
            Tasks = [task],
            InterruptionData = TestPlan(evidence).InterruptionData! with
            {
                InterruptedTaskId = task.TaskId,
                AttentionCategory = PlanAttentionCategory.AutomaticWorkExhausted,
            },
        };

        var presentation = PlanRecoveryActionSetBuilder.Build(
            plan, task.TaskId, currentHead: commit);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Primary?.Kind,
                Is.EqualTo(PlanRecoverySurfaceActionKind.AcceptStep));
            Assert.That(presentation.Primary?.Label,
                Is.EqualTo("Step 14a Is Complete & Continue"));
            Assert.That(presentation.Actions.Select(action => action.Label),
                Does.Not.Contain("Resume Verification for Step 14a"));
            Assert.That(presentation.Actions.Single(action =>
                    action.Kind == PlanRecoverySurfaceActionKind.EndPlan).Tone,
                Is.EqualTo(QuickReplyTone.Destructive));
            Assert.That(presentation.Actions.Last().Kind,
                Is.EqualTo(PlanRecoverySurfaceActionKind.EndPlan));
        });
    }

    [Test]
    public void AcceptedWorkContinuation_IgnoresStaleFailurePresentationAndOffersOneResume()
    {
        var completed = new PlanTask(
            "PLAN-AMD-001", "Stabilize tests", "Stabilize tests", [], "high",
            PlanTaskStatus.Complete, Commit: "dfcac58", DisplayStepLabel: "14a");
        var next = new PlanTask(
            "PLAN-AMD-002", "Establish rollback", "Establish rollback", [completed.TaskId], "high",
            PlanTaskStatus.Pending, DisplayStepLabel: "14b");
        var staleEvidence = new PlanTaskCommitEvidence(
            completed.TaskId, "attempt-1", "300baeb", "dfcac58", "Old evidence.");
        var plan = TestPlan(staleEvidence) with
        {
            Tasks = [completed, next],
            Progress = new PlanProgress(1, 2),
            IsPaused = true,
            InterruptionData = TestPlan(staleEvidence).InterruptionData! with
            {
                Reason = PlanRecoveryResumePolicy.BuildAcceptedWorkContinuationReason(
                    "Preserved work for PLAN-AMD-001 was adopted as dfcac58."),
                InterruptedTaskId = next.TaskId,
                LastCompletedTaskId = completed.TaskId,
                AttentionCategory = PlanAttentionCategory.AutomaticWorkExhausted,
                AttentionSummary = "The old result envelope failed.",
            },
        };

        var card = PlanAttentionCardPresentationBuilder.Build(
            plan, next.TaskId, humanReview: null, hasHumanChecklist: false);
        var actions = PlanRecoveryActionSetBuilder.Build(plan, next.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(card.Category, Is.EqualTo(PlanAttentionCategory.ReadyToResume));
            Assert.That(card.Title, Is.EqualTo("Step Complete — Ready to Resume"));
            Assert.That(card.Explanation, Is.EqualTo("Step 14a is complete. Step 14b has not started."));
            Assert.That(card.RecordedReason, Does.Contain("Resume it to start Step 14b"));
            Assert.That(card.RecordedReason, Does.Not.Contain("exhausted").IgnoreCase);
            Assert.That(actions.Primary?.Kind, Is.EqualTo(PlanRecoverySurfaceActionKind.ResumePlan));
            Assert.That(actions.Actions.Select(action => action.Kind), Is.EqualTo(new[]
            {
                PlanRecoverySurfaceActionKind.ResumePlan,
                PlanRecoverySurfaceActionKind.EndPlan,
            }));
            Assert.That(actions.Actions[0].Label, Is.EqualTo("Resume Plan"));
        });
    }

    [Test]
    public void AcceptedWorkContinuationCheckpoint_DropsAllPriorFailureState()
    {
        var staleEvidence = new PlanTaskCommitEvidence(
            "PLAN-AMD-001", "attempt-1", "300baeb", "dfcac58", "Old evidence.");
        var previous = new PlanInterruptionData(
            "Old failure",
            PlanRecoveryState.PendingRecovery,
            7,
            InterruptedTaskId: "PLAN-AMD-001",
            TaskCommitEvidence: staleEvidence,
            RecoveryAssessment: new PlanRecoveryDecisionEvidence(
                "Old assessment", [], DateTimeOffset.UtcNow),
            RecoveryCorrection: new PlanRecoveryCorrectionContext("300baeb", DateTimeOffset.UtcNow),
            LastCorrectionError: "Old correction error",
            WorkCompletePendingAcceptance: new PlanWorkCompletePendingAcceptance(
                "PLAN-AMD-001", "{}", ["Old validation problem"], DateTimeOffset.UtcNow),
            AttentionCategory: PlanAttentionCategory.AutomaticWorkExhausted,
            AttentionSummary: "Old failure summary");

        var checkpoint = PlanRecoveryAcceptancePolicy.CreateAcceptedWorkContinuation(
            previous,
            "PLAN-AMD-001",
            "PLAN-AMD-002",
            "dfcac58",
            ["Viewport.razor"],
            "dfcac58");

        Assert.Multiple(() =>
        {
            Assert.That(checkpoint.LoopIteration, Is.EqualTo(7));
            Assert.That(checkpoint.LastCompletedTaskId, Is.EqualTo("PLAN-AMD-001"));
            Assert.That(checkpoint.InterruptedTaskId, Is.EqualTo("PLAN-AMD-002"));
            Assert.That(checkpoint.LastCommit, Is.EqualTo("dfcac58"));
            Assert.That(checkpoint.AffectedPaths, Is.EqualTo(new[] { "Viewport.razor" }));
            Assert.That(checkpoint.TaskCommitEvidence, Is.Null);
            Assert.That(checkpoint.RecoveryAssessment, Is.Null);
            Assert.That(checkpoint.RecoveryCorrection, Is.Null);
            Assert.That(checkpoint.LastCorrectionError, Is.Null);
            Assert.That(checkpoint.WorkCompletePendingAcceptance, Is.Null);
            Assert.That(checkpoint.AttentionCategory, Is.Null);
            Assert.That(checkpoint.AttentionSummary, Is.Null);
        });
    }

    [Test]
    public void RecoveryActionSet_WithoutRecordedEvidence_StillOffersExplicitAcceptance()
    {
        var task = new PlanTask(
            "PLAN-001", "Stabilize tests", "Stabilize tests", [], "high",
            PlanTaskStatus.Executing, DisplayStepLabel: "14a");
        var plan = TestPlan(null) with { Tasks = [task] };

        var presentation = PlanRecoveryActionSetBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Primary?.Kind,
                Is.EqualTo(PlanRecoverySurfaceActionKind.AcceptStep));
            Assert.That(presentation.Primary?.PrimaryKind,
                Is.EqualTo(PlanRecoveryPrimaryActionKind.AcceptUnattributedStep));
            Assert.That(presentation.Primary?.Label,
                Is.EqualTo("Step 14a Is Complete & Continue"));
        });
    }

    [Test]
    public void LifecycleToolbar_IsHiddenWhileCanonicalRecoveryActionsOwnTheDecision()
    {
        var plan = TestPlan(null);
        var actions = PlanRecoveryActionSetBuilder.Build(plan, "PLAN-001");

        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryLifecycleControlPolicy.ShouldShowOrdinaryControls(plan, actions),
                Is.False);
            Assert.That(PlanRecoveryLifecycleControlPolicy.ShouldShowOrdinaryControls(
                    plan with { LifecycleStatus = PlanLifecycleStatus.Executing, InterruptionData = null },
                    actions),
                Is.True);
        });
    }

    [Test]
    public void RecoveryActionSet_WithDurableCorrection_ContinuesExistingRepair()
    {
        var task = new PlanTask(
            "PLAN-001", "Repair camera state", "Repair camera state", [], "high",
            PlanTaskStatus.Pending, DisplayStepLabel: "14a");
        var plan = TestPlan(null) with
        {
            Tasks = [task],
            InterruptionData = TestPlan(null).InterruptionData! with
            {
                RecoveryCorrection = new PlanRecoveryCorrectionContext(
                    "ba178610112bf6d7cbc8237eabbaad21465bdfd3",
                    DateTimeOffset.UtcNow),
            },
        };

        var presentation = PlanRecoveryActionSetBuilder.Build(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Primary?.Kind,
                Is.EqualTo(PlanRecoverySurfaceActionKind.ContinueWorking));
            Assert.That(presentation.Primary?.Label, Is.EqualTo("Continue Existing Repair"));
            Assert.That(presentation.Primary?.ProtocolAction, Is.EqualTo("request-corrections"));
        });
    }

    [Test]
    public void RecoveryActionSet_WithoutDurablePlan_UsesConservativeSharedActions()
    {
        var presentation = PlanRecoveryActionSetBuilder.BuildConservativeFallback("Step 14a");

        Assert.That(presentation.Actions.Select(action => (action.Kind, action.Label)), Is.EqualTo(new[]
        {
            (PlanRecoverySurfaceActionKind.ContinueWorking, "Continue Working on Step 14a"),
            (PlanRecoverySurfaceActionKind.ReviseRemainingPlan, "✎ Revise Remaining Plan…"),
        }));
    }

    [Test]
    public void AttentionCard_CopyTextIncludesVisibleReasonAndDiagnostics()
    {
        var presentation = new PlanAttentionCardPresentation(
            PlanAttentionCategory.UnexpectedState,
            "Unexpected State Encountered",
            "SquadDash stopped safely.",
            "The saved state was inconsistent.",
            ShowDiagnosticsActions: true);

        var copied = PlanAttentionCardPresentationBuilder.BuildCopyText(
            presentation,
            "Plan",
            "Step 4",
            "Integrate authoring",
            "Execution attempt: attempt-1");

        Assert.Multiple(() =>
        {
            Assert.That(copied, Does.Contain("Unexpected State Encountered"));
            Assert.That(copied, Does.Contain("What happened: The saved state was inconsistent."));
            Assert.That(copied, Does.Contain("Execution attempt: attempt-1"));
        });
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

    private static PlanTask ReviewTask() => new(
        "PLAN-007",
        "Commission hardware",
        "Prepare the bounded commissioning action.",
        [],
        "high",
        PlanTaskStatus.HumanReviewRequired,
        Handoff: new PlanTaskHandoff(
            "abcdef1234567",
            "Prepared commissioning.",
            ["commissioning.cs"],
            new DecomposeStepVerification("passed", "dotnet test", "Tests passed."),
            DateTimeOffset.UtcNow),
        DisplayStepLabel: "7");

    private static PlanApprovalGate ReviewGate(
        string gateId,
        string taskId,
        string downstreamTaskId,
        string requirementId,
        string question) => new(
        gateId,
        question,
        [taskId],
        [downstreamTaskId],
        PlanGateStatus.Pending,
        ProofRequirements:
        [
            new PlanTaskProofRequirement(
                requirementId,
                "human-observation",
                question,
                Question: question,
                AccessMode: HumanVerificationAccessMode.Recipe,
                VerificationRecipe: new HumanVerificationRecipe(
                    "Prepare the safe test state.",
                    "Open the prepared fixture and perform the bounded action.",
                    "Observe the stated result.",
                    "Stop and restore the safe state.")),
        ],
        Question: question);
}

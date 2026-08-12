using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class TranscriptApprovalCardBuilderTests
{
    private static Plan BuildTestPlan()
    {
        var tasks = new[]
        {
            new PlanTask("task-1", "Task 1", "Done", [], "high", PlanTaskStatus.Complete),
            new PlanTask("task-2", "Task 2", "Done", [], "high", PlanTaskStatus.Complete),
            new PlanTask("task-3", "Task 3", "Next", ["task-1"], "high", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            "gate-1", "Review completed tasks before continuing", ["task-1", "task-2"], ["task-3"],
            PlanGateStatus.AwaitingApproval);
        return new Plan(
            "PLAN-001", "rev-1", PlanSource.DecomposeDecision, PlanLifecycleStatus.AwaitingApproval,
            "Test Plan", "main", "Summary", tasks, [gate], new PlanProgress(2, 3),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot BuildTestSnapshot(
        int completedTaskCount = 3,
        int totalTaskCount = 5,
        int changedFileCount = 0,
        int downstreamTaskCount = 0)
    {
        var completedTasks = new List<ReviewTaskEntry>
        {
            new("task-1", "Implement auth module", "Added JWT auth",
                new List<ReviewCommitEntry>
                {
                    new(new CommitLink("abc1234", "abc1234567890", "Add JWT auth"),
                        VerificationPassed: true,
                        ChangedFiles: []),
                }),
            new("task-2", "Add user model", null,
                new List<ReviewCommitEntry>
                {
                    new(new CommitLink("def5678", "def5678901234", "Add user model"),
                        VerificationPassed: null,
                        ChangedFiles: []),
                }),
        };

        var changedFiles = Enumerable.Range(0, changedFileCount)
            .Select(i => new ChangedFileEntry(
                $"src/file{i}.cs",
                i % 3 == 0 ? FileChangeStatus.Added : FileChangeStatus.Modified,
                Insertions: 10 + i,
                Deletions: i,
                CommitSha: "abc1234567890",
                Link: new FileLink($"src/file{i}.cs", "abc1234567890")))
            .ToList();

        var downstreamTasks = Enumerable.Range(0, downstreamTaskCount)
            .Select(i => new DownstreamTaskEntry($"ds-{i}", $"Downstream task {i}", "pending"))
            .ToList();

        return new ApprovalReviewSnapshot(
            PlanId: "PLAN-001",
            PlanTitle: "Test Plan",
            CompletedTaskCount: completedTaskCount,
            TotalTaskCount: totalTaskCount,
            CurrentStage: null,
            GateId: "gate-1",
            GateReason: "Review completed tasks before continuing",
            AfterTaskIds: ["task-1", "task-2"],
            BeforeTaskIds: ["task-3", "task-4"],
            CompletedTasks: completedTasks,
            DownstreamTasks: downstreamTasks,
            AllChangedFiles: changedFiles,
            IndependentWork: [],
            BuiltAt: DateTimeOffset.UtcNow);
    }

    [Test]
    public void BuildApproveLabel_SingleGate_ReturnsCheckpointLabel()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(1);
        Assert.That(label, Is.EqualTo("Approve checkpoint and continue"));
        Assert.That(label, Does.Not.Contain("2"));
        Assert.That(label, Does.Not.Contain("✅"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ShowResolvedState_HidesEditorAndKeepsApprovedIndicatorAtFullContrast()
    {
        var plan = BuildTestPlan();
        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(), plan, plan.ApprovalGates[0], 14, _ => { });
        card.NoteTextBox.Text = "Reviewed locally";

        TranscriptApprovalCardBuilder.ShowResolvedState(card);

        Assert.Multiple(() =>
        {
            Assert.That(card.NoteSection.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(card.ResolutionNote.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(card.ResolutionNote.Text, Is.EqualTo("Approval note: Reviewed locally"));
            Assert.That(card.ActionsPanel.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(card.ResolvedIndicator.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(card.ResolvedIndicator.Text, Is.EqualTo("✓ Approved."));
            Assert.That(card.TitleBlock.Text, Is.EqualTo("Approval Acquired"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Build_PlanAndEvidenceAreActionableLinks()
    {
        var plan = BuildTestPlan();
        var openedPlan = false;
        var openedInbox = false;
        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(),
            plan,
            plan.ApprovalGates[0],
            14,
            _ => { },
            onOpenPlan: () => openedPlan = true,
            onOpenInbox: () => openedInbox = true);

        card.PlanLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        card.InboxMessageLink!.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(openedPlan, Is.True);
            Assert.That(openedInbox, Is.True);
            Assert.That(card.CommitLinks, Is.Empty);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Build_WithQuestion_UsesPlanTitleLinkWithoutRedundantInspectionShortcut()
    {
        var original = BuildTestPlan();
        var plan = original with
        {
            ApprovalGates =
            [
                original.ApprovalGates[0] with
                {
                    Question = "Does clicking an item show a selection highlight? Is the splitter draggable?",
                },
            ],
        };
        var openedPlan = false;
        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(), plan, plan.ApprovalGates[0], 14, _ => { },
            onOpenPlan: () => openedPlan = true);

        card.PlanLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(card.QuestionBlock, Is.Not.Null);
            Assert.That(card.QuestionBlock!.Text, Does.StartWith("Does clicking an item"));
            Assert.That(card.QuestionBlock.FontWeight, Is.EqualTo(FontWeights.SemiBold));
            Assert.That(card.InspectPlanLink, Is.Null);
            Assert.That(openedPlan, Is.True);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Build_FullEvidenceLink_OpensTheSelectedStepEvidence()
    {
        var plan = BuildTestPlan();
        ReviewTaskEntry? openedTask = null;
        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(),
            plan,
            plan.ApprovalGates[0],
            14,
            _ => { },
            onOpenEvidence: task => openedTask = task);

        card.InboxLink!.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.That(openedTask?.TaskId, Is.EqualTo("task-1"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Build_DefaultCardIsCompactAndPointsToInboxForFullEvidence()
    {
        var plan = BuildTestPlan();
        var openedInbox = false;
        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(changedFileCount: 5, downstreamTaskCount: 2),
            plan,
            plan.ApprovalGates[0],
            14,
            _ => { },
            onOpenInbox: () => openedInbox = true);
        card.InboxLink!.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        var directText = card.ContentStack.Children
            .OfType<TextBlock>()
            .Select(block => new TextRange(block.ContentStart, block.ContentEnd).Text)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(directText, Has.Some.Contains("Full evidence is here."));
            Assert.That(directText, Has.Some.Contains("See your inbox message for more detail."));
            Assert.That(directText, Has.None.Contains("Gate:"));
            Assert.That(directText, Has.None.Contains("Review completed tasks before continuing"));
            Assert.That(directText, Has.None.Contains("unblocked by approval"));
            Assert.That(card.ContentStack.Children.OfType<Expander>(), Is.Empty);
            Assert.That(card.CommitLinks, Is.Empty);
            Assert.That(openedInbox, Is.True);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Build_MultiplePendingCheckpoints_SummarizesQuestionsAndStepsInPlanOrder()
    {
        var original = BuildTestPlan();
        var tasks = original.Tasks.Select((task, index) => task with
        {
            DisplayStepLabel = (index + 6).ToString(),
        }).ToArray();
        var gate6 = original.ApprovalGates[0] with
        {
            AfterTaskIds = ["task-1"],
            Question = "Does the Step 6 behavior work?",
        };
        var gate7 = new PlanApprovalGate(
            "gate-2",
            "Review Step 7",
            ["task-2"],
            ["task-3"],
            PlanGateStatus.AwaitingApproval,
            Question: "Does the Step 7 transcript look correct?");
        var plan = original with { Tasks = tasks, ApprovalGates = [gate6, gate7] };

        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(), plan, gate6, 14, _ => { }, onOpenInbox: () => { });
        var directText = card.ContentStack.Children
            .OfType<TextBlock>()
            .Select(block => new TextRange(block.ContentStart, block.ContentEnd).Text)
            .ToArray();
        var helpText = AutomationProperties.GetHelpText((Border)card.Container.Child);

        Assert.Multiple(() =>
        {
            Assert.That(helpText, Does.Contain("Does the Step 6 behavior work?"));
            Assert.That(helpText, Does.Contain("Does the Step 7 transcript look correct?"));
            Assert.That(directText, Has.Some.Contains("Step 6 ready for review. Full evidence is here."));
            Assert.That(directText, Has.Some.Contains("Step 7 ready for review. Full evidence is here."));
            var step7Index = Array.FindIndex(directText,
                text => text.Contains("Step 7 ready for review.", StringComparison.Ordinal));
            var inboxIndex = Array.FindIndex(directText,
                text => text.Contains("See your inbox message for more detail.", StringComparison.Ordinal));
            Assert.That(inboxIndex, Is.GreaterThan(step7Index));
            Assert.That(card.ApproveButton.Content?.ToString(),
                Is.EqualTo("Approve both checkpoints and continue"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusNoteEditorAtPoint_PlacesCaretWhenHostedInReadOnlyTranscript()
    {
        var plan = BuildTestPlan();
        var card = TranscriptApprovalCardBuilder.Build(
            BuildTestSnapshot(), plan, plan.ApprovalGates[0], 14, _ => { });
        card.NoteTextBox.Text = "Reviewed";

        TranscriptApprovalCardBuilder.FocusNoteEditorAtPoint(
            card.NoteTextBox,
            new Point(double.MaxValue, double.MaxValue));

        Assert.That(card.NoteTextBox.CaretIndex, Is.EqualTo(card.NoteTextBox.Text.Length));
    }

    [Test]
    public void BuildApproveLabel_MultipleGates_UsesAllWording()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(3);
        Assert.That(label, Is.EqualTo("Approve all checkpoints and continue"));
    }

    [Test]
    public void Snapshot_ContainsExpectedFields()
    {
        var snapshot = BuildTestSnapshot(changedFileCount: 5, downstreamTaskCount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.PlanId, Is.EqualTo("PLAN-001"));
            Assert.That(snapshot.PlanTitle, Is.EqualTo("Test Plan"));
            Assert.That(snapshot.CompletedTasks, Has.Count.EqualTo(2));
            Assert.That(snapshot.AllChangedFiles, Has.Count.EqualTo(5));
            Assert.That(snapshot.DownstreamTasks, Has.Count.EqualTo(2));
            Assert.That(snapshot.GateReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Snapshot_CommitLink_HasExpectedProperties()
    {
        var snapshot = BuildTestSnapshot();
        var commit = snapshot.CompletedTasks[0].Commits[0];

        Assert.Multiple(() =>
        {
            Assert.That(commit.Link.ShortSha, Is.EqualTo("abc1234"));
            Assert.That(commit.Link.FullSha, Is.EqualTo("abc1234567890"));
            Assert.That(commit.Link.Subject, Is.EqualTo("Add JWT auth"));
            Assert.That(commit.Link.InternalUri, Is.EqualTo("app://commit-diff:abc1234567890"));
            Assert.That(commit.VerificationPassed, Is.True);
        });
    }

    [Test]
    public void Snapshot_ChangedFileEntry_StatusMapping()
    {
        var added = new ChangedFileEntry("a.cs", FileChangeStatus.Added, 10, 0, "sha", new FileLink("a.cs", "sha"));
        var deleted = new ChangedFileEntry("b.cs", FileChangeStatus.Deleted, 0, 5, "sha", new FileLink("b.cs", "sha"));
        var modified = new ChangedFileEntry("c.cs", FileChangeStatus.Modified, 3, 2, "sha", new FileLink("c.cs", "sha"));

        Assert.Multiple(() =>
        {
            Assert.That(added.Status, Is.EqualTo(FileChangeStatus.Added));
            Assert.That(deleted.Status, Is.EqualTo(FileChangeStatus.Deleted));
            Assert.That(modified.Status, Is.EqualTo(FileChangeStatus.Modified));
        });
    }

    [Test]
    public void TranscriptApprovalCardTag_Identity()
    {
        var tag1 = new TranscriptApprovalCardTag("plan-1", "gate-1", 3);
        var tag2 = new TranscriptApprovalCardTag("plan-1", "gate-1", 3);
        var tag3 = new TranscriptApprovalCardTag("plan-1", "gate-2", 3);

        Assert.Multiple(() =>
        {
            Assert.That(tag1, Is.EqualTo(tag2));
            Assert.That(tag1, Is.Not.EqualTo(tag3));
            Assert.That(tag1.PlanId, Is.EqualTo("plan-1"));
            Assert.That(tag1.GateId, Is.EqualTo("gate-1"));
            Assert.That(tag1.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public void BuildApproveLabel_ZeroGates_ReturnsCheckpointLabel()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(0);
        Assert.That(label, Is.EqualTo("Approve checkpoint and continue"));
        Assert.That(label, Does.Not.Contain("0"));
    }

    [Test]
    public void BuildApproveLabel_ExactlyTwoGates_UsesBothWording()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(2);
        Assert.That(label, Is.EqualTo("Approve both checkpoints and continue"));
    }

    [Test]
    public void BuildApproveLabel_SingleGate_DoesNotContainReadyCheckpoints()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(1);
        Assert.That(label, Does.Not.Contain("Ready Checkpoints"));
        Assert.That(label, Is.EqualTo("Approve checkpoint and continue"));
    }

    [Test]
    public void Snapshot_WithNoCompletedTasks_HasEmptyList()
    {
        var snapshot = new ApprovalReviewSnapshot(
            PlanId: "PLAN-EMPTY",
            PlanTitle: "Empty Plan",
            CompletedTaskCount: 0,
            TotalTaskCount: 3,
            CurrentStage: "stage-1",
            GateId: "gate-1",
            GateReason: "Early checkpoint",
            AfterTaskIds: [],
            BeforeTaskIds: ["task-1"],
            CompletedTasks: [],
            DownstreamTasks: [],
            AllChangedFiles: [],
            IndependentWork: [],
            BuiltAt: DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CompletedTasks, Is.Empty);
            Assert.That(snapshot.AllChangedFiles, Is.Empty);
            Assert.That(snapshot.DownstreamTasks, Is.Empty);
            Assert.That(snapshot.CurrentStage, Is.EqualTo("stage-1"));
        });
    }

    [Test]
    public void Snapshot_WithCurrentStage_IncludesStageInProperties()
    {
        var snapshot = BuildTestSnapshot();

        Assert.That(snapshot.CurrentStage, Is.Null);

        var snapshotWithStage = new ApprovalReviewSnapshot(
            PlanId: snapshot.PlanId,
            PlanTitle: snapshot.PlanTitle,
            CompletedTaskCount: snapshot.CompletedTaskCount,
            TotalTaskCount: snapshot.TotalTaskCount,
            CurrentStage: "deployment",
            GateId: snapshot.GateId,
            GateReason: snapshot.GateReason,
            AfterTaskIds: snapshot.AfterTaskIds,
            BeforeTaskIds: snapshot.BeforeTaskIds,
            CompletedTasks: snapshot.CompletedTasks,
            DownstreamTasks: snapshot.DownstreamTasks,
            AllChangedFiles: snapshot.AllChangedFiles,
            IndependentWork: snapshot.IndependentWork,
            BuiltAt: snapshot.BuiltAt);

        Assert.That(snapshotWithStage.CurrentStage, Is.EqualTo("deployment"));
    }

    [Test]
    public void Snapshot_ManyDownstreamTasks_AllPreserved()
    {
        var snapshot = BuildTestSnapshot(downstreamTaskCount: 8);

        Assert.That(snapshot.DownstreamTasks, Has.Count.EqualTo(8));
        Assert.That(snapshot.DownstreamTasks[7].Title, Is.EqualTo("Downstream task 7"));
    }

    [Test]
    public void Snapshot_ManyChangedFiles_AllPreserved()
    {
        var snapshot = BuildTestSnapshot(changedFileCount: 60);

        Assert.That(snapshot.AllChangedFiles, Has.Count.EqualTo(60));
        Assert.That(snapshot.AllChangedFiles[0].Status, Is.EqualTo(FileChangeStatus.Added));
        Assert.That(snapshot.AllChangedFiles[1].Status, Is.EqualTo(FileChangeStatus.Modified));
    }

    [Test]
    public void TranscriptApprovalCardTag_DifferentVersion_NotEqual()
    {
        var tag1 = new TranscriptApprovalCardTag("plan-1", "gate-1", 3);
        var tag2 = new TranscriptApprovalCardTag("plan-1", "gate-1", 4);

        Assert.That(tag1, Is.Not.EqualTo(tag2));
    }

    [Test]
    public void CommitLink_InternalUri_Format()
    {
        var link = new CommitLink("abc1234", "abc1234567890abcdef", "Fix bug");

        Assert.Multiple(() =>
        {
            Assert.That(link.InternalUri, Is.EqualTo("app://commit-diff:abc1234567890abcdef"));
            Assert.That(link.ShortSha, Is.EqualTo("abc1234"));
        });
    }

    [Test]
    public void Snapshot_UnverifiedCommit_HasNullVerification()
    {
        var snapshot = BuildTestSnapshot();
        var secondCommit = snapshot.CompletedTasks[1].Commits[0];

        Assert.That(secondCommit.VerificationPassed, Is.Null);
    }

    [Test]
    public void FileLink_UriFormats_AreCorrect()
    {
        var link = new FileLink("src/auth.cs", "abc1234567890");

        Assert.Multiple(() =>
        {
            Assert.That(link.ReviewedVersionUri, Is.EqualTo("app://file-at-commit:abc1234567890:src/auth.cs"));
            Assert.That(link.WorkspaceFileUri, Is.EqualTo("app://open-workspace-file:src/auth.cs"));
        });
    }

    [Test]
    public void IndependentWorkEntry_PreservesFields()
    {
        var commits = new List<ReviewCommitEntry>
        {
            new(new CommitLink("aaa1111", "aaa1111222233334444", "Refactor utils"),
                VerificationPassed: true,
                ChangedFiles: []),
        };
        var entry = new IndependentWorkEntry("ind-1", "Independent refactor", "Cleaned up utilities", commits);

        Assert.Multiple(() =>
        {
            Assert.That(entry.TaskId, Is.EqualTo("ind-1"));
            Assert.That(entry.Title, Is.EqualTo("Independent refactor"));
            Assert.That(entry.CompletionSummary, Is.EqualTo("Cleaned up utilities"));
            Assert.That(entry.Commits, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Snapshot_DownstreamTasks_ExactlyFive_AllPreserved()
    {
        var snapshot = BuildTestSnapshot(downstreamTaskCount: 5);

        Assert.That(snapshot.DownstreamTasks, Has.Count.EqualTo(5));
        Assert.That(snapshot.DownstreamTasks[4].Title, Is.EqualTo("Downstream task 4"));
    }
}

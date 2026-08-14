using System;
using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanVisualizerReviewTests
{
    [Test]
    public void Parser_ExtractsTheLastBalancedPayloadAndNormalizesFields()
    {
        var response = "prose\n" + PlanVisualizerReviewParser.Marker + """
            {
              "groupId":"EDITPLAN-20260814",
              "baseRevision":"abc123",
              "summary":" Review ",
              "annotations":[{
                "targetKind":"task",
                "targetId":"EDITPLAN-20260814-001",
                "severity":"Warning",
                "title":"Check boundary",
                "note":"Inspect the consumer."
              }]
            }
            trailing prose
            """;

        var parsed = PlanVisualizerReviewParser.TryParse(response, out var payload, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(payload!.Summary, Is.EqualTo("Review"));
            Assert.That(payload.Annotations[0].Severity, Is.EqualTo("warning"));
        });
    }

    [TestCase("unknown")]
    [TestCase("")]
    public void Parser_RejectsInvalidAnnotationTargets(string targetKind)
    {
        var response = PlanVisualizerReviewParser.Marker + $$"""
            {"groupId":"g","baseRevision":"r","summary":"s","annotations":[
              {"targetKind":"{{targetKind}}","targetId":"id","severity":"warning","title":"t","note":"n"}
            ]}
            """;

        Assert.That(PlanVisualizerReviewParser.TryParse(response, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void Parser_RejectsUnsupportedOrShortReferenceChanges()
    {
        var response = PlanVisualizerReviewParser.Marker + """
            {"groupId":"g","baseRevision":"r","summary":"s","annotations":[
              {"targetKind":"task","targetId":"g-001","severity":"warning","title":"t","note":"n",
               "change":{"op":"reopenTask","targetRef":"T1"}}
            ]}
            """;

        Assert.That(PlanVisualizerReviewParser.TryParse(response, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("supported staged-plan operation"));
    }

    [Test]
    public void Prompt_IsRevisionLockedAndRequestsSelectiveEntityRetrieval()
    {
        var plan = new PendingDecomposePlan(
            "revision-123", StagedPlanEditorTests.MakeGroup(), DateTimeOffset.UtcNow);
        var prompt = PlanVisualizerReviewParser.BuildPrompt(plan);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("revision-123"));
            Assert.That(prompt, Does.Contain("Read the revision-locked outline first"));
            Assert.That(prompt, Does.Contain("Retrieve only the task, validation, or gate entities needed"));
            Assert.That(prompt, Does.Contain(PlanVisualizerReviewParser.Marker));
        });
    }

    [Test]
    public void InteractiveAnnotations_CarryOnePropertyDeltaThatCanBeAppliedIndependently()
    {
        var group = StagedPlanEditorTests.MakeGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var response = PlanVisualizerReviewParser.Marker + $$$"""
            {
              "groupId":"{{{group.GroupId}}}",
              "baseRevision":"{{{revision}}}",
              "summary":"One suggested correction",
              "annotations":[{
                "targetKind":"task",
                "targetId":"{{{group.Tasks[0].Id}}}",
                "severity":"suggestion",
                "title":"Clarify title",
                "note":"Use an outcome-oriented task title.",
                "actionLabel":"Apply",
                "change":{"op":"updateTask","targetId":"{{{group.Tasks[0].Id}}}","patch":{"title":"Produce first result"}}
              }]
            }
            """;

        Assert.That(PlanVisualizerReviewParser.TryParse(response, out var review, out var error), Is.True, error);
        var pending = new PendingDecomposePlan(revision, group, DateTimeOffset.UtcNow);
        var proposal = new StagedPlanRevisionProposalPayload(
            group.GroupId, revision, review!.Annotations[0].Title,
            [review.Annotations[0].Change!]);
        var result = StagedPlanRevisionApplier.Apply(pending, proposal);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied));
            Assert.That(result.RevisedGroup!.Tasks[0].Title, Is.EqualTo("Produce first result"));
            Assert.That(result.RevisedGroup.Tasks[1].Title, Is.EqualTo(group.Tasks[1].Title));
            Assert.That(result.RevisedGroup.Tasks[1].Description, Is.EqualTo(group.Tasks[1].Description));
            Assert.That(result.RevisedGroup.Tasks[1].DependsOn, Is.EqualTo(group.Tasks[1].DependsOn));
        });
    }

    [Test]
    public void Store_LoadsOnlyTheMatchingPlanRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), "PlanReview_" + Guid.NewGuid().ToString("N"));
        var squad = Path.Combine(root, ".squad");
        try
        {
            var store = new PlanVisualizerReviewStore(squad);
            var payload = new PlanVisualizerReviewPayload(
                "EDITPLAN-20260814", "rev-a", "Summary",
                [new PlanVisualizerReviewAnnotation(
                    "plan", "EDITPLAN-20260814", "suggestion", "Title", "Note")]);
            store.Save(payload);

            Assert.Multiple(() =>
            {
                var loaded = store.Load(payload.GroupId, "rev-a");
                Assert.That(loaded?.GroupId, Is.EqualTo(payload.GroupId));
                Assert.That(loaded?.BaseRevision, Is.EqualTo(payload.BaseRevision));
                Assert.That(loaded?.Annotations, Is.EquivalentTo(payload.Annotations));
                Assert.That(store.Load(payload.GroupId, "rev-b"), Is.Null);
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanFamilyArtifactArchiverTests
{
    private string _root = null!;
    private string _squadFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "SquadDash-PlanFamilyArchive-" + Guid.NewGuid().ToString("N"));
        _squadFolder = Path.Combine(_root, ".squad");
        Directory.CreateDirectory(_squadFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Archive_RetiresEveryActiveRevisionAndPlanMessage_ButPreservesOtherPlans()
    {
        const string planId = "PLAN-ARCHIVED";
        var pendingStore = new PendingDecomposePlanStore(_squadFolder);
        var inboxStore = new InboxStore(_squadFolder);
        var pendingPlan = pendingStore.Save(BuildGroup(planId));

        var planMessage = BuildMessage(
            $"decompose-plan-{planId}-revision-two",
            planId,
            "Pending plan");
        var recoveryMessage = BuildMessage(
            $"decompose-recovery-{planId}-TASK-1-revision-one",
            planId,
            "Blocked plan");
        var completionMessage = BuildMessage(
            $"plan-completion-{planId}-revision-one",
            planId,
            "Plan completed");
        var approvalMessage = new InboxMessage
        {
            Id = DurableApprovalRequestManager.BuildMessageId(planId),
            Subject = "Approval needed",
            Timestamp = DateTimeOffset.UtcNow,
        };
        var unrelatedMessage = BuildMessage(
            "decompose-plan-PLAN-OTHER-revision-one",
            "PLAN-OTHER",
            "Other plan");

        foreach (var message in new[]
                 {
                     planMessage,
                     recoveryMessage,
                     completionMessage,
                     approvalMessage,
                     unrelatedMessage,
                 })
            inboxStore.Save(message);

        var archived = PlanFamilyArtifactArchiver.Archive(planId, pendingStore, inboxStore);

        Assert.Multiple(() =>
        {
            Assert.That(pendingStore.Load(planId), Is.Null);
            Assert.That(File.Exists(Path.Combine(
                _squadFolder,
                "tmp",
                "decompose",
                "archive",
                $"{planId}-{pendingPlan.Revision}.json")), Is.True);
            Assert.That(archived, Is.EquivalentTo(new[]
            {
                planMessage.Id,
                recoveryMessage.Id,
                completionMessage.Id,
                approvalMessage.Id,
            }));
            Assert.That(inboxStore.LoadAll().Select(message => message.Id),
                Is.EqualTo(new[] { unrelatedMessage.Id }));
            Assert.That(inboxStore.Exists(planMessage.Id, includeArchive: true), Is.True);
            Assert.That(inboxStore.Exists(recoveryMessage.Id, includeArchive: true), Is.True);
            Assert.That(inboxStore.Exists(completionMessage.Id, includeArchive: true), Is.True);
            Assert.That(inboxStore.Exists(approvalMessage.Id, includeArchive: true), Is.True);
        });
    }

    [Test]
    public void ArchivedPlanGuards_AreAppliedBeforeLateStagingAndInboxPromotion()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(ExtractMethod(source, "private void TryStartDecomposeGroupFromResponse(", "private bool TryValidateDecomposeRevisionAgainstWorkspace("),
                Does.Contain("IsDurablePlanArchived(group.GroupId)"));
            Assert.That(ExtractMethod(source, "private void PromoteBypassedDecomposePlans(", "private void SaveDecomposePlanInboxReminder("),
                Does.Contain("IsDurablePlanArchived(plan.Group.GroupId)"));
            Assert.That(ExtractMethod(source, "private void SaveDecomposePlanInboxReminder(", "private void ArchiveDecomposePlanInboxReminder("),
                Does.Contain("IsDurablePlanArchived(plan.Group.GroupId)"));
            Assert.That(ExtractMethod(source, "private void ArchivePlan(Plan plan)", "private void RequestPlanPauseAfterCurrentStep("),
                Does.Contain("ArchivePlanFamilyArtifacts(archived.PlanId)"));
        });
    }

    private static InboxMessage BuildMessage(string id, string planId, string subject) => new()
    {
        Id = id,
        Subject = subject,
        Timestamp = DateTimeOffset.UtcNow,
        Attachments =
        [
            new InboxAttachment
            {
                Type = DecomposePlanInbox.AttachmentType,
                Label = "View plan",
                PlanGroupId = planId,
                PlanRevision = "revision-one",
            },
        ],
    };

    private static DecomposedTaskGroup BuildGroup(string planId) => new(
        planId,
        "Archived plan",
        "feature/archive-test",
        "Archive every revision.",
        [new DecomposedSubTask($"{planId}-001", "Do work", [], "high")]);

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"Missing {endMarker}");
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }
}

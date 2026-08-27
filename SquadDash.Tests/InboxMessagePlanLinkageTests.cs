using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxMessagePlanLinkageTests
{
    [Test]
    public void ResolvePlanGroupId_PrefersDecomposePlanAttachment()
    {
        var message = new InboxMessage
        {
            Attachments =
            [
                new InboxAttachment
                {
                    Type = "text",
                    Label = "Legacy reference",
                    PlanGroupId = "PLAN-LEGACY",
                },
                new InboxAttachment
                {
                    Type = DecomposePlanInbox.AttachmentType,
                    Label = "View plan",
                    PlanGroupId = "PLAN-CANONICAL",
                    PlanRevision = "rev-7",
                },
            ],
        };

        Assert.That(message.ResolvePlanGroupId(), Is.EqualTo("PLAN-CANONICAL"));
        Assert.That(message.ResolvePlanAttachment()?.PlanRevision, Is.EqualTo("rev-7"));
    }

    [Test]
    public void ResolvePlanGroupId_FallsBackToLegacyPlanGroupAttachment()
    {
        var message = new InboxMessage
        {
            Attachments =
            [
                new InboxAttachment
                {
                    Type = "text",
                    Label = "Legacy plan id",
                    PlanGroupId = "PLAN-LEGACY",
                },
            ],
        };

        Assert.That(message.ResolvePlanGroupId(), Is.EqualTo("PLAN-LEGACY"));
    }

    [Test]
    public void ResolvePlanGroupId_ReturnsNullWhenNoPlanReferenceExists()
    {
        var message = new InboxMessage
        {
            Attachments =
            [
                new InboxAttachment
                {
                    Type = "url",
                    Label = "Reference",
                    Href = "https://example.com",
                },
            ],
        };

        Assert.That(message.ResolvePlanGroupId(), Is.Null);
        Assert.That(message.ResolvePlanAttachment(), Is.Null);
    }

    [Test]
    public void ResolvePlanGroupId_ToleratesLegacyNullAttachmentsCollection()
    {
        var legacy = new InboxMessage
        {
            Attachments = null!,
        };

        Assert.That(legacy.ResolvePlanGroupId(), Is.Null);
        Assert.That(legacy.ResolvePlanAttachment(), Is.Null);
    }
}

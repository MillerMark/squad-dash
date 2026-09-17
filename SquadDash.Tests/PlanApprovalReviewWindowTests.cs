using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanApprovalReviewWindowTests
{
    [Test]
    public void ReviewWindow_UsesStandardSquadDashChrome() =>
        Assert.That(typeof(PlanApprovalReviewWindow).IsSubclassOf(typeof(ChromedWindow)), Is.True);
}

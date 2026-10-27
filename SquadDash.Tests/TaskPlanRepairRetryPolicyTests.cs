namespace SquadDash.Tests;

[TestFixture]
internal sealed class TaskPlanRepairRetryPolicyTests
{
    [Test]
    public void Evaluate_AllowsFirstRepair()
    {
        var decision = TaskPlanRepairRetryPolicy.Evaluate(
            0,
            [],
            ["issue"],
            null,
            "candidate-a");

        Assert.That(decision.ShouldRetry, Is.True);
    }

    [TestCase(3, 2, true)]
    [TestCase(3, 3, true)]
    [TestCase(3, 4, false)]
    public void Evaluate_SecondRepairAllowsChangedIssuesWithoutRegression(
        int previousCount,
        int currentCount,
        bool expected)
    {
        var previous = Enumerable.Range(1, previousCount).Select(index => $"old-{index}").ToArray();
        var current = Enumerable.Range(1, currentCount).Select(index => $"new-{index}").ToArray();

        var decision = TaskPlanRepairRetryPolicy.Evaluate(
            1,
            previous,
            current,
            "candidate-a",
            "candidate-b");

        Assert.That(decision.ShouldRetry, Is.EqualTo(expected), decision.Reason);
    }

    [Test]
    public void Evaluate_StopsWhenCandidateOrIssuesDidNotChange()
    {
        var sameCandidate = TaskPlanRepairRetryPolicy.Evaluate(
            1,
            ["issue-a"],
            ["issue-b"],
            "candidate-a",
            "candidate-a");
        var sameIssues = TaskPlanRepairRetryPolicy.Evaluate(
            1,
            ["issue-a"],
            [" issue-a "],
            "candidate-a",
            "candidate-b");

        Assert.Multiple(() =>
        {
            Assert.That(sameCandidate.ShouldRetry, Is.False);
            Assert.That(sameIssues.ShouldRetry, Is.False);
        });
    }

    [Test]
    public void Evaluate_StopsAfterTwoRepairs()
    {
        var decision = TaskPlanRepairRetryPolicy.Evaluate(
            2,
            ["issue-a"],
            ["issue-b"],
            "candidate-a",
            "candidate-b");

        Assert.That(decision.ShouldRetry, Is.False);
    }
}

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskFileDiffResolverTests
{
    [Test]
    public void ParseNameStatus_UsesAuthoritativeGitCodesIncludingRenameDestination()
    {
        var result = PlanTaskFileDiffResolver.ParseNameStatus(
            "A\tsrc/Added.cs\nM\tsrc/Changed.cs\nD\tsrc/Deleted.cs\nR100\tsrc/Old.cs\tsrc/New.cs\n");

        Assert.That(result, Is.EqualTo(new[]
        {
            new PlanTaskFileChange("src/Added.cs", FileChangeStatus.Added),
            new PlanTaskFileChange("src/Changed.cs", FileChangeStatus.Modified),
            new PlanTaskFileChange("src/Deleted.cs", FileChangeStatus.Deleted),
            new PlanTaskFileChange("src/New.cs", FileChangeStatus.Renamed),
        }));
    }

    [Test]
    public void ResolveRange_MultipleCommits_UsesRecordedBaselineAndTerminalCommit()
    {
        var task = new PlanTask(
            "T1", "Task", "Description", [], "high", PlanTaskStatus.Complete,
            Commit: "terminal",
            Handoff: new PlanTaskHandoff(
                "terminal", "Done", ["src/File.cs"], null, DateTimeOffset.UtcNow,
                BaselineCommit: "baseline"),
            Commits:
            [
                new PlanEvidenceCommit("first", PlanRecoveryCommitRelation.Unknown, "range"),
                new PlanEvidenceCommit("terminal", PlanRecoveryCommitRelation.Unknown, "range"),
            ]);

        Assert.That(PlanTaskFileDiffResolver.ResolveRange(task),
            Is.EqualTo(new PlanTaskCommitRange("baseline", "terminal")));
    }

    [Test]
    public void ResolveRange_LegacyTask_FallsBackToParentOfFirstRecordedCommit()
    {
        var task = new PlanTask(
            "T1", "Task", "Description", [], "high", PlanTaskStatus.Complete,
            Commit: "terminal",
            Commits:
            [
                new PlanEvidenceCommit("first", PlanRecoveryCommitRelation.Unknown, "range"),
                new PlanEvidenceCommit("terminal", PlanRecoveryCommitRelation.Unknown, "range"),
            ]);

        Assert.That(PlanTaskFileDiffResolver.ResolveRange(task),
            Is.EqualTo(new PlanTaskCommitRange("first^", "terminal")));
    }
}

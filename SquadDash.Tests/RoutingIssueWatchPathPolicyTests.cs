namespace SquadDash.Tests;

[TestFixture]
internal sealed class RoutingIssueWatchPathPolicyTests {
    [TestCase(@"C:\Repo\.squad\team.md", true)]
    [TestCase(@"C:\Repo\.squad\routing.md", true)]
    [TestCase(@"C:\Repo\.squad\agents\lyra-morn\charter.md", true)]
    [TestCase(@"C:\Repo\.squad\agents\lyra-morn\notes.md", false)]
    [TestCase(@"C:\Repo\.squad\decisions.md", false)]
    [TestCase(@"C:\Repo\README.md", false)]
    public void IsRelevantPath_MatchesOnlyRoutingRelatedFiles(string path, bool expected) {
        var result = RoutingIssueWatchPathPolicy.IsRelevantPath(@"C:\Repo\.squad", path);

        Assert.That(result, Is.EqualTo(expected));
    }
}

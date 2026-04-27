namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceIssuePanelStateTests {
    [Test]
    public void BuildDismissalKey_UsesStableVisibleIssueFields() {
        var issue = new WorkspaceIssuePresentation(
            Title: "Squad couldn't finish that prompt",
            Message: "Timed out waiting for the bridge.",
            DetailText: "Retry once, then inspect diagnostics.",
            HelpButtonLabel: "View Diagnostics",
            HelpWindowTitle: "Squad Runtime Diagnostics");

        var key = WorkspaceIssuePanelState.BuildDismissalKey(issue);

        Assert.That(key, Is.EqualTo(
            "Squad couldn't finish that prompt|Timed out waiting for the bridge.|Retry once, then inspect diagnostics.|View Diagnostics|Squad Runtime Diagnostics"));
    }
}

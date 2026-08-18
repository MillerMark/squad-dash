namespace SquadDash.Tests;

[TestFixture]
internal sealed class ReportedCommitReconcilerTests
{
    private const string Head = "d77372ade488034a121e57051ab65abb7ab1bf91";

    [TestCase("d77372a")]
    [TestCase("D77372A")]
    [TestCase("d77372ade488034a121e57051ab65abb7ab1bf91")]
    public void MatchesObservedHead_AcceptsTranscriptPrefix(string transcriptCommit)
    {
        Assert.That(ReportedCommitReconciler.MatchesObservedHead(transcriptCommit, Head), Is.True);
    }

    [TestCase("d773724")]
    [TestCase("d77372")]
    [TestCase("notasha")]
    [TestCase("")]
    public void MatchesObservedHead_RejectsNonMatchingTranscriptCandidate(string transcriptCommit)
    {
        Assert.That(ReportedCommitReconciler.MatchesObservedHead(transcriptCommit, Head), Is.False);
    }

    [TestCase("d773724")]
    [TestCase("D773724")]
    [TestCase("d77372ade488034a121e57051ab65abb7ab1bf99")]
    public void IsSingleCharacterSubstitution_RecognizesOneChangedHexCharacter(string reported)
    {
        Assert.That(ReportedCommitReconciler.IsSingleCharacterSubstitution(reported, Head), Is.True);
    }

    [TestCase("d77372a")]
    [TestCase("d773744")]
    [TestCase("d77372")]
    [TestCase("d77372ade488034a121e57051ab65abb7ab1bf911")]
    [TestCase("notasha")]
    [TestCase("")]
    public void IsSingleCharacterSubstitution_RejectsAnythingOutsideNarrowTypoCase(string reported)
    {
        Assert.That(ReportedCommitReconciler.IsSingleCharacterSubstitution(reported, Head), Is.False);
    }
}

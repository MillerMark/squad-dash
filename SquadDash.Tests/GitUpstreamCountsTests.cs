namespace SquadDash.Tests;

[TestFixture]
internal sealed class GitUpstreamCountsTests
{
    [TestCase("99\t3", 99, 3)]
    [TestCase("99\t0", 99, 0)]
    [TestCase("0\t3", 0, 3)]
    [TestCase("0\t0", 0, 0)]
    [TestCase(" \t99   3\r\n", 99, 3)]
    [TestCase("2147483647\t2147483647", int.MaxValue, int.MaxValue)]
    public void TryParse_ValidCounts_PreservesBehindAheadOrder(string output, int behind, int ahead)
    {
        var parsed = GitUpstreamCounts.TryParse(output, out var counts);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(counts.Behind, Is.EqualTo(behind));
            Assert.That(counts.Ahead, Is.EqualTo(ahead));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \r\n\t")]
    [TestCase("99")]
    [TestCase("99 3 1")]
    [TestCase("-1 3")]
    [TestCase("99 -3")]
    [TestCase("+99 3")]
    [TestCase("99 3.0")]
    [TestCase("2147483648 0")]
    [TestCase("0 2147483648")]
    [TestCase("fatal: no upstream configured")]
    public void TryParse_InvalidCounts_ReturnsUnknown(string? output)
    {
        var parsed = GitUpstreamCounts.TryParse(output, out var counts);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(counts, Is.EqualTo(default(GitUpstreamCounts)));
        });
    }
}

using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class GitCommitReferenceResolverTests
{
    [Test]
    public void ResolvesToCommit_UsesProbeResultAndCachesCandidate()
    {
        var probes = 0;
        var resolver = new GitCommitReferenceResolver(
            () => @"C:\Source\Package",
            (_, candidate) =>
            {
                probes++;
                return candidate == "abc1234";
            });

        Assert.Multiple(() =>
        {
            Assert.That(resolver.ResolvesToCommit("abc1234"), Is.True);
            Assert.That(resolver.ResolvesToCommit("abc1234"), Is.True);
            Assert.That(resolver.ResolvesToCommit("d695fe71913f1ec3"), Is.False);
            Assert.That(resolver.ResolvesToCommit("d695fe71913f1ec3"), Is.False);
            Assert.That(probes, Is.EqualTo(2));
        });
    }

    [Test]
    public void ResolvesToCommit_ClearsCacheWhenWorkspaceChanges()
    {
        var workspace = @"C:\Source\One";
        var probes = 0;
        var resolver = new GitCommitReferenceResolver(
            () => workspace,
            (_, _) =>
            {
                probes++;
                return true;
            });

        Assert.That(resolver.ResolvesToCommit("abc1234"), Is.True);
        workspace = @"C:\Source\Two";
        Assert.That(resolver.ResolvesToCommit("abc1234"), Is.True);
        Assert.That(probes, Is.EqualTo(2));
    }

    [Test]
    public void ResolvesToCommit_RejectsMissingWorkspaceWithoutProbing()
    {
        var probes = 0;
        var resolver = new GitCommitReferenceResolver(
            () => null,
            (_, _) =>
            {
                probes++;
                return true;
            });

        Assert.That(resolver.ResolvesToCommit("abc1234"), Is.False);
        Assert.That(probes, Is.Zero);
    }
}

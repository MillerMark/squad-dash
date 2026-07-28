using NUnit.Framework;
using System.Threading.Tasks;

namespace SquadDash.Tests;

/// <summary>
/// Gap tests for <see cref="DecomposeWorktreePolicy.FilterMetadataOnlyAsync"/> covering
/// boundary cases not already exercised in <c>DecomposeWorktreePolicyTests.cs</c> (which
/// covers empty list, single-path empty diff, single-path non-empty diff, and mixed list).
/// </summary>
[TestFixture]
internal sealed class PlanWorktreeRecoveryContractTests
{
    // ── Multiple-path bulk cases ───────────────────────────────────────────────

    [Test]
    public async Task FilterMetadataOnlyAsync_allPathsGenuine_retainsAll()
    {
        // Three paths, all return a non-empty diff — all must be retained.
        const string fakeDiff = ":100644 100644 abc def M\tfile.cs";
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            ["src/A.cs", "src/B.cs", "src/C.cs"],
            _ => Task.FromResult(fakeDiff));

        Assert.That(result, Has.Count.EqualTo(3),
            "All paths with genuine content diffs must be retained.");
    }

    [Test]
    public async Task FilterMetadataOnlyAsync_allPathsMetadataOnly_returnsEmpty()
    {
        // Three paths all return empty diff (stat-cache / timestamp noise).
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            ["src/A.cs", "src/B.cs", "src/C.cs"],
            _ => Task.FromResult(string.Empty));

        Assert.That(result, Is.Empty,
            "All metadata-only paths must be filtered out.");
    }

    [Test]
    public async Task FilterMetadataOnlyAsync_whitespaceDiffOutput_treatedAsMetadataOnly()
    {
        // Some git invocations emit whitespace-only output; that should be treated as no diff.
        var result = await DecomposeWorktreePolicy.FilterMetadataOnlyAsync(
            ["src/Foo.cs"],
            _ => Task.FromResult("   \n\t  "));

        Assert.That(result, Is.Empty,
            "Whitespace-only diff output must be treated as metadata-only.");
    }
}

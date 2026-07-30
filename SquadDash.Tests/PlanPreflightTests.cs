using NUnit.Framework;
using System.Collections.Generic;

namespace SquadDash.Tests;

/// <summary>
/// Verifies the shape and contract of <see cref="PlanPreflightBlockedException"/>.
/// </summary>
[TestFixture]
internal sealed class PlanPreflightTests
{
    [Test]
    public void RecoveryContent_ExplainsThatPlanDidNotStartAndListsEveryPath()
    {
        var exception = new PlanPreflightBlockedException(
            "Uncommitted changes", [".squad/tasks.md", "src/App.cs"], "feature/recovery");

        var content = PlanPreflightRecoveryContent.From(exception);

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Plan not started"));
            Assert.That(content.Summary, Does.Contain("No plan work was started"));
            Assert.That(content.Summary, Does.Contain("feature/recovery"));
            Assert.That(content.ChangedFilesSummary, Does.Contain(".squad/tasks.md"));
            Assert.That(content.ChangedFilesSummary, Does.Contain("src/App.cs"));
            Assert.That(content.TechnicalDetails, Does.Contain("Changed files: 2"));
        });
    }

    [Test]
    public void PlanPreflightBlockedException_Properties_StoredCorrectly()
    {
        var paths = new List<string> { "src/Foo.cs", "src/Bar.cs" };
        var ex = new PlanPreflightBlockedException("Uncommitted changes", paths, "feature/my-branch");

        Assert.Multiple(() =>
        {
            Assert.That(ex.Condition,     Is.EqualTo("Uncommitted changes"));
            Assert.That(ex.TargetBranch,  Is.EqualTo("feature/my-branch"));
            Assert.That(ex.ChangedPaths,  Has.Count.EqualTo(2));
            Assert.That(ex.ChangedPaths,  Does.Contain("src/Foo.cs"));
            Assert.That(ex.ChangedPaths,  Does.Contain("src/Bar.cs"));
        });
    }

    [Test]
    public void PlanPreflightBlockedException_IsNotInvalidOperationException()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", [], "main");

        Assert.That(ex, Is.Not.InstanceOf<System.InvalidOperationException>(),
            "PlanPreflightBlockedException must be its own type, not a subclass of InvalidOperationException.");
    }

    [Test]
    public void PlanPreflightBlockedException_Message_IncludesConditionAndBranch()
    {
        var ex = new PlanPreflightBlockedException(
            "Uncommitted changes",
            ["src/A.cs"],
            "feature/xyz");

        Assert.That(ex.Message, Does.Contain("Uncommitted changes"));
        Assert.That(ex.Message, Does.Contain("feature/xyz"));
    }

    [Test]
    public void PlanPreflightBlockedException_NullBranch_MessageOmitsBranchClause()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", ["src/A.cs"], targetBranch: null);

        // No branch text — message must still be well-formed
        Assert.That(ex.Message, Does.Contain("Uncommitted changes"));
        Assert.That(ex.Message, Does.Not.Contain("branch"));
    }

    [Test]
    public void PlanPreflightBlockedException_EmptyPaths_ZeroCount()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", [], "main");

        Assert.That(ex.ChangedPaths, Is.Empty);
        Assert.That(ex.Message,      Does.Contain("0 file(s)"));
    }

    [Test]
    public void PlanPreflightBlockedException_IsException()
    {
        var ex = new PlanPreflightBlockedException("Uncommitted changes", [], null);

        Assert.That(ex, Is.InstanceOf<System.Exception>());
    }
}

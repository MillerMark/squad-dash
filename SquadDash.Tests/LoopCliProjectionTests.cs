using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopCliProjectionTests
{
    private string _root = null!;
    private string _workspace = null!;
    private string _projectionDirectory = null!;
    private string _sourcePath = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "SquadDash.Tests", Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _projectionDirectory = Path.Combine(_root, "projections");
        _sourcePath = Path.Combine(_workspace, ".squad", "loop-filtered-tasks.md");
        Directory.CreateDirectory(Path.GetDirectoryName(_sourcePath)!);
        File.WriteAllText(_sourcePath, "source remains unchanged");
        File.WriteAllText(Path.Combine(_workspace, "Example.sln"), "");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestCase(0.1, 1)]
    [TestCase(1.0, 1)]
    [TestCase(1.01, 2)]
    [TestCase(60.25, 61)]
    public void NormalizeCliMinutes_RoundsUpAndEnforcesOneMinuteMinimum(double value, int expected)
    {
        Assert.That(LoopCliProjection.NormalizeCliMinutes(value), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(-0.1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void NormalizeCliMinutes_InvalidValue_Throws(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopCliProjection.NormalizeCliMinutes(value));
    }

    [Test]
    public void Create_WritesNormalizedMergedProjectionWithoutChangingSource()
    {
        var config = new LoopMdConfig(
            IntervalMinutes: 0.1,
            TimeoutMinutes: 2.2,
            Description: "Filtered Tasks",
            Instructions:
                "Mode={{mode}}\nFilter=[**FILTER**]\nBuild={{build_command}}\n" +
                "Workspace={{workspace_path}}\nGroups={{feature_groups}}\nTrailer={{copilot_trailer}}\n" +
                "Iteration={{iteration}}",
            Options:
            [
                new LoopOption("mode", "safe", "string", null, null, null),
            ]);

        string projectionPath;
        using (var projection = LoopCliProjection.Create(
                   _sourcePath,
                   config,
                   _workspace,
                   filterText: "God class",
                   featureGroups: ["Core", "UI"],
                   projectionDirectory: _projectionDirectory))
        {
            projectionPath = projection.FilePath;
            var content = File.ReadAllText(projectionPath);

            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Contain("interval: 1"));
                Assert.That(content, Does.Contain("timeout: 3"));
                Assert.That(content, Does.Contain("Mode=safe"));
                Assert.That(content, Does.Contain("God class"));
                Assert.That(content, Does.Contain("Build=dotnet build"));
                Assert.That(content, Does.Contain($"Workspace={_workspace}"));
                Assert.That(content, Does.Contain("Groups=- Core\n- UI"));
                Assert.That(content, Does.Contain(LoopController.CopilotTrailer));
                Assert.That(content, Does.Not.Contain("[**FILTER**]"));
                Assert.That(content, Does.Not.Contain("{{build_command}}"));
                Assert.That(content, Does.Contain("{{iteration}}"),
                    "Iteration is dynamic and remains for the separate host-owned iteration repair.");
                Assert.That(File.ReadAllText(_sourcePath), Is.EqualTo("source remains unchanged"));
            });
        }

        Assert.That(File.Exists(projectionPath), Is.False, "Disposing the projection must delete it.");
    }

    [Test]
    public void Create_DeletesStaleProjectionFiles()
    {
        Directory.CreateDirectory(_projectionDirectory);
        var stalePath = Path.Combine(_projectionDirectory, "loop-cli-stale.md");
        File.WriteAllText(stalePath, "stale");
        var config = new LoopMdConfig(1, 5, "Loop", "Prompt");

        using var projection = LoopCliProjection.Create(
            _sourcePath,
            config,
            _workspace,
            filterText: null,
            featureGroups: null,
            projectionDirectory: _projectionDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(stalePath), Is.False);
            Assert.That(File.Exists(projection.FilePath), Is.True);
        });
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DeveloperApprovalSimulatorTests
{
    private string _temporaryFolder = null!;
    private InboxStore _inbox = null!;
    private DeveloperApprovalSimulator _simulator = null!;

    [SetUp]
    public void SetUp()
    {
        _temporaryFolder = Path.Combine(
            Path.GetTempPath(),
            "SquadDash-DeveloperApprovalSimulatorTests-" + Guid.NewGuid().ToString("N"));
        _inbox = new InboxStore(_temporaryFolder);
        _simulator = new DeveloperApprovalSimulator(_inbox);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_temporaryFolder))
            Directory.Delete(_temporaryFolder, recursive: true);
    }

    [Test]
    public async Task StartAsync_CreatesRealDurableApprovalWithoutPlanStoreEntry()
    {
        var result = await _simulator.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Plan.PlanId, Is.EqualTo(DeveloperApprovalSimulator.PlanId));
            Assert.That(result.Plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
            Assert.That(result.Gate.Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(result.Snapshot.CompletedTasks, Has.Count.EqualTo(1));
            Assert.That(result.ClickToken.GateIds, Does.Contain(DeveloperApprovalSimulator.GateId));
            Assert.That(_inbox.GetById(DeveloperApprovalSimulator.MessageId), Is.Not.Null);
            Assert.That(Directory.Exists(Path.Combine(_temporaryFolder, "plans")), Is.False);
        });
    }

    [Test]
    public async Task ApproveAsync_UsesVersionedRuntimeButCannotStartExecution()
    {
        var started = await _simulator.StartAsync();

        var resolution = await _simulator.ApproveAsync(started.ClickToken, "Simulation approved.");

        var message = _inbox.GetById(DeveloperApprovalSimulator.MessageId);
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(resolution.ShouldResume, Is.True,
                "The production runtime reports resumability; the simulator intentionally has no loop callback.");
            Assert.That(_simulator.CurrentPlan!.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Approved));
            Assert.That(message, Is.Not.Null);
            Assert.That(message!.Read, Is.True);
            Assert.That(message.Actions, Is.Empty);
        });
    }

    [Test]
    public async Task Clear_RemovesOnlySimulationDurableState()
    {
        await _simulator.StartAsync();
        _inbox.Save(new InboxMessage
        {
            Id = "unrelated",
            Subject = "Unrelated",
            Timestamp = DateTimeOffset.UtcNow,
        });

        _simulator.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(_simulator.IsActive, Is.False);
            Assert.That(_inbox.GetById(DeveloperApprovalSimulator.MessageId), Is.Null);
            Assert.That(_inbox.GetById("unrelated"), Is.Not.Null);
        });
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ValidationStateSimulatorTests
{
    private static WeakEventBroker CreateBroker() => new();

    [Test]
    public void Start_CreatesReadyPlan_AndPublishesProgressEvent()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);

        var plan = simulator.Start();

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.PlanId, Is.EqualTo(ValidationStateSimulator.PlanId));
        Assert.That(plan.Validations, Has.Count.EqualTo(1));
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Ready));
        Assert.That(simulator.PublishedEvents, Has.Count.EqualTo(1));
        Assert.That(simulator.PublishedEvents[0], Is.InstanceOf<PlanProgressEvent>());

        simulator.CleanUp();
    }

    [Test]
    public void AdvanceState_ReadyToValidating_PublishesPlanProgressEvent()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();
        simulator.PublishedEvents.Clear();

        simulator.AdvanceState();

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Validating));
        Assert.That(simulator.CurrentPlan!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Validating));
        Assert.That(simulator.PublishedEvents.OfType<PlanProgressEvent>().Count(), Is.GreaterThanOrEqualTo(1));

        simulator.CleanUp();
    }

    [Test]
    public void AdvanceState_ValidatingToPassed_WhenNextResultIsNotFailed()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();
        simulator.NextResultIsFailed = false;

        simulator.AdvanceState(); // Ready → Validating
        simulator.AdvanceState(); // Validating → Passed

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Passed));
        Assert.That(simulator.CurrentPlan!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));

        simulator.CleanUp();
    }

    [Test]
    public void AdvanceState_ValidatingToFailed_WhenNextResultIsFailed()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();
        simulator.NextResultIsFailed = true;

        simulator.AdvanceState(); // Ready → Validating
        simulator.AdvanceState(); // Validating → Failed

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Failed));
        Assert.That(simulator.CurrentPlan!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));

        simulator.CleanUp();
    }

    [Test]
    public void AdvanceState_PassedToStaleToReady_CompletesFullCycle()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();
        simulator.NextResultIsFailed = false;

        simulator.AdvanceState(); // Ready → Validating
        simulator.AdvanceState(); // Validating → Passed
        simulator.AdvanceState(); // Passed → Stale

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Stale));
        Assert.That(simulator.CurrentPlan!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));

        simulator.AdvanceState(); // Stale → Ready

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Ready));
        Assert.That(simulator.CurrentPlan!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
        Assert.That(simulator.CycleCount, Is.EqualTo(1));

        simulator.CleanUp();
    }

    [Test]
    public void AdvanceState_FailedToStaleToReady_CompletesFullCycle()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();
        simulator.NextResultIsFailed = true;

        simulator.AdvanceState(); // Ready → Validating
        simulator.AdvanceState(); // Validating → Failed
        simulator.AdvanceState(); // Failed → Stale

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Stale));

        simulator.AdvanceState(); // Stale → Ready

        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Ready));
        Assert.That(simulator.CurrentPlan!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
        Assert.That(simulator.CycleCount, Is.EqualTo(1));

        simulator.CleanUp();
    }

    [Test]
    public void CleanUp_RemovesCurrentPlan_AndResetsPhase()
    {
        var broker = CreateBroker();
        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();

        simulator.CleanUp();

        Assert.That(simulator.CurrentPlan, Is.Null);
        Assert.That(simulator.Phase, Is.EqualTo(ValidationStateSimulator.SimulationPhase.Idle));
    }

    [Test]
    public void PulseEvents_AreFiredDuringValidatingPhase()
    {
        var broker = CreateBroker();
        var receivedPulses = new List<PlanValidationActivityPulseEvent>();
        Action<PlanValidationActivityPulseEvent> handler = evt => receivedPulses.Add(evt);
        broker.Subscribe(handler);

        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();
        simulator.AdvanceState(); // Ready → Validating

        // The pulse timer fires immediately on start; give it a moment
        System.Threading.Thread.Sleep(50);

        Assert.That(receivedPulses.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(receivedPulses[0].PlanId, Is.EqualTo(ValidationStateSimulator.PlanId));
        Assert.That(receivedPulses[0].ValidationId, Is.EqualTo(ValidationStateSimulator.ValidationId));

        simulator.CleanUp();
        broker.Unsubscribe(handler);
    }

    [Test]
    public void LiveSyncHandler_ReceivesValidationStateTransitions()
    {
        var broker = CreateBroker();
        Plan? lastReceived = null;
        var syncHandler = new PlanViewerLiveSyncHandler(
            ValidationStateSimulator.PlanId,
            BuildMinimalPlan(),
            broker,
            plan => lastReceived = plan);

        using var simulator = new ValidationStateSimulator(broker, planStore: null, stepIntervalMs: 100_000);
        simulator.Start();

        Assert.That(lastReceived, Is.Not.Null);
        Assert.That(lastReceived!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));

        simulator.AdvanceState(); // Ready → Validating
        Assert.That(lastReceived!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Validating));

        simulator.NextResultIsFailed = false;
        simulator.AdvanceState(); // Validating → Passed
        Assert.That(lastReceived!.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));

        syncHandler.Detach();
        simulator.CleanUp();
    }

    private static Plan BuildMinimalPlan() => new(
        PlanId: ValidationStateSimulator.PlanId,
        Revision: "initial",
        Source: PlanSource.Manual,
        LifecycleStatus: PlanLifecycleStatus.Executing,
        Title: "Minimal",
        Branch: "main",
        Summary: "Minimal plan for sync handler seeding",
        Tasks: [],
        ApprovalGates: [],
        Progress: new PlanProgress(0, 1),
        Timestamps: new PlanTimestamps(CreatedAt: System.DateTimeOffset.UtcNow));
}

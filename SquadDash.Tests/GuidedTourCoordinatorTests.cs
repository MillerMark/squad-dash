using System;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class GuidedTourCoordinatorTests
{
    private GuidedTourCoordinator MakeCoordinator(
        Dispatcher? dispatcher = null,
        Func<bool>? isIntelliSensePopupOpen = null,
        Action? clearIntelliSenseState = null)
        => new GuidedTourCoordinator(
            dispatcher ?? Dispatcher.CurrentDispatcher,
            isIntelliSensePopupOpen ?? (() => false),
            clearIntelliSenseState ?? (() => { }));

    [Test]
    public void Constructor_NullDispatcher_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GuidedTourCoordinator(null!, () => false, () => { }));
    }

    [Test]
    public void Constructor_NullIsIntelliSensePopupOpen_Throws()
    {
        WpfTestContext.Run(() =>
        {
            Assert.Throws<ArgumentNullException>(
                () => new GuidedTourCoordinator(Dispatcher.CurrentDispatcher, null!, () => { }));
        });
    }

    [Test]
    public void Constructor_NullClearIntelliSenseState_Throws()
    {
        WpfTestContext.Run(() =>
        {
            Assert.Throws<ArgumentNullException>(
                () => new GuidedTourCoordinator(Dispatcher.CurrentDispatcher, () => false, null!));
        });
    }

    [Test]
    public void MenuRecoveryRunning_StartsAsFalse()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            Assert.That(coordinator.MenuRecoveryRunning, Is.False);
        });
    }

    [Test]
    public void IntelliSenseRecoveryRunning_StartsAsFalse()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            Assert.That(coordinator.IntelliSenseRecoveryRunning, Is.False);
        });
    }

    [Test]
    public void RecoverKeptOpenTourIntelliSense_WhenTriggerNull_DoesNothing()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            coordinator.RecoverKeptOpenTourIntelliSense();
            Assert.That(coordinator.IntelliSenseRecoveryRunning, Is.False);
        });
    }

    [Test]
    public void RecoverKeptOpenTourMenuPath_WhenPathNull_DoesNothing()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            coordinator.RecoverKeptOpenTourMenuPath();
            Assert.That(coordinator.MenuRecoveryRunning, Is.False);
        });
    }

    [Test]
    public void StopKeepingTourMenusOpen_IncrementsGeneration()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            int gen0 = coordinator.MenuTrackingGeneration;
            coordinator.StopKeepingTourMenusOpen();
            Assert.That(coordinator.MenuTrackingGeneration, Is.EqualTo(gen0 + 1));
        });
    }

    [Test]
    public void StopKeepingTourMenusOpen_ClearsMenuPath()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            coordinator.StopKeepingTourMenusOpen();
            Assert.That(coordinator.KeptOpenMenuPath, Is.Null);
        });
    }

    [Test]
    public void StopKeepingTourIntelliSenseOpen_ClearsTrigger()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            coordinator.KeptOpenIntelliSenseTrigger = "slash";
            coordinator.StopKeepingTourIntelliSenseOpen();
            Assert.That(coordinator.KeptOpenIntelliSenseTrigger, Is.Null);
        });
    }

    [Test]
    public void StopKeepingTourIntelliSenseOpen_CallsClearCallback_WhenTriggerSet()
    {
        WpfTestContext.Run(() =>
        {
            bool cleared = false;
            var coordinator = MakeCoordinator(clearIntelliSenseState: () => cleared = true);
            coordinator.KeptOpenIntelliSenseTrigger = "at";
            coordinator.StopKeepingTourIntelliSenseOpen();
            Assert.That(cleared, Is.True);
        });
    }

    [Test]
    public void ClearTourMenuTracking_ClearsKeptOpenMenuPath()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            coordinator.KeptOpenMenuPath = "SomeMenu";
            coordinator.ClearTourMenuTracking(closeMenus: false);
            Assert.That(coordinator.KeptOpenMenuPath, Is.Null);
        });
    }

    [Test]
    public void RaiseQuickReplySelected_FiresEvent()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            bool fired = false;
            coordinator.QuickReplySelected += () => fired = true;
            coordinator.RaiseQuickReplySelected();
            Assert.That(fired, Is.True);
        });
    }

    [Test]
    public void RaiseCycleCaseForward_FiresEvent()
    {
        WpfTestContext.Run(() =>
        {
            var coordinator = MakeCoordinator();
            bool fired = false;
            coordinator.CycleCaseForward += (s, e) => fired = true;
            coordinator.RaiseCycleCaseForward(coordinator);
            Assert.That(fired, Is.True);
        });
    }
}

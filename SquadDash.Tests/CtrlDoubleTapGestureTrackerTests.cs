using System;
using System.Windows.Input;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CtrlDoubleTapGestureTrackerTests {
    [Test]
    public void DoubleTapWithinGap_Triggers() {
        var tracker = new CtrlDoubleTapGestureTracker(maxTapHoldMs: 250, doubleTapGapMs: 350);
        var t0 = new DateTime(2026, 4, 25, 15, 0, 0, DateTimeKind.Utc);

        var firstDown = tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: false, t0);
        tracker.HandleKeyUp(Key.LeftCtrl, t0.AddMilliseconds(50));
        var secondDown = tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: false, t0.AddMilliseconds(150));

        Assert.Multiple(() => {
            Assert.That(firstDown, Is.EqualTo(CtrlDoubleTapGestureAction.None));
            Assert.That(secondDown, Is.EqualTo(CtrlDoubleTapGestureAction.Triggered));
            Assert.That(tracker.State, Is.EqualTo(CtrlDoubleTapGestureTracker.GestureState.Idle));
        });
    }

    [Test]
    public void HeldFirstTapTooLong_ResetsWithoutTriggering() {
        var tracker = new CtrlDoubleTapGestureTracker(maxTapHoldMs: 250, doubleTapGapMs: 350);
        var t0 = new DateTime(2026, 4, 25, 15, 0, 0, DateTimeKind.Utc);

        tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: false, t0);
        var result = tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: true, t0.AddMilliseconds(300));

        Assert.Multiple(() => {
            Assert.That(result, Is.EqualTo(CtrlDoubleTapGestureAction.None));
            Assert.That(tracker.State, Is.EqualTo(CtrlDoubleTapGestureTracker.GestureState.Idle));
        });
    }

    [Test]
    public void NonCtrlKeyDuringGesture_ResetsTracker() {
        var tracker = new CtrlDoubleTapGestureTracker(maxTapHoldMs: 250, doubleTapGapMs: 350);
        var t0 = new DateTime(2026, 4, 25, 15, 0, 0, DateTimeKind.Utc);

        tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: false, t0);
        tracker.HandleKeyUp(Key.LeftCtrl, t0.AddMilliseconds(50));
        tracker.HandleKeyDown(Key.A, isRepeat: false, t0.AddMilliseconds(100));

        Assert.That(tracker.State, Is.EqualTo(CtrlDoubleTapGestureTracker.GestureState.Idle));
    }

    [Test]
    public void SlowSecondTap_BecomesNewFirstTap() {
        var tracker = new CtrlDoubleTapGestureTracker(maxTapHoldMs: 250, doubleTapGapMs: 350);
        var t0 = new DateTime(2026, 4, 25, 15, 0, 0, DateTimeKind.Utc);

        tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: false, t0);
        tracker.HandleKeyUp(Key.LeftCtrl, t0.AddMilliseconds(40));
        var result = tracker.HandleKeyDown(Key.LeftCtrl, isRepeat: false, t0.AddMilliseconds(500));

        Assert.Multiple(() => {
            Assert.That(result, Is.EqualTo(CtrlDoubleTapGestureAction.None));
            Assert.That(tracker.State, Is.EqualTo(CtrlDoubleTapGestureTracker.GestureState.TapDown));
            Assert.That(tracker.FirstDownAtUtc, Is.EqualTo(t0.AddMilliseconds(500)));
        });
    }
}

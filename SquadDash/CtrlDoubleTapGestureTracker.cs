using System;
using System.Windows.Input;

namespace SquadDash;

internal enum CtrlDoubleTapGestureAction {
    None,
    Triggered
}

internal sealed class CtrlDoubleTapGestureTracker {
    private readonly TimeSpan _maxTapHold;
    private readonly TimeSpan _doubleTapGap;

    internal enum GestureState {
        Idle,
        TapDown,
        TapReleased
    }

    public CtrlDoubleTapGestureTracker(int maxTapHoldMs, int doubleTapGapMs) {
        if (maxTapHoldMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTapHoldMs));
        if (doubleTapGapMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(doubleTapGapMs));

        _maxTapHold = TimeSpan.FromMilliseconds(maxTapHoldMs);
        _doubleTapGap = TimeSpan.FromMilliseconds(doubleTapGapMs);
    }

    public GestureState State { get; private set; } = GestureState.Idle;

    public DateTime FirstDownAtUtc { get; private set; }

    public DateTime FirstReleaseAtUtc { get; private set; }

    public CtrlDoubleTapGestureAction HandleKeyDown(Key key, bool isRepeat, DateTime nowUtc) {
        if (!IsCtrlKey(key)) {
            if (State != GestureState.Idle)
                Reset();
            return CtrlDoubleTapGestureAction.None;
        }

        switch (State) {
            case GestureState.Idle:
                if (!isRepeat) {
                    FirstDownAtUtc = nowUtc;
                    State = GestureState.TapDown;
                }
                break;

            case GestureState.TapDown:
                if (isRepeat && nowUtc - FirstDownAtUtc > _maxTapHold)
                    Reset();
                break;

            case GestureState.TapReleased:
                if (!isRepeat) {
                    if (nowUtc - FirstReleaseAtUtc <= _doubleTapGap) {
                        Reset();
                        return CtrlDoubleTapGestureAction.Triggered;
                    }

                    FirstDownAtUtc = nowUtc;
                    State = GestureState.TapDown;
                }
                break;
        }

        return CtrlDoubleTapGestureAction.None;
    }

    public void HandleKeyUp(Key key, DateTime nowUtc) {
        if (!IsCtrlKey(key))
            return;

        if (State != GestureState.TapDown)
            return;

        if (nowUtc - FirstDownAtUtc <= _maxTapHold) {
            FirstReleaseAtUtc = nowUtc;
            State = GestureState.TapReleased;
            return;
        }

        Reset();
    }

    public void Reset() {
        State = GestureState.Idle;
        FirstDownAtUtc = default;
        FirstReleaseAtUtc = default;
    }

    public static bool IsCtrlKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl;
}

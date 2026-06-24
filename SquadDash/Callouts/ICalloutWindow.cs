using System;
using System.Windows;

namespace SquadDash;
public interface ICalloutWindow {
    event EventHandler RefreshTargetRect;
    event EventHandler AngleChanged;
    bool ShowDiagnostics { get; set; }
    void UpdateTargetRect(Rect rect);
    void TargetMoved();
    void ForceRefresh();
    double LastDragAngle { get; }
}
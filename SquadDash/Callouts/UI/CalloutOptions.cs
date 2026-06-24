using System;

namespace SquadDash;
public class CalloutOptions {
    public bool AnimateBackAfterDrag { get; set; } = true;
    public double CornerRadius { get; set; } = 6;
    public double OuterMargin { get; set; } = 30;
    public double TargetSpacing { get; set; } = 5;
    public double InitialAngle { get; set; } = double.MinValue;

    /// <summary>
    /// The angle of the dangle, in degrees.
    /// </summary>
    public double DangleAngle { get; set; } = 38;

    public double Width { get; set; } = 200;
    public double AnimationTimeMs { get; set; } = 450;
    public bool AnimateAppearance { get; set; } = true;

    /// <summary>
    /// When set, overrides the dangle-based animation start offset.
    /// The callout window starts at (windowLeft + X, windowTop + Y) and animates to (windowLeft, windowTop).
    /// Use negative Y for "drop in from above", positive Y for "rise in from below".
    /// </summary>
    public System.Windows.Vector? AnimationOffset { get; set; } = null;

    public CalloutOptions() {

    }
}
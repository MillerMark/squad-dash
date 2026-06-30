using System;

namespace SquadDash;
public class GuidelineIntersectionData {
    public CalloutSide CalloutDangleSide { get; set; }
    public CalloutSide TargetDangleSide { get; set; }
    public MyLine CalloutLeft { get; set; } = null!;
    public MyLine CalloutTop { get; set; } = null!;
    public MyLine CalloutRight { get; set; } = null!;
    public MyLine CalloutBottom { get; set; } = null!;
    public MyLine TargetLeft { get; set; } = null!;
    public MyLine TargetTop { get; set; } = null!;
    public MyLine TargetRight { get; set; } = null!;
    public MyLine TargetBottom { get; set; } = null!;
    public MyLine InnerWindowLeft { get; set; } = null!;
    public MyLine InnerWindowTop { get; set; } = null!;
    public MyLine InnerWindowRight { get; set; } = null!;
    public MyLine InnerWindowBottom { get; set; } = null!;

    public MyLine CalloutInsideLeft => CalloutLeft.MoveRight(1);
    public MyLine CalloutInsideTop => CalloutTop.MoveDown(1);
    public MyLine CalloutInsideRight => CalloutRight.MoveLeft(1);
    public MyLine CalloutInsideBottom => CalloutBottom.MoveUp(1);

    public GuidelineIntersectionData() {

    }
}
using System;

namespace SquadDash;
public class GuidelineIntersectionData {
    public CalloutSide CalloutDangleSide { get; set; }
    public CalloutSide TargetDangleSide { get; set; }
    public MyLine CalloutLeft { get; set; }
    public MyLine CalloutTop { get; set; }
    public MyLine CalloutRight { get; set; }
    public MyLine CalloutBottom { get; set; }
    public MyLine TargetLeft { get; set; }
    public MyLine TargetTop { get; set; }
    public MyLine TargetRight { get; set; }
    public MyLine TargetBottom { get; set; }
    public MyLine InnerWindowLeft { get; set; }
    public MyLine InnerWindowTop { get; set; }
    public MyLine InnerWindowRight { get; set; }
    public MyLine InnerWindowBottom { get; set; }

    public MyLine CalloutInsideLeft => CalloutLeft.MoveRight(1);
    public MyLine CalloutInsideTop => CalloutTop.MoveDown(1);
    public MyLine CalloutInsideRight => CalloutRight.MoveLeft(1);
    public MyLine CalloutInsideBottom => CalloutBottom.MoveUp(1);

    public GuidelineIntersectionData() {

    }
}
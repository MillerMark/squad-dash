using System;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Collections.Generic;


namespace SquadDash;
/// <summary>
/// Interaction logic for FrmUltimateCallout.xaml
/// </summary>
public partial class FrmUltimateCallout : Window, ICalloutWindow {
    public event EventHandler RefreshTargetRect;
    public event EventHandler AngleChanged;
    DispatcherTimer waitingForMouseUpTimer;
    DispatcherTimer calloutAnimationTimer;
    private const double indicatorMargin = 10d;
    SolidColorBrush closeButtonBackgroundBrush;
    SolidColorBrush closeButtonForegroundBrush;
    SolidColorBrush closeButtonBorderBrush;
    SolidColorBrush calloutStrokeBrush;
    SolidColorBrush calloutFillBrush;


    double idealCalloutWidth;

    CalloutTheme theme = CalloutTheme.Light;
    public CalloutTheme Theme {
        get => theme;
        set {
            if (theme == value)
                return;
            theme = value;
            LoadColorsForTheme();
        }
    }

    bool showDiagnostics;
    public bool ShowDiagnostics {
        get {
            return showDiagnostics;
        }
        set {
            if (showDiagnostics == value)
                return;
            showDiagnostics = value;
            RefreshLayout();
        }
    }

    public Color GlowColor {
        get => glowColor;
        set {
            if (glowColor == value)
                return;
            glowColor = value;
            glowHsl = new HueSatLight(glowColor);
            LoadColorsForTheme();
            RefreshLayout();
        }
    }

    SolidColorBrush GetBrushFromGlow(double saturation, double lightness) {
        var hueSatLight = new HueSatLight() { Hue = glowHsl.Hue, Saturation = saturation / 255.0, Lightness = lightness / 255.0 };
        return new SolidColorBrush(hueSatLight.AsRGB);
    }

    void InitializeColors() {
        if (Theme == CalloutTheme.Light) {
            closeButtonBackgroundBrush = GetBrushFromGlow(234, 238);
            closeButtonForegroundBrush = GetBrushFromGlow(102, 115);
            closeButtonBorderBrush = GetBrushFromGlow(100, 195);
            calloutStrokeBrush = GetBrushFromGlow(94, 114);
            calloutFillBrush = GetBrushFromGlow(192, 250);
        }
        else if (Theme == CalloutTheme.Dark) {
            closeButtonBackgroundBrush = GetBrushFromGlow(27, 52);
            closeButtonForegroundBrush = GetBrushFromGlow(135, 87);
            closeButtonBorderBrush = GetBrushFromGlow(98, 79);
            calloutStrokeBrush = GetBrushFromGlow(glowHsl.Saturation * 255.0, glowHsl.Lightness * 255.0);
            calloutFillBrush = GetBrushFromGlow(13, 38);
        }
    }

    void LoadColorsForTheme() {
        InitializeColors();
        RefreshLayout();
    }

    protected virtual void OnRefreshTargetRect() {
        if (frameworkElementTarget == null)  // Only fire the event when we are *not* targeting a FrameworkElement.
            RefreshTargetRect?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshLayout() {
        InvalidateLayout();
        if (initializationComplete)
            LayoutEverything();
    }

    protected virtual void OnAngleChanged(object sender, EventArgs e) {
        AngleChanged?.Invoke(sender, e);
    }

    public CalloutOptions Options { get; set; } = new CalloutOptions();
    public FrmUltimateCallout() {
        InitializeComponent();
    }

    void InvalidateLayout() {
        layoutValid = false;
    }

    const double closeButtonEdgeSize = 16d;

    void PlaceCloseButton() {
        Button closeButton = new Button();
        if (closeButtonBackgroundBrush == null)
            InitializeColors();

        closeButton.Background = closeButtonBackgroundBrush;
        closeButton.Foreground = closeButtonForegroundBrush;
        closeButton.BorderBrush = closeButtonBorderBrush;
        closeButton.Content = "x";
        closeButton.Padding = new Thickness(0, -6, 0, 0);
        closeButton.FontSize = closeButtonEdgeSize;
        closeButton.Width = closeButtonEdgeSize;
        closeButton.Height = closeButtonEdgeSize;
        closeButton.Click += CloseButton_Click;
        cvsCallout.Children.Add(closeButton);
        double rightEdge = calloutLeft + calloutWidth;
        Canvas.SetLeft(closeButton, rightEdge - Options.CornerRadius - closeButton.Width);
        Canvas.SetTop(closeButton, calloutTop + Options.CornerRadius);
    }

    double GetMinHeight() {
        return 2 * Options.CornerRadius + closeButtonEdgeSize;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    void CalculateDummyBounds() {
        calloutHeight = 200;
        calloutWidth = Options.Width;
        calloutTop = OutsideMargin;
        calloutLeft = OutsideMargin;
        Width = calloutWidth + OutsideMargin * 2;
        Height = calloutHeight + OutsideMargin * 2;
    }

    void CalculateBounds() {
        calloutHeight = calculatedHeight;
        if (idealCalloutWidth != 0)
            calloutWidth = idealCalloutWidth;
        else
            calloutWidth = Options.Width;
        calloutTop = OutsideMargin;
        calloutLeft = OutsideMargin;
        Width = calloutWidth + OutsideMargin * 2;
        Height = calloutHeight + OutsideMargin * 2;
    }

    void CreateCalloutFrame() {
        AddCalloutPathToBackOfCanvas(calloutStrokeBrush, 1, calloutFillBrush);
        if (Theme == CalloutTheme.Light)
            AddCalloutPathToBackOfCanvas(null, 0, new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), 5, 5);
        else if (Theme == CalloutTheme.Dark)
            for (int i = 1; i <= 8; i += 2)
                AddCalloutPathToBackOfCanvas(new SolidColorBrush(Color.FromArgb((byte)Math.Round(GlowOpacity * 255), glowColor.R, glowColor.G, glowColor.B)), i, null);
    }

    private void AddCalloutPathToBackOfCanvas(SolidColorBrush calloutStrokeBrush, int thickness, SolidColorBrush calloutFillBrush, double offsetX = 0, double offsetY = 0) {
        System.Windows.Shapes.Path calloutPath = new System.Windows.Shapes.Path() {
            Stroke = calloutStrokeBrush,
            StrokeThickness = thickness,
            Fill = calloutFillBrush
        };
        CreateCalloutGeometry(calloutPath);
        // Place the callout in the back:
        cvsCallout.Children.Insert(0, calloutPath);
        if (offsetX != 0)
            Canvas.SetLeft(calloutPath, offsetX);
        if (offsetY != 0)
            Canvas.SetTop(calloutPath, offsetY);
    }

    private void CreateCalloutGeometry(System.Windows.Shapes.Path calloutPath) {
        CombinedGeometry combinedGeometry = new CombinedGeometry() { GeometryCombineMode = GeometryCombineMode.Union };
        calloutPath.Data = combinedGeometry;

        RectangleGeometry rectangleGeometry = new RectangleGeometry();
        rectangleGeometry.RadiusX = Options.CornerRadius;
        rectangleGeometry.RadiusY = Options.CornerRadius;

        Rect rect = new Rect();
        rect.Width = Math.Max(20, calloutWidth);
        rect.Height = calloutHeight;
        rect.Location = new Point(calloutLeft, calloutTop);
        rectangleGeometry.Rect = rect;

        combinedGeometry.Geometry1 = rectangleGeometry;

        StreamGeometry triangleGeometry = new StreamGeometry();

        using (StreamGeometryContext ctx = triangleGeometry.Open()) {
            ctx.BeginFigure(trianglePoint1, true, true);
            ctx.LineTo(trianglePoint2, true, true);
            ctx.LineTo(trianglePoint3, true, true);
        }

        triangleGeometry.Freeze();
        combinedGeometry.Geometry2 = triangleGeometry;
    }

    void CreateTemporaryMarkdownViewer() {
        UnloadMarkdownViewer(markdownViewer);
        CalculateDummyBounds();
        markdownViewer = LoadMarkdownViewer();
        cvsCallout.Children.Add(markdownViewer);
        markdownViewer.Tag = STR_TempMarkdown;
    }

    void LayoutText() {
        UnloadMarkdownViewer(markdownViewer);
        markdownViewer.Height = topExtension + calloutHeight + bottomExtension;
        Canvas.SetLeft(markdownViewer, GetMarkdownLeft());
        Canvas.SetTop(markdownViewer, GetMarkdownTop());
        cvsCallout.Children.Add(markdownViewer);
    }

    private void UnloadMarkdownViewer(Control markdownControl) {
        if (markdownControl != null)
            markdownControl.Loaded -= MarkdownViewer_Loaded;
    }

    void SetMarkDown(Control markdownControl, string markDownText) {
        if (markdownControl is SimpleMarkdownViewer simpleMarkdownViewer)
            simpleMarkdownViewer.Markdown = markDownText;
        else
            throw new Exception($"Unknown control type.");
    }

    private Control LoadMarkdownViewer() {
        CreateMarkdownViewer();
        LoadStyles(markdownViewer);
        SetMarkDown(markdownViewer, markDownText);
        markdownViewer.Padding = new Thickness(0);
        markdownViewer.Margin = new Thickness(GetMarkdownMargin());
        markdownViewer.IsHitTestVisible = false;
        markdownViewer.Width = leftExtension + calloutWidth + rightExtension + GetMarkdownWidthAdjust();
        idealCalloutWidth = 0;
        markdownViewer.Loaded += MarkdownViewer_Loaded;
        return markdownViewer;
    }

    private double GetMarkdownTop() {
        return calloutTop + Options.CornerRadius - topExtension + GetMarkdownVerticalOffset();
    }

    const double leftExtension = 14d;
    const double topExtension = 16d;
    const double rightExtension = 2d;
    const double bottomExtension = 10d;
    const string STR_TempMarkdown = "Temp";


    private double GetMarkdownLeft() {
        return calloutLeft + Options.CornerRadius - leftExtension + GetMarkdownHorizontalOffset();
    }

    void ShowFlowDocumentDiagnostics(FlowDocument flowDocument) {
        if (markdownViewer == null)
            return;

        double lowestBlockSoFar = 0;

        if (flowDocument != null)
            foreach (var b in flowDocument.Blocks) {
                Rect endCharacterRect = b.ElementEnd.GetCharacterRect(LogicalDirection.Forward);

                if (double.IsInfinity(endCharacterRect.Width) || double.IsInfinity(endCharacterRect.Height))
                    continue;

                if (endCharacterRect.Bottom > lowestBlockSoFar)
                    lowestBlockSoFar = endCharacterRect.Bottom;

                AddDiagnosticForBlock(endCharacterRect, Brushes.LightCoral, -1);
            }
    }

    double CalculateFlowDocumentHeight(FlowDocument flowDocument) {
        if (markdownViewer == null)
            return 0d;

        var lowestBlockSoFar = FlowDocumentHelper.GetLowestBlock(flowDocument);
        const double bottomMargin = 5;
        return Math.Max(GetMinHeight(), lowestBlockSoFar + bottomMargin) + GetExtraBottomMargin();
    }


    private void AddDiagnosticForBlock(Rect characterRect, SolidColorBrush strokeBrush, double offset) {
        if (double.IsInfinity(characterRect.Width) || double.IsInfinity(characterRect.Height))
            return;

        Rectangle blockRect = new Rectangle();
        blockRect.Width = Math.Max(10, characterRect.Width);
        blockRect.Height = characterRect.Height;
        blockRect.Stroke = strokeBrush;

        Canvas.SetLeft(blockRect, offset + characterRect.Left + calloutLeft + Options.CornerRadius - leftExtension);
        Canvas.SetTop(blockRect, offset + characterRect.Top + calloutTop + Options.CornerRadius - topExtension);
        cvsCallout.Children.Add(blockRect);
        //AddDiagnostic(blockRect);
    }

    /// <summary>
    /// Adds a figure to the layout to reserve space for the close button so words don't wrap behind it.
    /// </summary>
    private void ReserveSpaceForCloseButton(FlowDocument flowDocument) {
        if (flowDocument == null || flowDocument.Blocks.Count == 0)
            return;

        Block firstBlock = flowDocument.Blocks.First();
        if (firstBlock == null)
            return;

        if (!(firstBlock is Paragraph paragraph))
            return;

        double closeButtonMargin = 2d;

        Figure closeButtonFigure = new() {
            Width = new FigureLength(closeButtonEdgeSize * GetCloseButtonFigureHorizontalScale() + closeButtonMargin, FigureUnitType.Pixel),
            Height = new FigureLength(closeButtonEdgeSize  /* has no impact on height. */, FigureUnitType.Pixel),
            HorizontalAnchor = FigureHorizontalAnchor.PageRight,
            HorizontalOffset = GetMarkdownHorizontalOffset(),
            VerticalOffset = 0,   // has no impact on vertical position.
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };

        if (showDiagnostics) {
            closeButtonFigure.Background = Brushes.BlueViolet;
        }

        paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, closeButtonFigure);
    }

    double GetDistanceToIntersection(MyLine testLine, MyLine topLine) {
        Point intersection = testLine.GetSegmentIntersection(topLine);
        if (double.IsNaN(intersection.X))
            return double.MaxValue;
        double deltaX = intersection.X - targetCenter.X;
        double deltaY = intersection.Y - targetCenter.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    void SetCalloutSides(MyLine testLine, GuidelineIntersectionData data) {
        // TODO: Opportunities to refactor here, but it's tricky so be careful.
        double topWindowDistance = GetDistanceToIntersection(testLine, data.InnerWindowTop);
        double leftWindowDistance = GetDistanceToIntersection(testLine, data.InnerWindowLeft);
        double rightWindowDistance = GetDistanceToIntersection(testLine, data.InnerWindowRight);
        double bottomWindowDistance = GetDistanceToIntersection(testLine, data.InnerWindowBottom);

        double minCalloutDistance = Min(topWindowDistance, leftWindowDistance, rightWindowDistance, bottomWindowDistance);

        if (minCalloutDistance == topWindowDistance)
            data.CalloutDangleSide = CalloutSide.Top;
        else if (minCalloutDistance == rightWindowDistance)
            data.CalloutDangleSide = CalloutSide.Right;
        else if (minCalloutDistance == bottomWindowDistance)
            data.CalloutDangleSide = CalloutSide.Bottom;
        else if (minCalloutDistance == leftWindowDistance)
            data.CalloutDangleSide = CalloutSide.Left;

        double topTargetDistance = GetDistanceToIntersection(testLine, data.TargetTop);
        double leftTargetDistance = GetDistanceToIntersection(testLine, data.TargetLeft);
        double rightTargetDistance = GetDistanceToIntersection(testLine, data.TargetRight);
        double bottomTargetDistance = GetDistanceToIntersection(testLine, data.TargetBottom);

        double minTargetDistance = Min(topTargetDistance, leftTargetDistance, rightTargetDistance, bottomTargetDistance);

        if (minTargetDistance == topTargetDistance)
            data.TargetDangleSide = CalloutSide.Top;
        else if (minTargetDistance == rightTargetDistance)
            data.TargetDangleSide = CalloutSide.Right;
        else if (minTargetDistance == bottomTargetDistance)
            data.TargetDangleSide = CalloutSide.Bottom;
        else if (minTargetDistance == leftTargetDistance)
            data.TargetDangleSide = CalloutSide.Left;

    }

    private static double Min(params double[] args) => args.Min();

    GuidelineIntersectionData GetGuidelineIntersectionData(MyLine testLine, double windowLeft, double windowTop) {
        double calloutLeft = windowLeft + OutsideMargin;
        double calloutTop = windowTop + OutsideMargin;
        double calloutRight = calloutLeft + calloutWidth;
        double calloutBottom = calloutTop + calloutHeight;

        double targetLeft = targetCenter.X - TargetWidth / 2;
        double targetTop = targetCenter.Y - TargetHeight / 2;
        double targetRight = targetLeft + TargetWidth;
        double targetBottom = targetTop + TargetHeight;

        double windowRight = windowLeft + calloutWidth + 2 * OutsideMargin;
        double windowBottom = windowTop + calloutHeight + 2 * OutsideMargin;

        GuidelineIntersectionData guidelineIntersectionData = new GuidelineIntersectionData();

        guidelineIntersectionData.CalloutTop = MyLine.Horizontal(calloutLeft, calloutRight, calloutTop);
        guidelineIntersectionData.CalloutLeft = MyLine.Vertical(calloutLeft, calloutTop, calloutBottom);
        guidelineIntersectionData.CalloutRight = MyLine.Vertical(calloutRight, calloutTop, calloutBottom);
        guidelineIntersectionData.CalloutBottom = MyLine.Horizontal(calloutLeft, calloutRight, calloutBottom);

        guidelineIntersectionData.TargetTop = MyLine.Horizontal(targetLeft, targetRight, targetTop - Options.TargetSpacing);
        guidelineIntersectionData.TargetLeft = MyLine.Vertical(targetLeft + Options.TargetSpacing, targetTop, targetBottom);
        guidelineIntersectionData.TargetRight = MyLine.Vertical(targetRight - Options.TargetSpacing, targetTop, targetBottom);
        guidelineIntersectionData.TargetBottom = MyLine.Horizontal(targetLeft, targetRight, targetBottom + Options.TargetSpacing);

        const double contraction = 10d; // So dangle calculation works at close proximity to the target.
        double innerWindowMargin = indicatorMargin + contraction;
        guidelineIntersectionData.InnerWindowTop = MyLine.Horizontal(windowLeft, windowRight, windowTop + innerWindowMargin);
        guidelineIntersectionData.InnerWindowLeft = MyLine.Vertical(windowLeft + innerWindowMargin, windowTop, windowBottom);
        guidelineIntersectionData.InnerWindowRight = MyLine.Vertical(windowRight - innerWindowMargin, windowTop, windowBottom);
        guidelineIntersectionData.InnerWindowBottom = MyLine.Horizontal(windowLeft, windowRight, windowBottom - innerWindowMargin);

        SetCalloutSides(testLine, guidelineIntersectionData);

        return guidelineIntersectionData;
    }

    object diagnosticTag = new object();

    void AddDiagnostic(FrameworkElement element) {
        element.Tag = diagnosticTag;
        cvsCallout.Children.Add(element);
    }

    void ShowIntersectedSide(CalloutSide side) {
        const double indicatorThickness = 7d;
        Rectangle sideIndicator = new Rectangle();
        switch (side) {
            case CalloutSide.Left:
            case CalloutSide.Right:
                sideIndicator.Width = indicatorThickness;
                sideIndicator.Height = calloutHeight;
                Canvas.SetTop(sideIndicator, OutsideMargin);
                if (side == CalloutSide.Right)
                    Canvas.SetLeft(sideIndicator, calloutWidth + OutsideMargin - indicatorThickness);
                else
                    Canvas.SetLeft(sideIndicator, OutsideMargin);
                break;
            case CalloutSide.Top:
            case CalloutSide.Bottom:
                sideIndicator.Width = calloutWidth;
                sideIndicator.Height = indicatorThickness;
                Canvas.SetLeft(sideIndicator, OutsideMargin);
                if (side == CalloutSide.Bottom)
                    Canvas.SetTop(sideIndicator, calloutHeight + OutsideMargin - indicatorThickness);
                else
                    Canvas.SetTop(sideIndicator, OutsideMargin);
                break;
        }
        sideIndicator.Fill = Brushes.Blue;
        sideIndicator.Opacity = 0.25;
        AddDiagnostic(sideIndicator);
    }

    Point ScreenToCanvasPoint(Point screenPoint, double windowLeft, double windowTop) {
        return new Point(screenPoint.X - windowLeft, screenPoint.Y - windowTop);
    }

    private Point GetTriangleScreenPoint(GuidelineIntersectionData guidelineIntersectionData, Point triangleScreenPoint1, double angle) {
        Point rotatedScreenPt = MathEx.GetRotatedMyLineSegment(triangleScreenPoint1, calloutScreenCenter, angle).End;
        MyLine line = new MyLine(triangleScreenPoint1, rotatedScreenPt);

        Point intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
            CalloutSide.Right => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideRight),
            CalloutSide.Left => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideLeft),
            CalloutSide.Bottom => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideBottom),
            CalloutSide.Top => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideTop),
            _ => throw new NotImplementedException(),
        };

        if (double.IsNaN(intersectionPoint.X)) {
            // Try adjacent edges on one side...
            intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
                CalloutSide.Right => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideBottom),
                CalloutSide.Left => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideBottom),
                CalloutSide.Bottom => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideRight),
                CalloutSide.Top => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideRight),
                _ => throw new NotImplementedException(),
            };

            if (double.IsNaN(intersectionPoint.X)) {
                // Try adjacent edges on the other side...
                intersectionPoint = guidelineIntersectionData.CalloutDangleSide switch {
                    CalloutSide.Right => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideTop),
                    CalloutSide.Left => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideTop),
                    CalloutSide.Bottom => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideLeft),
                    CalloutSide.Top => line.GetSegmentIntersection(guidelineIntersectionData.CalloutInsideLeft),
                    _ => throw new NotImplementedException(),
                };
                if (double.IsNaN(intersectionPoint.X))
                    intersectionPoint = GetClosestConnectionPoint(rotatedScreenPt, guidelineIntersectionData);
            }
        }

        rotatedScreenPt = intersectionPoint;

        return rotatedScreenPt;
    }

    Point GetClosestConnectionPoint(Point rotatedScreenPt, GuidelineIntersectionData data) {
        Point topConnector = data.CalloutTop.MidPoint;
        Point leftConnector = data.CalloutLeft.MidPoint;
        Point bottomConnector = data.CalloutBottom.MidPoint;
        Point rightConnector = data.CalloutRight.MidPoint;

        double topLength = (rotatedScreenPt - topConnector).Length;
        double leftLength = (rotatedScreenPt - leftConnector).Length;
        double bottomLength = (rotatedScreenPt - bottomConnector).Length;
        double rightLength = (rotatedScreenPt - rightConnector).Length;

        if (topLength < leftLength)
            if (topLength < bottomLength)
                if (topLength < rightLength)
                    return topConnector;
                else
                    return rightConnector;
            else if (bottomLength < rightLength)
                return bottomConnector;
            else
                return rightConnector;
        else if (leftLength < bottomLength)
            if (leftLength < rightLength)
                return leftConnector;
            else
                return rightConnector;
        else if (bottomLength < rightLength)
            return bottomConnector;
        else
            return rightConnector;
    }

    double windowLeft;
    double windowTop;

    GuidelineIntersectionData GetGuidelineIntersectionData(bool positionWindow = false) {
        CalculateWindowPosition(out MyLine testLine, out GuidelineIntersectionData guidelineIntersectionData);

        if (positionWindow) {
            if (Options.AnimateAppearance) {
                Vector vector = screenDanglePoint - targetCenter;
                Point halfwayPoint = new Point(windowLeft, windowTop) + vector * 0.5;
                AnimateFrom(halfwayPoint.X, halfwayPoint.Y);
                Left = halfwayPoint.X;
                Top = halfwayPoint.Y;
            }
            else {
                Left = windowLeft;
                Top = windowTop;
            }
        }
        else {
            windowLeft = Left;
            windowTop = Top;
        }

        calloutScreenCenter = new Point(windowLeft + calloutCenter.X, windowTop + calloutCenter.Y);

        GuidelineIntersectionData correctGuidelineIntersectionData = GetGuidelineIntersectionData(testLine, windowLeft, windowTop);
        GetTrianglePoints(correctGuidelineIntersectionData, guidelineIntersectionData.CalloutDangleSide, windowLeft, windowTop);

        if (double.IsNaN(trianglePoint1.X)) {
            CalculateWindowPosition(out testLine, out guidelineIntersectionData);
            calloutScreenCenter = new Point(windowLeft + calloutCenter.X, windowTop + calloutCenter.Y);

            correctGuidelineIntersectionData = GetGuidelineIntersectionData(testLine, windowLeft, windowTop);
            GetTrianglePoints(correctGuidelineIntersectionData, guidelineIntersectionData.CalloutDangleSide, windowLeft, windowTop);
        }

        return guidelineIntersectionData;
    }

    private void CalculateWindowPosition(out MyLine testLine, out GuidelineIntersectionData guidelineIntersectionData) {
        targetCenter = GetTargetCenter();

        const int almostInfiniteDistance = 222222;
        RotateCalloutToGetPosition(almostInfiniteDistance, out windowLeft, out windowTop);

        Point infiniteCalloutStartPos = GetTargetCenter(-almostInfiniteDistance);
        Point infiniteCalloutCenterPoint = MathEx.RotatePoint(infiniteCalloutStartPos, targetCenter, lastCalloutAngle);

        testLine = new MyLine(targetCenter, infiniteCalloutCenterPoint);
        guidelineIntersectionData = GetGuidelineIntersectionData(testLine, windowLeft, windowTop);
        //double distance = GetDistance(guidelineIntersectionData);

        //RotateCalloutToGetPosition(distance, guidelineIntersectionData.CalloutDangleSide, out windowLeft, out windowTop);
        calloutCenter = new Point(OutsideMargin + calloutWidth / 2, OutsideMargin + calloutHeight / 2);
        GetCalloutPosition(guidelineIntersectionData, out windowLeft, out windowTop);
    }

    private Point GetTargetCenter(double verticalOffset = 0) {
        double horizontalCenterTargetOffset = HorizontalPercentOffset * TargetWidth / 2;
        return TargetClientPointToScreen(new Point(TargetWidth / 2 + horizontalCenterTargetOffset, TargetHeight / 2 + verticalOffset));
    }

    private void RotateCalloutToGetPosition(double distance, out double windowLeft, out double windowTop) {
        Point calloutStartPos = GetTargetCenter(-distance);
        Point calloutCenterPoint = MathEx.RotatePoint(calloutStartPos, targetCenter, lastCalloutAngle);
        windowLeft = calloutCenterPoint.X - (OutsideMargin + calloutWidth / 2);
        windowTop = calloutCenterPoint.Y - (OutsideMargin + calloutHeight / 2);
    }

    double GetXSign() {
        // ![](5D631E255DF1F17130A1FB5820FE16E3.png)
        double angleDegrees = GetAngleDegrees();
        if (angleDegrees > 90 && angleDegrees <= 270)
            return 1;

        return -1;
    }

    double GetYSign() {
        // ![](7EB85C87527FE5FBB12762A9DD59A1B1.png)
        double angleDegrees = GetAngleDegrees();
        if (angleDegrees > 0 && angleDegrees <= 180)
            return 1;

        return -1;
    }

    Point GetCalloutDanglePointForHorizontalExit() {
        // ![](164BA7B27FE650FD419F6223A6677E33.png)

        double adjacentC = calloutWidth / 2 + Options.OuterMargin;
        double theta = GetTheta();
        double oppositeD = Math.Abs(adjacentC * Math.Tan(theta));

        return GetCalloutPoint(adjacentC, oppositeD);
    }

    Point GetCalloutDanglePointForVerticalExit() {
        // ![](9536BE665614588B86AA0DAF4F971BBB.png)
        double oppositeD = calloutHeight / 2 + Options.OuterMargin;
        double theta = GetTheta();
        double tanTheta = Math.Tan(theta);
        double adjacentC;
        if (tanTheta != 0)
            adjacentC = Math.Abs(oppositeD / tanTheta);
        else
            throw new Exception($"tanTheta was zero. We should never reach this point.");

        return GetCalloutPoint(adjacentC, oppositeD);
    }

    public double OutsideMargin {
        get => Options.OuterMargin + indicatorMargin;
    }

    private Point GetCalloutPoint(double adjacentC, double oppositeD) {
        double calloutX = OutsideMargin + calloutWidth / 2 + GetXSign() * adjacentC;
        double calloutY = OutsideMargin + calloutHeight / 2 + GetYSign() * oppositeD;
        return new Point(calloutX, calloutY);
    }

    private Point GetTargetPoint(double adjacentA, double oppositeB) {
        double screenX = targetCenter.X - GetXSign() * adjacentA;
        double screenY = targetCenter.Y - GetYSign() * oppositeB;
        return new Point(screenX, screenY);
    }

    Point GetScreenDanglePointForHorizontalExit() {
        // ![](473394D46C1D2A4F0FA89BEEE7DA7405.png)
        double adjacentA = TargetWidth / 2 + Options.TargetSpacing;
        double theta = GetTheta();
        double oppositeB = Math.Abs(adjacentA * Math.Tan(theta));

        return GetTargetPoint(adjacentA, oppositeB);
    }

    Point GetScreenDanglePointForVerticalExit() {
        // ![](1DDD9F289F77FC56734B77A13828B6B0.png)
        double oppositeB = TargetHeight / 2 + Options.TargetSpacing;
        double theta = GetTheta();
        double tanTheta = Math.Tan(theta);
        double adjacentA;
        if (tanTheta != 0)
            adjacentA = Math.Abs(oppositeB / tanTheta);
        else {
            throw new Exception($"tanTheta is zero. Should never reach this point.");
            //System.Diagnostics.Debugger.Break();
            //adjacentA = TargetWidth / 2 + Options.TargetSpacing;
        }

        return GetTargetPoint(adjacentA, oppositeB);
    }

    private double GetTheta() {
        return GetAngleDegrees() * Math.PI / 180;
    }

    private double GetAngleDegrees() {
        double angleDegrees = 90 - lastCalloutAngle;
        while (angleDegrees < 0)
            angleDegrees += 360;
        return angleDegrees % 360;
    }

    void PlaceGuidelineDiagnostics() {
        Point calloutCenterPoint = new Point(calloutWidth / 2 + OutsideMargin, calloutHeight / 2 + OutsideMargin);
        Line angleGuideline = MathEx.GetRotatedLine(calloutCenterPoint, lastCalloutAngle + 180);
        AddDiagnostic(angleGuideline);

        Rectangle outerMarginRect = new Rectangle();
        outerMarginRect.Width = calloutWidth + 2 * OutsideMargin;
        outerMarginRect.Height = calloutHeight + 2 * OutsideMargin;
        outerMarginRect.Stroke = Brushes.Purple;
        AddDiagnostic(outerMarginRect);

        AddDiagnosticCircle(Brushes.Red, closestIntersectingPoint);
        AddDiagnosticCircle(Brushes.Blue, calloutCenter);
    }

    private void AddDiagnosticCircle(SolidColorBrush fill, Point point) {
        Ellipse ellipse = new Ellipse();
        const double radius = 3d;
        const double diameter = 2 * radius;
        ellipse.Width = diameter;
        ellipse.Height = diameter;
        ellipse.Fill = fill;
        Canvas.SetLeft(ellipse, point.X - radius);
        Canvas.SetTop(ellipse, point.Y - radius);
        AddDiagnostic(ellipse);
    }

    void ShowTriangleDiagnostics() {
        System.Windows.Shapes.Path trianglePath = new System.Windows.Shapes.Path() {
            Stroke = new SolidColorBrush(Color.FromArgb(177, 140, 0, 0)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(44, 255, 0, 0))
        };
        StreamGeometry triangleGeometry = new StreamGeometry();
        trianglePath.Data = triangleGeometry;
        using (StreamGeometryContext ctx = triangleGeometry.Open()) {
            ctx.BeginFigure(trianglePoint1, true, true);
            ctx.LineTo(trianglePoint2, true, true);
            ctx.LineTo(trianglePoint3, true, true);
        }
        AddDiagnostic(trianglePath);
    }

    void LayoutEverything() {
        if (layoutValid)
            return;

        cvsCallout.Children.Clear();
        CreateTemporaryMarkdownViewer();

        layoutValid = true;
    }

    private void ResumeCalloutConstruction() {
        cvsCallout.Children.Clear();
        CalculateBounds();
        GuidelineIntersectionData guidelineIntersectionData = GetGuidelineIntersectionData(true);
        CreateCalloutFrame();
        PlaceCloseButton();
        LayoutText();

        if (showDiagnostics) {
            ShowFlowDocumentDiagnostics(GetDocument(markdownViewer));
        }

        ShowDiagnosticControls(guidelineIntersectionData);
    }

    void RemoveDiagnostics() {
        for (int i = cvsCallout.Children.Count - 1; i >= 0; i--)
            if (cvsCallout.Children[i] is FrameworkElement frameworkElement)
                if (frameworkElement.Tag == diagnosticTag)
                    cvsCallout.Children.RemoveAt(i);
    }

    private void ShowDiagnosticControls(GuidelineIntersectionData guidelineIntersectionData) {
        RemoveDiagnostics();
        if (!showDiagnostics)
            return;
        ShowIntersectedSide(guidelineIntersectionData.CalloutDangleSide);
        PlaceGuidelineDiagnostics();
        ShowTriangleDiagnostics();
    }

    void LoadStyles(Control markdownControl) {
        ResourceDictionary myResourceDictionary = new ResourceDictionary();
        string styleName;
        if (Theme == CalloutTheme.Light)
            styleName = "LightCalloutStyles";
        else if (Theme == CalloutTheme.Dark)
            styleName = "DarkCalloutStyles";
        else {
            // TODO: Add additional style resource loading here.
            return;
        }
        myResourceDictionary.Source = new Uri($"pack://application:,,,/SquadDash;component/Callouts/Styles/{styleName}.xaml", UriKind.Absolute);

        markdownControl.Resources.MergedDictionaries.Add(myResourceDictionary);
    }

    Window targetParentWindow;
    bool layoutValid;
    double calloutWidth;
    double calloutHeight;
    double calloutLeft;
    double calloutTop;
    string markDownText;
    FrameworkElement frameworkElementTarget;
    Point targetCenter;
    Point trianglePoint1;
    Point trianglePoint2;
    Point trianglePoint3;
    Point calloutScreenCenter;
    Point calloutCenter;
    double lastCalloutAngle;
    Point closestIntersectingPoint;
    Control markdownViewer;
    double calculatedHeight;
    double targetParentLeft;
    double targetParentTop;
    double originalLeft;
    double originalTop;
    double deltaLeft;
    double deltaTop;
    bool animating;
    DateTime animationStartTime;
    Point screenDanglePoint;
    Rect rectTarget;

    void PointTo(FrameworkElement target) {
        frameworkElementTarget = target;
        if (frameworkElementTarget.IsVisible) {
            Point screenPosition = frameworkElementTarget.PointToScreen(new Point(0, 0));
            // In case we lose this element later...
            rectTarget = new Rect(screenPosition.X, screenPosition.Y, frameworkElementTarget.ActualWidth, frameworkElementTarget.ActualHeight);
        }
        SetParentWindow(Window.GetWindow(target));
    }

    private void SetParentWindow(Window window) {
        targetParentWindow = window;
        targetParentLeft = targetParentWindow.Left;
        targetParentTop = targetParentWindow.Top;
        this.Owner = window;
    }

    void PointTo(Rect targetRect) {
        frameworkElementTarget = null;
        rectTarget = targetRect;
    }

    private void TargetParentWindow_LocationChanged(object sender, EventArgs e) {
        WindowsLocationChanged();
    }

    private void WindowsLocationChanged() {
        if (targetParentWindow == null)
            return;
        OnRefreshTargetRect();
        double deltaLeft = targetParentWindow.Left - targetParentLeft;
        double deltaTop = targetParentWindow.Top - targetParentTop;
        Left += deltaLeft;
        Top += deltaTop;
        targetParentLeft = targetParentWindow.Left;
        targetParentTop = targetParentWindow.Top;
    }

    void HookTargetParentWindowEvents() {
        if (targetParentWindow == null)
            return;
        targetParentWindow.LocationChanged += TargetParentWindow_LocationChanged;
        targetParentWindow.Closed += ParentWindow_Closed;
        targetParentWindow.Activated += TargetParentWindow_Activated;
        targetParentWindow.Deactivated += TargetParentWindow_Deactivated;
        targetParentWindow.StateChanged += TargetParentWindow_StateChanged;
    }

    private void TargetParentWindow_StateChanged(object sender, EventArgs e) {
        WindowsLocationChanged();
    }

    private void TargetParentWindow_Deactivated(object sender, EventArgs e) {
        CheckTopMostWindow();
    }

    private void TargetParentWindow_Activated(object sender, EventArgs e) {
        CheckTopMostWindow();
    }

    private void UnhookTargetParentWindowEvents() {
        if (targetParentWindow == null)
            return;
        targetParentWindow.Closed -= ParentWindow_Closed;
        targetParentWindow.LocationChanged -= TargetParentWindow_LocationChanged;
        targetParentWindow.Activated -= TargetParentWindow_Activated;
        targetParentWindow.Deactivated -= TargetParentWindow_Deactivated;
        targetParentWindow.StateChanged -= TargetParentWindow_StateChanged;
    }

    bool initializationComplete = false;
    private const string CLR_SkyBlue = "#18b1fc";
    static Color glowColor = (Color)ColorConverter.ConvertFromString(CLR_SkyBlue);
    HueSatLight glowHsl = new HueSatLight(glowColor);
    double lastDragAngle = double.MinValue;

    void FinalizeAndShow() {
        HookTargetParentWindowEvents();
        LayoutEverything();
        initializationComplete = true;
        Show();
    }

    void SetAngle(double angle) {
        if (angle == double.MinValue) {
            angle = GetBestAngleToTarget();
        }
        Options.InitialAngle = angle;
        lastCalloutAngle = angle;
    }

    /// <summary>
    /// Returns the best angle to the specified target, based on the target center position in the screen.
    /// </summary>
    private double GetBestAngleToTarget() {
        var targetCenter = GetTargetCenter();
        Rect screenRect = NativeMethods.GetMonitorBoundsForPhysicalPoint((int)targetCenter.X, (int)targetCenter.Y);
        Point screenCenter = new Point(screenRect.X + screenRect.Width / 2, screenRect.Y + screenRect.Height / 2);

        if (targetCenter.X < screenCenter.X)  // Target is left of screen center.
            if (targetCenter.Y < screenCenter.Y)
                return 135;     // Above left
            else
                return 45;      // Below left
        else  // Target is right of screen center.
            if (targetCenter.Y < screenCenter.Y)
                return 225;     // Above right
            else
                return 315;     // Below right
    }

    public static FrmUltimateCallout ShowCallout(string markDownText, FrameworkElement target, double width = 200, double angle = double.MinValue, CalloutTheme theme = CalloutTheme.Light, double fontSize = 12, double horizontalPercentOffset = 0) {
        var frmUltimateCallout = CreateNewCallout(markDownText, width, theme, fontSize, horizontalPercentOffset);
        frmUltimateCallout.Options.TargetSpacing = fontSize / 2;
        frmUltimateCallout.PointTo(target);
        frmUltimateCallout.SetAngle(angle);
        frmUltimateCallout.FinalizeAndShow();

        return frmUltimateCallout;
    }

    public static FrmUltimateCallout ShowCallout(string markDownText, Rect target, Window parentWindow, double width = 200, double angle = double.MinValue, CalloutTheme theme = CalloutTheme.Light, double fontSize = 12, double horizontalPercentOffset = 0) {
        var frmUltimateCallout = CreateNewCallout(markDownText, width, theme, fontSize, horizontalPercentOffset);
        frmUltimateCallout.Options.TargetSpacing = fontSize / 2;
        frmUltimateCallout.PointTo(target);
        frmUltimateCallout.SetAngle(angle);
        frmUltimateCallout.SetParentWindow(parentWindow);
        frmUltimateCallout.FinalizeAndShow();

        return frmUltimateCallout;
    }

    private static FrmUltimateCallout CreateNewCallout(string markDownText, double width, CalloutTheme theme, double fontSize = 12, double horizontalPercentOffset = 0) {
        FrmUltimateCallout frmUltimateCallout = new FrmUltimateCallout();
        frmUltimateCallout.Options.Width = width;
        frmUltimateCallout.markDownText = markDownText;
        frmUltimateCallout.Theme = theme;
        frmUltimateCallout.FontSize = fontSize;
        frmUltimateCallout.HorizontalPercentOffset = horizontalPercentOffset;
        return frmUltimateCallout;
    }

    public void MoveCallout(string markDownText, double angle, double width) {
        InvalidateLayout();
        lastCalloutAngle = angle;
        Options.InitialAngle = angle;
        Options.Width = width;
        if (this.markDownText != markDownText)
            this.markDownText = markDownText;
        LayoutEverything();
    }

    private void ParentWindow_Closed(object sender, EventArgs e) {
        Close();
    }

    private void Callout_Closed(object sender, EventArgs e) {
        UnhookTargetParentWindowEvents();
    }


    private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    private void Window_Activated(object sender, EventArgs e) {
        CheckTopMostWindow();
    }

    private void Window_Deactivated(object sender, EventArgs e) {
        CheckTopMostWindow();
    }

    void CheckTopMostWindow() {
        if (targetParentWindow != null)
            Topmost = WindowHelper.IsForegroundWindow(targetParentWindow);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        WindowInteropHelper wndHelper = new WindowInteropHelper(this);
        WindowHelper.HideFromAltTab(this);
    }

    Point GetProperLocation(Point danglePoint, GuidelineIntersectionData data) {
        MyLine danglePointGuideline = new MyLine(calloutCenter, danglePoint);
        double calloutLeft = OutsideMargin;
        double calloutTop = OutsideMargin;
        double calloutRight = calloutLeft + calloutWidth;
        double calloutBottom = calloutTop + calloutHeight;

        // TODO: we might have similar code elsewhere.

        MyLine calloutTopLine = MyLine.Horizontal(calloutLeft, calloutRight, calloutTop);
        MyLine calloutBottomLine = MyLine.Horizontal(calloutLeft, calloutRight, calloutBottom);
        MyLine calloutLeftLine = MyLine.Vertical(calloutLeft, calloutTop, calloutBottom);
        MyLine calloutRightLine = MyLine.Vertical(calloutRight, calloutTop, calloutBottom);

        closestIntersectingPoint = danglePointGuideline.GetClosestIntersectingPoint(danglePoint, calloutTopLine, calloutBottomLine, calloutLeftLine, calloutRightLine);
        if (double.IsNaN(closestIntersectingPoint.X))
            return danglePoint;

        MyLine guidelineToEdgeOfCallout = new MyLine(calloutCenter, closestIntersectingPoint);

        double length = guidelineToEdgeOfCallout.Length;
        double desiredLength = length + Options.OuterMargin;

        guidelineToEdgeOfCallout.MatchLength(desiredLength);

        return guidelineToEdgeOfCallout.End;
    }

    void GetCalloutPosition(GuidelineIntersectionData data, out double windowLeft, out double windowTop) {
        Point danglePoint = data.CalloutDangleSide switch {
            CalloutSide.Left => GetCalloutDanglePointForHorizontalExit(),
            CalloutSide.Right => GetCalloutDanglePointForHorizontalExit(),
            CalloutSide.Top => GetCalloutDanglePointForVerticalExit(),
            CalloutSide.Bottom => GetCalloutDanglePointForVerticalExit(),
            _ => throw new NotImplementedException()
        };

        danglePoint = GetProperLocation(danglePoint, data);
        screenDanglePoint = data.TargetDangleSide switch {
            CalloutSide.Left => GetScreenDanglePointForHorizontalExit(),
            CalloutSide.Right => GetScreenDanglePointForHorizontalExit(),
            CalloutSide.Top => GetScreenDanglePointForVerticalExit(),
            CalloutSide.Bottom => GetScreenDanglePointForVerticalExit(),
            _ => throw new NotImplementedException()
        };

        windowLeft = screenDanglePoint.X - danglePoint.X;
        windowTop = screenDanglePoint.Y - danglePoint.Y;
    }

    void GetTrianglePoints(GuidelineIntersectionData data, CalloutSide previousCalloutSide, double windowLeft, double windowTop) {
        MyLine guideline = MathEx.GetRotatedMyLine(targetCenter, lastCalloutAngle);
        Point pt1 = data.CalloutDangleSide switch {
            CalloutSide.Right => guideline.GetSegmentIntersection(data.InnerWindowRight),
            CalloutSide.Left => guideline.GetSegmentIntersection(data.InnerWindowLeft),
            CalloutSide.Bottom => guideline.GetSegmentIntersection(data.InnerWindowBottom),
            CalloutSide.Top => guideline.GetSegmentIntersection(data.InnerWindowTop),
            _ => throw new Exception($"Come on!!!")
        };

        double border = Options.OuterMargin;
        Point calloutUpperLeft = new Point(calloutScreenCenter.X - calloutWidth / 2 - border, calloutScreenCenter.Y - calloutHeight / 2 - border);
        Point calloutLowerRight = new Point(calloutScreenCenter.X + calloutWidth / 2 + border, calloutScreenCenter.Y + calloutHeight / 2 + border);

        double deltaLeft = Left - windowLeft;
        double deltaTop = Top - windowTop;

        Point adjustedCenter = targetCenter;

        pt1.Offset(-Left, -Top);
        pt1 = GetProperLocation(pt1, data);
        pt1.Offset(Left, Top);

        const double innerMargin = 10;
        if ((pt1 - targetCenter).Length < indicatorMargin / 2 || MathEx.IsBetween(targetCenter, calloutUpperLeft, calloutLowerRight, innerMargin)) {
            // Callout is over the target - no dangle needed!
            trianglePoint1 = calloutScreenCenter;
            trianglePoint2 = calloutScreenCenter;
            trianglePoint3 = calloutScreenCenter;
            return;
        }

        Point pt2 = GetTriangleScreenPoint(data, pt1, Options.DangleAngle / 2);
        Point pt3 = GetTriangleScreenPoint(data, pt1, -Options.DangleAngle / 2);

        adjustedCenter.Offset(-deltaLeft, -deltaTop);

        trianglePoint1 = ScreenToCanvasPoint(pt1, windowLeft, windowTop);
        trianglePoint2 = ScreenToCanvasPoint(pt2, windowLeft, windowTop);
        trianglePoint3 = ScreenToCanvasPoint(pt3, windowLeft, windowTop);
    }

    void MouseUpCheck(object sender, EventArgs e) {
        if (GetMouseIsDown())
            return;

        waitingForMouseUpTimer.Stop();
        ActivateParentWindow();
        StartAnimatingTowardTarget();
    }

    void WaitForMouseUp() {
        if (waitingForMouseUpTimer == null)
            waitingForMouseUpTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Input, MouseUpCheck, Dispatcher);

        waitingForMouseUpTimer.Start();
    }

    private void Window_LocationChanged(object sender, EventArgs e) {
        WindowPositionChanged();
    }

    private void WindowPositionChanged() {
        if (cvsCallout == null || cvsCallout.Children.Count == 0)
            return;

        double calloutCenterScreenX = Left + OutsideMargin + calloutWidth / 2;
        double calloutCenterScreenY = Top + OutsideMargin + calloutHeight / 2;
        Point calloutCenter = new Point(calloutCenterScreenX, calloutCenterScreenY);
        double angleDegrees = MathEx.GetAngleDegrees(targetCenter, calloutCenter) + 90;
        while (angleDegrees < 0)
            angleDegrees += 360;
        angleDegrees %= 360;
        if (angleDegrees != lastCalloutAngle) {
            for (int i = cvsCallout.Children.Count - 1; i >= 0; i--)
                if (cvsCallout.Children[i] is System.Windows.Shapes.Path)
                    cvsCallout.Children.RemoveAt(i);

            lastCalloutAngle = angleDegrees;
            GuidelineIntersectionData guidelineIntersectionData = GetGuidelineIntersectionData();
            CreateCalloutFrame();
            ShowDiagnosticControls(guidelineIntersectionData);
        }

        if (GetMouseIsDown()) {
            if (animating)
                StopAnimationTimer();

            if (Options.AnimateBackAfterDrag)
                WaitForMouseUp();
        }
    }

    private static bool GetMouseIsDown() {
        return System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed;
    }

    void StopAnimationTimer() {
        if (!animating)
            return;

        animating = false;
        calloutAnimationTimer?.Stop();
    }

    void MoveWindowToFinalPosition() {
        Left = originalLeft + deltaLeft;
        Top = originalTop + deltaTop;
    }

    public double LastDragAngle { get => lastDragAngle; }

    void TriggerAngleChangedIfNeeded() {
        if (lastCalloutAngle != lastDragAngle) {
            bool shouldFireEvent = lastDragAngle != double.MinValue;
            lastDragAngle = lastCalloutAngle;

            if (shouldFireEvent)
                OnAngleChanged(this, EventArgs.Empty);
        }
    }
    void MoveTheCallout(object sender, EventArgs e) {
        double timeSpanSinceAnimationStartMs = (DateTime.Now - animationStartTime).TotalMilliseconds;

        bool reachedEndOfAnimation = timeSpanSinceAnimationStartMs > Options.AnimationTimeMs;

        if (reachedEndOfAnimation) {
            TriggerAngleChangedIfNeeded();
            MoveWindowToFinalPosition();
            StopAnimationTimer();
            return;
        }

        double percentComplete = InOutQuadBlend(timeSpanSinceAnimationStartMs / Options.AnimationTimeMs);

        Left = originalLeft + deltaLeft * percentComplete;
        Top = originalTop + deltaTop * percentComplete;
    }

    double InOutQuadBlend(double t) {
        if (t <= 0.5f)
            return 2.0f * t * t;
        t -= 0.5f;
        return 2.0f * t * (1.0f - t) + 0.5f;
    }

    void ActivateParentWindow() {
        targetParentWindow?.Activate();
    }

    void StartAnimatingTowardTarget() {
        CalculateWindowPosition(out MyLine testLine, out GuidelineIntersectionData guidelineIntersectionData);
        AnimateFrom(Left, Top);
    }

    /// <summary>
    /// Animates the window from the specified position to the position specified by windowLeft and windowTop.
    /// </summary>
    private void AnimateFrom(double left, double top) {
        originalLeft = left;
        originalTop = top;
        deltaLeft = windowLeft - originalLeft;
        deltaTop = windowTop - originalTop;
        animating = true;
        animationStartTime = DateTime.Now;
        if (calloutAnimationTimer == null)
            calloutAnimationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Input, MoveTheCallout, Dispatcher);
        calloutAnimationTimer.Start();
    }

    public void ForceRefresh() {
        WindowPositionChanged();  // Force a refresh of every calculation.
    }

    public void TargetMoved() {
        Point newTargetCenter = GetTargetCenter();
        double deltaX = newTargetCenter.X - targetCenter.X;
        double deltaY = newTargetCenter.Y - targetCenter.Y;
        Left += deltaX;
        Top += deltaY;
        targetCenter = newTargetCenter;
        if (deltaX == 0 && deltaY == 0)
            WindowPositionChanged();  // Force a refresh of every calculation.

    }

    FlowDocument GetDocument(Control control) {
        if (control is SimpleMarkdownViewer simpleMarkdownViewer)
            return simpleMarkdownViewer.Document;

        return null;

    }

    private void MarkdownViewer_Loaded(object sender, RoutedEventArgs e) {
        Control markdownControl = sender as Control;
        if (markdownControl == null)
            return;

        FlowDocument flowDocument = GetDocument(markdownControl);

        if (flowDocument != null) {
            ReserveSpaceForCloseButton(flowDocument);
            if ((string)markdownControl.Tag == STR_TempMarkdown) {
                calculatedHeight = CalculateFlowDocumentHeight(flowDocument);
                if (flowDocument.Parent is FlowDocumentScrollViewer flowDocumentScrollViewer) {
                    double originalMarkdownWidth = markdownViewer.Width;
                    double lastGoodWidth = markdownViewer.Width;
                    int numTries = 0;
                    while (numTries < 300 && markdownViewer.Width > 10) {
                        markdownViewer.Width -= 5;
                        flowDocument.PageWidth = markdownViewer.Width;
                        double newHeight = CalculateFlowDocumentHeight(flowDocument);
                        if (newHeight != calculatedHeight)
                            break;
                        lastGoodWidth = markdownViewer.Width;
                        numTries++;
                    }
                    double widthDelta = lastGoodWidth - originalMarkdownWidth;
                    if (widthDelta != 0) {
                        idealCalloutWidth = calloutWidth + widthDelta;
                    }
                    markdownViewer.Width = lastGoodWidth;
                    flowDocument.PageWidth = lastGoodWidth;
                    calculatedHeight = CalculateFlowDocumentHeight(flowDocument);
                }
                ResumeCalloutConstruction();
            }
        }
    }

    private void CreateMarkdownViewer() {
        markdownViewer = new SimpleMarkdownViewer();
        markdownViewer.FontSize = FontSize;
    }

    Point TargetClientPointToScreen(Point clientPoint) {
        if (frameworkElementTarget != null && frameworkElementTarget.IsVisible)
            return frameworkElementTarget.PointToScreen(clientPoint);
        return new Point(rectTarget.X + clientPoint.X, rectTarget.Y + clientPoint.Y);
    }

    public void UpdateTargetRect(Rect targetRect) {
        rectTarget = targetRect;
    }

    public double TargetWidth {
        get {
            if (frameworkElementTarget != null && frameworkElementTarget.IsVisible)
                return frameworkElementTarget.ActualWidth;
            else
                return rectTarget.Width;
        }
    }

    public double TargetHeight {
        get {
            if (frameworkElementTarget != null && frameworkElementTarget.IsVisible)
                return frameworkElementTarget.ActualHeight;
            else
                return rectTarget.Height;
        }
    }

    /// <summary>
    /// The opacity of the glow when the dark theme is active.
    /// </summary>
    public double GlowOpacity { get; set; } = 0.2;

    /// <summary>
    /// The amount to shift the center of the target left or right (as a percentage of half the width).
    /// 0 has no shift. 1 shifts the center to the right by half the width, -1 shifts the center target to the left by the same amount.
    /// </summary>
    public double HorizontalPercentOffset { get; set; }
}
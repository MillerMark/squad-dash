using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

[Flags]
internal enum PlanGraphConnectorEndpointRole
{
    None = 0,
    Start = 1,
    End = 2,
}

internal sealed class PlanGraphConnectorGroup
{
    public readonly List<UIElement> GlowElements = [];
    public readonly List<UIElement> MainElements = [];
    public readonly List<string> TaskIds = [];
    public readonly List<Border> GateBadges = [];
    public int SkipCount { get; set; }
    public Point OriginalFrom { get; private set; }
    public Point OriginalTo { get; private set; }
    public Point CurrentFrom { get; private set; }
    public Point CurrentTo { get; private set; }

    private readonly Dictionary<string, PlanGraphConnectorEndpointRole> _endpointRoles =
        new(StringComparer.Ordinal);
    private Action<Point, Point>? _endpointUpdater;

    public void RefreshGlowBrushes()
    {
        var brush = new SolidColorBrush(ConnectorGlowColor(SkipCount));
        foreach (var element in GlowElements)
        {
            if (element is not Shape shape)
                continue;

            if (shape is Polygon)
                shape.Fill = brush;
            else
                shape.Stroke = brush;
        }
    }

    public void InitializeEndpoints(Point from, Point to, Action<Point, Point> endpointUpdater)
    {
        OriginalFrom = from;
        OriginalTo = to;
        CurrentFrom = from;
        CurrentTo = to;
        _endpointUpdater = endpointUpdater;
    }

    public void RegisterEndpointRole(string taskId, PlanGraphConnectorEndpointRole role)
    {
        _endpointRoles.TryGetValue(taskId, out var existing);
        _endpointRoles[taskId] = existing | role;
    }

    public PlanGraphConnectorEndpointRole GetEndpointRole(string taskId) =>
        _endpointRoles.TryGetValue(taskId, out var role)
            ? role
            : PlanGraphConnectorEndpointRole.None;

    public void UpdateEndpoints(Point from, Point to)
    {
        CurrentFrom = from;
        CurrentTo = to;
        _endpointUpdater?.Invoke(from, to);
    }

    private const double ConnectorBaseHue = 210.0;

    private static Color ConnectorGlowColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return AgentStatusCard.IsDarkTheme
            ? HslToRgb(hue, 0.95, 0.20)
            : HslToRgb(hue, 0.95, 0.88);
    }

    private static Color HslToRgb(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;

        double r;
        double g;
        double b;
        if (hue < 60)
        {
            r = c; g = x; b = 0;
        }
        else if (hue < 120)
        {
            r = x; g = c; b = 0;
        }
        else if (hue < 180)
        {
            r = 0; g = c; b = x;
        }
        else if (hue < 240)
        {
            r = 0; g = x; b = c;
        }
        else if (hue < 300)
        {
            r = x; g = 0; b = c;
        }
        else
        {
            r = c; g = 0; b = x;
        }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}

internal sealed class PlanGraphEdgeRenderingContract
{
    private const double ArrowLength = 11;
    private const double ArrowHalfWidth = 5;
    private const double GlowArrowHalf = 10;
    private const double BaseGlowThickness = 8;
    private const double BaseMainThickness = 2;
    private const double BaseHitThickness = 12;
    private const double ConnectorBaseHue = 210.0;
    private const double ConnectorSaturation = 0.70;
    private const double ConnectorLightness = 0.45;

    public PlanGraphEdgeRenderingContract(double lineThicknessMultiplier = 1.0)
    {
        LineThicknessMultiplier = lineThicknessMultiplier > 0
            ? lineThicknessMultiplier
            : 1.0;
    }

    public double LineThicknessMultiplier { get; }

    private double GlowThickness => BaseGlowThickness * LineThicknessMultiplier;
    private double MainThickness => BaseMainThickness * LineThicknessMultiplier;
    private double HitThickness => BaseHitThickness * LineThicknessMultiplier;

    public PlanGraphConnectorGroup AddConnector(
        Canvas canvas,
        Point from,
        Point to,
        bool arrowHead,
        int skipCount = 0,
        bool dashed = false,
        string? toolTip = null,
        double splitAtX = double.NaN,
        PlanGraphConnectorGroup? existingGroup = null)
    {
        var group = existingGroup ?? new PlanGraphConnectorGroup();
        group.SkipCount = skipCount;

        var color = ConnectorColor(skipCount);
        var glowColor = ConnectorGlowColor(skipCount);
        var mainBrush = new SolidColorBrush(color);
        var glowBrush = new SolidColorBrush(glowColor);
        var dashArray = dashed ? new DoubleCollection { 7, 2 } : null;

        var lineEnd = arrowHead ? new Point(to.X - ArrowLength, to.Y) : to;
        var renderStraight = skipCount > 0 || Math.Abs(to.Y - from.Y) < 1.0 || Math.Abs(to.X - from.X) < 1.0;
        var doLineSplit = !double.IsNaN(splitAtX) && splitAtX > from.X + 1.0 && splitAtX < lineEnd.X - 1.0;

        if (renderStraight)
        {
            var glowLine = new Line
            {
                X1 = from.X,
                Y1 = from.Y,
                X2 = lineEnd.X,
                Y2 = lineEnd.Y,
                StrokeThickness = GlowThickness,
                Stroke = glowBrush,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Visibility = Visibility.Hidden,
            };
            if (dashArray is not null)
                glowLine.StrokeDashArray = null;
            canvas.Children.Add(glowLine);
            group.GlowElements.Add(glowLine);

            Line? leftLine = null;
            Line? rightLine = null;
            Line? mainLine = null;
            if (doLineSplit)
            {
                var tSplit = (splitAtX - from.X) / (lineEnd.X - from.X);
                var splitY = from.Y + tSplit * (lineEnd.Y - from.Y);
                var splitPoint = new Point(splitAtX, splitY);

                leftLine = new Line
                {
                    X1 = from.X,
                    Y1 = from.Y,
                    X2 = splitPoint.X,
                    Y2 = splitPoint.Y,
                    StrokeThickness = MainThickness,
                    Stroke = mainBrush,
                };
                if (dashArray is not null)
                    leftLine.StrokeDashArray = dashArray;
                canvas.Children.Add(leftLine);
                group.MainElements.Add(leftLine);

                rightLine = new Line
                {
                    X1 = splitPoint.X,
                    Y1 = splitPoint.Y,
                    X2 = lineEnd.X,
                    Y2 = lineEnd.Y,
                    StrokeThickness = MainThickness,
                    Stroke = mainBrush,
                    StrokeDashArray = new DoubleCollection { 7, 2 },
                };
                if (toolTip is not null)
                    rightLine.ToolTip = toolTip;
                canvas.Children.Add(rightLine);
                group.MainElements.Add(rightLine);
            }
            else
            {
                mainLine = new Line
                {
                    X1 = from.X,
                    Y1 = from.Y,
                    X2 = lineEnd.X,
                    Y2 = lineEnd.Y,
                    StrokeThickness = MainThickness,
                    Stroke = mainBrush,
                };
                if (dashArray is not null)
                    mainLine.StrokeDashArray = dashArray;
                if (toolTip is not null)
                    mainLine.ToolTip = toolTip;
                canvas.Children.Add(mainLine);
                group.MainElements.Add(mainLine);
            }

            var hitLine = new Line
            {
                X1 = from.X,
                Y1 = from.Y,
                X2 = lineEnd.X,
                Y2 = lineEnd.Y,
                StrokeThickness = HitThickness,
                Opacity = 0.01,
            };
            hitLine.SetResourceReference(Shape.StrokeProperty, "CardSurface");
            if (toolTip is not null)
                hitLine.ToolTip = toolTip;
            canvas.Children.Add(hitLine);
            group.MainElements.Add(hitLine);

            Polygon? glowArrow = null;
            Polygon? mainArrow = null;
            if (arrowHead)
            {
                var perpendicular = new Vector(0, 1);
                glowArrow = new Polygon
                {
                    Fill = glowBrush,
                    Visibility = Visibility.Hidden,
                    Points = [to, lineEnd + perpendicular * GlowArrowHalf, lineEnd - perpendicular * GlowArrowHalf],
                };
                canvas.Children.Add(glowArrow);
                group.GlowElements.Add(glowArrow);

                mainArrow = new Polygon
                {
                    Fill = mainBrush,
                    Points = [to, lineEnd + perpendicular * ArrowHalfWidth, lineEnd - perpendicular * ArrowHalfWidth],
                };
                canvas.Children.Add(mainArrow);
                group.MainElements.Add(mainArrow);
            }

            group.InitializeEndpoints(from, to, (updatedFrom, updatedTo) =>
            {
                var updatedLineEnd = arrowHead
                    ? new Point(updatedTo.X - ArrowLength, updatedTo.Y)
                    : updatedTo;

                glowLine.X1 = updatedFrom.X;
                glowLine.Y1 = updatedFrom.Y;
                glowLine.X2 = updatedLineEnd.X;
                glowLine.Y2 = updatedLineEnd.Y;

                if (doLineSplit && leftLine is not null && rightLine is not null)
                {
                    var tSplit = (splitAtX - updatedFrom.X) / (updatedLineEnd.X - updatedFrom.X);
                    var splitY = updatedFrom.Y + tSplit * (updatedLineEnd.Y - updatedFrom.Y);
                    var splitPoint = new Point(splitAtX, splitY);

                    leftLine.X1 = updatedFrom.X;
                    leftLine.Y1 = updatedFrom.Y;
                    leftLine.X2 = splitPoint.X;
                    leftLine.Y2 = splitPoint.Y;
                    rightLine.X1 = splitPoint.X;
                    rightLine.Y1 = splitPoint.Y;
                    rightLine.X2 = updatedLineEnd.X;
                    rightLine.Y2 = updatedLineEnd.Y;
                }
                else if (mainLine is not null)
                {
                    mainLine.X1 = updatedFrom.X;
                    mainLine.Y1 = updatedFrom.Y;
                    mainLine.X2 = updatedLineEnd.X;
                    mainLine.Y2 = updatedLineEnd.Y;
                }

                hitLine.X1 = updatedFrom.X;
                hitLine.Y1 = updatedFrom.Y;
                hitLine.X2 = updatedLineEnd.X;
                hitLine.Y2 = updatedLineEnd.Y;

                if (arrowHead && glowArrow is not null && mainArrow is not null)
                {
                    var perpendicular = new Vector(0, 1);
                    glowArrow.Points = [updatedTo, updatedLineEnd + perpendicular * GlowArrowHalf, updatedLineEnd - perpendicular * GlowArrowHalf];
                    mainArrow.Points = [updatedTo, updatedLineEnd + perpendicular * ArrowHalfWidth, updatedLineEnd - perpendicular * ArrowHalfWidth];
                }
            });
        }
        else
        {
            var dx = lineEnd.X - from.X;
            var handleLength = Math.Max(0, dx * 0.5);
            var controlPoint1 = new Point(from.X + handleLength, from.Y);
            var controlPoint2 = new Point(lineEnd.X - handleLength, lineEnd.Y);

            PathGeometry MakeBezierGeometry()
            {
                var figure = new PathFigure { StartPoint = from };
                figure.Segments.Add(new BezierSegment(controlPoint1, controlPoint2, lineEnd, isStroked: true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                return geometry;
            }

            var glowPath = new Path
            {
                Data = MakeBezierGeometry(),
                StrokeThickness = GlowThickness,
                Stroke = glowBrush,
                Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Visibility = Visibility.Hidden,
            };
            if (dashArray is not null)
                glowPath.StrokeDashArray = null;
            canvas.Children.Add(glowPath);
            group.GlowElements.Add(glowPath);

            var doBezierSplit = !double.IsNaN(splitAtX) && splitAtX > from.X + 1.0 && splitAtX < lineEnd.X - 1.0;
            Path? leftPath = null;
            Path? rightPath = null;
            Path? mainPath = null;
            if (doBezierSplit)
            {
                var lo = 0.0;
                var hi = 1.0;
                for (var index = 0; index < 50; index++)
                {
                    var mid = (lo + hi) * 0.5;
                    if (BezierX(from, controlPoint1, controlPoint2, lineEnd, mid) < splitAtX)
                        lo = mid;
                    else
                        hi = mid;
                }

                SplitBezier(from, controlPoint1, controlPoint2, lineEnd, (lo + hi) * 0.5,
                    out var lp0, out var lp1, out var lp2, out var lp3,
                    out var rp0, out var rp1, out var rp2, out var rp3);

                var leftFigure = new PathFigure { StartPoint = lp0 };
                leftFigure.Segments.Add(new BezierSegment(lp1, lp2, lp3, isStroked: true));
                var leftGeometry = new PathGeometry();
                leftGeometry.Figures.Add(leftFigure);
                leftPath = new Path
                {
                    Data = leftGeometry,
                    StrokeThickness = MainThickness,
                    Stroke = mainBrush,
                    Fill = Brushes.Transparent,
                };
                if (dashArray is not null)
                    leftPath.StrokeDashArray = dashArray;
                canvas.Children.Add(leftPath);
                group.MainElements.Add(leftPath);

                var rightFigure = new PathFigure { StartPoint = rp0 };
                rightFigure.Segments.Add(new BezierSegment(rp1, rp2, rp3, isStroked: true));
                var rightGeometry = new PathGeometry();
                rightGeometry.Figures.Add(rightFigure);
                rightPath = new Path
                {
                    Data = rightGeometry,
                    StrokeThickness = MainThickness,
                    Stroke = mainBrush,
                    Fill = Brushes.Transparent,
                    StrokeDashArray = new DoubleCollection { 7, 2 },
                };
                if (toolTip is not null)
                    rightPath.ToolTip = toolTip;
                canvas.Children.Add(rightPath);
                group.MainElements.Add(rightPath);
            }
            else
            {
                mainPath = new Path
                {
                    Data = MakeBezierGeometry(),
                    StrokeThickness = MainThickness,
                    Stroke = mainBrush,
                    Fill = Brushes.Transparent,
                };
                if (dashArray is not null)
                    mainPath.StrokeDashArray = dashArray;
                if (toolTip is not null)
                    mainPath.ToolTip = toolTip;
                canvas.Children.Add(mainPath);
                group.MainElements.Add(mainPath);
            }

            var hitPath = new Path
            {
                Data = MakeBezierGeometry(),
                StrokeThickness = HitThickness,
                Fill = Brushes.Transparent,
                Opacity = 0.01,
            };
            hitPath.SetResourceReference(Shape.StrokeProperty, "CardSurface");
            if (toolTip is not null)
                hitPath.ToolTip = toolTip;
            canvas.Children.Add(hitPath);
            group.MainElements.Add(hitPath);

            Polygon? glowArrow = null;
            Polygon? mainArrow = null;
            if (arrowHead)
            {
                var perpendicular = new Vector(0, 1);
                glowArrow = new Polygon
                {
                    Fill = glowBrush,
                    Visibility = Visibility.Hidden,
                    Points = [to, lineEnd + perpendicular * GlowArrowHalf, lineEnd - perpendicular * GlowArrowHalf],
                };
                canvas.Children.Add(glowArrow);
                group.GlowElements.Add(glowArrow);

                mainArrow = new Polygon
                {
                    Fill = mainBrush,
                    Points = [to, lineEnd + perpendicular * ArrowHalfWidth, lineEnd - perpendicular * ArrowHalfWidth],
                };
                canvas.Children.Add(mainArrow);
                group.MainElements.Add(mainArrow);
            }

            group.InitializeEndpoints(from, to, (updatedFrom, updatedTo) =>
            {
                var updatedLineEnd = arrowHead
                    ? new Point(updatedTo.X - ArrowLength, updatedTo.Y)
                    : updatedTo;
                var updatedDx = updatedLineEnd.X - updatedFrom.X;
                var updatedHandleLength = Math.Max(0, updatedDx * 0.5);
                var updatedCp1 = new Point(updatedFrom.X + updatedHandleLength, updatedFrom.Y);
                var updatedCp2 = new Point(updatedLineEnd.X - updatedHandleLength, updatedLineEnd.Y);

                PathGeometry MakeUpdatedBezierGeometry()
                {
                    var figure = new PathFigure { StartPoint = updatedFrom };
                    figure.Segments.Add(new BezierSegment(updatedCp1, updatedCp2, updatedLineEnd, isStroked: true));
                    var geometry = new PathGeometry();
                    geometry.Figures.Add(figure);
                    return geometry;
                }

                glowPath.Data = MakeUpdatedBezierGeometry();
                if (doBezierSplit && leftPath is not null && rightPath is not null)
                {
                    var lo = 0.0;
                    var hi = 1.0;
                    for (var index = 0; index < 50; index++)
                    {
                        var mid = (lo + hi) * 0.5;
                        if (BezierX(updatedFrom, updatedCp1, updatedCp2, updatedLineEnd, mid) < splitAtX)
                            lo = mid;
                        else
                            hi = mid;
                    }

                    SplitBezier(updatedFrom, updatedCp1, updatedCp2, updatedLineEnd, (lo + hi) * 0.5,
                        out var lp0, out var lp1, out var lp2, out var lp3,
                        out var rp0, out var rp1, out var rp2, out var rp3);

                    var leftFigure = new PathFigure { StartPoint = lp0 };
                    leftFigure.Segments.Add(new BezierSegment(lp1, lp2, lp3, isStroked: true));
                    var leftGeometry = new PathGeometry();
                    leftGeometry.Figures.Add(leftFigure);
                    leftPath.Data = leftGeometry;

                    var rightFigure = new PathFigure { StartPoint = rp0 };
                    rightFigure.Segments.Add(new BezierSegment(rp1, rp2, rp3, isStroked: true));
                    var rightGeometry = new PathGeometry();
                    rightGeometry.Figures.Add(rightFigure);
                    rightPath.Data = rightGeometry;
                }
                else if (mainPath is not null)
                {
                    mainPath.Data = MakeUpdatedBezierGeometry();
                }

                hitPath.Data = MakeUpdatedBezierGeometry();
                if (arrowHead && glowArrow is not null && mainArrow is not null)
                {
                    var perpendicular = new Vector(0, 1);
                    glowArrow.Points = [updatedTo, updatedLineEnd + perpendicular * GlowArrowHalf, updatedLineEnd - perpendicular * GlowArrowHalf];
                    mainArrow.Points = [updatedTo, updatedLineEnd + perpendicular * ArrowHalfWidth, updatedLineEnd - perpendicular * ArrowHalfWidth];
                }
            });
        }

        return group;
    }

    public PlanGraphConnectorGroup AddRoundedConnectorRoute(
        Canvas canvas,
        IReadOnlyList<(double X, double Y)> route,
        bool arrowHead,
        int skipCount = 0,
        bool dashed = false,
        string? toolTip = null)
    {
        var group = new PlanGraphConnectorGroup { SkipCount = skipCount };
        if (route.Count < 2)
            return group;

        var drawableRoute = route.ToArray();
        var target = new Point(drawableRoute[^1].X, drawableRoute[^1].Y);
        if (arrowHead)
            drawableRoute[^1] = (drawableRoute[^1].X - ArrowLength, drawableRoute[^1].Y);

        PathGeometry MakeGeometry(IReadOnlyList<(double X, double Y)> geometryRoute)
        {
            var corners = ValidationShieldPresenter.ComputeRoundedRouteCorners(geometryRoute)
                .ToDictionary(corner => corner.PointIndex);
            var figure = new PathFigure
            {
                StartPoint = new Point(geometryRoute[0].X, geometryRoute[0].Y),
            };
            for (var index = 1; index < geometryRoute.Count - 1; index++)
            {
                if (!corners.TryGetValue(index, out var corner))
                    continue;
                figure.Segments.Add(new LineSegment(
                    new Point(corner.Entry.X, corner.Entry.Y), isStroked: true));
                figure.Segments.Add(new QuadraticBezierSegment(
                    new Point(corner.Control.X, corner.Control.Y),
                    new Point(corner.Exit.X, corner.Exit.Y),
                    isStroked: true));
            }
            figure.Segments.Add(new LineSegment(
                new Point(geometryRoute[^1].X, geometryRoute[^1].Y), isStroked: true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        var color = ConnectorColor(skipCount);
        var glowColor = ConnectorGlowColor(skipCount);
        var mainBrush = new SolidColorBrush(color);
        var glowBrush = new SolidColorBrush(glowColor);

        var glowPath = new Path
        {
            Data = MakeGeometry(drawableRoute),
            StrokeThickness = GlowThickness,
            Stroke = glowBrush,
            Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Visibility = Visibility.Hidden,
        };
        canvas.Children.Add(glowPath);
        group.GlowElements.Add(glowPath);

        var mainPath = new Path
        {
            Data = MakeGeometry(drawableRoute),
            StrokeThickness = MainThickness,
            Stroke = mainBrush,
            Fill = Brushes.Transparent,
        };
        if (dashed)
            mainPath.StrokeDashArray = new DoubleCollection { 7, 2 };
        if (toolTip is not null)
            mainPath.ToolTip = toolTip;
        canvas.Children.Add(mainPath);
        group.MainElements.Add(mainPath);

        var hitPath = new Path
        {
            Data = MakeGeometry(drawableRoute),
            StrokeThickness = HitThickness,
            Fill = Brushes.Transparent,
            Opacity = 0.01,
        };
        hitPath.SetResourceReference(Shape.StrokeProperty, "CardSurface");
        if (toolTip is not null)
            hitPath.ToolTip = toolTip;
        canvas.Children.Add(hitPath);
        group.MainElements.Add(hitPath);

        Polygon? glowArrow = null;
        Polygon? mainArrow = null;
        if (arrowHead)
        {
            var lineEnd = new Point(drawableRoute[^1].X, drawableRoute[^1].Y);
            var perpendicular = new Vector(0, 1);
            glowArrow = new Polygon
            {
                Fill = glowBrush,
                Visibility = Visibility.Hidden,
                Points = [target, lineEnd + perpendicular * GlowArrowHalf, lineEnd - perpendicular * GlowArrowHalf],
            };
            canvas.Children.Add(glowArrow);
            group.GlowElements.Add(glowArrow);

            mainArrow = new Polygon
            {
                Fill = mainBrush,
                Points = [target, lineEnd + perpendicular * ArrowHalfWidth, lineEnd - perpendicular * ArrowHalfWidth],
            };
            canvas.Children.Add(mainArrow);
            group.MainElements.Add(mainArrow);
        }

        var baseRoute = route.ToArray();
        group.InitializeEndpoints(
            new Point(baseRoute[0].X, baseRoute[0].Y),
            new Point(baseRoute[^1].X, baseRoute[^1].Y),
            (fromPoint, toPoint) =>
            {
                var adjustedRoute = baseRoute.ToArray();
                adjustedRoute[0] = (fromPoint.X, fromPoint.Y);
                adjustedRoute[^1] = (toPoint.X, toPoint.Y);
                var adjustedDrawableRoute = adjustedRoute.ToArray();
                var adjustedTarget = new Point(adjustedDrawableRoute[^1].X, adjustedDrawableRoute[^1].Y);
                if (arrowHead)
                    adjustedDrawableRoute[^1] = (adjustedDrawableRoute[^1].X - ArrowLength, adjustedDrawableRoute[^1].Y);

                var geometry = MakeGeometry(adjustedDrawableRoute);
                glowPath.Data = geometry;
                mainPath.Data = geometry.Clone();
                hitPath.Data = geometry.Clone();
                if (arrowHead && glowArrow is not null && mainArrow is not null)
                {
                    var adjustedLineEnd = new Point(adjustedDrawableRoute[^1].X, adjustedDrawableRoute[^1].Y);
                    var perpendicular = new Vector(0, 1);
                    glowArrow.Points = [adjustedTarget, adjustedLineEnd + perpendicular * GlowArrowHalf, adjustedLineEnd - perpendicular * GlowArrowHalf];
                    mainArrow.Points = [adjustedTarget, adjustedLineEnd + perpendicular * ArrowHalfWidth, adjustedLineEnd - perpendicular * ArrowHalfWidth];
                }
            });

        return group;
    }

    public void RemoveConnectorGroupElements(Canvas canvas, PlanGraphConnectorGroup group)
    {
        foreach (var element in group.GlowElements.Concat(group.MainElements).Distinct())
            canvas.Children.Remove(element);
    }

    private static Color ConnectorColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return HslToRgb(hue, ConnectorSaturation, ConnectorLightness);
    }

    private static Color ConnectorGlowColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return AgentStatusCard.IsDarkTheme
            ? HslToRgb(hue, 0.95, 0.20)
            : HslToRgb(hue, 0.95, 0.88);
    }

    private static Color HslToRgb(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;

        double r;
        double g;
        double b;
        if (hue < 60)
        {
            r = c; g = x; b = 0;
        }
        else if (hue < 120)
        {
            r = x; g = c; b = 0;
        }
        else if (hue < 180)
        {
            r = 0; g = c; b = x;
        }
        else if (hue < 240)
        {
            r = 0; g = x; b = c;
        }
        else if (hue < 300)
        {
            r = x; g = 0; b = c;
        }
        else
        {
            r = c; g = 0; b = x;
        }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static double BezierX(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var mt = 1.0 - t;
        return mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
    }

    private static void SplitBezier(Point p0, Point p1, Point p2, Point p3, double t,
        out Point lp0, out Point lp1, out Point lp2, out Point lp3,
        out Point rp0, out Point rp1, out Point rp2, out Point rp3)
    {
        static Point Lerp(Point a, Point b, double f) => new(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
        var a = Lerp(p0, p1, t);
        var b = Lerp(p1, p2, t);
        var c = Lerp(p2, p3, t);
        var d = Lerp(a, b, t);
        var e = Lerp(b, c, t);
        var f = Lerp(d, e, t);
        lp0 = p0; lp1 = a; lp2 = d; lp3 = f;
        rp0 = f; rp1 = e; rp2 = c; rp3 = p3;
    }
}

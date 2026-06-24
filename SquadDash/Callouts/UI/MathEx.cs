using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Reflection.Metadata;

namespace SquadDash;
public static class MathEx {
    /// <summary>
    /// Rotates one point around another.
    /// </summary>
    /// <param name="pointToRotate">The point to rotate.</param>
    /// <param name="centerPoint">The center point of rotation.</param>
    /// <param name="angleInDegrees">The rotation angle in degrees.</param>
    /// <returns>The rotated point.</returns>
    public static Point RotatePoint(Point pointToRotate, Point centerPoint, double angleInDegrees) {
        double angleInRadians = angleInDegrees * (Math.PI / 180);
        double cosTheta = Math.Cos(angleInRadians);
        double sinTheta = Math.Sin(angleInRadians);
        return new Point {
            X =
                (int)
                (cosTheta * (pointToRotate.X - centerPoint.X) -
                sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X),
            Y =
                (int)
                (sinTheta * (pointToRotate.X - centerPoint.X) +
                cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y)
        };
    }

    public static MyLine GetRotatedMyLine(Point centerPoint, double angleDegrees) {
        Point edgePoint = new Point(centerPoint.X, centerPoint.Y - 50000);
        edgePoint = RotatePoint(edgePoint, centerPoint, angleDegrees);
        return new MyLine(centerPoint, edgePoint);
    }

    public static MyLine GetRotatedMyLineSegment(Point centerPoint, Point edgePoint, double angleDegrees) {
        edgePoint = RotatePoint(edgePoint, centerPoint, angleDegrees);
        return new MyLine(centerPoint, edgePoint);
    }

    public static Line GetRotatedLine(Point centerPoint, double angle) {
        Line angleGuideline = new Line();
        angleGuideline.Stroke = Brushes.Red;
        angleGuideline.X1 = centerPoint.X;
        angleGuideline.Y1 = centerPoint.Y;
        Point point = new Point(angleGuideline.X1, angleGuideline.Y1 - 1000);
        point = RotatePoint(point, centerPoint, angle);
        angleGuideline.X2 = point.X;
        angleGuideline.Y2 = point.Y;
        return angleGuideline;
    }

    static double RadiansToDegrees(double radians) {
        return radians * 180.0 / Math.PI;
    }

    public static double GetAngleDegrees(Point point1, Point point2) {
        double xDiff = point2.X - point1.X;
        double yDiff = point2.Y - point1.Y;
        return RadiansToDegrees(Math.Atan2(yDiff, xDiff));
    }

    static bool IsBetween(double testValue, double min, double max, double innerMargin = 0) {
        return testValue >= min + innerMargin && testValue <= max - innerMargin;
    }

    public static bool IsBetween(Point testPoint, Point bounds1, Point bounds2, double innerMargin = 0) {
        double left = Math.Min(bounds1.X, bounds2.X);
        double right = Math.Max(bounds1.X, bounds2.X);
        double top = Math.Min(bounds1.Y, bounds2.Y);
        double bottom = Math.Max(bounds1.Y, bounds2.Y);
        return IsBetween(testPoint.X, left, right, innerMargin) && IsBetween(testPoint.Y, top, bottom, innerMargin);
    }
}
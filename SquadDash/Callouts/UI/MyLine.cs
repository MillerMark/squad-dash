using System;
using System.Windows;
using System.Diagnostics;
using System.Windows.Markup;
using System.Windows.Shapes;
using Microsoft.VisualBasic;

namespace SquadDash;
[DebuggerDisplay("{DiagnosticDelta} (Length: {Length})")]
public class MyLine {
    public Point Start { get; set; }
    public Point End { get; set; }

    public string DiagnosticDelta {
        get {
            Vector delta = End - Start;
            if (delta.Y == 0)
                return $"Horizontal at {Start.Y}, length = {delta.X}.";
            if (delta.X == 0)
                return $"Vertical at {Start.X}, length = {delta.Y}.";
            return $"Delta: {delta.X}, {delta.Y}";
        }
    }

    public Vector Delta {
        get {
            return End - Start;
        }
    }

    public Point MidPoint {
        get {
            return new Point((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);
        }
    }

    public double Length => Delta.Length;

    public MyLine(double startX, double startY, double endX, double endY) {
        Start = new Point(startX, startY);
        End = new Point(endX, endY);
    }

    public MyLine(Point start, Point end) : this(start.X, start.Y, end.X, end.Y) {
    }

    public bool IntersectsWith(MyLine cd) {
        return DoLinesIntersect(this, cd);
    }


    // Find the point of intersection between
    // the lines p1 --> p2 and p3 --> p4.
    void FindIntersection(MyLine cd, out bool linesIntersect, out bool segmentsIntersect, out Point intersection, out Point closestPoint1, out Point closestPoint2) {
        Point p1 = this.Start;
        Point p2 = this.End;
        Point p3 = cd.Start;
        Point p4 = cd.End;

        // Get the segments' parameters.
        double dx12 = p2.X - p1.X;
        double dy12 = p2.Y - p1.Y;
        double dx34 = p4.X - p3.X;
        double dy34 = p4.Y - p3.Y;

        // Solve for t1 and t2
        double denominator = (dy12 * dx34 - dx12 * dy34);

        double t1 = ((p1.X - p3.X) * dy34 + (p3.Y - p1.Y) * dx34) / denominator;
        if (double.IsInfinity(t1)) {
            // The lines are parallel (or close enough to it).
            linesIntersect = false;
            segmentsIntersect = false;
            intersection = new Point(double.NaN, double.NaN);
            closestPoint1 = new Point(double.NaN, double.NaN);
            closestPoint2 = new Point(double.NaN, double.NaN);
            return;
        }
        linesIntersect = true;

        double t2 =
            ((p3.X - p1.X) * dy12 + (p1.Y - p3.Y) * dx12)
                / -denominator;

        // Find the point of intersection.
        intersection = new Point(p1.X + dx12 * t1, p1.Y + dy12 * t1);

        // The segments intersect if t1 and t2 are between 0 and 1.
        segmentsIntersect =
            ((t1 >= 0) && (t1 <= 1) &&
             (t2 >= 0) && (t2 <= 1));

        // Find the closest points on the segments.
        if (t1 < 0) {
            t1 = 0;
        }
        else if (t1 > 1) {
            t1 = 1;
        }

        if (t2 < 0) {
            t2 = 0;
        }
        else if (t2 > 1) {
            t2 = 1;
        }

        closestPoint1 = new Point(p1.X + dx12 * t1, p1.Y + dy12 * t1);
        closestPoint2 = new Point(p3.X + dx34 * t2, p3.Y + dy34 * t2);
    }

    public Point GetIntersection(MyLine cd) {
        FindIntersection(cd, out bool linesIntersect, out bool segmentsIntersect, out Point intersection, out Point closestPoint1, out Point closestPoint2);
        if (linesIntersect)
            return intersection;

        return new Point(double.NaN, double.NaN);
    }

    public Point GetSegmentIntersection(MyLine cd) {
        FindIntersection(cd, out bool linesIntersect, out bool segmentsIntersect, out Point intersection, out Point closestPoint1, out Point closestPoint2);
        if (segmentsIntersect)
            return intersection;

        return new Point(double.NaN, double.NaN);
    }


    /* Let's say your point A = (x1, y1), point B = (x2, y2), C = (x_3, y_3), D = (x_4, y_4). 
 * Your first line is defined by AB (with A != B), and your second one by CD (with C != D). */
    static bool DoLinesIntersect(MyLine ab, MyLine cd) {
        double x1 = ab.Start.X;
        double x2 = ab.End.X;
        double x3 = cd.Start.X;
        double x4 = cd.End.X;
        double y1 = ab.Start.Y;
        double y2 = ab.End.Y;
        double y3 = cd.Start.Y;
        double y4 = cd.End.Y;

        if (x1 == x2)
            return !(x3 == x4 && x1 != x3);

        if (x3 == x4)
            return true;

        // Both lines are not parallel to the y-axis
        double m1 = (y1 - y2) / (x1 - x2);
        double m2 = (y3 - y4) / (x3 - x4);
        return m1 != m2;
    }

    public static MyLine Horizontal(double left, double right, double y) {
        return new MyLine(left, y, right, y);
    }

    public static MyLine Vertical(double x, double top, double bottom) {
        return new MyLine(x, top, x, bottom);
    }

    static double GetDistanceToIntersection(Point testPoint, MyLine line1, MyLine line2, out Point intersection) {
        intersection = line1.GetIntersection(line2);
        if (double.IsNaN(intersection.X))
            return double.MaxValue;
        return (intersection - testPoint).Length;
    }

    public Point GetClosestIntersectingPoint(Point testPoint, params MyLine[] lines) {
        double shortestDistance = double.MaxValue;
        Point closestPoint = new Point(double.NaN, double.NaN);

        foreach (MyLine myLine in lines) {
            Point intersection = GetSegmentIntersection(myLine);
            if (double.IsNaN(intersection.X))
                continue;

            double distance = (intersection - testPoint).Length;
            if (distance < shortestDistance) {
                shortestDistance = distance;
                closestPoint = intersection;
            }
        }

        return closestPoint;
    }

    void SetEnd(Vector delta) {
        End = new Point(Start.X + delta.X, Start.Y + delta.Y);
    }

    public void MatchLength(double desiredLength) {
        Vector delta = Delta;
        double existingLength = delta.Length;
        double factor = desiredLength / existingLength;
        delta *= factor;
        SetEnd(delta);
    }

    /// <summary>
    /// Creates a new MyLine at the specified offset from this MyLine.
    /// </summary>
    /// <param name="offsetX">The Delta X for the new MyLine.</param>
    /// <param name="offsetY">The Delta Y for the new MyLine.</param>
    /// <returns></returns>
    MyLine Move(int offsetX, int offsetY) {
        return new MyLine(new Point(Start.X + offsetX, Start.Y + offsetY), new Point(End.X + offsetX, End.Y + offsetY));
    }

    /// <summary>
    /// Creates a new MyLine above this one, offset by the specified distance.
    /// </summary>
    /// <param name="value">The vertical distance in pixels to offset.</param>
    /// <returns>The newly created MyLine.</returns>
    public MyLine MoveUp(int value) {
        return Move(0, -value);
    }

    /// <summary>
    /// Creates a new MyLine to the Left of this one, offset by the specified distance.
    /// </summary>
    /// <param name="value">The horizontal distance in pixels to offset.</param>
    /// <returns>The newly created MyLine.</returns>
    public MyLine MoveLeft(int value) {
        return Move(-value, 0);
    }

    /// <summary>
    /// Creates a new MyLine to the Right of this one, offset by the specified distance.
    /// </summary>
    /// <param name="value">The horizontal distance in pixels to offset.</param>
    /// <returns>The newly created MyLine.</returns>
    public MyLine MoveRight(int value) {
        return Move(value, 0);
    }

    /// <summary>
    /// Creates a new MyLine below this one, offset by the specified distance.
    /// </summary>
    /// <param name="value">The vertical distance in pixels to offset.</param>
    /// <returns>The newly created MyLine.</returns>
    public MyLine MoveDown(int value) {
        return Move(0, value);
    }
}
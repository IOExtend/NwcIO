using System.Globalization;

namespace NwcIO.Model.Grids;

/// <summary>
/// A straight spatial grid line (axis) in a BIM or Navisworks coordination model.
/// </summary>
public sealed class NwcGridLine
{
    public string Name { get; set; } = string.Empty;

    public Vector3d Start { get; set; }

    public Vector3d End { get; set; }

    public double Elevation { get; set; }

    public string? SystemName { get; set; }

    public double Length => Vector3d.Distance(Start, End);

    public Vector3d Direction => (End - Start).Normalize();

    public NwcGridLine() { }

    public NwcGridLine(string name, Vector3d start, Vector3d end, double elevation = 0.0, string? systemName = null)
    {
        Name = name;
        Start = start;
        End = end;
        Elevation = elevation;
        SystemName = systemName;
    }

    /// <summary>
    /// Computes the point on this grid line closest to the specified 3D point.
    /// </summary>
    public Vector3d ClosestPoint(Vector3d point, bool segmentOnly = false)
    {
        var v = End - Start;
        double lenSq = v.LengthSquared;
        if (lenSq < 1e-12) return Start;

        double t = Vector3d.Dot(point - Start, v) / lenSq;
        if (segmentOnly)
        {
            t = Math.Clamp(t, 0.0, 1.0);
        }

        return Start + v * t;
    }

    /// <summary>
    /// Computes the 3D distance from the specified point to this grid line.
    /// </summary>
    public double DistanceToPoint(Vector3d point, bool segmentOnly = false)
    {
        var closest = ClosestPoint(point, segmentOnly);
        return Vector3d.Distance(point, closest);
    }

    /// <summary>
    /// Computes the horizontal 2D distance (XY plane) from the specified point to this grid line.
    /// </summary>
    public double DistanceToPoint2D(Vector3d point, bool segmentOnly = false)
    {
        var p2 = new Vector3d(point.X, point.Y, 0.0);
        var s2 = new Vector3d(Start.X, Start.Y, 0.0);
        var e2 = new Vector3d(End.X, End.Y, 0.0);

        var v = e2 - s2;
        double lenSq = v.LengthSquared;
        if (lenSq < 1e-12) return Vector3d.Distance(p2, s2);

        double t = Vector3d.Dot(p2 - s2, v) / lenSq;
        if (segmentOnly)
        {
            t = Math.Clamp(t, 0.0, 1.0);
        }

        var closest = s2 + v * t;
        return Vector3d.Distance(p2, closest);
    }

    /// <summary>
    /// Computes signed offset distance from the line in 2D (positive on left side, negative on right side).
    /// </summary>
    public double SignedDistance2D(Vector3d point)
    {
        var v = End - Start;
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len < 1e-12) return 0.0;

        double nx, ny;
        if (Math.Abs(v.Y) >= Math.Abs(v.X))
        {
            // Vertical or near-vertical line: orient normal towards +X (increasing grid coordinates)
            nx = v.Y / len;
            ny = -v.X / len;
            if (nx < 0) { nx = -nx; ny = -ny; }
        }
        else
        {
            // Horizontal or near-horizontal line: orient normal towards +Y (increasing grid coordinates)
            nx = -v.Y / len;
            ny = v.X / len;
            if (ny < 0) { nx = -nx; ny = -ny; }
        }

        return (point.X - Start.X) * nx + (point.Y - Start.Y) * ny;
    }

    /// <summary>
    /// Calculates the 2D intersection point in the XY plane between this grid line and another.
    /// </summary>
    public bool Intersect2D(NwcGridLine other, out Vector3d intersectionPoint)
    {
        ArgumentNullException.ThrowIfNull(other);

        double x1 = Start.X, y1 = Start.Y;
        double x2 = End.X, y2 = End.Y;
        double x3 = other.Start.X, y3 = other.Start.Y;
        double x4 = other.End.X, y4 = other.End.Y;

        double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denom) < 1e-9)
        {
            intersectionPoint = Vector3d.Zero;
            return false;
        }

        double px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
        double py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
        double pz = (Elevation + other.Elevation) * 0.5;

        intersectionPoint = new Vector3d(px, py, pz);
        return true;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"GridLine '{Name}': ({Start.X:F1},{Start.Y:F1}) -> ({End.X:F1},{End.Y:F1})");
}

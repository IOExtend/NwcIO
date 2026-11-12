namespace NwcIO.Model.Clash;

/// <summary>
/// High-performance 3D triangle-to-triangle intersection test based on Tomas Möller's algorithm.
/// Pure managed C# with zero external dependencies.
/// </summary>
internal static class NwcTriangleIntersection
{
    private const double Epsilon = 1e-7;

    /// <summary>
    /// Tests whether two 3D triangles intersect and returns the approximate intersection point.
    /// </summary>
    public static bool TrianglesIntersect(
        in Vector3d v0, in Vector3d v1, in Vector3d v2,
        in Vector3d u0, in Vector3d u1, in Vector3d u2,
        out Vector3d intersectionPoint)
    {
        intersectionPoint = Vector3d.Zero;

        // Unit normals make plane distances independent of triangle area.
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var n1 = Vector3d.Cross(e1, e2);
        var f1 = u1 - u0;
        var f2 = u2 - u0;
        var n2 = Vector3d.Cross(f1, f2);
        var length1 = Math.Sqrt(Vector3d.Dot(n1, n1));
        var length2 = Math.Sqrt(Vector3d.Dot(n2, n2));
        if (length1 == 0.0 || length2 == 0.0)
        {
            return false;
        }

        n1 /= length1;
        n2 /= length2;
        var size = Math.Sqrt(Math.Max(
            Math.Max(Vector3d.Dot(e1, e1), Math.Max(Vector3d.Dot(e2, e2), Vector3d.Dot(v2 - v1, v2 - v1))),
            Math.Max(Vector3d.Dot(f1, f1), Math.Max(Vector3d.Dot(f2, f2), Vector3d.Dot(u2 - u1, u2 - u1)))));
        var distanceTolerance = Epsilon * size;

        // Distances of triangle U to plane 1
        var du0 = Vector3d.Dot(n1, u0 - v0);
        var du1 = Vector3d.Dot(n1, u1 - v0);
        var du2 = Vector3d.Dot(n1, u2 - v0);

        if (Math.Abs(du0) < distanceTolerance) du0 = 0.0;
        if (Math.Abs(du1) < distanceTolerance) du1 = 0.0;
        if (Math.Abs(du2) < distanceTolerance) du2 = 0.0;

        double du0du1 = du0 * du1;
        double du0du2 = du0 * du2;

        if (du0du1 > 0.0 && du0du2 > 0.0) return false; // Same side of plane 1

        // Distances of triangle V to plane 2
        var dv0 = Vector3d.Dot(n2, v0 - u0);
        var dv1 = Vector3d.Dot(n2, v1 - u0);
        var dv2 = Vector3d.Dot(n2, v2 - u0);

        if (Math.Abs(dv0) < distanceTolerance) dv0 = 0.0;
        if (Math.Abs(dv1) < distanceTolerance) dv1 = 0.0;
        if (Math.Abs(dv2) < distanceTolerance) dv2 = 0.0;

        double dv0dv1 = dv0 * dv1;
        double dv0dv2 = dv0 * dv2;

        if (dv0dv1 > 0.0 && dv0dv2 > 0.0) return false; // Same side of plane 2

        // Unit normals make this parallelism test dimensionless.
        var d = Vector3d.Cross(n1, n2);
        double maxD = Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z)));
        if (maxD < Epsilon)
        {
            if (du0 != 0.0 || du1 != 0.0 || du2 != 0.0 ||
                dv0 != 0.0 || dv1 != 0.0 || dv2 != 0.0)
            {
                return false;
            }

            if (CoplanarTrianglesIntersect(v0, v1, v2, u0, u1, u2, n1, distanceTolerance))
            {
                intersectionPoint = (v0 + v1 + v2 + u0 + u1 + u2) / 6.0;
                return true;
            }
            return false;
        }

        // Compute intersection interval for triangle V
        ComputeInterval(v0, v1, v2, dv0, dv1, dv2, dv0dv1, dv0dv2, d, out double t0, out double t1, out Vector3d pt0, out Vector3d pt1);

        // Compute intersection interval for triangle U
        ComputeInterval(u0, u1, u2, du0, du1, du2, du0du1, du0du2, d, out double s0, out double s1, out Vector3d ps0, out Vector3d ps1);

        if (t0 > t1) { (t0, t1) = (t1, t0); (pt0, pt1) = (pt1, pt0); }
        if (s0 > s1) { (s0, s1) = (s1, s0); (ps0, ps1) = (ps1, ps0); }

        if (t1 < s0 || s1 < t0) return false;

        // Overlapping segment center
        intersectionPoint = (pt0 + pt1 + ps0 + ps1) / 4.0;
        return true;
    }

    private static bool CoplanarTrianglesIntersect(
        in Vector3d v0, in Vector3d v1, in Vector3d v2,
        in Vector3d u0, in Vector3d u1, in Vector3d u2,
        in Vector3d normal, double tolerance)
    {
        // Drop the dominant normal axis and test the six edge normals in 2D.
        var axis = Math.Abs(normal.X) > Math.Abs(normal.Y) ? 0 : 1;
        if (Math.Abs(normal.Z) > (axis == 0 ? Math.Abs(normal.X) : Math.Abs(normal.Y)))
        {
            axis = 2;
        }

        var a = Project(Vector3d.Zero, axis);
        var b = Project(v1 - v0, axis);
        var c = Project(v2 - v0, axis);
        var d = Project(u0 - v0, axis);
        var e = Project(u1 - v0, axis);
        var f = Project(u2 - v0, axis);
        return !IsSeparatingEdge(a, b, a, b, c, d, e, f, tolerance) &&
            !IsSeparatingEdge(b, c, a, b, c, d, e, f, tolerance) &&
            !IsSeparatingEdge(c, a, a, b, c, d, e, f, tolerance) &&
            !IsSeparatingEdge(d, e, a, b, c, d, e, f, tolerance) &&
            !IsSeparatingEdge(e, f, a, b, c, d, e, f, tolerance) &&
            !IsSeparatingEdge(f, d, a, b, c, d, e, f, tolerance);
    }

    private static (double X, double Y) Project(in Vector3d point, int axis)
    {
        return axis == 0 ? (point.Y, point.Z) : axis == 1 ? (point.X, point.Z) : (point.X, point.Y);
    }

    private static bool IsSeparatingEdge(
        (double X, double Y) start, (double X, double Y) end,
        (double X, double Y) a, (double X, double Y) b, (double X, double Y) c,
        (double X, double Y) d, (double X, double Y) e, (double X, double Y) f,
        double tolerance)
    {
        var x = start.Y - end.Y;
        var y = end.X - start.X;
        var pa = x * a.X + y * a.Y;
        var pb = x * b.X + y * b.Y;
        var pc = x * c.X + y * c.Y;
        var pd = x * d.X + y * d.Y;
        var pe = x * e.X + y * e.Y;
        var pf = x * f.X + y * f.Y;
        var margin = tolerance * Math.Sqrt(x * x + y * y);
        return Math.Max(pa, Math.Max(pb, pc)) < Math.Min(pd, Math.Min(pe, pf)) - margin ||
            Math.Max(pd, Math.Max(pe, pf)) < Math.Min(pa, Math.Min(pb, pc)) - margin;
    }

    private static void ComputeInterval(
        in Vector3d a, in Vector3d b, in Vector3d c,
        double da, double db, double dc,
        double dadb, double dadc,
        in Vector3d d,
        out double t0, out double t1,
        out Vector3d p0, out Vector3d p1)
    {
        // Distances have already been snapped using the relative plane tolerance.
        if (dadb > 0.0)
        {
            // c is on the other side
            p0 = Interpolate(a, c, da, dc);
            p1 = Interpolate(b, c, db, dc);
        }
        else if (dadc > 0.0)
        {
            // b is on the other side
            p0 = Interpolate(a, b, da, db);
            p1 = Interpolate(c, b, dc, db);
        }
        else if (db * dc > 0.0 || da != 0.0)
        {
            // a is on the other side
            p0 = Interpolate(b, a, db, da);
            p1 = Interpolate(c, a, dc, da);
        }
        else if (db != 0.0)
        {
            p0 = Interpolate(a, b, da, db);
            p1 = Interpolate(c, b, dc, db);
        }
        else
        {
            p0 = a;
            p1 = b;
        }

        t0 = Vector3d.Dot(p0, d);
        t1 = Vector3d.Dot(p1, d);
    }

    private static Vector3d Interpolate(in Vector3d p1, in Vector3d p2, double d1, double d2)
    {
        double diff = d1 - d2;
        if (diff == 0.0) return (p1 + p2) * 0.5;
        double alpha = d1 / diff;
        return p1 + (p2 - p1) * alpha;
    }
}

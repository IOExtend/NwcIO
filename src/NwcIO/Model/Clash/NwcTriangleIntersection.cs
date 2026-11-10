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

        // Plane 1: N1 . X + d1 = 0
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var n1 = Vector3d.Cross(e1, e2);
        double d1 = -Vector3d.Dot(n1, v0);

        // Distances of triangle U to plane 1
        double du0 = Vector3d.Dot(n1, u0) + d1;
        double du1 = Vector3d.Dot(n1, u1) + d1;
        double du2 = Vector3d.Dot(n1, u2) + d1;

        if (Math.Abs(du0) < Epsilon) du0 = 0.0;
        if (Math.Abs(du1) < Epsilon) du1 = 0.0;
        if (Math.Abs(du2) < Epsilon) du2 = 0.0;

        double du0du1 = du0 * du1;
        double du0du2 = du0 * du2;

        if (du0du1 > 0.0 && du0du2 > 0.0) return false; // Same side of plane 1

        // Plane 2: N2 . X + d2 = 0
        var f1 = u1 - u0;
        var f2 = u2 - u0;
        var n2 = Vector3d.Cross(f1, f2);
        double d2 = -Vector3d.Dot(n2, u0);

        // Distances of triangle V to plane 2
        double dv0 = Vector3d.Dot(n2, v0) + d2;
        double dv1 = Vector3d.Dot(n2, v1) + d2;
        double dv2 = Vector3d.Dot(n2, v2) + d2;

        if (Math.Abs(dv0) < Epsilon) dv0 = 0.0;
        if (Math.Abs(dv1) < Epsilon) dv1 = 0.0;
        if (Math.Abs(dv2) < Epsilon) dv2 = 0.0;

        double dv0dv1 = dv0 * dv1;
        double dv0dv2 = dv0 * dv2;

        if (dv0dv1 > 0.0 && dv0dv2 > 0.0) return false; // Same side of plane 2

        // Direction of intersection line
        var d = Vector3d.Cross(n1, n2);
        double maxD = Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z)));
        if (maxD < Epsilon)
        {
            // Triangles are nearly coplanar; check AABB overlap as conservative test
            var minV = new Vector3d(
                Math.Min(v0.X, Math.Min(v1.X, v2.X)),
                Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)),
                Math.Min(v0.Z, Math.Min(v1.Z, v2.Z)));
            var maxV = new Vector3d(
                Math.Max(v0.X, Math.Max(v1.X, v2.X)),
                Math.Max(v0.Y, Math.Max(v1.Y, v2.Y)),
                Math.Max(v0.Z, Math.Max(v1.Z, v2.Z)));

            var minU = new Vector3d(
                Math.Min(u0.X, Math.Min(u1.X, u2.X)),
                Math.Min(u0.Y, Math.Min(u1.Y, u2.Y)),
                Math.Min(u0.Z, Math.Min(u1.Z, u2.Z)));
            var maxU = new Vector3d(
                Math.Max(u0.X, Math.Max(u1.X, u2.X)),
                Math.Max(u0.Y, Math.Max(u1.Y, u2.Y)),
                Math.Max(u0.Z, Math.Max(u1.Z, u2.Z)));

            bool aabbIntersect =
                minV.X <= maxU.X && maxV.X >= minU.X &&
                minV.Y <= maxU.Y && maxV.Y >= minU.Y &&
                minV.Z <= maxU.Z && maxV.Z >= minU.Z;

            if (aabbIntersect)
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

    private static void ComputeInterval(
        in Vector3d a, in Vector3d b, in Vector3d c,
        double da, double db, double dc,
        double dadb, double dadc,
        in Vector3d d,
        out double t0, out double t1,
        out Vector3d p0, out Vector3d p1)
    {
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
        else if (db * dc > 0.0 || Math.Abs(da) > Epsilon)
        {
            // a is on the other side
            p0 = Interpolate(b, a, db, da);
            p1 = Interpolate(c, a, dc, da);
        }
        else if (Math.Abs(db) > Epsilon)
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
        if (Math.Abs(diff) < Epsilon) return (p1 + p2) * 0.5;
        double alpha = d1 / diff;
        return p1 + (p2 - p1) * alpha;
    }
}

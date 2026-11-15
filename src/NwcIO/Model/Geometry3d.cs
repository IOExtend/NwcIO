using System.Globalization;

namespace NwcIO.Model;

/// <summary>Represents a 3D vector with double-precision components.</summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d Zero { get; } = new(0, 0, 0);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => Math.Sqrt(LengthSquared);

    public Vector3d Normalize()
    {
        double len = Length;
        return len > 1e-12 ? new(X / len, Y / len, Z / len) : Zero;
    }

    public static double Dot(Vector3d a, Vector3d b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3d Cross(Vector3d a, Vector3d b) =>
        new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    public static double Distance(Vector3d a, Vector3d b) =>
        (a - b).Length;

    public static Vector3d operator +(Vector3d a, Vector3d b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator -(Vector3d v) =>
        new(-v.X, -v.Y, -v.Z);

    public static Vector3d operator *(Vector3d v, double scalar) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static Vector3d operator *(double scalar, Vector3d v) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static Vector3d operator /(Vector3d v, double scalar) =>
        new(v.X / scalar, v.Y / scalar, v.Z / scalar);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:F3}, {Y:F3}, {Z:F3})");
}

/// <summary>Represents a unit rotation quaternion with double-precision components (X, Y, Z, W).</summary>
public readonly record struct Quaterniond(double X, double Y, double Z, double W)
{
    public static Quaterniond Identity { get; } = new(0, 0, 0, 1);

    /// <summary>Returns true if this quaternion is approximately normalized.</summary>
    public bool IsNormalized(double epsilon = 1e-4) =>
        Math.Abs((X * X + Y * Y + Z * Z + W * W) - 1.0) < epsilon;

    /// <summary>
    /// Returns this quaternion scaled to unit length, or <see cref="Identity"/> when its
    /// length is approximately zero or a component is not finite. Squaring is done after
    /// dividing by the largest component, so large finite inputs cannot overflow the norm.
    /// </summary>
    public Quaterniond Normalized()
    {
        double max = Math.Max(Math.Max(Math.Abs(X), Math.Abs(Y)), Math.Max(Math.Abs(Z), Math.Abs(W)));
        if (!double.IsFinite(max) || max <= 0)
        {
            return Identity;
        }

        double sx = X / max;
        double sy = Y / max;
        double sz = Z / max;
        double sw = W / max;
        double scaledNorm = Math.Sqrt(sx * sx + sy * sy + sz * sz + sw * sw); // in [1, 2]

        // Keep the near-zero cut-off on the full norm (scaledNorm * max is the true
        // norm, within rounding): a small-but-valid quaternion such as
        // (7.5e-10, 0, 0, 7.5e-10) has norm > 1e-9 and must still normalize.
        if (!(scaledNorm * max > 1e-9))
        {
            return Identity;
        }

        return new(sx / scaledNorm, sy / scaledNorm, sz / scaledNorm, sw / scaledNorm);
    }

    /// <summary>
    /// Creates a normalized quaternion from a 3x3 rotation matrix (row-major: m00, m01, m02, m10, m11, m12, m20, m21, m22).
    /// Uses Shepperd's algorithm matching Autodesk Navisworks <c>LcLMatrix3f::ConvertRotation</c>.
    /// </summary>
    public static Quaterniond FromRotationMatrix(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22)
    {
        double trace = m00 + m11 + m22;
        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;
            double invS = 1.0 / s;
            return new Quaterniond(
                (m21 - m12) * invS,
                (m02 - m20) * invS,
                (m10 - m01) * invS,
                0.25 * s).Normalized();
        }

        if (m00 > m11 && m00 > m22)
        {
            double s = Math.Sqrt(Math.Max(0.0, 1.0 + m00 - m11 - m22)) * 2.0;
            double invS = s > 1e-9 ? 1.0 / s : 0.0;
            return new Quaterniond(
                0.25 * s,
                (m01 + m10) * invS,
                (m02 + m20) * invS,
                (m21 - m12) * invS).Normalized();
        }

        if (m11 > m22)
        {
            double s = Math.Sqrt(Math.Max(0.0, 1.0 + m11 - m00 - m22)) * 2.0;
            double invS = s > 1e-9 ? 1.0 / s : 0.0;
            return new Quaterniond(
                (m01 + m10) * invS,
                0.25 * s,
                (m12 + m21) * invS,
                (m02 - m20) * invS).Normalized();
        }
        else
        {
            double s = Math.Sqrt(Math.Max(0.0, 1.0 + m22 - m00 - m11)) * 2.0;
            double invS = s > 1e-9 ? 1.0 / s : 0.0;
            return new Quaterniond(
                (m02 + m20) * invS,
                (m12 + m21) * invS,
                0.25 * s,
                (m10 - m01) * invS).Normalized();
        }
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:F4}, {Y:F4}, {Z:F4}, {W:F4})");
}

/// <summary>An axis-aligned 3D bounding box defined by minimum and maximum extents.</summary>
public readonly record struct BoundingBox3d(Vector3d Min, Vector3d Max)
{
    public static BoundingBox3d Empty { get; } = new(
        new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public Vector3d Center => new((Min.X + Max.X) * 0.5, (Min.Y + Max.Y) * 0.5, (Min.Z + Max.Z) * 0.5);

    public Vector3d Size => new(Max.X - Min.X, Max.Y - Min.Y, Max.Z - Min.Z);

    public double Volume => IsEmpty ? 0.0 : Math.Max(0.0, Size.X) * Math.Max(0.0, Size.Y) * Math.Max(0.0, Size.Z);

    public bool Contains(Vector3d point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y &&
        point.Z >= Min.Z && point.Z <= Max.Z;

    public bool Intersects(BoundingBox3d other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    /// <summary>
    /// Computes the intersection of this bounding box with another.
    /// Returns <see cref="Empty"/> if the boxes do not intersect.
    /// </summary>
    public BoundingBox3d Intersect(BoundingBox3d other)
    {
        if (!Intersects(other)) return Empty;
        return new(
            new(Math.Max(Min.X, other.Min.X), Math.Max(Min.Y, other.Min.Y), Math.Max(Min.Z, other.Min.Z)),
            new(Math.Min(Max.X, other.Max.X), Math.Min(Max.Y, other.Max.Y), Math.Min(Max.Z, other.Max.Z)));
    }

    public BoundingBox3d Union(BoundingBox3d other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        return new(
            new(Math.Min(Min.X, other.Min.X), Math.Min(Min.Y, other.Min.Y), Math.Min(Min.Z, other.Min.Z)),
            new(Math.Max(Max.X, other.Max.X), Math.Max(Max.Y, other.Max.Y), Math.Max(Max.Z, other.Max.Z)));
    }

    public BoundingBox3d Union(Vector3d point)
    {
        if (IsEmpty) return new(point, point);

        return new(
            new(Math.Min(Min.X, point.X), Math.Min(Min.Y, point.Y), Math.Min(Min.Z, point.Z)),
            new(Math.Max(Max.X, point.X), Math.Max(Max.Y, point.Y), Math.Max(Max.Z, point.Z)));
    }

    /// <summary>
    /// Tests for ray-AABB intersection using the fast slab method.
    /// </summary>
    public bool IntersectsRay(Vector3d origin, Vector3d invDir, double maxDistance, out double tMin)
    {
        if (IntersectsRay(origin, invDir, maxDistance, out double tNear, out double tFar))
        {
            tMin = tNear >= 0 ? tNear : tFar;
            return true;
        }

        tMin = double.PositiveInfinity;
        return false;
    }

    /// <summary>
    /// Like <see cref="IntersectsRay(Vector3d, Vector3d, double, out double)"/>, but <paramref name="tEntry"/> is the
    /// distance at which the ray enters the box: 0 when the origin is inside it, never the exit distance.
    /// </summary>
    internal bool IntersectsRayEntry(Vector3d origin, Vector3d invDir, double maxDistance, out double tEntry)
    {
        if (IntersectsRay(origin, invDir, maxDistance, out double tNear, out _))
        {
            tEntry = Math.Max(tNear, 0);
            return true;
        }

        tEntry = double.PositiveInfinity;
        return false;
    }

    private bool IntersectsRay(Vector3d origin, Vector3d invDir, double maxDistance, out double tNear, out double tFar)
    {
        double t1 = (Min.X - origin.X) * invDir.X;
        double t2 = (Max.X - origin.X) * invDir.X;
        tNear = Math.Min(t1, t2);
        tFar = Math.Max(t1, t2);

        t1 = (Min.Y - origin.Y) * invDir.Y;
        t2 = (Max.Y - origin.Y) * invDir.Y;
        tNear = Math.Max(tNear, Math.Min(t1, t2));
        tFar = Math.Min(tFar, Math.Max(t1, t2));

        t1 = (Min.Z - origin.Z) * invDir.Z;
        t2 = (Max.Z - origin.Z) * invDir.Z;
        tNear = Math.Max(tNear, Math.Min(t1, t2));
        tFar = Math.Min(tFar, Math.Max(t1, t2));

        return tNear <= tFar && tFar >= 0 && tNear <= maxDistance;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[Min: {Min}, Max: {Max}]");
}

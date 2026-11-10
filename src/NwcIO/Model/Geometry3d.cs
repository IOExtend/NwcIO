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
        double t1 = (Min.X - origin.X) * invDir.X;
        double t2 = (Max.X - origin.X) * invDir.X;
        double tNear = Math.Min(t1, t2);
        double tFar = Math.Max(t1, t2);

        t1 = (Min.Y - origin.Y) * invDir.Y;
        t2 = (Max.Y - origin.Y) * invDir.Y;
        tNear = Math.Max(tNear, Math.Min(t1, t2));
        tFar = Math.Min(tFar, Math.Max(t1, t2));

        t1 = (Min.Z - origin.Z) * invDir.Z;
        t2 = (Max.Z - origin.Z) * invDir.Z;
        tNear = Math.Max(tNear, Math.Min(t1, t2));
        tFar = Math.Min(tFar, Math.Max(t1, t2));

        if (tNear <= tFar && tFar >= 0 && tNear <= maxDistance)
        {
            tMin = tNear >= 0 ? tNear : tFar;
            return true;
        }

        tMin = double.PositiveInfinity;
        return false;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[Min: {Min}, Max: {Max}]");
}

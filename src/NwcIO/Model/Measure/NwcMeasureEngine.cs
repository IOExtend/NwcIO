using System.Globalization;
using NwcIO.Model;

namespace NwcIO.Model.Measure;

/// <summary>
/// Result of a 3D point-to-point distance measurement.
/// </summary>
public sealed record NwcDistanceMeasurement(
    Vector3d PointA,
    Vector3d PointB,
    double Distance,
    double DeltaX,
    double DeltaY,
    double DeltaZ,
    double HorizontalDistance,
    double VerticalDistance,
    double PitchDegrees,
    double AzimuthDegrees
)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Distance: {Distance:F3} m (dX: {DeltaX:F3}, dY: {DeltaY:F3}, dZ: {DeltaZ:F3}, Horiz: {HorizontalDistance:F3}, Pitch: {PitchDegrees:F1}°)");
}

/// <summary>
/// Result of a point-to-mesh shortest distance measurement.
/// </summary>
public sealed record NwcPointToMeshMeasurement(
    Vector3d QueryPoint,
    Vector3d ClosestPoint,
    double Distance,
    int ClosestTriangleIndex,
    Vector3d Normal
)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Point-to-Mesh Distance: {Distance:F3} m at ({ClosestPoint.X:F3}, {ClosestPoint.Y:F3}, {ClosestPoint.Z:F3})");
}

/// <summary>
/// Result of a mesh-to-mesh shortest distance measurement.
/// </summary>
public sealed record NwcMeshToMeshMeasurement(
    NwcMesh MeshA,
    NwcMesh MeshB,
    double Distance,
    Vector3d PointA,
    Vector3d PointB
)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Mesh-to-Mesh Clearance: {Distance:F3} m between ({PointA.X:F3},{PointA.Y:F3},{PointA.Z:F3}) and ({PointB.X:F3},{PointB.Y:F3},{PointB.Z:F3})");
}

/// <summary>
/// Result of a cumulative polyline path distance measurement.
/// </summary>
public sealed record NwcPolylineMeasurement(
    IReadOnlyList<Vector3d> Points,
    IReadOnlyList<double> SegmentLengths,
    double TotalLength
);

/// <summary>
/// High-precision 3D measurement engine providing point-to-point, point-to-mesh,
/// mesh-to-mesh clearance, triangle area, normal vector, and polyline distance calculations.
/// </summary>
public static class NwcMeasureEngine
{
    /// <summary>
    /// Measures the 3D Euclidean distance and directional projections between two points.
    /// </summary>
    public static NwcDistanceMeasurement MeasureDistance(Vector3d a, Vector3d b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double dz = b.Z - a.Z;

        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double horiz = Math.Sqrt(dx * dx + dy * dy);
        double vert = Math.Abs(dz);

        double pitch = dist > 1e-12 ? Math.Asin(Math.Clamp(dz / dist, -1.0, 1.0)) * (180.0 / Math.PI) : 0.0;
        double azimuth = horiz > 1e-12 ? Math.Atan2(dy, dx) * (180.0 / Math.PI) : 0.0;
        if (azimuth < 0.0) azimuth += 360.0;

        return new NwcDistanceMeasurement(a, b, dist, dx, dy, dz, horiz, vert, pitch, azimuth);
    }

    /// <summary>
    /// Computes the angle in degrees between two 3D direction vectors.
    /// </summary>
    public static double MeasureAngle(Vector3d dirA, Vector3d dirB)
    {
        double lenA = dirA.Length;
        double lenB = dirB.Length;
        if (lenA < 1e-12 || lenB < 1e-12) return 0.0;

        double cosTheta = Vector3d.Dot(dirA, dirB) / (lenA * lenB);
        cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
        return Math.Acos(cosTheta) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Computes the angle in degrees between two line segments meeting at a common vertex: angle(A - Vertex - B).
    /// </summary>
    public static double MeasureAngle(Vector3d a, Vector3d vertex, Vector3d b)
    {
        return MeasureAngle(a - vertex, b - vertex);
    }

    /// <summary>
    /// Measures cumulative polyline distance across a sequence of 3D points.
    /// </summary>
    public static NwcPolylineMeasurement MeasurePath(IEnumerable<Vector3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var ptList = points.ToList();
        var segments = new List<double>();
        double total = 0.0;

        for (int i = 0; i < ptList.Count - 1; i++)
        {
            double len = Vector3d.Distance(ptList[i], ptList[i + 1]);
            segments.Add(len);
            total += len;
        }

        return new NwcPolylineMeasurement(ptList, segments, total);
    }

    /// <summary>
    /// Computes the surface area of a 3D triangle defined by three vertices.
    /// </summary>
    public static double ComputeTriangleArea(Vector3d a, Vector3d b, Vector3d c)
    {
        var cross = Vector3d.Cross(b - a, c - a);
        return cross.Length * 0.5;
    }

    /// <summary>
    /// Computes the unit normal vector of a 3D triangle defined by three vertices in CCW order.
    /// </summary>
    public static Vector3d ComputeTriangleNormal(Vector3d a, Vector3d b, Vector3d c)
    {
        return Vector3d.Cross(b - a, c - a).Normalize();
    }

    /// <summary>
    /// Computes the point on a 3D triangle closest to the specified query point.
    /// Implements Christer Ericson's Voronoi region algorithm.
    /// </summary>
    public static Vector3d ClosestPointOnTriangle(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        double d1 = Vector3d.Dot(ab, ap);
        double d2 = Vector3d.Dot(ac, ap);
        if (d1 <= 0.0 && d2 <= 0.0) return a; // Vertex A

        var bp = p - b;
        double d3 = Vector3d.Dot(ab, bp);
        double d4 = Vector3d.Dot(ac, bp);
        if (d3 >= 0.0 && d4 <= d3) return b; // Vertex B

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
        {
            double v = d1 / (d1 - d3);
            return a + ab * v; // Edge AB
        }

        var cp = p - c;
        double d5 = Vector3d.Dot(ab, cp);
        double d6 = Vector3d.Dot(ac, cp);
        if (d6 >= 0.0 && d5 <= d6) return c; // Vertex C

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
        {
            double w = d2 / (d2 - d6);
            return a + ac * w; // Edge AC
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
        {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + (c - b) * w; // Edge BC
        }

        // Interior face region
        double denom = 1.0 / (va + vb + vc);
        double vCoord = vb * denom;
        double wCoord = vc * denom;
        return a + ab * vCoord + ac * wCoord;
    }

    /// <summary>
    /// Measures the shortest distance from a 3D point to a mesh surface, locating the closest point.
    /// </summary>
    public static NwcPointToMeshMeasurement MeasurePointToMesh(Vector3d point, NwcMesh mesh, bool useWorldCoordinates = true)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = useWorldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
        var indices = mesh.Indices;

        if (verts.Count == 0 || indices.Count < 3)
        {
            return new NwcPointToMeshMeasurement(point, point, 0.0, -1, Vector3d.Zero);
        }

        double minDistance = double.PositiveInfinity;
        var closestPt = Vector3d.Zero;
        int closestTri = -1;
        var closestNormal = Vector3d.Zero;

        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];

            if (idx0 >= verts.Count || idx1 >= verts.Count || idx2 >= verts.Count)
            {
                continue;
            }

            var v0 = verts[idx0];
            var v1 = verts[idx1];
            var v2 = verts[idx2];

            var ptOnTri = ClosestPointOnTriangle(point, v0, v1, v2);
            double dist = Vector3d.Distance(point, ptOnTri);

            if (dist < minDistance)
            {
                minDistance = dist;
                closestPt = ptOnTri;
                closestTri = i;
                closestNormal = ComputeTriangleNormal(v0, v1, v2);
            }
        }

        return new NwcPointToMeshMeasurement(point, closestPt, minDistance, closestTri, closestNormal);
    }

    /// <summary>
    /// Measures the clearance (minimum distance) between two 3D polygon meshes.
    /// </summary>
    public static NwcMeshToMeshMeasurement MeasureMeshToMesh(NwcMesh meshA, NwcMesh meshB, bool useWorldCoordinates = true)
    {
        ArgumentNullException.ThrowIfNull(meshA);
        ArgumentNullException.ThrowIfNull(meshB);

        var vertsA = useWorldCoordinates ? meshA.GetWorldVertices() : meshA.Vertices;
        var vertsB = useWorldCoordinates ? meshB.GetWorldVertices() : meshB.Vertices;

        var indicesA = meshA.Indices;
        var indicesB = meshB.Indices;

        if (vertsA.Count == 0 || vertsB.Count == 0 || indicesA.Count < 3 || indicesB.Count < 3)
        {
            return new NwcMeshToMeshMeasurement(meshA, meshB, 0.0, Vector3d.Zero, Vector3d.Zero);
        }

        var minDistance = double.PositiveInfinity;
        var bestPtA = Vector3d.Zero;
        var bestPtB = Vector3d.Zero;
        Span<Vector3d> a = stackalloc Vector3d[3];
        Span<Vector3d> b = stackalloc Vector3d[3];

        for (var i = 0; i + 2 < indicesA.Count; i += 3)
        {
            if (!ReadTriangle(vertsA, indicesA, i, a))
            {
                continue;
            }

            for (var j = 0; j + 2 < indicesB.Count; j += 3)
            {
                if (!ReadTriangle(vertsB, indicesB, j, b))
                {
                    continue;
                }

                if (TriangleBoundsDistance(a, b) >= minDistance)
                {
                    continue;
                }

                for (var edge = 0; edge < 3; edge++)
                {
                    if (SegmentIntersectsTriangle(a[edge], a[(edge + 1) % 3], b, out var intersection) ||
                        SegmentIntersectsTriangle(b[edge], b[(edge + 1) % 3], a, out intersection))
                    {
                        return new NwcMeshToMeshMeasurement(meshA, meshB, 0.0, intersection, intersection);
                    }

                    Consider(a[edge], ClosestPointOnTriangle(a[edge], b[0], b[1], b[2]));
                    Consider(ClosestPointOnTriangle(b[edge], a[0], a[1], a[2]), b[edge]);
                    for (var otherEdge = 0; otherEdge < 3; otherEdge++)
                    {
                        ClosestPointsOnSegments(a[edge], a[(edge + 1) % 3],
                            b[otherEdge], b[(otherEdge + 1) % 3], out var pointA, out var pointB);
                        Consider(pointA, pointB);
                    }
                }
            }
        }

        return new NwcMeshToMeshMeasurement(meshA, meshB, minDistance, bestPtA, bestPtB);

        void Consider(Vector3d pointA, Vector3d pointB)
        {
            var distance = Vector3d.Distance(pointA, pointB);
            if (distance < minDistance)
            {
                minDistance = distance;
                bestPtA = pointA;
                bestPtB = pointB;
            }
        }
    }

    private static double TriangleBoundsDistance(ReadOnlySpan<Vector3d> a, ReadOnlySpan<Vector3d> b)
    {
        var dx = Gap(a[0].X, a[1].X, a[2].X, b[0].X, b[1].X, b[2].X);
        var dy = Gap(a[0].Y, a[1].Y, a[2].Y, b[0].Y, b[1].Y, b[2].Y);
        var dz = Gap(a[0].Z, a[1].Z, a[2].Z, b[0].Z, b[1].Z, b[2].Z);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);

        static double Gap(double a0, double a1, double a2, double b0, double b1, double b2)
        {
            var minA = Math.Min(a0, Math.Min(a1, a2));
            var maxA = Math.Max(a0, Math.Max(a1, a2));
            var minB = Math.Min(b0, Math.Min(b1, b2));
            var maxB = Math.Max(b0, Math.Max(b1, b2));
            return Math.Max(0.0, Math.Max(minA - maxB, minB - maxA));
        }
    }

    private static bool ReadTriangle(IReadOnlyList<Vector3d> vertices, IReadOnlyList<int> indices,
        int offset, Span<Vector3d> triangle)
    {
        for (var i = 0; i < 3; i++)
        {
            var index = indices[offset + i];
            if (index < 0 || index >= vertices.Count)
            {
                return false;
            }
            triangle[i] = vertices[index];
        }
        return true;
    }

    private static bool SegmentIntersectsTriangle(Vector3d start, Vector3d end,
        ReadOnlySpan<Vector3d> triangle, out Vector3d point)
    {
        // Moller-Trumbore with a segment parameter in [0, 1]. Coplanar contact
        // is covered by the vertex-to-face and edge-to-edge distance checks.
        point = Vector3d.Zero;
        var direction = end - start;
        var ab = triangle[1] - triangle[0];
        var ac = triangle[2] - triangle[0];
        var cross = Vector3d.Cross(direction, ac);
        var determinant = Vector3d.Dot(ab, cross);
        if (determinant == 0.0)
        {
            return false;
        }

        var offset = start - triangle[0];
        var u = Vector3d.Dot(offset, cross) / determinant;
        var q = Vector3d.Cross(offset, ab);
        var v = Vector3d.Dot(direction, q) / determinant;
        var t = Vector3d.Dot(ac, q) / determinant;
        if (u < 0.0 || v < 0.0 || u + v > 1.0 || t < 0.0 || t > 1.0)
        {
            return false;
        }

        point = start + direction * t;
        return true;
    }

    private static void ClosestPointsOnSegments(Vector3d a0, Vector3d a1, Vector3d b0, Vector3d b1,
        out Vector3d pointA, out Vector3d pointB)
    {
        var a = a1 - a0;
        var b = b1 - b0;
        var offset = a0 - b0;
        var aa = Vector3d.Dot(a, a);
        var bb = Vector3d.Dot(b, b);
        var ab = Vector3d.Dot(a, b);
        var ar = Vector3d.Dot(a, offset);
        var br = Vector3d.Dot(b, offset);
        var denominator = aa * bb - ab * ab;
        var s = aa == 0.0 || denominator <= 0.0 ? 0.0 : Math.Clamp((ab * br - ar * bb) / denominator, 0.0, 1.0);
        var t = bb == 0.0 ? 0.0 : Math.Clamp((ab * s + br) / bb, 0.0, 1.0);
        s = aa == 0.0 ? 0.0 : Math.Clamp((ab * t - ar) / aa, 0.0, 1.0);
        pointA = a0 + a * s;
        pointB = b0 + b * t;
    }

    /// <summary>
    /// Computes the total surface area of all triangles in a mesh.
    /// </summary>
    public static double ComputeMeshSurfaceArea(NwcMesh mesh, bool useWorldCoordinates = true)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = useWorldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
        var indices = mesh.Indices;
        double totalArea = 0.0;

        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];

            if (idx0 < verts.Count && idx1 < verts.Count && idx2 < verts.Count)
            {
                totalArea += ComputeTriangleArea(verts[idx0], verts[idx1], verts[idx2]);
            }
        }

        return totalArea;
    }

    /// <summary>
    /// Computes the signed volume of a closed 3D manifold solid mesh.
    /// </summary>
    public static double ComputeMeshVolume(NwcMesh mesh, bool useWorldCoordinates = true)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = useWorldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
        var indices = mesh.Indices;
        double volume = 0.0;

        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];

            if (idx0 < verts.Count && idx1 < verts.Count && idx2 < verts.Count)
            {
                var v0 = verts[idx0];
                var v1 = verts[idx1];
                var v2 = verts[idx2];

                // Signed tetrahedron volume: (v0 . (v1 x v2)) / 6
                volume += Vector3d.Dot(v0, Vector3d.Cross(v1, v2)) / 6.0;
            }
        }

        return Math.Abs(volume);
    }

    /// <summary>
    /// Computes the average vertex centroid of a mesh.
    /// </summary>
    public static Vector3d ComputeMeshCentroid(NwcMesh mesh, bool useWorldCoordinates = true)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = useWorldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
        if (verts.Count == 0) return Vector3d.Zero;

        double sumX = 0, sumY = 0, sumZ = 0;
        for (int i = 0; i < verts.Count; i++)
        {
            sumX += verts[i].X;
            sumY += verts[i].Y;
            sumZ += verts[i].Z;
        }

        return new Vector3d(sumX / verts.Count, sumY / verts.Count, sumZ / verts.Count);
    }
}

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

        // Bounding box pre-check
        var boxA = meshA.GetWorldBoundingBox();
        var boxB = meshB.GetWorldBoundingBox();
        if (boxA.Intersects(boxB))
        {
            // Boxes intersect - check if vertices of A are close to B
        }

        double minDistance = double.PositiveInfinity;
        var bestPtA = Vector3d.Zero;
        var bestPtB = Vector3d.Zero;

        int triCountA = indicesA.Count / 3;
        int triCountB = indicesB.Count / 3;

        // Sample vertices of A against mesh B
        for (int i = 0; i < vertsA.Count; i++)
        {
            var pA = vertsA[i];
            var m = MeasurePointToMesh(pA, meshB, useWorldCoordinates);
            if (m.Distance < minDistance)
            {
                minDistance = m.Distance;
                bestPtA = pA;
                bestPtB = m.ClosestPoint;
                if (minDistance < 1e-6) break;
            }
        }

        // Sample vertices of B against mesh A
        if (minDistance > 1e-6)
        {
            for (int i = 0; i < vertsB.Count; i++)
            {
                var pB = vertsB[i];
                var m = MeasurePointToMesh(pB, meshA, useWorldCoordinates);
                if (m.Distance < minDistance)
                {
                    minDistance = m.Distance;
                    bestPtA = m.ClosestPoint;
                    bestPtB = pB;
                    if (minDistance < 1e-6) break;
                }
            }
        }

        return new NwcMeshToMeshMeasurement(meshA, meshB, minDistance, bestPtA, bestPtB);
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

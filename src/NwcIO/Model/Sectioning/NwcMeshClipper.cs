namespace NwcIO.Model.Sectioning;

/// <summary>
/// High-performance Sutherland-Hodgman 3D polygon mesh clipping engine
/// for Navisworks dynamic section planes and section boxes.
/// </summary>
public static class NwcMeshClipper
{
    /// <summary>
    /// Clips a 3D polygon mesh against a collection of active section planes.
    /// Returns a new <see cref="NwcMesh"/> containing the clipped geometry inside the retained volume.
    /// </summary>
    public static NwcMesh Clip(NwcMesh mesh, IReadOnlyList<NwcSectionPlane> planes)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(planes);

        var activePlanes = planes.Where(p => p.IsEnabled).ToList();
        if (activePlanes.Count == 0 || mesh.Vertices.Count < 3 || mesh.Indices.Count < 3)
        {
            return mesh;
        }

        var currentVerts = new List<Vector3d>(mesh.Vertices);
        var currentIndices = new List<int>(mesh.Indices);

        foreach (var plane in activePlanes)
        {
            if (currentIndices.Count == 0) break;

            ClipMeshByPlane(currentVerts, currentIndices, plane, out var outVerts, out var outIndices);
            currentVerts = outVerts;
            currentIndices = outIndices;
        }

        return new NwcMesh
        {
            Id = mesh.Id,
            Name = $"{mesh.Name}_Clipped",
            Vertices = currentVerts,
            Indices = currentIndices,
            Material = mesh.Material,
            Transform = mesh.Transform,
            Node = mesh.Node,
        };
    }

    /// <summary>
    /// Clips a 3D polygon mesh against an active section box.
    /// </summary>
    public static NwcMesh Clip(NwcMesh mesh, NwcSectionBox sectionBox)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(sectionBox);

        if (!sectionBox.IsEnabled) return mesh;
        return Clip(mesh, sectionBox.ToSectionPlanes());
    }

    private static void ClipMeshByPlane(
        List<Vector3d> inVerts,
        List<int> inIndices,
        NwcSectionPlane plane,
        out List<Vector3d> outVerts,
        out List<int> outIndices)
    {
        outVerts = new List<Vector3d>();
        outIndices = new List<int>();

        var normal = plane.Normal.Normalize();
        double planeDist = plane.Distance;

        for (int i = 0; i + 2 < inIndices.Count; i += 3)
        {
            int idx0 = inIndices[i];
            int idx1 = inIndices[i + 1];
            int idx2 = inIndices[i + 2];
            if (idx0 >= inVerts.Count || idx1 >= inVerts.Count || idx2 >= inVerts.Count) continue;

            var p0 = inVerts[idx0];
            var p1 = inVerts[idx1];
            var p2 = inVerts[idx2];

            double d0 = Vector3d.Dot(normal, p0) - planeDist;
            double d1 = Vector3d.Dot(normal, p1) - planeDist;
            double d2 = Vector3d.Dot(normal, p2) - planeDist;

            bool in0 = d0 >= -1e-7;
            bool in1 = d1 >= -1e-7;
            bool in2 = d2 >= -1e-7;

            int insideCount = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0);

            if (insideCount == 3)
            {
                AddTriangle(outVerts, outIndices, p0, p1, p2);
            }
            else if (insideCount == 0)
            {
                continue;
            }
            else if (insideCount == 1)
            {
                Vector3d vIn, vOut1, vOut2;
                double dIn, dOut1, dOut2;

                if (in0)
                {
                    vIn = p0; dIn = d0;
                    vOut1 = p1; dOut1 = d1;
                    vOut2 = p2; dOut2 = d2;
                }
                else if (in1)
                {
                    vIn = p1; dIn = d1;
                    vOut1 = p2; dOut1 = d2;
                    vOut2 = p0; dOut2 = d0;
                }
                else
                {
                    vIn = p2; dIn = d2;
                    vOut1 = p0; dOut1 = d0;
                    vOut2 = p1; dOut2 = d1;
                }

                var i1 = IntersectSegment(vIn, vOut1, dIn, dOut1);
                var i2 = IntersectSegment(vIn, vOut2, dIn, dOut2);

                AddTriangle(outVerts, outIndices, vIn, i1, i2);
            }
            else // insideCount == 2
            {
                Vector3d vOut, vIn1, vIn2;
                double dOut, dIn1, dIn2;

                if (!in0)
                {
                    vOut = p0; dOut = d0;
                    vIn1 = p1; dIn1 = d1;
                    vIn2 = p2; dIn2 = d2;
                }
                else if (!in1)
                {
                    vOut = p1; dOut = d1;
                    vIn1 = p2; dIn1 = d2;
                    vIn2 = p0; dIn2 = d0;
                }
                else
                {
                    vOut = p2; dOut = d2;
                    vIn1 = p0; dIn1 = d0;
                    vIn2 = p1; dIn2 = d1;
                }

                var i1 = IntersectSegment(vIn1, vOut, dIn1, dOut);
                var i2 = IntersectSegment(vIn2, vOut, dIn2, dOut);

                AddTriangle(outVerts, outIndices, vIn1, i1, i2);
                AddTriangle(outVerts, outIndices, vIn1, i2, vIn2);
            }
        }
    }

    private static Vector3d IntersectSegment(in Vector3d a, in Vector3d b, double da, double db)
    {
        double diff = da - db;
        if (Math.Abs(diff) < 1e-12) return (a + b) * 0.5;
        double t = da / diff;
        return a + (b - a) * t;
    }

    private static void AddTriangle(
        List<Vector3d> vertices,
        List<int> indices,
        in Vector3d p0, in Vector3d p1, in Vector3d p2)
    {
        int baseIdx = vertices.Count;
        vertices.Add(p0);
        vertices.Add(p1);
        vertices.Add(p2);
        indices.Add(baseIdx);
        indices.Add(baseIdx + 1);
        indices.Add(baseIdx + 2);
    }
}

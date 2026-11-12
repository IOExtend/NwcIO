namespace NwcIO.Model.Spatial;

/// <summary>
/// A fast 3D Bounding Volume Hierarchy (BVH) spatial index over polygon meshes,
/// enabling sub-millisecond bounding box queries, raycasting, and element selection.
/// </summary>
public sealed class NwcSpatialIndex
{
    private const int MaxLeafItems = 4;

    private readonly MeshEntry[] _entries;
    private readonly BvhNode? _root;

    public int MeshCount => _entries.Length;
    public int NodeCount { get; private set; }
    public int LeafCount { get; private set; }
    public int MaxDepth { get; private set; }
    public BoundingBox3d Bounds => _root?.Bounds ?? BoundingBox3d.Empty;

    public NwcSpatialIndex(IReadOnlyList<NwcMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);

        _entries = new MeshEntry[meshes.Count];
        for (int i = 0; i < meshes.Count; i++)
        {
            var m = meshes[i];
            var b = m.GetWorldBoundingBox();
            _entries[i] = new MeshEntry(m, b, b.Center);
        }

        if (_entries.Length > 0)
        {
            int nodeCount = 0;
            int leafCount = 0;
            int maxDepth = 0;

            _root = BuildNode(0, _entries.Length, 1, ref nodeCount, ref leafCount, ref maxDepth);

            NodeCount = nodeCount;
            LeafCount = leafCount;
            MaxDepth = maxDepth;
        }
    }

    /// <summary>
    /// Finds all meshes whose world bounding box intersects <paramref name="box"/>.
    /// </summary>
    public IReadOnlyList<NwcMesh> QueryBox(BoundingBox3d box)
    {
        if (_root is null || box.IsEmpty || !_root.Bounds.Intersects(box))
        {
            return Array.Empty<NwcMesh>();
        }

        var results = new List<NwcMesh>();
        QueryBoxRecursive(_root, box, results);
        return results;
    }

    private void QueryBoxRecursive(BvhNode node, BoundingBox3d box, List<NwcMesh> results)
    {
        if (!node.Bounds.Intersects(box))
        {
            return;
        }

        if (node.IsLeaf)
        {
            for (int i = node.StartIndex; i < node.StartIndex + node.Count; i++)
            {
                if (_entries[i].Bounds.Intersects(box))
                {
                    results.Add(_entries[i].Mesh);
                }
            }
            return;
        }

        if (node.Left is not null) QueryBoxRecursive(node.Left, box, results);
        if (node.Right is not null) QueryBoxRecursive(node.Right, box, results);
    }

    /// <summary>
    /// Performs a 3D raycast and returns the closest geometric intersection hit, if any.
    /// </summary>
    public bool Raycast(Vector3d origin, Vector3d direction, out RaycastHit hit, double maxDistance = double.PositiveInfinity)
    {
        hit = default;
        if (_root is null || direction.LengthSquared < 1e-12)
        {
            return false;
        }

        var dir = direction.Normalize();
        var invDir = new Vector3d(
            Math.Abs(dir.X) > 1e-12 ? 1.0 / dir.X : (dir.X >= 0 ? double.PositiveInfinity : double.NegativeInfinity),
            Math.Abs(dir.Y) > 1e-12 ? 1.0 / dir.Y : (dir.Y >= 0 ? double.PositiveInfinity : double.NegativeInfinity),
            Math.Abs(dir.Z) > 1e-12 ? 1.0 / dir.Z : (dir.Z >= 0 ? double.PositiveInfinity : double.NegativeInfinity));

        double closestDist = maxDistance;
        RaycastHit? bestHit = null;

        RaycastClosestRecursive(_root, origin, dir, invDir, ref closestDist, ref bestHit);

        if (bestHit.HasValue)
        {
            hit = bestHit.Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Performs a 3D raycast and returns all geometric intersections sorted by distance.
    /// </summary>
    public IReadOnlyList<RaycastHit> RaycastAll(Vector3d origin, Vector3d direction, double maxDistance = double.PositiveInfinity)
    {
        if (_root is null || direction.LengthSquared < 1e-12)
        {
            return Array.Empty<RaycastHit>();
        }

        var dir = direction.Normalize();
        var invDir = new Vector3d(
            Math.Abs(dir.X) > 1e-12 ? 1.0 / dir.X : (dir.X >= 0 ? double.PositiveInfinity : double.NegativeInfinity),
            Math.Abs(dir.Y) > 1e-12 ? 1.0 / dir.Y : (dir.Y >= 0 ? double.PositiveInfinity : double.NegativeInfinity),
            Math.Abs(dir.Z) > 1e-12 ? 1.0 / dir.Z : (dir.Z >= 0 ? double.PositiveInfinity : double.NegativeInfinity));

        var hits = new List<RaycastHit>();
        RaycastAllRecursive(_root, origin, dir, invDir, maxDistance, hits);
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    private void RaycastClosestRecursive(
        BvhNode node,
        Vector3d origin,
        Vector3d dir,
        Vector3d invDir,
        ref double closestDist,
        ref RaycastHit? bestHit)
    {
        if (!node.Bounds.IntersectsRayEntry(origin, invDir, closestDist, out double tNode) || tNode >= closestDist)
        {
            return;
        }

        if (node.IsLeaf)
        {
            for (int i = node.StartIndex; i < node.StartIndex + node.Count; i++)
            {
                var entry = _entries[i];
                if (!entry.Bounds.IntersectsRay(origin, invDir, closestDist, out _))
                {
                    continue;
                }

                TestMeshTriangles(entry.Mesh, origin, dir, ref closestDist, ref bestHit);
            }
            return;
        }

        // Branch node: test both children and visit the closer one first
        double distL = 0;
        double distR = 0;
        bool hitL = node.Left is not null && node.Left.Bounds.IntersectsRayEntry(origin, invDir, closestDist, out distL);
        bool hitR = node.Right is not null && node.Right.Bounds.IntersectsRayEntry(origin, invDir, closestDist, out distR);

        if (hitL && hitR)
        {
            if (distL <= distR)
            {
                RaycastClosestRecursive(node.Left!, origin, dir, invDir, ref closestDist, ref bestHit);
                if (distR < closestDist)
                {
                    RaycastClosestRecursive(node.Right!, origin, dir, invDir, ref closestDist, ref bestHit);
                }
            }
            else
            {
                RaycastClosestRecursive(node.Right!, origin, dir, invDir, ref closestDist, ref bestHit);
                if (distL < closestDist)
                {
                    RaycastClosestRecursive(node.Left!, origin, dir, invDir, ref closestDist, ref bestHit);
                }
            }
        }
        else if (hitL)
        {
            RaycastClosestRecursive(node.Left!, origin, dir, invDir, ref closestDist, ref bestHit);
        }
        else if (hitR)
        {
            RaycastClosestRecursive(node.Right!, origin, dir, invDir, ref closestDist, ref bestHit);
        }
    }

    private void RaycastAllRecursive(
        BvhNode node,
        Vector3d origin,
        Vector3d dir,
        Vector3d invDir,
        double maxDistance,
        List<RaycastHit> hits)
    {
        if (!node.Bounds.IntersectsRay(origin, invDir, maxDistance, out _))
        {
            return;
        }

        if (node.IsLeaf)
        {
            for (int i = node.StartIndex; i < node.StartIndex + node.Count; i++)
            {
                var entry = _entries[i];
                if (!entry.Bounds.IntersectsRay(origin, invDir, maxDistance, out _))
                {
                    continue;
                }

                CollectMeshTriangles(entry.Mesh, origin, dir, maxDistance, hits);
            }
            return;
        }

        if (node.Left is not null) RaycastAllRecursive(node.Left, origin, dir, invDir, maxDistance, hits);
        if (node.Right is not null) RaycastAllRecursive(node.Right, origin, dir, invDir, maxDistance, hits);
    }

    private static void TestMeshTriangles(
        NwcMesh mesh,
        Vector3d origin,
        Vector3d dir,
        ref double closestDist,
        ref RaycastHit? bestHit)
    {
        var worldVerts = mesh.GetWorldVertices();
        var indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if (i0 < 0 || i0 >= worldVerts.Count ||
                i1 < 0 || i1 >= worldVerts.Count ||
                i2 < 0 || i2 >= worldVerts.Count)
            {
                continue;
            }

            var v0 = worldVerts[i0];
            var v1 = worldVerts[i1];
            var v2 = worldVerts[i2];

            if (IntersectTriangle(origin, dir, v0, v1, v2, out double t, out Vector3d normal) && t > 1e-6 && t < closestDist)
            {
                closestDist = t;
                bestHit = new RaycastHit(t, origin + dir * t, normal, mesh, i / 3);
            }
        }
    }

    private static void CollectMeshTriangles(
        NwcMesh mesh,
        Vector3d origin,
        Vector3d dir,
        double maxDistance,
        List<RaycastHit> hits)
    {
        var worldVerts = mesh.GetWorldVertices();
        var indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if (i0 < 0 || i0 >= worldVerts.Count ||
                i1 < 0 || i1 >= worldVerts.Count ||
                i2 < 0 || i2 >= worldVerts.Count)
            {
                continue;
            }

            var v0 = worldVerts[i0];
            var v1 = worldVerts[i1];
            var v2 = worldVerts[i2];

            if (IntersectTriangle(origin, dir, v0, v1, v2, out double t, out Vector3d normal) && t > 1e-6 && t <= maxDistance)
            {
                hits.Add(new RaycastHit(t, origin + dir * t, normal, mesh, i / 3));
            }
        }
    }

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection algorithm.
    /// </summary>
    public static bool IntersectTriangle(
        Vector3d origin,
        Vector3d dir,
        Vector3d v0,
        Vector3d v1,
        Vector3d v2,
        out double t,
        out Vector3d normal)
    {
        t = 0;
        normal = Vector3d.Zero;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var h = Vector3d.Cross(dir, edge2);
        double a = Vector3d.Dot(edge1, h);

        if (Math.Abs(a) < 1e-10)
        {
            return false; // Ray is parallel to triangle
        }

        double f = 1.0 / a;
        var s = origin - v0;
        double u = f * Vector3d.Dot(s, h);
        if (u < 0.0 || u > 1.0)
        {
            return false;
        }

        var q = Vector3d.Cross(s, edge1);
        double v = f * Vector3d.Dot(dir, q);
        if (v < 0.0 || u + v > 1.0)
        {
            return false;
        }

        t = f * Vector3d.Dot(edge2, q);
        if (t > 1e-9)
        {
            var rawNormal = Vector3d.Cross(edge1, edge2).Normalize();
            normal = Vector3d.Dot(dir, rawNormal) <= 0 ? rawNormal : -rawNormal;
            return true;
        }

        return false;
    }

    private BvhNode BuildNode(
        int start,
        int count,
        int depth,
        ref int totalNodes,
        ref int totalLeaves,
        ref int maxDepth)
    {
        totalNodes++;
        if (depth > maxDepth) maxDepth = depth;

        var nodeBounds = BoundingBox3d.Empty;
        var centroidBounds = BoundingBox3d.Empty;

        for (int i = start; i < start + count; i++)
        {
            nodeBounds = nodeBounds.Union(_entries[i].Bounds);
            centroidBounds = centroidBounds.Union(_entries[i].Centroid);
        }

        if (count <= MaxLeafItems || centroidBounds.IsEmpty)
        {
            totalLeaves++;
            return new BvhNode
            {
                Bounds = nodeBounds,
                StartIndex = start,
                Count = count,
            };
        }

        // Split along the axis with largest centroid extent
        var cSize = centroidBounds.Size;
        int axis = 0; // 0=X, 1=Y, 2=Z
        if (cSize.Y > cSize.X && cSize.Y > cSize.Z) axis = 1;
        else if (cSize.Z > cSize.X && cSize.Z > cSize.Y) axis = 2;

        int mid = start + count / 2;
        Array.Sort(_entries, start, count, new CentroidComparer(axis));

        var left = BuildNode(start, mid - start, depth + 1, ref totalNodes, ref totalLeaves, ref maxDepth);
        var right = BuildNode(mid, start + count - mid, depth + 1, ref totalNodes, ref totalLeaves, ref maxDepth);

        return new BvhNode
        {
            Bounds = nodeBounds,
            Left = left,
            Right = right,
            StartIndex = start,
            Count = count,
        };
    }

    private readonly record struct MeshEntry(NwcMesh Mesh, BoundingBox3d Bounds, Vector3d Centroid);

    private sealed class CentroidComparer : IComparer<MeshEntry>
    {
        private readonly int _axis;
        public CentroidComparer(int axis) => _axis = axis;

        public int Compare(MeshEntry a, MeshEntry b)
        {
            double va = _axis switch { 0 => a.Centroid.X, 1 => a.Centroid.Y, _ => a.Centroid.Z };
            double vb = _axis switch { 0 => b.Centroid.X, 1 => b.Centroid.Y, _ => b.Centroid.Z };
            return va.CompareTo(vb);
        }
    }

    private sealed class BvhNode
    {
        public required BoundingBox3d Bounds { get; init; }
        public BvhNode? Left { get; init; }
        public BvhNode? Right { get; init; }
        public int StartIndex { get; init; }
        public int Count { get; init; }
        public bool IsLeaf => Left is null;
    }
}

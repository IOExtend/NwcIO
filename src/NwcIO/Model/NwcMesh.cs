namespace NwcIO.Model;

/// <summary>
/// A 3D polygon mesh extracted from Navisworks geometry streams,
/// composed of 3D vertices, triangle indices, and local/world bounding boxes.
/// </summary>
public sealed class NwcMesh
{
    public required int Id { get; init; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Vertex positions in local mesh space.</summary>
    public required IReadOnlyList<Vector3d> Vertices { get; init; }

    /// <summary>
    /// Triangle vertex index triplets (0-based indexing into <see cref="Vertices"/>).
    /// Every 3 consecutive integers define one triangle face: (i0, i1, i2).
    /// </summary>
    public required IReadOnlyList<int> Indices { get; init; }

    /// <summary>Axis-aligned bounding box of the mesh in local coordinates.</summary>
    public BoundingBox3d? BoundingBox { get; init; }

    /// <summary>Optional 3D world transformation associated with this mesh instance.</summary>
    public NwcTransform? Transform { get; set; }

    /// <summary>Surface material and color assigned to this mesh.</summary>
    public NwcMaterial? Material { get; set; }

    /// <summary>Owning scene hierarchy node associated with this mesh.</summary>
    public NwcNode? Node { get; set; }

    /// <summary>Number of triangle faces in this mesh.</summary>
    public int TriangleCount => Indices.Count / 3;

    private BoundingBox3d? _cachedWorldBounds;

    /// <summary>
    /// Gets the axis-aligned bounding box of the mesh in world space coordinates.
    /// </summary>
    public BoundingBox3d GetWorldBoundingBox()
    {
        if (_cachedWorldBounds.HasValue)
        {
            return _cachedWorldBounds.Value;
        }

        if (Transform is null)
        {
            if (BoundingBox.HasValue && !BoundingBox.Value.IsEmpty)
            {
                _cachedWorldBounds = BoundingBox.Value;
                return BoundingBox.Value;
            }
        }

        var worldVerts = GetWorldVertices();
        if (worldVerts.Count == 0)
        {
            _cachedWorldBounds = BoundingBox3d.Empty;
            return BoundingBox3d.Empty;
        }

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (int i = 0; i < worldVerts.Count; i++)
        {
            var v = worldVerts[i];
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.Z < minZ) minZ = v.Z;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
            if (v.Z > maxZ) maxZ = v.Z;
        }

        var bounds = new BoundingBox3d(new(minX, minY, minZ), new(maxX, maxY, maxZ));
        _cachedWorldBounds = bounds;
        return bounds;
    }

    /// <summary>
    /// Returns vertices transformed into world space coordinates by <see cref="Transform"/>.
    /// If no transform is assigned, returns <see cref="Vertices"/>.
    /// </summary>
    public IReadOnlyList<Vector3d> GetWorldVertices()
    {
        if (Transform is null)
        {
            return Vertices;
        }

        double[] m = Transform.ToMatrix4x4();
        var world = new Vector3d[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            var v = Vertices[i];
            double wx = v.X * m[0] + v.Y * m[4] + v.Z * m[8] + m[12];
            double wy = v.X * m[1] + v.Y * m[5] + v.Z * m[9] + m[13];
            double wz = v.X * m[2] + v.Y * m[6] + v.Z * m[10] + m[14];

            if (!double.IsFinite(wx) || !double.IsFinite(wy) || !double.IsFinite(wz))
            {
                return Vertices;
            }

            world[i] = new Vector3d(wx, wy, wz);
        }

        return world;
    }
}

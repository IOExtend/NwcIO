namespace NwcIO.Model.Clash;

internal sealed class ClashGeometry(CancellationToken cancellationToken = default)
{
    private readonly Dictionary<NwcMesh, IReadOnlyList<Vector3d>> _worldVertices = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NwcMesh, BoundingBox3d> _vertexBounds = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<Vector3d> GetVertices(NwcMesh mesh)
    {
        if (mesh.Transform is null) return mesh.Vertices;
        if (!_worldVertices.TryGetValue(mesh, out var vertices))
        {
            vertices = mesh.TransformVertices(cancellationToken);
            _worldVertices.Add(mesh, vertices);
        }
        return vertices;
    }

    public BoundingBox3d GetBounds(NwcMesh mesh) => mesh.GetWorldBoundingBox(GetVertices, cancellationToken);

    public BoundingBox3d GetVertexBounds(NwcMesh mesh, IReadOnlyList<Vector3d> vertices)
    {
        if (!_vertexBounds.TryGetValue(mesh, out var bounds))
        {
            bounds = BoundingBox3d.Empty;
            for (int i = 0; i < vertices.Count; i++)
            {
                if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                bounds = bounds.Union(vertices[i]);
            }
            _vertexBounds.Add(mesh, bounds);
        }
        return bounds;
    }

    public static BoundingBox3d Expand(BoundingBox3d bounds, double tolerance)
    {
        var offset = new Vector3d(tolerance, tolerance, tolerance);
        return tolerance > 0 ? new BoundingBox3d(bounds.Min - offset, bounds.Max + offset) : bounds;
    }
}

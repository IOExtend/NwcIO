using NwcIO.Model;
using NwcIO.Model.Spatial;
using Xunit;

namespace NwcIO.Tests;

public class SpatialIndexTests
{
    [Fact]
    public void BuildIndex_SyntheticMeshes_ConstructsValidBvh()
    {
        var meshes = CreateTestMeshes(10);
        var index = new NwcSpatialIndex(meshes);

        Assert.Equal(10, index.MeshCount);
        Assert.True(index.NodeCount > 0);
        Assert.True(index.LeafCount > 0);
        Assert.True(index.MaxDepth >= 1);
        Assert.False(index.Bounds.IsEmpty);
    }

    [Fact]
    public void QueryBox_FindsIntersectingMeshes()
    {
        var meshes = CreateTestMeshes(20);
        var index = new NwcSpatialIndex(meshes);

        // Query a box around the first mesh (placed near (0, 0, 0))
        var searchBox = new BoundingBox3d(new Vector3d(-1, -1, -1), new Vector3d(2, 2, 2));
        var hits = index.QueryBox(searchBox);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, m => m.Id == 0);

        // Query a distant box: should return empty
        var emptyBox = new BoundingBox3d(new Vector3d(1000, 1000, 1000), new Vector3d(1010, 1010, 1010));
        var noHits = index.QueryBox(emptyBox);
        Assert.Empty(noHits);
    }

    [Fact]
    public void Raycast_DirectHit_ReturnsClosestHitAndNormal()
    {
        // Single quad at Z = 5 spanning X [-1, 1], Y [-1, 1]
        var vertices = new Vector3d[]
        {
            new(-1, -1, 5),
            new( 1, -1, 5),
            new( 1,  1, 5),
            new(-1,  1, 5),
        };
        var indices = new int[] { 0, 1, 2, 0, 2, 3 };

        var mesh = new NwcMesh
        {
            Id = 42,
            Vertices = vertices,
            Indices = indices,
            BoundingBox = new BoundingBox3d(new(-1, -1, 5), new(1, 1, 5)),
        };

        var index = new NwcSpatialIndex([mesh]);

        // Cast ray from origin along +Z
        var origin = new Vector3d(0, 0, 0);
        var direction = new Vector3d(0, 0, 1);

        bool hasHit = index.Raycast(origin, direction, out var hit);

        Assert.True(hasHit);
        Assert.Equal(5.0, hit.Distance, precision: 4);
        Assert.Equal(42, hit.Mesh.Id);
        Assert.Equal(new Vector3d(0, 0, 5), hit.Point);
        // Outward normal opposing incoming ray (so pointing towards -Z)
        Assert.True(hit.Normal.Z < 0);
    }

    [Fact]
    public void Raycast_Miss_ReturnsFalse()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(10, 10, 10), new(12, 10, 10), new(11, 12, 10)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(10, 10, 10), new(12, 12, 10)),
        };

        var index = new NwcSpatialIndex([mesh]);

        // Shoot away from the triangle
        bool hasHit = index.Raycast(new Vector3d(0, 0, 0), new Vector3d(0, -1, 0), out _);
        Assert.False(hasHit);
    }

    [Fact]
    public void RaycastAll_ReturnsMultipleSortedHits()
    {
        // Two parallel quads at Z = 5 and Z = 10
        var mesh1 = new NwcMesh
        {
            Id = 1,
            Vertices = [new(-1, -1, 5), new(1, -1, 5), new(1, 1, 5)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(-1, -1, 5), new(1, 1, 5)),
        };
        var mesh2 = new NwcMesh
        {
            Id = 2,
            Vertices = [new(-1, -1, 10), new(1, -1, 10), new(1, 1, 10)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(-1, -1, 10), new(1, 1, 10)),
        };

        var index = new NwcSpatialIndex([mesh1, mesh2]);

        var hits = index.RaycastAll(new Vector3d(0, 0, 0), new Vector3d(0, 0, 1));

        Assert.Equal(2, hits.Count);
        Assert.Equal(1, hits[0].Mesh.Id);
        Assert.Equal(5.0, hits[0].Distance, precision: 4);
        Assert.Equal(2, hits[1].Mesh.Id);
        Assert.Equal(10.0, hits[1].Distance, precision: 4);
    }

    [Fact]
    public void Raycast_OriginInsideRoot_FindsHitWithinMaxDistance()
    {
        var index = new NwcSpatialIndex(
        [
            Tri(1, new(-10, -10, -10), new(10, 10, 10), new(10, -10, 10)),
            Tri(2, new(0.5, -1, -1), new(0.5, -1, 1), new(0.5, 2, 0)),
        ]);

        bool hasHit = index.Raycast(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), out var hit, maxDistance: 5);

        Assert.True(hasHit);
        Assert.Equal(2, hit.Mesh.Id);
        Assert.Equal(0.5, hit.Distance, precision: 6);
    }

    [Fact]
    public void Raycast_OriginInsideFartherExitingChild_ReturnsClosestHit()
    {
        // Split on Y into two leaves of four. The upper leaf holds the hit at x = 1 and a wide triangle off the ray,
        // so the origin lies inside it and it exits at x = 40; the lower leaf holds the hit at x = 5.
        var meshes = new List<NwcMesh>
        {
            Tri(1, new(1, -1, -1), new(1, -1, 1), new(1, 3, 0)),
            Tri(2, new(-5, 2, 0), new(40, 2, 0), new(10, 20, 0)),
            Tri(3, new(0, 40, 0), new(1, 40, 0), new(0, 41, 0)),
            Tri(4, new(0, 50, 0), new(1, 50, 0), new(0, 51, 0)),
            Tri(5, new(5, -3, -1), new(5, -3, 1), new(5, 1, 0)),
            Tri(6, new(10, -20, 0), new(11, -20, 0), new(10, -19, 0)),
            Tri(7, new(10, -40, 0), new(11, -40, 0), new(10, -39, 0)),
            Tri(8, new(10, -50, 0), new(11, -50, 0), new(10, -49, 0)),
        };
        var index = new NwcSpatialIndex(meshes);
        Assert.Equal(2, index.LeafCount);

        bool hasHit = index.Raycast(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), out var hit);

        Assert.True(hasHit);
        Assert.Equal(1, hit.Mesh.Id);
        Assert.Equal(1.0, hit.Distance, precision: 6);
    }

    private static NwcMesh Tri(int id, Vector3d a, Vector3d b, Vector3d c) => new()
    {
        Id = id,
        Vertices = [a, b, c],
        Indices = [0, 1, 2],
        BoundingBox = new BoundingBox3d(a, a).Union(b).Union(c),
    };

    private static List<NwcMesh> CreateTestMeshes(int count)
    {
        var list = new List<NwcMesh>(count);
        for (int i = 0; i < count; i++)
        {
            double offset = i * 2.0;
            var verts = new Vector3d[]
            {
                new(offset, 0, 0),
                new(offset + 1, 0, 0),
                new(offset + 0.5, 1, 0),
            };
            list.Add(new NwcMesh
            {
                Id = i,
                Vertices = verts,
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(offset, 0, 0), new(offset + 1, 1, 0)),
            });
        }
        return list;
    }
}

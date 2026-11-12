using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class MeshParserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mesh_GetWorldBoundingBox_AfterTransformAssignment_UsesCurrentTransform(bool initiallyTransformed)
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 2, 3)],
            Indices = [],
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 2, 3)),
            Transform = initiallyTransformed
                ? new NwcTransform { Id = 1, Translation = new Vector3d(10, 20, 30) }
                : null,
        };

        Assert.Equal(initiallyTransformed ? 10.0 : 0.0, mesh.GetWorldBoundingBox().Min.X);

        mesh.Transform = new NwcTransform { Id = 2, Translation = new Vector3d(100, 200, 300) };

        var bounds = mesh.GetWorldBoundingBox();
        var vertices = mesh.GetWorldVertices();
        Assert.Equal(new Vector3d(100, 200, 300), bounds.Min);
        Assert.Equal(new Vector3d(101, 202, 303), bounds.Max);
        Assert.Equal(vertices[0], bounds.Min);
        Assert.Equal(vertices[1], bounds.Max);
    }

    [Fact]
    public void Mesh_GetWorldBoundingBox_AfterTransformRemoval_UsesLocalBounds()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 2, 3)],
            Indices = [],
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 2, 3)),
            Transform = new NwcTransform { Id = 1, Translation = new Vector3d(100, 200, 300) },
        };

        Assert.Equal(100.0, mesh.GetWorldBoundingBox().Min.X);

        mesh.Transform = null;

        var bounds = mesh.GetWorldBoundingBox();
        Assert.Equal(mesh.BoundingBox.Value, bounds);
        Assert.Equal(mesh.GetWorldVertices()[0], bounds.Min);
        Assert.Equal(mesh.GetWorldVertices()[1], bounds.Max);
    }

    [Fact]
    public void Mesh_TriangleCountAndIndices_AreCalculatedCorrectly()
    {
        var vertices = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 1, 0),
            new Vector3d(1, 1, 0),
        };

        var indices = new[] { 0, 1, 2, 2, 1, 3 };

        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "QuadMesh",
            Vertices = vertices,
            Indices = indices,
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0)),
        };

        Assert.Equal(1, mesh.Id);
        Assert.Equal("QuadMesh", mesh.Name);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(6, mesh.Indices.Count);
        Assert.Equal(2, mesh.TriangleCount);
    }

    [Fact]
    public void Mesh_GetWorldVertices_WithoutTransform_ReturnsLocalVertices()
    {
        var vertices = new[] { new Vector3d(10, 20, 30) };
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = vertices,
            Indices = [0, 0, 0],
        };

        var world = mesh.GetWorldVertices();
        Assert.Single(world);
        Assert.Equal(10.0, world[0].X);
        Assert.Equal(20.0, world[0].Y);
        Assert.Equal(30.0, world[0].Z);
    }

    [Fact]
    public void Mesh_GetWorldVertices_WithTranslationTransform_TranslatesVertices()
    {
        var vertices = new[] { new Vector3d(5, 10, 15) };
        var transform = new NwcTransform
        {
            Id = 1,
            Translation = new Vector3d(100, 200, 300),
            Rotation = Quaterniond.Identity,
        };

        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = vertices,
            Indices = [0, 0, 0],
            Transform = transform,
        };

        var world = mesh.GetWorldVertices();
        Assert.Single(world);
        Assert.Equal(105.0, world[0].X);
        Assert.Equal(210.0, world[0].Y);
        Assert.Equal(315.0, world[0].Z);
    }

    [Fact]
    public void MeshParser_SyntheticBuffer_ParsesTriangleStripCorrectly()
    {
        // Construct a synthetic raw stream with Tag 60 header and 4 vertices
        // Tag 60 layout: [id: 4][tag 60: 4][vCount: 4][p2: 4][4 x 12B float triples]
        var data = new byte[16 + 4 * 12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 101);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 60);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 12);

        // v0: (0, 0, 0)
        BitConverter.TryWriteBytes(data.AsSpan(16), 0f);
        BitConverter.TryWriteBytes(data.AsSpan(20), 0f);
        BitConverter.TryWriteBytes(data.AsSpan(24), 0f);

        // v1: (1, 0, 0)
        BitConverter.TryWriteBytes(data.AsSpan(28), 1f);
        BitConverter.TryWriteBytes(data.AsSpan(32), 0f);
        BitConverter.TryWriteBytes(data.AsSpan(36), 0f);

        // v2: (0, 1, 0)
        BitConverter.TryWriteBytes(data.AsSpan(40), 0f);
        BitConverter.TryWriteBytes(data.AsSpan(44), 1f);
        BitConverter.TryWriteBytes(data.AsSpan(48), 0f);

        // v3: (1, 1, 0)
        BitConverter.TryWriteBytes(data.AsSpan(52), 1f);
        BitConverter.TryWriteBytes(data.AsSpan(56), 1f);
        BitConverter.TryWriteBytes(data.AsSpan(60), 0f);

        var stream = new LcRawStream
        {
            Index = 0,
            FileOffset = 0,
            CompressedLength = data.Length,
            GapBefore = 0,
            Data = data,
            SectionIndex = 0,
            Kind = NwcStreamKind.Geometry,
        };

        var container = new NwcContainer
        {
            Sections = [],
            Streams = [stream],
            FileLength = data.Length,
            TrailingGap = 0,
        };

        var meshes = MeshParser.Parse(container);
        Assert.Single(meshes);

        var m = meshes[0];
        Assert.Equal(4, m.Vertices.Count);
        Assert.Equal(6, m.Indices.Count); // (4 - 2) * 3 = 6
        Assert.Equal(2, m.TriangleCount);

        // Check bounding box
        Assert.NotNull(m.BoundingBox);
        var bbox = m.BoundingBox.Value;
        Assert.Equal(0, bbox.Min.X);
        Assert.Equal(0, bbox.Min.Y);
        Assert.Equal(0, bbox.Min.Z);
        Assert.Equal(1, bbox.Max.X);
        Assert.Equal(1, bbox.Max.Y);
        Assert.Equal(0, bbox.Max.Z);

        // Check alternating strip winding: tri 0 is (0, 1, 2), tri 1 is (2, 1, 3)
        Assert.Equal(0, m.Indices[0]);
        Assert.Equal(1, m.Indices[1]);
        Assert.Equal(2, m.Indices[2]);
        Assert.Equal(2, m.Indices[3]);
        Assert.Equal(1, m.Indices[4]);
        Assert.Equal(3, m.Indices[5]);
    }

    [Fact]
    public void MeshParser_CorruptedFloats_AreRejectedGracefully()
    {
        var data = new byte[16 + 4 * 12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 101);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 60);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 12);

        // Put NaN in vertex 0
        BitConverter.TryWriteBytes(data.AsSpan(16), float.NaN);

        var stream = new LcRawStream
        {
            Index = 0,
            FileOffset = 0,
            CompressedLength = data.Length,
            GapBefore = 0,
            Data = data,
            SectionIndex = 0,
            Kind = NwcStreamKind.Geometry,
        };

        var container = new NwcContainer
        {
            Sections = [],
            Streams = [stream],
            FileLength = data.Length,
            TrailingGap = 0,
        };

        var meshes = MeshParser.Parse(container);
        Assert.Empty(meshes);
    }
}

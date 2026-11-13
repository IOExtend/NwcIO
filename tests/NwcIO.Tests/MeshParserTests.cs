using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class MeshParserTests
{
    [Theory]
    [InlineData(3, 0, 72)]
    [InlineData(0, 9, 72)]
    [InlineData(3, 0, int.MaxValue)]
    [InlineData(0, 9, int.MaxValue)]
    public void Parse_LastTocRecordWithOverlongLength_ParsesVerticesThatFit(int p1, int p2, int declaredLength)
    {
        var record = Int32Bytes(0, 94, 0, 60, p1, p2,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var chunk = record.Concat(record).ToArray();
        var toc = Int32Bytes(2, 1, chunk.Length, record.Length, declaredLength);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        // The first mesh prevents heuristic fallback from masking a skipped final record.
        var meshes = MeshParser.Parse(container);

        Assert.Equal(2, meshes.Count);
        Assert.Equal(3, meshes[1].Vertices.Count);
        Assert.Equal(new Vector3d(0, 0, 0), meshes[1].Vertices[0]);
        Assert.Equal(new Vector3d(1, 0, 0), meshes[1].Vertices[1]);
        Assert.Equal(new Vector3d(0, 1, 0), meshes[1].Vertices[2]);
        Assert.Equal(1, meshes[1].TriangleCount);
    }

    [Theory]
    [InlineData(100, 0, 1224)]
    [InlineData(0, 0, -100)]
    [InlineData(0, 0, int.MaxValue)]
    [InlineData(1073741827, 0, 60)]
    [InlineData(0, 1073741829, 60)]
    public void Open_MalformedTocRecord_SkipsMesh(int p1, int p2, int recordLength)
    {
        var chunk = Int32Bytes(0, 94, 0, 60, p1, p2, 0, 0);
        var toc = recordLength < 0
            ? Int32Bytes(2, 1, chunk.Length, recordLength, 24)
            : Int32Bytes(1, 1, chunk.Length, recordLength);
        var bytes = NwcContainerWriter.Write([chunk, toc]);
        using var file = new MemoryStream(bytes);

        Assert.Empty(NwcDocument.Open(file).Meshes);

        // Classification rejects negative lengths already; exercise the parser guard too.
        var container = NwcContainerReader.Read(bytes);
        container.Streams[1].Kind = NwcStreamKind.Toc;
        Assert.Empty(MeshParser.Parse(container));
    }

    [Theory]
    [InlineData(536870912)]
    [InlineData(int.MaxValue)]
    public void Parse_OverflowingTocRecordCount_SkipsMesh(int recordCount)
    {
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([Int32Bytes(recordCount, 1, 32, 24)]));
        container.Streams[0].Kind = NwcStreamKind.Toc;

        Assert.Empty(MeshParser.Parse(container));
    }

    private static byte[] Int32Bytes(params int[] values)
    {
        var data = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), values[i]);
        }

        return data;
    }

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
    public void Mesh_GetWorldVertices_WithMirroredTransform_AppliesScale()
    {
        var mesh = new NwcMesh { Id = 1, Vertices = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)], Indices = [0, 1, 2],
            Transform = new NwcTransform { Id = 1, Translation = new(10, 0, 0), Scale = -1 } };
        Assert.Equal([new Vector3d(9, 0, 0), new(10, -1, 0), new(10, 0, -1)], mesh.GetWorldVertices());
        Assert.Equal(new BoundingBox3d(new(9, -1, -1), new(10, 0, 0)), mesh.GetWorldBoundingBox());
    }

    [Theory]
    [InlineData(-1.0, new[] { 0, 2, 1, 2, 3, 1 })]
    [InlineData(100.0, new[] { 0, 1, 2, 2, 1, 3 })]
    [InlineData(1.0, new[] { 0, 1, 2, 2, 1, 3 })]
    public void Mesh_GetWorldIndices_ReversesTrianglesOnlyForNegativeScale(double scale, int[] expected)
    {
        int[] indices = [0, 1, 2, 2, 1, 3];
        var mesh = new NwcMesh { Id = 1, Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)], Indices = indices,
            Transform = new NwcTransform { Id = 1, Scale = scale } };
        Assert.Equal(expected, mesh.GetWorldIndices());
        Assert.Equal([0, 1, 2, 2, 1, 3], indices);
        if (scale > 0) Assert.Same(indices, mesh.GetWorldIndices());
    }

    [Fact]
    public void Mesh_NewMembers_DefaultToEmpty()
    {
        var mesh = new NwcMesh { Id = 1, Vertices = [], Indices = [] };
        Assert.Null(mesh.Normals);
        Assert.Empty(mesh.LineIndices);
        Assert.Same(mesh.Indices, mesh.GetWorldIndices());
        Assert.Equal(1.0, new NwcTransform { Id = 1 }.Scale);
        Assert.Empty(new NwcNode { Id = 0, Name = "n", Class = "c" }.Meshes);
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

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

    [Fact]
    public void Parse_TocSkippedRecordNextToValidMesh_CountsSkippedRecord()
    {
        // The valid first record keeps the heuristic fallback from running, so only the TOC path counts.
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var skipped = Int32Bytes(0, 94, 0, 60, 100, 0, 0, 0);
        var chunk = triangle.Concat(skipped).ToArray();
        var toc = Int32Bytes(2, 1, chunk.Length, triangle.Length, skipped.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var meshes = MeshParser.Parse(container, null, MeshParser.MaxPlausibleVertices, null, out int skippedRecords);

        Assert.Single(meshes);
        Assert.Equal(1, skippedRecords);
    }

    [Fact]
    public void Parse_ValidRecords_CountNoSkippedRecords()
    {
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var toc = Int32Bytes(1, 1, triangle.Length, triangle.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([triangle, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        MeshParser.Parse(container, null, MeshParser.MaxPlausibleVertices, null, out int skippedRecords);

        Assert.Equal(0, skippedRecords);
    }

    [Fact]
    public void Parse_LoneTruncatedRecord_CountsSkippedRecord()
    {
        // A vertex count in range but more bytes than present is the typical truncation. No mesh is found,
        // so the heuristic fallback replaces the TOC attempt and must count the record as well.
        var chunk = Int32Bytes(0, 94, 0, 60, 100, 0, 0, 0);
        var toc = Int32Bytes(1, 1, chunk.Length, 1224);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var meshes = MeshParser.Parse(container, null, MeshParser.MaxPlausibleVertices, null, out int skippedRecords);

        Assert.Empty(meshes);
        Assert.Equal(1, skippedRecords);
    }

    [Fact]
    public void Open_TruncatedGeometryRecord_ReportsMeshesSkipped()
    {
        var chunk = Int32Bytes(0, 94, 0, 60, 100, 0, 0, 0);
        var toc = Int32Bytes(1, 1, chunk.Length, 1224);
        using var file = new MemoryStream(NwcContainerWriter.Write([chunk, toc]));

        var diagnostic = Assert.Single(NwcDocument.Open(file).Diagnostics);

        Assert.Equal("meshes-skipped", diagnostic.Code);
        Assert.Equal(NwcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.StartsWith("1 geometry records were skipped", diagnostic.Message);
    }

    [Fact]
    public void Open_OutOfRangeVertexCount_ReportsCapFromOptions()
    {
        var chunk = Int32Bytes(0, 94, 0, 60, 5, 0, 0, 0);
        var toc = Int32Bytes(1, 1, chunk.Length, chunk.Length);
        using var file = new MemoryStream(NwcContainerWriter.Write([chunk, toc]));

        var diagnostic = Assert.Single(NwcDocument.Open(file, new NwcReaderOptions { MaxMeshVertices = 4 }).Diagnostics);

        Assert.Equal("meshes-skipped", diagnostic.Code);
        Assert.Contains("3..4", diagnostic.Message);
    }

    [Fact]
    public void Open_ModelWithoutSkippedRecords_HasNoDiagnostics()
    {
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var toc = Int32Bytes(1, 1, triangle.Length, triangle.Length);
        using var file = new MemoryStream(NwcContainerWriter.Write([triangle, toc]));

        var doc = NwcDocument.Open(file);

        Assert.Single(doc.Meshes);
        Assert.Empty(doc.Diagnostics);
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

    [Fact]
    public void Parse_SkippedRecord_DoesNotShiftLaterIdsOrTransforms()
    {
        // Record 0 is malformed and rejected; records 1 and 2 are triangles.
        var skipped = Int32Bytes(0, 94, 0, 60, 100, 0, 0, 0);
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var chunk = skipped.Concat(triangle).Concat(triangle).ToArray();
        var toc = Int32Bytes(3, 1, chunk.Length, skipped.Length, triangle.Length, triangle.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var transforms = new[]
        {
            new NwcTransform { Id = 100, Translation = new Vector3d(100, 0, 0) },
            new NwcTransform { Id = 101, Translation = new Vector3d(101, 0, 0) },
            new NwcTransform { Id = 102, Translation = new Vector3d(102, 0, 0) },
        };

        var meshes = MeshParser.Parse(container, transforms);

        Assert.Equal(2, meshes.Count);
        Assert.Equal([1, 2], meshes.Select(m => m.Id));
        Assert.Same(transforms[1], meshes[0].Transform);
        Assert.Same(transforms[2], meshes[1].Transform);
    }

    [Fact]
    public void Parse_RecordRejectedByVertexValidation_DoesNotShiftLaterIdsOrTransforms()
    {
        // Record 0 passes the header checks but its coordinate is beyond the reader limit.
        var overLimit = Int32Bytes(0, 94, 0, 60, 3, 9,
            BitConverter.SingleToInt32Bits(3e8f), 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var chunk = overLimit.Concat(triangle).Concat(triangle).ToArray();
        var toc = Int32Bytes(3, 1, chunk.Length, overLimit.Length, triangle.Length, triangle.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var transforms = new[]
        {
            new NwcTransform { Id = 100 },
            new NwcTransform { Id = 101 },
            new NwcTransform { Id = 102 },
        };

        var meshes = MeshParser.Parse(container, transforms);

        Assert.Equal([1, 2], meshes.Select(m => m.Id));
        Assert.Same(transforms[1], meshes[0].Transform);
        Assert.Same(transforms[2], meshes[1].Transform);
    }

    [Fact]
    public void Parse_SkippedRecord_BindsLinkIndicesByRecordOrder()
    {
        var skipped = Int32Bytes(0, 94, 0, 60, 100, 0, 0, 0);
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var chunk = skipped.Concat(triangle).Concat(triangle).ToArray();
        var toc = Int32Bytes(3, 1, chunk.Length, skipped.Length, triangle.Length, triangle.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var transforms = new[]
        {
            new NwcTransform { Id = 100 },
            new NwcTransform { Id = 101 },
            new NwcTransform { Id = 102 },
        };

        var meshes = MeshParser.Parse(container, transforms, transformIndices: [0, 2, 1]);

        Assert.Same(transforms[2], meshes[0].Transform);
        Assert.Same(transforms[1], meshes[1].Transform);
    }

    [Fact]
    public void Parse_FewerTransformsThanRecords_LeavesTheLaterRecordsUnplaced()
    {
        var skipped = Int32Bytes(0, 94, 0, 60, 100, 0, 0, 0);
        var triangle = Int32Bytes(0, 94, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var chunk = skipped.Concat(triangle).Concat(triangle).ToArray();
        var toc = Int32Bytes(3, 1, chunk.Length, skipped.Length, triangle.Length, triangle.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var transforms = new[]
        {
            new NwcTransform { Id = 100 },
            new NwcTransform { Id = 101 },
        };

        var meshes = MeshParser.Parse(container, transforms);

        Assert.Equal(2, meshes.Count);
        Assert.Same(transforms[1], meshes[0].Transform);
        Assert.Null(meshes[1].Transform);
    }

    [Fact]
    public void Parse_TwoVertexClass95Record_ReturnsLineMesh()
    {
        var record = Int32Bytes(0, 95, 0, 60, 2, 6,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0);
        var toc = Int32Bytes(1, 1, record.Length, record.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([record, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var mesh = Assert.Single(MeshParser.Parse(container));

        Assert.Equal(2, mesh.Vertices.Count);
        Assert.Empty(mesh.Indices);
        Assert.Equal([0, 1], mesh.LineIndices);
        Assert.Equal(0, mesh.TriangleCount);
    }

    [Fact]
    public void Parse_MaxMeshVertices_IsConfigurable()
    {
        var record = Int32Bytes(0, 94, 0, 60, 4, 12,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0,
            BitConverter.SingleToInt32Bits(1f), BitConverter.SingleToInt32Bits(1f), 0);
        var toc = Int32Bytes(1, 1, record.Length, record.Length);
        var bytes = NwcContainerWriter.Write([record, toc]);

        var container = NwcContainerReader.Read(bytes);
        container.Streams[1].Kind = NwcStreamKind.Toc;

        Assert.Empty(MeshParser.Parse(container, maxVertices: 3));
        Assert.Single(MeshParser.Parse(container, maxVertices: 4));

        using var file = new MemoryStream(bytes);
        Assert.Empty(NwcDocument.Open(file, new NwcReaderOptions { MaxMeshVertices = 3 }).Meshes);
    }

    [Fact]
    public void Parse_Class95RecordWithThreeVertices_ReturnsPolylineNotTriangles()
    {
        var record = Int32Bytes(0, 95, 0, 60, 3, 9,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0,
            0, BitConverter.SingleToInt32Bits(1f), 0);
        var toc = Int32Bytes(1, 1, record.Length, record.Length);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([record, toc]));
        container.Streams[1].Kind = NwcStreamKind.Toc;

        var mesh = Assert.Single(MeshParser.Parse(container));

        Assert.Empty(mesh.Indices);
        Assert.Equal([0, 1, 1, 2], mesh.LineIndices);
    }

    [Fact]
    public void Parse_TwoVertexRecordWithoutToc_ReturnsLineMesh()
    {
        var data = Int32Bytes(0, 60, 2, 0,
            0, 0, 0,
            BitConverter.SingleToInt32Bits(1f), 0, 0);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([data]));

        var mesh = Assert.Single(MeshParser.Parse(container));

        Assert.Equal([0, 1], mesh.LineIndices);
    }

    [Fact]
    public void Parse_HeuristicVertexCountBeyondTheBuffer_SkipsRecordWithoutAllocating()
    {
        // 357913942 * 12 wraps to 8 in an int; the cap is raised so the count reaches the size check.
        var data = Int32Bytes(0, 60, 357913942, 0, 0, 0, 0, 0);
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([data]));

        Assert.Empty(MeshParser.Parse(container, maxVertices: int.MaxValue));
    }

    [Fact]
    public void Parse_MaxVerticesBelowTheMinimum_Throws()
    {
        var container = NwcContainerReader.Read(NwcContainerWriter.Write([Int32Bytes(0, 0, 0, 0)]));

        Assert.Throws<ArgumentOutOfRangeException>(() => MeshParser.Parse(container, maxVertices: 2));
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
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)],
            Indices = [0, 1, 2],
            Transform = new NwcTransform { Id = 1, Translation = new(10, 0, 0), Scale = -1 }
        };
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
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)],
            Indices = indices,
            Transform = new NwcTransform { Id = 1, Scale = scale }
        };
        Assert.Equal(expected, mesh.GetWorldIndices());
        Assert.Equal([0, 1, 2, 2, 1, 3], indices);
        if (scale > 0) Assert.Same(indices, mesh.GetWorldIndices());
    }

    [Fact]
    public void Mesh_GetWorldIndices_LeavesAnIncompleteTrailingTriple()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)],
            Indices = [0, 1, 2, 3],
            Transform = new NwcTransform { Id = 1, Scale = -1 }
        };
        Assert.Equal([0, 2, 1, 3], mesh.GetWorldIndices());
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

    [Fact]
    public void Parse_WithoutLinkTable_BindsTransformsByPosition()
    {
        var data = new byte[16 + 3 * 12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 101);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 60);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 9);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(16), 0f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(28), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(44), 1f);

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
        var first = new NwcTransform { Id = 999, Translation = new Vector3d(1, 2, 3) };
        var second = new NwcTransform { Id = 998, Translation = new Vector3d(4, 5, 6) };

        var mesh = Assert.Single(MeshParser.Parse(container, [first, second]));

        Assert.Same(first, mesh.Transform);
    }

    [Fact]
    public void Parse_TrailerIds_PreserveZeroAndMakeDuplicatesUnique()
    {
        var first = TrailerRecord(0, meshId: 5, [0, 1, 2], "A");
        var second = TrailerRecord(1, meshId: 0, [0, 1, 2], "B");
        var third = TrailerRecord(2, meshId: 0, [0, 1, 2], "C");

        var meshes = ParseRecords(first, second, third);

        Assert.Equal([5, 0, 2], meshes.Select(mesh => mesh.Id));
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("indexCount")]
    [InlineData("negativeIndexCount")]
    [InlineData("indexRange")]
    [InlineData("nameLength")]
    [InlineData("truncated")]
    public void Parse_MalformedTrailer_FallsBackToTheStrip(string defect)
    {
        var record = TrailerRecord(0, meshId: 0, [0, 1, 2], "Name", defect);

        var mesh = Assert.Single(ParseRecords(record));

        Assert.Equal([0, 1, 2], mesh.Indices);
        Assert.Equal("Mesh_0", mesh.Name);
    }

    [Fact]
    public void Parse_TrailerName_DropsControlCharacters()
    {
        var record = TrailerRecord(0, meshId: 0, [0, 1, 2], "A\nB\rC");

        var mesh = Assert.Single(ParseRecords(record));

        Assert.Equal("ABC", mesh.Name);
    }

    [Fact]
    public void Parse_EmptyTrailerName_UsesTrailerIdForSynthesizedName()
    {
        var record = TrailerRecord(0, meshId: 7, [0, 1, 2], "");

        var mesh = Assert.Single(ParseRecords(record));

        Assert.Equal(7, mesh.Id);
        Assert.Equal("Mesh_7", mesh.Name);
    }

    private static List<NwcMesh> ParseRecords(params byte[][] records)
    {
        var chunk = records.SelectMany(record => record).ToArray();
        var toc = new int[3 + records.Length];
        toc[0] = records.Length;
        toc[1] = 1;
        toc[2] = chunk.Length;
        for (int i = 0; i < records.Length; i++)
        {
            toc[3 + i] = records[i].Length;
        }

        var container = NwcContainerReader.Read(NwcContainerWriter.Write([chunk, Int32Bytes(toc)]));
        container.Streams[1].Kind = NwcStreamKind.Toc;
        return MeshParser.Parse(container).ToList();
    }

    private static byte[] TrailerRecord(int recId, int meshId, int[] indices, string name, string? defect = null)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(recId);
        writer.Write(94);
        writer.Write(0);
        writer.Write(60);
        writer.Write(3);
        writer.Write(9);
        writer.Write(0f); writer.Write(0f); writer.Write(0f);
        writer.Write(1f); writer.Write(0f); writer.Write(0f);
        writer.Write(0f); writer.Write(1f); writer.Write(0f);

        int magic = defect == "magic" ? 0x12345678 : NwcStreamSerializer.MeshTrailerMagic;
        writer.Write(magic);
        writer.Write(defect == "indexCount" ? 4 : defect == "negativeIndexCount" ? -3 : indices.Length);
        foreach (int index in indices)
        {
            writer.Write(defect == "indexRange" ? 3 : index);
        }

        writer.Write(meshId);
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        writer.Write(defect == "nameLength" ? -1 : nameBytes.Length);
        writer.Write(nameBytes);
        while ((ms.Position & 3) != 0)
        {
            writer.Write((byte)0);
        }

        byte[] record = ms.ToArray();
        return defect == "truncated" ? record[..^2] : record;
    }

    [Fact]
    public void IsStrip_AgreesWithBuildStripIndices_AndRejectsSwappedPair()
    {
        for (int n = 3; n <= 8; n++)
        {
            int[] strip = MeshParser.BuildStripIndices(n);
            Assert.True(MeshParser.IsStrip(strip, n));

            int[] swapped = (int[])strip.Clone();
            (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
            Assert.False(MeshParser.IsStrip(swapped, n));
            Assert.False(MeshParser.IsStrip(strip.Take(strip.Length - 1).ToArray(), n));
        }
    }
}

using System.Buffers.Binary;
using System.Text.Json;
using NwcIO.Internal.Export;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class GlbExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlbExporter_WriteFile_KeepsDestinationOnFailure(bool existing)
    {
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-test-{Guid.NewGuid():N}.glb");
        byte[] original = [1, 2, 3];
        try
        {
            if (existing)
            {
                File.WriteAllBytes(path, original);
            }

            Assert.Throws<InvalidOperationException>(() =>
                GlbExporter.WriteFile(new ThrowingMeshList(), Array.Empty<NwcMaterial>(), path));
            Assert.Equal(existing, File.Exists(path));
            if (existing)
            {
                Assert.Equal(original, File.ReadAllBytes(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GlbExporter_CreateBinBuffer_FailsWhenTempDirectoryIsMissing()
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-glb-");
        try
        {
            string unavailable = Path.Combine(directory.FullName, "missing");
            Assert.ThrowsAny<IOException>(() => GlbExporter.CreateBinBuffer(unavailable));
        }
        finally
        {
            directory.Delete();
        }
    }

    [Theory]
    [InlineData(2_147_483_649L, 2_147_483_652u, 2_147_483_684u)]
    [InlineData(4_294_967_260L, 4_294_967_260u, 4_294_967_292u)]
    public void GlbExporter_WriteContainer_WritesUnsignedLengthsAboveTwoGiB(long bytes, uint chunkLength, uint totalLength)
    {
        using var bin = new HeaderOnlyBin(bytes);
        using var output = new MemoryStream();

        GlbExporter.WriteContainer(output, "{}"u8, bin);

        Assert.True(bin.CopyRequested);
        byte[] header = output.ToArray();
        Assert.Equal(totalLength, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)));
        Assert.Equal(chunkLength, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24)));
        Assert.Equal(0x004E4942u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28)));
        Assert.Equal(32 + chunkLength - bytes, header.Length);
        Assert.All(header[32..], value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(4_294_967_261L, "GLB file")]
    [InlineData(4_294_967_293L, "GLB BIN chunk")]
    [InlineData(long.MaxValue, "GLB BIN chunk")]
    public void GlbExporter_WriteContainer_RejectsSizeOverflowBeforeWriting(long bytes, string expectedError)
    {
        using var bin = new HeaderOnlyBin(bytes);
        using var output = new MemoryStream();

        var error = Assert.Throws<InvalidOperationException>(() => GlbExporter.WriteContainer(output, "{}"u8, bin));

        Assert.Contains(expectedError, error.Message, StringComparison.Ordinal);
        Assert.Contains("4294967292", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
        Assert.False(bin.CopyRequested);
    }

    [Theory]
    [InlineData(32_769)]
    [InlineData(65_541)]
    public void GlbExporter_PacksCompleteBinAcrossBatches(int vertexCount)
    {
        var vertices = Enumerable.Range(0, vertexCount).Select(i =>
            new Vector3d(i / 3, i % 3 == 1 ? 1 : 0, i % 3 == 2 ? 1 : 0)).ToArray();
        var indices = Enumerable.Range(0, vertexCount).ToArray();
        var colors = Enumerable.Range(0, vertexCount).Select(i => new RgbaColor(i % 2, 0.25f, 0.5f, 1f)).ToArray();
        var glb = ExportGlb([new NwcMesh
        {
            Id = 1,
            Name = "Batched",
            Vertices = vertices,
            Indices = indices,
            LineIndices = indices[..^1],
            LineColors = colors,
        }]);

        using var expected = new MemoryStream();
        using var writer = new BinaryWriter(expected);
        foreach (var vertex in vertices)
        {
            writer.Write((float)vertex.X);
            writer.Write((float)vertex.Y);
            writer.Write((float)vertex.Z);
        }

        for (int i = 0; i < vertexCount; i++)
        {
            writer.Write(1f);
            writer.Write(0f);
            writer.Write(0f);
        }

        foreach (var color in colors)
        {
            writer.Write(color.R);
            writer.Write(color.G);
            writer.Write(color.B);
            writer.Write(color.A);
        }

        WriteIndices(indices);
        WriteIndices(indices.AsSpan(0, vertexCount - 1));
        writer.Flush();

        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal((uint)expected.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(20 + jsonLength)));
        Assert.Equal(expected.ToArray(), glb[(28 + jsonLength)..]);

        void WriteIndices(ReadOnlySpan<int> values)
        {
            foreach (int index in values)
            {
                if (vertexCount <= ushort.MaxValue)
                {
                    writer.Write((ushort)index);
                }
                else
                {
                    writer.Write((uint)index);
                }
            }

            while (expected.Length % 4 != 0)
            {
                writer.Write((byte)0);
            }
        }
    }

    private sealed class HeaderOnlyBin(long length) : MemoryStream
    {
        public bool CopyRequested { get; private set; }
        public override long Length { get; } = length;
        public override long Position { get; set; } = length;

        public override void CopyTo(Stream destination, int bufferSize)
        {
            Assert.Equal(0, Position);
            CopyRequested = true;
        }
    }

    private sealed class ThrowingMeshList : IReadOnlyList<NwcMesh>
    {
        public int Count => 1;

        public NwcMesh this[int index] => throw new InvalidOperationException("boom");

        public IEnumerator<NwcMesh> GetEnumerator() => throw new InvalidOperationException("boom");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlbExporter_WithoutGeometry_OmitsEmptyArraysAndBinaryChunk(bool cameraOnly)
    {
        var viewpoints = cameraOnly
            ? new[] { NwcSavedViewpoint.CreateDefault(BoundingBox3d.Empty) }
            : Array.Empty<NwcSavedViewpoint>();
        var document = NwcDocument.Create(fileInfo: new NwcFileInfo { LoaderPlugin = "Source Loader" }, savedViewpoints: viewpoints);
        using var output = new MemoryStream();

        document.SaveGlb(output);

        var glb = output.ToArray();
        Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(glb));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0, jsonLength % 4);
        Assert.Equal(0x4E4F534Au, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)));
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = json.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        Assert.StartsWith("NwcIO", root.GetProperty("asset").GetProperty("generator").GetString());
        Assert.Equal(0, root.GetProperty("scene").GetInt32());
        var scene = Assert.Single(root.GetProperty("scenes").EnumerateArray());
        foreach (var property in new[] { "meshes", "materials", "accessors", "bufferViews", "buffers" })
        {
            Assert.False(root.TryGetProperty(property, out _), $"Unexpected {property}");
        }

        if (cameraOnly)
        {
            Assert.Equal(0, Assert.Single(scene.GetProperty("nodes").EnumerateArray()).GetInt32());
            var node = Assert.Single(root.GetProperty("nodes").EnumerateArray());
            Assert.Equal(0, node.GetProperty("camera").GetInt32());
            Assert.False(node.TryGetProperty("mesh", out _));
            var camera = Assert.Single(root.GetProperty("cameras").EnumerateArray());
            Assert.Equal("perspective", camera.GetProperty("type").GetString());
        }
        else
        {
            Assert.False(scene.TryGetProperty("nodes", out _));
            Assert.False(root.TryGetProperty("nodes", out _));
            Assert.False(root.TryGetProperty("cameras", out _));
        }

        Assert.Equal(20 + jsonLength, glb.Length);
    }

    [Theory]
    [InlineData(3, -1)]
    [InlineData(3, 3)]
    [InlineData(65536, -1)]
    [InlineData(65536, 65536)]
    public void GlbExporter_InvalidTriangles_KeepsValidTriangles(int vertexCount, int invalidIndex)
    {
        for (var slot = 0; slot < 3; slot++)
        {
            var vertices = new Vector3d[vertexCount];
            vertices[1] = new(1, 0, 0);
            vertices[2] = new(0, 1, 0);
            int[] badTriangle = [0, 1, 2];
            badTriangle[slot] = invalidIndex;
            var mesh = new NwcMesh
            {
                Id = 1,
                Name = "Filtered",
                Vertices = vertices,
                Indices = [0, 1, 2, .. badTriangle, 1, 2, 0, 0, invalidIndex],
            };

            AssertExportedTriangles(mesh, [0, 1, 2, 1, 2, 0], vertexCount <= 65535 ? 5123 : 5125);
            Assert.Equal(11, mesh.Indices.Count);
            Assert.Equal(invalidIndex, mesh.Indices[3 + slot]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void GlbExporter_IncompleteTriangle_KeepsOnlyWholeTriangles(int indexCount)
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Filtered",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = new[] { 0, 1, 2, -1, 3 }.Take(indexCount).ToArray(),
        };

        if (indexCount < 3)
        {
            AssertMeshSkipped(mesh);
        }
        else
        {
            AssertExportedTriangles(mesh, [0, 1, 2], 5123);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void GlbExporter_NoValidTriangle_SkipsMesh(int invalidIndex)
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, invalidIndex, invalidIndex, 1, 2, 0, 1],
        };

        AssertMeshSkipped(mesh);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void GlbExporter_InvalidPosition_SkipsMesh(double value)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            var position = new Vector3d(axis == 0 ? value : 0, axis == 1 ? value : 0, axis == 2 ? value : 0);
            var mesh = new NwcMesh
            {
                Id = 1,
                Vertices = [position, new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
            };

            AssertMeshSkipped(mesh);
        }
    }

    private static void AssertMeshSkipped(NwcMesh mesh)
    {
        var validMesh = new NwcMesh
        {
            Id = 2,
            Name = "Survivor",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };
        using var output = new MemoryStream();
        GlbExporter.Write([mesh, validMesh, mesh], [], output, worldCoordinates: false);
        var glb = output.ToArray();
        using var json = ReadGlbJson(glb);
        var root = json.RootElement;
        Assert.Equal("Survivor", Assert.Single(root.GetProperty("meshes").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(0, Assert.Single(root.GetProperty("nodes").EnumerateArray()).GetProperty("mesh").GetInt32());
        Assert.Equal(3, root.GetProperty("accessors").GetArrayLength());
        Assert.Equal(3, root.GetProperty("bufferViews").GetArrayLength());

        output.SetLength(0);
        GlbExporter.Write([mesh], [], output, worldCoordinates: false);
        glb = output.ToArray();
        using var emptyJson = ReadGlbJson(glb);
        foreach (var property in new[] { "nodes", "meshes", "accessors", "bufferViews", "buffers" })
        {
            Assert.False(emptyJson.RootElement.TryGetProperty(property, out _), $"Unexpected {property}");
        }

        Assert.Equal(20 + (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)), glb.Length);
    }

    [Theory]
    [InlineData(true, new[] { 0, 2, 1, 2, 3, 1 }, -1f)]
    [InlineData(false, new[] { 0, 1, 2, 2, 1, 3 }, 1f)]
    public void GlbExporter_MirroredTransform_ReversesOnlyWorldTriangles(bool world, int[] expected, float normalZ)
    {
        var mesh = TestMeshes.MirroredQuad();
        AssertExportedTriangles(mesh, expected, 5123, world, normalZ);
        Assert.Equal([0, 1, 2, 2, 1, 3], mesh.Indices);
    }

    private static void AssertExportedTriangles(NwcMesh mesh, int[] expectedIndices, int componentType, bool worldCoordinates = false, float normalZ = 1f)
    {
        using var output = new MemoryStream();
        GlbExporter.Write([mesh], [], output, worldCoordinates: worldCoordinates);
        var glb = output.ToArray();
        using var json = ReadGlbJson(glb);
        var root = json.RootElement;
        var primitive = Assert.Single(root.GetProperty("meshes").EnumerateArray()).GetProperty("primitives")[0];
        var accessor = root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
        Assert.Equal(componentType, accessor.GetProperty("componentType").GetInt32());
        Assert.Equal(expectedIndices.Length, accessor.GetProperty("count").GetInt32());
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int size = componentType == 5123 ? 2 : 4;
        Assert.Equal(expectedIndices.Length * size, view.GetProperty("byteLength").GetInt32());
        int binStart = 28 + (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int offset = binStart + view.GetProperty("byteOffset").GetInt32() + accessor.GetProperty("byteOffset").GetInt32();
        for (var i = 0; i < expectedIndices.Length; i++)
        {
            uint actual = size == 2
                ? BinaryPrimitives.ReadUInt16LittleEndian(glb.AsSpan(offset + i * size))
                : BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(offset + i * size));
            Assert.Equal((uint)expectedIndices[i], actual);
        }

        var normalAccessor = root.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32()];
        var normalView = root.GetProperty("bufferViews")[normalAccessor.GetProperty("bufferView").GetInt32()];
        int normalOffset = binStart + normalView.GetProperty("byteOffset").GetInt32();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(0f, BitConverter.ToSingle(glb, normalOffset + i * 12));
            Assert.Equal(0f, BitConverter.ToSingle(glb, normalOffset + i * 12 + 4));
            Assert.Equal(normalZ, BitConverter.ToSingle(glb, normalOffset + i * 12 + 8));
        }
    }

    private static JsonDocument ReadGlbJson(byte[] glb)
    {
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return JsonDocument.Parse(glb.AsMemory(20, jsonLength));
    }

    [Fact]
    public void GlbExporter_SingleMesh_ProducesValidGlbContainer()
    {
        var vertices = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(10, 0, 0),
            new Vector3d(0, 10, 0),
        };

        var material = new NwcMaterial
        {
            Id = 1,
            Name = "TestMaterial",
            Color = new RgbaColor(1.0f, 0.5f, 0.0f, 1.0f),
            Metallic = 0.2f,
            Roughness = 0.8f,
        };

        var mesh = new NwcMesh
        {
            Id = 0,
            Name = "Triangle",
            Vertices = vertices,
            Indices = [0, 1, 2],
            Material = material,
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [material], ms, worldCoordinates: false);
        byte[] glb = ms.ToArray();

        // 1. Verify 12-byte header
        Assert.True(glb.Length >= 12);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4));
        uint totalLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8));

        Assert.Equal(0x46546C67u, magic); // "glTF"
        Assert.Equal(2u, version);
        Assert.Equal((uint)glb.Length, totalLength);

        // 2. Verify Chunk 0 (JSON)
        uint jsonChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        uint jsonChunkType = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16));

        Assert.Equal(0x4E4F534Au, jsonChunkType); // "JSON"
        Assert.True(jsonChunkLength % 4 == 0, "JSON chunk must be 4-byte aligned");

        byte[] jsonBytes = glb.AsSpan(20, (int)jsonChunkLength).ToArray();
        string json = System.Text.Encoding.UTF8.GetString(jsonBytes).Trim();

        using var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;

        // Verify glTF structure
        Assert.True(root.TryGetProperty("asset", out var assetEl));
        Assert.Equal("2.0", assetEl.GetProperty("version").GetString());
        Assert.True(root.TryGetProperty("scene", out var sceneEl));
        Assert.Equal(0, sceneEl.GetInt32());
        Assert.True(root.TryGetProperty("meshes", out var meshesEl));
        Assert.Equal(1, meshesEl.GetArrayLength());
        Assert.True(root.TryGetProperty("materials", out var materialsEl));
        Assert.Equal(1, materialsEl.GetArrayLength());
        Assert.True(root.TryGetProperty("accessors", out var accessorsEl));
        Assert.True(root.TryGetProperty("bufferViews", out var bufferViewsEl));
        Assert.True(root.TryGetProperty("buffers", out var buffersEl));

        // 3. Verify Chunk 1 (BIN)
        int binChunkOffset = 20 + (int)jsonChunkLength;
        Assert.True(glb.Length >= binChunkOffset + 8);
        uint binChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binChunkOffset));
        uint binChunkType = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binChunkOffset + 4));

        Assert.Equal(0x004E4942u, binChunkType); // "BIN\0"
        Assert.True(binChunkLength % 4 == 0, "BIN chunk must be 4-byte aligned");
        Assert.Equal((uint)(glb.Length - (binChunkOffset + 8)), binChunkLength);
    }

    [Fact]
    public void AssemblyGlb_SameNamedMaterialsInDifferentModels_KeepTheirOwnColors()
    {
        // Two models, each with its OWN material instance named "Concrete" but a
        // different colour: grey in A, blue in B. The by-name assembly dedupe used to
        // drop B's material, silently recolouring B's mesh with material index 0.
        static NwcDocument DocWith(string source, string meshName, float r, float g, float b)
        {
            var material = new NwcMaterial { Id = 1, Name = "Concrete", Color = new RgbaColor(r, g, b) };
            return new NwcDocumentBuilder().SetFileInfo(source)
                .AddMaterial(material)
                .AddMesh(new NwcMesh
                {
                    Id = 1,
                    Name = meshName,
                    Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                    Indices = [0, 1, 2],
                    BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
                    Material = material,
                })
                .Build();
        }

        var assembly = NwcAssembly.Create("X", DocWith("A.rvt", "MeshA", 128, 128, 128), DocWith("B.rvt", "MeshB", 0, 0, 255));

        // Both distinct material instances must survive the assembly material table.
        Assert.Equal(2, assembly.AllMaterials.Count);

        using var ms = new MemoryStream();
        GlbExporter.Write(assembly.AllMeshes, assembly.AllMaterials, ms, worldCoordinates: false);
        byte[] glb = ms.ToArray();

        uint jsonChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        string json = System.Text.Encoding.UTF8.GetString(glb.AsSpan(20, (int)jsonChunkLength).ToArray()).Trim();

        using var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;

        // Two materials exported: one grey, one blue.
        var materials = root.GetProperty("materials");
        Assert.Equal(2, materials.GetArrayLength());
        int blueIndex = -1, greyIndex = -1;
        for (int i = 0; i < 2; i++)
        {
            float r = materials[i].GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[0].GetSingle();
            float b = materials[i].GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[2].GetSingle();
            if (b > 0.9f && r < 0.1f) blueIndex = i;
            if (r > 0.4f && b > 0.4f) greyIndex = i;
        }
        Assert.True(blueIndex >= 0, "blue Concrete missing from the materials array");
        Assert.True(greyIndex >= 0, "grey Concrete missing from the materials array");

        // Each mesh references its own material: MeshA grey, MeshB blue.
        var meshes = root.GetProperty("meshes");
        Assert.Equal(2, meshes.GetArrayLength());
        foreach (var mesh in meshes.EnumerateArray())
        {
            int matIdx = mesh.GetProperty("primitives")[0].GetProperty("material").GetInt32();
            string? meshName = mesh.GetProperty("name").GetString();
            if (meshName == "MeshA")
            {
                Assert.Equal(greyIndex, matIdx);
            }
            else if (meshName == "MeshB")
            {
                Assert.Equal(blueIndex, matIdx);
            }
        }
    }

    [Fact]
    public void GlbExporter_LineMesh_ExportsLinesPrimitiveWithMode1()
    {
        var mesh = new NwcMesh
        {
            Id = 5,
            Name = "LineSegment",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 2, 3)],
            Indices = [],
            LineIndices = [0, 1],
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [], ms, worldCoordinates: false);

        using var doc = ReadGlbJson(ms.ToArray());
        var root = doc.RootElement;

        var meshes = root.GetProperty("meshes");
        Assert.Equal(1, meshes.GetArrayLength());

        var primitives = meshes[0].GetProperty("primitives");
        Assert.Equal(1, primitives.GetArrayLength());

        var prim = primitives[0];
        Assert.Equal(1, prim.GetProperty("mode").GetInt32()); // 1 = LINES
        Assert.True(prim.GetProperty("attributes").TryGetProperty("POSITION", out _));
        Assert.False(prim.GetProperty("attributes").TryGetProperty("NORMAL", out _));
        Assert.True(prim.TryGetProperty("indices", out var idxAccessor));
        Assert.True(idxAccessor.GetInt32() >= 0);
    }

    [Fact]
    public void GlbExporter_LineMeshWithColors_ExportsColor0Attribute()
    {
        var mesh = new NwcMesh
        {
            Id = 6,
            Name = "ColoredLine",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(5, 5, 5)],
            Indices = [],
            LineIndices = [0, 1],
            LineColors = [new RgbaColor(1f, 0f, 0f, 1f), new RgbaColor(0f, 1f, 0f, 1f)],
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [], ms, worldCoordinates: false);

        using var doc = ReadGlbJson(ms.ToArray());
        var root = doc.RootElement;

        var prim = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        Assert.Equal(1, prim.GetProperty("mode").GetInt32());
        Assert.True(prim.GetProperty("attributes").TryGetProperty("COLOR_0", out var colAccIdx));

        var accessors = root.GetProperty("accessors");
        var colAcc = accessors[colAccIdx.GetInt32()];
        Assert.Equal("VEC4", colAcc.GetProperty("type").GetString());
        Assert.Equal(5126, colAcc.GetProperty("componentType").GetInt32()); // FLOAT
    }

    [Fact]
    public void GlbExporter_MixedMesh_ExportsBothTrianglesAndLinesPrimitives()
    {
        var mesh = new NwcMesh
        {
            Id = 7,
            Name = "MixedMesh",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            LineIndices = [0, 1],
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [], ms, worldCoordinates: false);

        using var doc = ReadGlbJson(ms.ToArray());
        var root = doc.RootElement;

        var primitives = root.GetProperty("meshes")[0].GetProperty("primitives");
        Assert.Equal(2, primitives.GetArrayLength());

        // First primitive: TRIANGLES (mode 4) with NORMAL
        var triPrim = primitives[0];
        Assert.Equal(4, triPrim.GetProperty("mode").GetInt32());
        Assert.True(triPrim.GetProperty("attributes").TryGetProperty("NORMAL", out _));

        // Second primitive: LINES (mode 1)
        var linePrim = primitives[1];
        Assert.Equal(1, linePrim.GetProperty("mode").GetInt32());
        Assert.False(linePrim.GetProperty("attributes").TryGetProperty("NORMAL", out _));
    }

    [Fact]
    public void GlbExporter_TransparentLineColors_SetsMaterialAlphaModeBlend()
    {
        var mesh = new NwcMesh
        {
            Id = 8,
            Name = "TransparentLines",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(10, 10, 10)],
            Indices = [],
            LineIndices = [0, 1],
            LineColors = [new RgbaColor(1f, 0f, 0f, 0.5f), new RgbaColor(0f, 1f, 0f, 0.5f)],
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [], ms, worldCoordinates: false);

        using var doc = ReadGlbJson(ms.ToArray());
        var root = doc.RootElement;

        var prim = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        Assert.Equal(1, prim.GetProperty("mode").GetInt32());
        Assert.True(prim.TryGetProperty("material", out var matIdx));

        var materials = root.GetProperty("materials");
        var mat = materials[matIdx.GetInt32()];
        Assert.Equal("BLEND", mat.GetProperty("alphaMode").GetString());
    }

    [Fact]
    public void GlbExporter_TransparentMaterial_SetsAlphaModeBlend()
    {
        var mesh = new NwcMesh
        {
            Id = 9,
            Name = "GlassMesh",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            Material = new NwcMaterial
            {
                Id = 1,
                Name = "Glass",
                Color = new RgbaColor(0.8f, 0.8f, 0.9f, 0.3f),
            }
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [mesh.Material], ms, worldCoordinates: false);

        using var doc = ReadGlbJson(ms.ToArray());
        var root = doc.RootElement;

        var materials = root.GetProperty("materials");
        var mat = materials[0];
        Assert.Equal("BLEND", mat.GetProperty("alphaMode").GetString());
    }

    [Fact]
    public void GlbExporter_ColoredLine_BlackMaterial_RendersTheLineColor()
    {
        var black = new NwcMaterial { Id = 1, Name = "Black", Color = RgbaColor.Black };
        var red = new RgbaColor(1f, 0f, 0f, 1f);

        var glb = ExportGlb([LineMesh(1, black, [red, red])], black);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        Assert.True(line.GetProperty("attributes").TryGetProperty("COLOR_0", out _));
        Assert.All(EffectiveColors(glb, root, line), color => AssertColor([1f, 0f, 0f, 1f], color));
        Assert.Equal("OPAQUE", AlphaModeOf(root, line));
    }

    [Fact]
    public void GlbExporter_ColoredLine_TintedMaterial_RendersTheLineColorNotTheProduct()
    {
        var tint = new NwcMaterial { Id = 1, Name = "Tint", Color = new RgbaColor(0.2f, 0.4f, 0.6f, 1f) };
        var orange = new RgbaColor(1f, 0.5f, 0f, 1f);
        var green = new RgbaColor(0f, 1f, 0f, 1f);

        var glb = ExportGlb([LineMesh(1, tint, [orange, green])], tint);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        var colors = EffectiveColors(glb, root, line);
        AssertColor([1f, 0.5f, 0f, 1f], colors[0]);
        AssertColor([0f, 1f, 0f, 1f], colors[1]);
        Assert.Equal("OPAQUE", AlphaModeOf(root, line));
        Assert.DoesNotContain(root.GetProperty("materials").EnumerateArray(), material => material.TryGetProperty("alphaMode", out _));
    }

    [Fact]
    public void GlbExporter_MixedMesh_TransparentLineColors_TrianglesKeepTheSourceMaterial()
    {
        var tint = new NwcMaterial { Id = 1, Name = "Tint", Color = new RgbaColor(0.2f, 0.4f, 0.6f, 1f) };
        var mixed = new NwcMesh
        {
            Id = 10,
            Name = "MixedTinted",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            LineIndices = [0, 1],
            LineColors = [new RgbaColor(1f, 0f, 0f, 0.25f), new RgbaColor(0f, 1f, 0f, 0.25f), new RgbaColor(0f, 0f, 1f, 0.25f)],
            Material = tint,
        };

        var glb = ExportGlb([mixed], tint);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var triangles = PrimitiveOf(root, 0, TrianglesMode);
        var line = PrimitiveOf(root, 0, LinesMode);

        // Both primitives share the position accessor; only the line carries COLOR_0 (one entry per vertex).
        Assert.Equal(triangles.GetProperty("attributes").GetProperty("POSITION").GetInt32(), line.GetProperty("attributes").GetProperty("POSITION").GetInt32());
        Assert.False(triangles.GetProperty("attributes").TryGetProperty("COLOR_0", out _));
        Assert.True(line.GetProperty("attributes").TryGetProperty("COLOR_0", out var colorAccessor));
        Assert.Equal(3, root.GetProperty("accessors")[colorAccessor.GetInt32()].GetProperty("count").GetInt32());

        // The triangles keep the tint and stay opaque.
        AssertColor([0.2f, 0.4f, 0.6f, 1f], BaseColorOf(root, triangles));
        Assert.Equal("OPAQUE", AlphaModeOf(root, triangles));

        // The line shows its own colours, translucent.
        var colors = EffectiveColors(glb, root, line);
        AssertColor([1f, 0f, 0f, 0.25f], colors[0]);
        AssertColor([0f, 1f, 0f, 0.25f], colors[1]);
        Assert.Equal("BLEND", AlphaModeOf(root, line));
        Assert.NotEqual(triangles.GetProperty("material").GetInt32(), line.GetProperty("material").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GlbExporter_TransparentLineColors_DoNotBlendTheMaterialSharedWithTriangles(bool triangleMeshFirst)
    {
        var white = new NwcMaterial { Id = 1, Name = "White", Color = RgbaColor.White };
        var half = new RgbaColor(1f, 0f, 0f, 0.5f);
        var triangleMesh = TriangleMesh(1, white);
        var lineMesh = LineMesh(2, white, [half, half]);
        NwcMesh[] meshes = triangleMeshFirst ? [triangleMesh, lineMesh] : [lineMesh, triangleMesh];

        var glb = ExportGlb(meshes, white);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var triangles = PrimitiveOf(root, triangleMeshFirst ? 0 : 1, TrianglesMode);
        var line = PrimitiveOf(root, triangleMeshFirst ? 1 : 0, LinesMode);

        Assert.Equal("OPAQUE", AlphaModeOf(root, triangles));
        AssertColor([1f, 1f, 1f, 1f], BaseColorOf(root, triangles));
        Assert.Equal("BLEND", AlphaModeOf(root, line));
        Assert.NotEqual(triangles.GetProperty("material").GetInt32(), line.GetProperty("material").GetInt32());
        AssertColor([1f, 0f, 0f, 0.5f], EffectiveColors(glb, root, line)[0]);
        Assert.Single(root.GetProperty("materials").EnumerateArray(), material => material.TryGetProperty("alphaMode", out _));
    }

    [Fact]
    public void GlbExporter_LineColorCountDifferentFromVertexCount_KeepsSourceMaterialAndWritesNoColor()
    {
        var tint = new NwcMaterial { Id = 1, Name = "Tint", Color = new RgbaColor(0.2f, 0.4f, 0.6f, 1f) };
        var translucent = new RgbaColor(1f, 0f, 0f, 0.25f);
        var mixed = new NwcMesh
        {
            Id = 1,
            Name = "Mixed",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            LineIndices = [0, 1],
            LineColors = [translucent, translucent],
            Material = tint,
        };
        var pure = LineMesh(2, tint, [translucent, translucent, translucent]);

        var glb = ExportGlb([mixed, pure], tint);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var materials = root.GetProperty("materials");
        Assert.Equal(1, materials.GetArrayLength());
        Assert.DoesNotContain(materials.EnumerateArray(), material => material.TryGetProperty("alphaMode", out _));
        foreach (var primitive in new[] { PrimitiveOf(root, 0, TrianglesMode), PrimitiveOf(root, 0, LinesMode), PrimitiveOf(root, 1, LinesMode) })
        {
            Assert.False(primitive.GetProperty("attributes").TryGetProperty("COLOR_0", out _));
            Assert.Equal(0, primitive.GetProperty("material").GetInt32());
            AssertColor([0.2f, 0.4f, 0.6f, 1f], EffectiveColors(glb, root, primitive)[0]);
        }
    }

    [Fact]
    public void GlbExporter_TranslucentSourceMaterial_OpaqueLineColors_LineColorReplacesMaterialColor()
    {
        var glass = new NwcMaterial { Id = 1, Name = "Glass", Color = new RgbaColor(0.8f, 0.8f, 0.9f, 0.5f) };
        var green = new RgbaColor(0f, 1f, 0f, 1f);

        var glb = ExportGlb([TriangleMesh(1, glass), LineMesh(2, glass, [green, green])], glass);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var triangles = PrimitiveOf(root, 0, TrianglesMode);
        var line = PrimitiveOf(root, 1, LinesMode);
        AssertColor([0.8f, 0.8f, 0.9f, 0.5f], BaseColorOf(root, triangles));
        Assert.Equal("BLEND", AlphaModeOf(root, triangles));
        Assert.All(EffectiveColors(glb, root, line), color => AssertColor([0f, 1f, 0f, 1f], color));
        Assert.Equal("OPAQUE", AlphaModeOf(root, line));
    }

    [Fact]
    public void GlbExporter_TranslucentColorOfAnUnusedVertex_DoesNotBlendTheLine()
    {
        var white = new NwcMaterial { Id = 1, Name = "White", Color = RgbaColor.White };
        var opaque = new RgbaColor(1f, 0f, 0f, 1f);
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Mixed",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            LineIndices = [0, 1],
            LineColors = [opaque, opaque, new RgbaColor(0f, 0f, 1f, 0.5f)],
            Material = white,
        };

        var glb = ExportGlb([mesh], white);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        Assert.DoesNotContain(root.GetProperty("materials").EnumerateArray(), material => material.TryGetProperty("alphaMode", out _));
        Assert.Equal("OPAQUE", AlphaModeOf(root, PrimitiveOf(root, 0, LinesMode)));
    }

    [Fact]
    public void GlbExporter_LineColorsOutsideUnitRange_AreClampedInColor0()
    {
        var start = new RgbaColor(2f, -1f, 0.5f, 1f);
        var end = new RgbaColor(0f, 0.25f, 1.5f, 3f);

        var glb = ExportGlb([LineMesh(1, null, [start, end])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        var colors = EffectiveColors(glb, root, line);
        AssertColor([1f, 0f, 0.5f, 1f], colors[0]);
        AssertColor([0f, 0.25f, 1f, 1f], colors[1]);
        Assert.Equal("OPAQUE", AlphaModeOf(root, line));
    }

    [Fact]
    public void GlbExporter_NonFiniteLineColorComponents_AreWrittenAsZero()
    {
        var start = new RgbaColor(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1f);
        var end = new RgbaColor(0.5f, 0.5f, 0.5f, float.NaN);

        var glb = ExportGlb([LineMesh(1, null, [start, end])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        var colors = EffectiveColors(glb, root, line);
        AssertColor([0f, 0f, 0f, 1f], colors[0]);
        AssertColor([0.5f, 0.5f, 0.5f, 0f], colors[1]);
        Assert.All(colors.SelectMany(color => color), component => Assert.True(float.IsFinite(component)));
    }

    [Fact]
    public void GlbExporter_LineColorsClampEveryComponentOnBothSides()
    {
        var below = new RgbaColor(-1f, -1f, -1f, -1f);
        var above = new RgbaColor(5f, 5f, 5f, 5f);
        var largest = new RgbaColor(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);

        var glb = ExportGlb([LineMesh(1, null, [below, above]), LineMesh(2, null, [largest, largest])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var first = EffectiveColors(glb, root, PrimitiveOf(root, 0, LinesMode));
        AssertColor([0f, 0f, 0f, 0f], first[0]);
        AssertColor([1f, 1f, 1f, 1f], first[1]);
        var second = EffectiveColors(glb, root, PrimitiveOf(root, 1, LinesMode));
        Assert.All(second, color => AssertColor([1f, 0f, 1f, 0f], color));
    }

    [Fact]
    public void GlbExporter_LineColorOfAnUnusedVertex_IsSanitisedToo()
    {
        var opaque = new RgbaColor(1f, 0f, 0f, 1f);
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Mixed",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [],
            LineIndices = [0, 1],
            LineColors = [opaque, opaque, new RgbaColor(float.NaN, 5f, -5f, float.NaN)],
        };

        var glb = ExportGlb([mesh]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        AssertColor([0f, 1f, 0f, 0f], EffectiveColors(glb, root, line)[2]);
        Assert.Equal("OPAQUE", AlphaModeOf(root, line));
    }

    [Fact]
    public void GlbExporter_LineColorsInUnitRange_AreWrittenExactly()
    {
        var start = new RgbaColor(100f / 255f, 37f / 255f, 211f / 255f, 128f / 255f);
        var end = new RgbaColor(0.123456789f, 1f, 0f, 1f);

        var glb = ExportGlb([LineMesh(1, null, [start, end])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var colors = EffectiveColors(glb, root, PrimitiveOf(root, 0, LinesMode));
        float[][] expected = [[start.R, start.G, start.B, start.A], [end.R, end.G, end.B, end.A]];
        for (int v = 0; v < expected.Length; v++)
        {
            Assert.Equal(expected[v], colors[v]);
        }
    }

    [Theory]
    [InlineData(-0.5f, 0f, "BLEND")]
    [InlineData(float.NaN, 0f, "BLEND")]
    [InlineData(1.5f, 1f, "OPAQUE")]
    [InlineData(float.PositiveInfinity, 0f, "BLEND")]
    [InlineData(float.NegativeInfinity, 0f, "BLEND")]
    public void GlbExporter_LineColorAlphaOutsideUnitRange_BlendsOnTheWrittenAlpha(float alpha, float expectedWrittenAlpha, string expectedAlphaMode)
    {
        var color = new RgbaColor(1f, 0f, 0f, alpha);

        var glb = ExportGlb([LineMesh(1, null, [color, color])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        Assert.Equal(expectedAlphaMode, AlphaModeOf(root, line));
        Assert.Equal(expectedWrittenAlpha, EffectiveColors(glb, root, line)[0][3]);
    }

    [Theory]
    [InlineData(1f, 0.5f)]
    [InlineData(0.5f, 1f)]
    public void GlbExporter_LineSegmentWithOneTranslucentEndpoint_BlendsTheLine(float startAlpha, float endAlpha)
    {
        var start = new RgbaColor(1f, 0f, 0f, startAlpha);
        var end = new RgbaColor(0f, 0f, 1f, endAlpha);

        var glb = ExportGlb([LineMesh(1, null, [start, end])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var line = PrimitiveOf(root, 0, LinesMode);
        Assert.Equal("BLEND", AlphaModeOf(root, line));
        var colors = EffectiveColors(glb, root, line);
        AssertColor([1f, 0f, 0f, startAlpha], colors[0]);
        AssertColor([0f, 0f, 1f, endAlpha], colors[1]);
    }

    [Fact]
    public void GlbExporter_LaterTranslucentSegment_BlendsTheLine()
    {
        var opaque = new RgbaColor(1f, 0f, 0f, 1f);
        var half = new RgbaColor(0f, 0f, 1f, 0.5f);
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "TwoSegments",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(2, 0, 0), new Vector3d(3, 0, 0)],
            Indices = [],
            LineIndices = [0, 1, 2, 3],
            LineColors = [opaque, opaque, half, half],
        };

        var glb = ExportGlb([mesh]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        Assert.Equal("BLEND", AlphaModeOf(root, PrimitiveOf(root, 0, LinesMode)));
    }

    [Fact]
    public void GlbExporter_LineMaterials_AreSharedPerSourceMaterialAndKind()
    {
        var tint = new NwcMaterial { Id = 1, Name = "Tint", Color = new RgbaColor(0.2f, 0.4f, 0.6f, 1f) };
        var red = new RgbaColor(1f, 0f, 0f, 1f);
        var halfGreen = new RgbaColor(0f, 1f, 0f, 0.5f);
        var blue = new RgbaColor(0f, 0f, 1f, 1f);
        var halfYellow = new RgbaColor(1f, 1f, 0f, 0.5f);
        NwcMesh[] meshes =
        [
            TriangleMesh(1, tint),
            LineMesh(2, tint, [red, red]),
            LineMesh(3, tint, [halfGreen, halfGreen]),
            LineMesh(4, tint, [blue, blue]),
            LineMesh(5, tint, [halfYellow, halfYellow]),
        ];

        var glb = ExportGlb(meshes, tint);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var triangles = PrimitiveOf(root, 0, TrianglesMode);
        var lines = Enumerable.Range(1, 4).Select(m => PrimitiveOf(root, m, LinesMode)).ToArray();
        int[] materialOf = [.. lines.Select(line => line.GetProperty("material").GetInt32())];

        // The source material, one opaque line material and one translucent line material.
        Assert.Equal(3, root.GetProperty("materials").GetArrayLength());
        Assert.Equal(materialOf[0], materialOf[2]);
        Assert.Equal(materialOf[1], materialOf[3]);
        Assert.NotEqual(materialOf[0], materialOf[1]);
        Assert.DoesNotContain(materialOf, index => index == triangles.GetProperty("material").GetInt32());
        Assert.Equal("OPAQUE", AlphaModeOf(root, triangles));
        AssertColor([0.2f, 0.4f, 0.6f, 1f], BaseColorOf(root, triangles));

        RgbaColor[] expected = [red, halfGreen, blue, halfYellow];
        for (int i = 0; i < lines.Length; i++)
        {
            AssertColor([expected[i].R, expected[i].G, expected[i].B, expected[i].A], EffectiveColors(glb, root, lines[i])[0]);
            Assert.Equal(expected[i].A < 1f ? "BLEND" : "OPAQUE", AlphaModeOf(root, lines[i]));
        }
    }

    [Fact]
    public void GlbExporter_TransparentLineColorsWithoutMaterial_ShareOneBlendMaterial()
    {
        var half = new RgbaColor(0f, 0f, 1f, 0.5f);

        var glb = ExportGlb([LineMesh(1, null, [half, half]), LineMesh(2, null, [half, half])]);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var first = PrimitiveOf(root, 0, LinesMode);
        var second = PrimitiveOf(root, 1, LinesMode);
        Assert.Equal(1, root.GetProperty("materials").GetArrayLength());
        Assert.Equal(first.GetProperty("material").GetInt32(), second.GetProperty("material").GetInt32());
        Assert.Equal("BLEND", AlphaModeOf(root, first));
        AssertColor([0f, 0f, 1f, 0.5f], EffectiveColors(glb, root, first)[1]);
    }

    [Fact]
    public void GlbExporter_TriangleMeshWithLineColorsAndNoLines_WritesNoLineData()
    {
        var white = new NwcMaterial { Id = 1, Name = "White", Color = RgbaColor.White };
        var half = new RgbaColor(1f, 0f, 0f, 0.5f);
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Triangle",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            LineColors = [half, half, half],
            Material = white,
        };

        var glb = ExportGlb([mesh], white);

        using var doc = ReadGlbJson(glb);
        var root = doc.RootElement;
        var primitive = Assert.Single(root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray());
        Assert.Equal(TrianglesMode, primitive.GetProperty("mode").GetInt32());
        Assert.False(primitive.GetProperty("attributes").TryGetProperty("COLOR_0", out _));
        Assert.Equal(1, root.GetProperty("materials").GetArrayLength());
        Assert.Equal("OPAQUE", AlphaModeOf(root, primitive));
        Assert.Equal(3, root.GetProperty("accessors").GetArrayLength()); // positions, normals, triangle indices
    }

    [Fact]
    public void GlbExporter_LineScene_IsDeterministic()
    {
        var tint = new NwcMaterial { Id = 1, Name = "Tint", Color = new RgbaColor(0.2f, 0.4f, 0.6f, 1f) };
        var other = new NwcMaterial { Id = 2, Name = "Other", Color = RgbaColor.White };
        var half = new RgbaColor(1f, 0f, 0f, 0.5f);
        var red = new RgbaColor(1f, 0f, 0f, 1f);
        NwcMesh[] Scene() =>
        [
            TriangleMesh(1, tint),
            LineMesh(2, tint, [half, half]),
            LineMesh(3, other, [red, red]),
            LineMesh(4, null, [half, half]),
            LineMesh(5, tint, [red, red]),
            LineMesh(6, tint, null),
        ];

        Assert.Equal(ExportGlb(Scene(), tint, other), ExportGlb(Scene(), tint, other));
    }

    private const int LinesMode = 1;
    private const int TrianglesMode = 4;

    private static byte[] ExportGlb(IReadOnlyList<NwcMesh> meshes, params NwcMaterial[] materials)
    {
        using var output = new MemoryStream();
        GlbExporter.Write(meshes, materials, output, worldCoordinates: false);
        return output.ToArray();
    }

    private static NwcMesh TriangleMesh(int id, NwcMaterial? material) => new()
    {
        Id = id,
        Name = $"Triangle{id}",
        Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
        Indices = [0, 1, 2],
        Material = material,
    };

    private static NwcMesh LineMesh(int id, NwcMaterial? material, RgbaColor[]? colors) => new()
    {
        Id = id,
        Name = $"Line{id}",
        Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)],
        Indices = [],
        LineIndices = [0, 1],
        LineColors = colors,
        Material = material,
    };

    private static JsonElement PrimitiveOf(JsonElement root, int mesh, int mode) =>
        root.GetProperty("meshes")[mesh].GetProperty("primitives").EnumerateArray().Single(p => p.GetProperty("mode").GetInt32() == mode);

    // A primitive without a material uses the glTF default material: white and OPAQUE.
    private static JsonElement? MaterialOf(JsonElement root, JsonElement primitive) =>
        primitive.TryGetProperty("material", out var index) ? root.GetProperty("materials")[index.GetInt32()] : null;

    private static string AlphaModeOf(JsonElement root, JsonElement primitive) =>
        MaterialOf(root, primitive) is { } material && material.TryGetProperty("alphaMode", out var mode) ? mode.GetString()! : "OPAQUE";

    private static float[] BaseColorOf(JsonElement root, JsonElement primitive) =>
        MaterialOf(root, primitive) is { } material
            ? [.. material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor").EnumerateArray().Select(c => c.GetSingle())]
            : [1f, 1f, 1f, 1f];

    // The colour glTF renders at each vertex of a primitive: baseColorFactor times COLOR_0, component-wise.
    private static float[][] EffectiveColors(byte[] glb, JsonElement root, JsonElement primitive)
    {
        var factor = BaseColorOf(root, primitive);
        var attributes = primitive.GetProperty("attributes");
        int count = root.GetProperty("accessors")[attributes.GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32();
        if (!attributes.TryGetProperty("COLOR_0", out var colorIndex))
        {
            return [.. Enumerable.Repeat(factor, count)];
        }

        var accessor = root.GetProperty("accessors")[colorIndex.GetInt32()];
        Assert.Equal("VEC4", accessor.GetProperty("type").GetString());
        Assert.Equal(5126, accessor.GetProperty("componentType").GetInt32());
        Assert.Equal(count, accessor.GetProperty("count").GetInt32());
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int start = 28 + (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12))
            + view.GetProperty("byteOffset").GetInt32() + accessor.GetProperty("byteOffset").GetInt32();
        return [.. Enumerable.Range(0, count).Select(v =>
            Enumerable.Range(0, 4).Select(c => factor[c] * BitConverter.ToSingle(glb, start + v * 16 + c * 4)).ToArray())];
    }

    private static void AssertColor(float[] expected, float[] actual)
    {
        Assert.Equal(4, actual.Length);
        for (int c = 0; c < 4; c++)
        {
            Assert.Equal(expected[c], actual[c], 4);
        }
    }
}

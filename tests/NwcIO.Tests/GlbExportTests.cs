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
    public void GlbExporter_WithoutGeometry_OmitsEmptyArraysAndBinaryChunk(bool cameraOnly)
    {
        var viewpoints = cameraOnly
            ? new[] { NwcSavedViewpoint.CreateDefault(BoundingBox3d.Empty) }
            : Array.Empty<NwcSavedViewpoint>();
        var document = NwcDocument.Create(savedViewpoints: viewpoints);
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
}

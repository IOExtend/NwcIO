using System.Buffers.Binary;
using System.Text.Json;
using NwcIO.Internal.Export;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class GlbExportTests
{
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

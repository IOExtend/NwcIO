using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class TextureExportTests
{
    private const string Appearance = """
        {"version":2,"userassets":["surface"],"materials":{
          "surface":{"definition":"Generic","properties":{"colors":{"generic_diffuse":{
            "IsConnectionEnabled":true,"connections":["bitmap"]}}}},
          "unrelated":{"definition":"UnifiedBitmap","properties":{"uris":{"unifiedbitmap_Bitmap":{"values":["wrong.png"]}}}},
          "bitmap":{"definition":"UnifiedBitmap","properties":{"uris":{"unifiedbitmap_Bitmap":{"values":["old/paint.png"]}}}}
        }}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAndExport_ReadsUvAndRemappedDiffuseTexture(bool sharedAppearance)
    {
        var extra = new List<(string, byte[])>
        {
            ("LcOpXRefTable", Encoding.UTF8.GetBytes("""
                {"Type":"XRefTable","Table":[{"OriginalPath":"wrong.png","RemappedPath":"wrong-2.png"},
                {"OriginalPath":"old\\paint.png","RemappedPath":"maps/paint color.png"}]}
                """)),
        };
        var look = new Look(1, 1, 1, 0, 0, MaterialJson: Appearance);
        if (sharedAppearance)
        {
            var shared = new LcGraphWriter().Int(3);
            for (int i = 0; i < 3; i++)
            {
                shared.New(185);
                shared.Str(i == 2 ? Appearance : null).Str(null).Int(0);
            }
            extra.Add(("LcOpSharedNodes", shared.ToArray()));
            look = look with { MaterialJson = null, SharedMaterial = 2, EditedMaterial = true };
        }

        var fragments = SyntheticFragments.Build(new Frag(2, Vector3d.Zero, 1, 2, 4, Rec: 1, Look: look),
            new Frag(7, Vector3d.Zero, 1, 2, 4, Rec: 2, Look: look));
        byte[] data = SyntheticNwcFile.Build(FormatVectors.H1, fragments, [[.. FormatVectors.R4, .. FormatVectors.R1]],
            [FormatVectors.R4.Length, FormatVectors.R1.Length], extraStreams: extra);
        var doc = NwcDocument.Open(new MemoryStream(data));
        Assert.Equal([Vector2.Zero, Vector2.One, Vector2.One, Vector2.Zero], doc.Meshes[0].TexCoords);
        Assert.Null(doc.Meshes[1].TexCoords);
        Assert.Same(doc.XRefs!.References[1], doc.Meshes[0].Material!.DiffuseTexture);

        var directory = Directory.CreateTempSubdirectory("nwcio-textures-");
        try
        {
            string obj = Path.Combine(directory.FullName, "model.obj");
            await doc.SaveObjAsync(obj);
            var lines = File.ReadAllLines(obj);
            Assert.Contains("vt 0 0", lines);
            Assert.Contains("f 1/1 2/2 3/3", lines);
            Assert.Contains("f 5 6 7", lines);
            Assert.Contains("map_Kd maps/paint color.png", File.ReadAllLines(Path.ChangeExtension(obj, ".mtl")));

            using var output = new MemoryStream();
            await doc.SaveGlbAsync(output);
            byte[] glb = output.ToArray();
            int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
            using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
            var root = json.RootElement;
            var textured = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var untextured = root.GetProperty("meshes")[1].GetProperty("primitives")[0];
            int accessorIndex = textured.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
            Assert.False(untextured.GetProperty("attributes").TryGetProperty("TEXCOORD_0", out _));
            var accessor = root.GetProperty("accessors")[accessorIndex];
            Assert.Equal("VEC2", accessor.GetProperty("type").GetString());
            Assert.Equal(4, accessor.GetProperty("count").GetInt32());
            var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int binOffset = 28 + jsonLength + view.GetProperty("byteOffset").GetInt32();
            Assert.Equal(0, BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(binOffset)));
            Assert.Equal(1, BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(binOffset + 4)));
            Assert.Equal(1, BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(binOffset + 8)));
            Assert.Equal(0, BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(binOffset + 12)));
            var materials = root.GetProperty("materials");
            Assert.False(materials[untextured.GetProperty("material").GetInt32()].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out _));
            int texIndex = materials[textured.GetProperty("material").GetInt32()].GetProperty("pbrMetallicRoughness")
                .GetProperty("baseColorTexture").GetProperty("index").GetInt32();
            int imageIndex = root.GetProperty("textures")[texIndex].GetProperty("source").GetInt32();
            Assert.Equal("maps/paint%20color.png", root.GetProperty("images")[imageIndex].GetProperty("uri").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

using NwcIO.Internal.Export;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class ObjMaterialTests
{
    [Fact]
    public void SaveObj_WritesNormalizedColorsAndMatchingMaterialNames()
    {
        var material = new NwcMaterial
        {
            Id = 7,
            Name = "Concrete Wall",
            Color = new RgbaColor(0.5f, 0.25f, 0.125f, 0.5f),
        };
        var mesh = CreateMesh(0, "Triangle", material);
        var doc = NwcDocument.Create(meshes: [mesh], materials: [material]);
        string path = TempPath(".obj");
        string mtlPath = Path.ChangeExtension(path, ".mtl");
        try
        {
            doc.SaveObj(path);

            string obj = File.ReadAllText(path);
            string mtl = File.ReadAllText(mtlPath);

            Assert.Contains($"mtllib {Path.GetFileName(mtlPath)}", obj, StringComparison.Ordinal);
            Assert.Contains("newmtl Concrete_Wall_0", mtl, StringComparison.Ordinal);
            Assert.Contains("usemtl Concrete_Wall_0", obj, StringComparison.Ordinal);
            Assert.Contains("Kd 0.500000 0.250000 0.125000", mtl, StringComparison.Ordinal);
            Assert.Contains("d 0.500000", mtl, StringComparison.Ordinal);
            Assert.DoesNotContain("Kd 0.00", mtl, StringComparison.Ordinal);
        }
        finally
        {
            Delete(path, mtlPath);
        }
    }

    [Fact]
    public void SaveObj_MaterialsWithEqualNameAndIdInDifferentModels_GetDistinctNames()
    {
        var first = new NwcMaterial { Id = 0, Name = "Default", Color = new RgbaColor(0.5f, 0.25f, 0.125f) };
        var second = new NwcMaterial { Id = 0, Name = "Default", Color = new RgbaColor(0.1f, 0.2f, 0.3f) };
        var firstDoc = NwcDocument.Create(meshes: [CreateMesh(0, "A", first)], materials: [first]);
        var secondDoc = NwcDocument.Create(meshes: [CreateMesh(0, "B", second)], materials: [second]);
        string path = TempPath(".obj");
        string mtlPath = Path.ChangeExtension(path, ".mtl");
        try
        {
            NwcAssembly.Create("Federation", firstDoc, secondDoc).SaveObj(path);

            string obj = File.ReadAllText(path);
            string mtl = File.ReadAllText(mtlPath);

            Assert.Contains("newmtl Default_0", mtl, StringComparison.Ordinal);
            Assert.Contains("newmtl Default_1", mtl, StringComparison.Ordinal);
            Assert.Contains("Kd 0.500000 0.250000 0.125000", mtl, StringComparison.Ordinal);
            Assert.Contains("Kd 0.100000 0.200000 0.300000", mtl, StringComparison.Ordinal);
            Assert.Contains("usemtl Default_0", obj, StringComparison.Ordinal);
            Assert.Contains("usemtl Default_1", obj, StringComparison.Ordinal);
        }
        finally
        {
            Delete(path, mtlPath);
        }
    }

    [Fact]
    public void SaveObj_MeshWithoutMaterial_UsesTheDefaultInsteadOfThePreviousMaterial()
    {
        var material = new NwcMaterial { Id = 0, Name = "Wall", Color = new RgbaColor(0.2f, 0.4f, 0.6f) };
        var first = CreateMesh(0, "First", material);
        var second = CreateMesh(1, "Second", null);
        var doc = NwcDocument.Create(meshes: [first, second], materials: [material]);
        string path = TempPath(".obj");
        string mtlPath = Path.ChangeExtension(path, ".mtl");
        try
        {
            doc.SaveObj(path);

            string obj = File.ReadAllText(path);
            string mtl = File.ReadAllText(mtlPath);

            Assert.Contains("usemtl Wall_0", obj, StringComparison.Ordinal);
            Assert.Contains("usemtl NwcDefault", obj, StringComparison.Ordinal);
            Assert.Contains("newmtl NwcDefault", mtl, StringComparison.Ordinal);
            Assert.Contains("Kd 1.000000 1.000000 1.000000", mtl, StringComparison.Ordinal);
        }
        finally
        {
            Delete(path, mtlPath);
        }
    }

    [Fact]
    public void WriteFile_WithoutMaterials_WritesNoLibraryOrUsemtl()
    {
        var material = new NwcMaterial { Id = 0, Name = "Wall", Color = new RgbaColor(1f, 0f, 0f) };
        string path = TempPath(".obj");
        string mtlPath = Path.ChangeExtension(path, ".mtl");
        try
        {
            ObjExporter.WriteFile([CreateMesh(0, "Triangle", material)], path, writeMaterials: false);

            string obj = File.ReadAllText(path);
            Assert.False(File.Exists(mtlPath));
            Assert.DoesNotContain("mtllib", obj, StringComparison.Ordinal);
            Assert.DoesNotContain("usemtl", obj, StringComparison.Ordinal);
        }
        finally
        {
            Delete(path, mtlPath);
        }
    }

    private static NwcMesh CreateMesh(int id, string name, NwcMaterial? material) => new()
    {
        Id = id,
        Name = name,
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Indices = [0, 1, 2],
        BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
        Material = material,
    };

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), "nwcio-obj-" + Guid.NewGuid().ToString("N") + extension);

    private static void Delete(string path, string mtlPath)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(mtlPath)) File.Delete(mtlPath);
    }
}

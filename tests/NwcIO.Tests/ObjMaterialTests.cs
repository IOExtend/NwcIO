using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class ObjMaterialTests
{
    [Fact]
    public void SaveObj_WritesSiblingMtlAndReferencesIt()
    {
        var material = new NwcMaterial
        {
            Id = 7,
            Name = "Concrete",
            Color = new RgbaColor(128, 64, 32, 255),
        };
        var mesh = new NwcMesh
        {
            Id = 0,
            Name = "Triangle",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            Material = material,
        };
        var doc = NwcDocument.Create(meshes: [mesh], materials: [material]);
        string path = Path.Combine(Path.GetTempPath(), "nwcio-obj-" + Guid.NewGuid().ToString("N") + ".obj");
        string mtlPath = Path.ChangeExtension(path, ".mtl");
        try
        {
            doc.SaveObj(path);

            string obj = File.ReadAllText(path);
            Assert.Contains("mtllib ", obj, StringComparison.Ordinal);
            Assert.Contains("usemtl ", obj, StringComparison.Ordinal);

            Assert.True(File.Exists(mtlPath));
            string mtl = File.ReadAllText(mtlPath);
            Assert.Contains("newmtl ", mtl, StringComparison.Ordinal);
            Assert.Contains("Kd ", mtl, StringComparison.Ordinal);
            Assert.Contains("d ", mtl, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(mtlPath)) File.Delete(mtlPath);
        }
    }
}

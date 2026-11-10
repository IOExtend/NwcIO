using IfcIO;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class InteroperabilityTests
{
    [Fact]
    public void NwcDocumentToIfcAndIfcIOOpenRoundtrip()
    {
        // 1. Create synthetic NwcDocument with meshes, properties, and hierarchy
        var mesh = new NwcMesh
        {
            Id = 42,
            Name = "External_Wall_01",
            Vertices =
            [
                new(0, 0, 0),
                new(5000, 0, 0),
                new(5000, 0, 3000),
                new(0, 0, 3000),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(5000, 0, 3000)),
            Material = new NwcMaterial
            {
                Id = 1,
                Name = "Concrete_Cast_In_Place",
                Color = new RgbaColor(0.8f, 0.8f, 0.8f, 1.0f),
            },
        };

        var node = new NwcNode
        {
            Id = 42,
            Name = "External_Wall_01",
            Class = "IfcWallStandardCase",
            Mesh = mesh,
        };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Class = "Pset_WallCommon", Name = "LoadBearing", Value = "TRUE" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 2, Class = "Pset_WallCommon", Name = "Reference", Value = "EW-300" });

        var root = new NwcNode { Id = 0, Name = "Project Root", Class = "LcOpModelFile" };
        root.Children.Add(node);

        var doc = NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = "InteroperabilityModel.rvt" },
            meshes: [mesh],
            materials: [mesh.Material],
            rootNode: root);

        // 2. Export NwcDocument to IFC STEP physical string
        string ifcContent = doc.ToIfcString();
        Assert.NotEmpty(ifcContent);
        Assert.Contains("ISO-10303-21;", ifcContent);
        Assert.Contains("IFCFACETEDBREP", ifcContent);

        // 3. Open exported IFC with IfcIO.IfcModel
        using var reader = new StringReader(ifcContent);
        var ifcModel = IfcModel.Open(reader, "InteroperabilityModel");

        Assert.NotNull(ifcModel);
        var elements = ifcModel.AllElements.ToList();
        Assert.NotEmpty(elements);

        var ifcWall = elements.FirstOrDefault(e => e.Name.Contains("External_Wall_01") || e.Mesh is not null);
        Assert.NotNull(ifcWall);
        Assert.NotNull(ifcWall.Mesh);
        Assert.True(ifcWall.Mesh.Vertices.Count >= 3);
        Assert.True(ifcWall.Mesh.Indices.Count >= 3);
    }
}

using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class MaterialParserTests
{
    [Theory]
    [InlineData("255, 128, 0", 1.0f, 0.50196f, 0.0f, 1.0f)]
    [InlineData("0, 255, 255", 0.0f, 1.0f, 1.0f, 1.0f)]
    [InlineData("#FF8000", 1.0f, 0.50196f, 0.0f, 1.0f)]
    [InlineData("#00FFFF80", 0.0f, 1.0f, 1.0f, 0.50196f)]
    [InlineData("0.5, 0.5, 0.5", 0.5f, 0.5f, 0.5f, 1.0f)]
    public void RgbaColor_TryParse_ParsesValidColorStrings(
        string input, float expectedR, float expectedG, float expectedB, float expectedA)
    {
        bool success = RgbaColor.TryParse(input, out var color);
        Assert.True(success);
        Assert.InRange(color.R, expectedR - 0.01f, expectedR + 0.01f);
        Assert.InRange(color.G, expectedG - 0.01f, expectedG + 0.01f);
        Assert.InRange(color.B, expectedB - 0.01f, expectedB + 0.01f);
        Assert.InRange(color.A, expectedA - 0.01f, expectedA + 0.01f);
    }

    [Fact]
    public void RgbaColor_ToHex_ReturnsCorrectHexRepresentation()
    {
        var color = new RgbaColor(1.0f, 0.5f, 0.0f, 1.0f);
        Assert.Equal("#FF8000", color.ToHex(includeAlpha: false));
        Assert.Equal("#FF8000FF", color.ToHex(includeAlpha: true));
    }

    [Fact]
    public void MaterialParser_SyntheticProperties_ExtractsMaterialsAndColors()
    {
        var records = new List<NwcPropertyRecord>
        {
            new()
            {
                FirstInt = 1,
                Tag = 52,
                Name = "Material \"ADSK_Арматура_А500\", #4452641",
                Class = "LcRevitElement",
                ValueType = 1,
                Value = "155",
            },
            new()
            {
                FirstInt = 2,
                Tag = 52,
                Name = "Color",
                Class = "revit_Color",
                ValueType = 4,
                Value = "255, 128, 0",
            },
            new()
            {
                FirstInt = 3,
                Tag = 52,
                Name = "Material \"ITR_Бетон_В25\", #2918434",
                Class = "LcRevitElement",
                ValueType = 1,
                Value = "200",
            },
        };

        var mesh = new NwcMesh
        {
            Id = 0,
            Name = "Rebar_Mesh",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var materials = MaterialParser.Parse(records, [mesh]);

        Assert.NotEmpty(materials);
        var rebarMat = materials.FirstOrDefault(m => m.Name == "ADSK_Арматура_А500");
        Assert.NotNull(rebarMat);
        Assert.Equal(4452641, rebarMat.Id);
        Assert.Equal("#FF8000", rebarMat.Color.ToHex());

        // Check mesh assignment
        Assert.NotNull(mesh.Material);
        Assert.True(mesh.Material.IsInferred);
        Assert.Equal("Default Material", mesh.Material.Name);
        Assert.DoesNotContain(materials, m => m.Name is "Default Concrete" or "Default Rebar");
    }

    [Fact]
    public void MaterialParser_NoMaterialRecords_ReturnsNoPresetsAndMarksMeshDefault()
    {
        var mesh = new NwcMesh
        {
            Id = 0,
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var materials = MaterialParser.Parse([], [mesh]);

        Assert.Empty(materials);
        Assert.NotNull(mesh.Material);
        Assert.True(mesh.Material.IsInferred);
        Assert.Equal("Default Material", mesh.Material.Name);
    }
}

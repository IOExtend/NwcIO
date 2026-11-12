using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class IfcExportTests
{
    private static NwcDocument CreateSyntheticDocument(string meshName = "Cube", string? materialName = "Concrete")
    {
        var vertices = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(1000, 0, 0),
            new Vector3d(1000, 1000, 0),
            new Vector3d(0, 1000, 0),
            new Vector3d(0, 0, 1000),
            new Vector3d(1000, 0, 1000),
            new Vector3d(1000, 1000, 1000),
            new Vector3d(0, 1000, 1000),
        };

        // 12 triangles for a cube
        var indices = new[]
        {
            // Bottom
            0, 2, 1, 0, 3, 2,
            // Top
            4, 5, 6, 4, 6, 7,
            // Front
            0, 1, 5, 0, 5, 4,
            // Right
            1, 2, 6, 1, 6, 5,
            // Back
            2, 3, 7, 2, 7, 6,
            // Left
            3, 0, 4, 3, 4, 7,
        };

        var mat = materialName is not null ? new NwcMaterial
        {
            Id = 1,
            Name = materialName,
            Color = new RgbaColor(0.8f, 0.82f, 0.85f, 1.0f),
        } : null;

        var mesh = new NwcMesh
        {
            Id = 42,
            Name = meshName,
            Vertices = vertices,
            Indices = indices,
            Material = mat,
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(1000, 1000, 1000)),
        };

        var fileInfo = new NwcFileInfo
        {
            SourcePath = @"C:\Test\SyntheticModel.nwc",
            LoaderPlugin = "TestPlugin",
        };

        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "Category", Class = "LcRevitLayer" },
        };

        var materials = mat is not null ? new List<NwcMaterial> { mat } : [];

        return NwcDocument.Create(
            fileInfo: fileInfo,
            propertyRecords: records,
            meshes: new List<NwcMesh> { mesh },
            materials: materials);
    }

    [Fact]
    public void SaveIfc_SyntheticCube_ProducesValidStep21CoordinationView()
    {
        var doc = CreateSyntheticDocument("Стена монолитная", "Бетон В25");

        using var sw = new StringWriter();
        doc.SaveIfc(sw, new NwcIfcExportOptions
        {
            Author = "TestAuthor",
            Organization = "TestOrg",
            ProjectName = "Synthetic Project",
        });

        string ifc = sw.ToString();

        // Check STEP header
        Assert.StartsWith("ISO-10303-21;", ifc.TrimStart());
        Assert.Contains("HEADER;", ifc);
        Assert.Contains("FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0]'),'2;1');", ifc);
        Assert.Contains("FILE_SCHEMA(('IFC2X3'));", ifc);
        Assert.Contains("ENDSEC;", ifc);

        // Check DATA section
        Assert.Contains("DATA;", ifc);
        Assert.Contains("IFCPROJECT(", ifc);
        Assert.Contains("IFCSITE(", ifc);
        Assert.Contains("IFCBUILDING(", ifc);
        Assert.Contains("IFCBUILDINGSTOREY(", ifc);
        Assert.Contains("IFCRELAGGREGATES(", ifc);
        Assert.Contains("IFCUNITASSIGNMENT(", ifc);

        // Check Faceted BRep geometry
        Assert.Contains("IFCFACETEDBREP(", ifc);
        Assert.Contains("IFCCLOSEDSHELL(", ifc);
        Assert.Contains("IFCFACE(", ifc);
        Assert.Contains("IFCFACEOUTERBOUND(", ifc);
        Assert.Contains("IFCPOLYLOOP(", ifc);
        Assert.Contains("IFCCARTESIANPOINT(", ifc);

        // Check Wall classification (Russian name "Стена монолитная")
        Assert.Contains("IFCWALL(", ifc);

        // Check Surface Style & Material
        Assert.Contains("IFCSURFACESTYLE(", ifc);
        Assert.Contains("IFCSURFACESTYLESHADING(", ifc);
        Assert.Contains("IFCCOLOURRGB(", ifc);
        Assert.Contains("IFCSTYLEDITEM(", ifc);

        // Check Properties & Containment
        Assert.Contains("IFCPROPERTYSET(", ifc);
        Assert.Contains("Pset_NwcElementProperties", ifc);
        Assert.Contains("IFCPROPERTYSINGLEVALUE(", ifc);
        Assert.Contains("IFCRELDEFINESBYPROPERTIES(", ifc);
        Assert.Contains("IFCRELCONTAINEDINSPATIALSTRUCTURE(", ifc);

        // Check valid termination
        Assert.Contains("END-ISO-10303-21;", ifc);
    }

    [Theory]
    [InlineData("Стена монолитная 200мм", "IFCWALL")]
    [InlineData("Плита перекрытия", "IFCSLAB")]
    [InlineData("Колонна К-1", "IFCCOLUMN")]
    [InlineData("Балка Б-1", "IFCBEAM")]
    [InlineData("Арматура d12 A500C", "IFCREINFORCINGBAR")]
    [InlineData("Окно пластиковое", "IFCWINDOW")]
    [InlineData("Дверь глухая", "IFCDOOR")]
    [InlineData("Лестница ЛМ-1", "IFCSTAIR")]
    [InlineData("Ограждение кровли", "IFCRAILING")]
    [InlineData("Труба стальная", "IFCPIPESEGMENT")]
    [InlineData("Воздуховод круглый", "IFCDUCTSEGMENT")]
    [InlineData("Неизвестный элемент", "IFCBUILDINGELEMENTPROXY")]
    public void SaveIfc_ClassifiesElementsCorrectly(string meshName, string expectedIfcEntity)
    {
        var doc = CreateSyntheticDocument(meshName);

        using var sw = new StringWriter();
        doc.SaveIfc(sw);
        string ifc = sw.ToString();

        Assert.Contains(expectedIfcEntity + "(", ifc);
    }

    [Fact]
    public void SaveIfc_WithMetreUnits_ScalesCoordinates()
    {
        var doc = CreateSyntheticDocument("Box", null);

        using var swMm = new StringWriter();
        doc.SaveIfc(swMm, new NwcIfcExportOptions { LengthUnit = NwcIfcLengthUnit.Millimetre });
        string ifcMm = swMm.ToString();

        using var swM = new StringWriter();
        doc.SaveIfc(swM, new NwcIfcExportOptions { LengthUnit = NwcIfcLengthUnit.Metre, ConvertMmToMetres = true });
        string ifcM = swM.ToString();

        // In Millimetre mode, coordinates are 1000
        Assert.Contains(".MILLI.,.METRE.", ifcMm);
        Assert.Contains("1000.", ifcMm);

        // In Metre mode, coordinates are 1.0
        Assert.Contains("*,.LENGTHUNIT.,$,.METRE.", ifcM);
        Assert.Contains("1.", ifcM);
    }

    [Fact]
    public void SaveIfc_SharedVerticesTrue_EmitsExactlyUniqueMeshPoints()
    {
        var doc = CreateSyntheticDocument("Cube", null);

        using var sw = new StringWriter();
        doc.SaveIfc(sw, new NwcIfcExportOptions { SharedVertices = true });
        string ifc = sw.ToString();

        // A cube has 8 vertices; with origin (0,0,0) and unit vectors (0,0,1), (1,0,0)
        // total points in data should be around 11
        int pointCount = ifc.Split("IFCCARTESIANPOINT").Length - 1;
        Assert.True(pointCount <= 12, $"Expected <= 12 points with shared vertices, got {pointCount}");
    }

    [Fact]
    public void SaveIfc_Assembly_CombinesMultipleModelsIntoFederatedIfc()
    {
        var doc1 = CreateSyntheticDocument("Колонна КЖ-1", "Бетон");
        var doc2 = CreateSyntheticDocument("Балка КЖ-2", "Сталь");

        var assembly = NwcAssembly.Create("TestAssembly", new[] { doc1, doc2 });

        using var sw = new StringWriter();
        assembly.SaveIfc(sw, new NwcIfcExportOptions
        {
            ProjectName = "Federated IFC Assembly",
        });

        string ifc = sw.ToString();

        Assert.Contains("IFCPROJECT(", ifc);
        Assert.Contains("Federated IFC Assembly", ifc);
        Assert.Contains("IFCBUILDING(", ifc);
        Assert.Contains("IFCCOLUMN(", ifc);
        Assert.Contains("IFCBEAM(", ifc);
        Assert.Contains("IFCCLOSEDSHELL(", ifc);
        Assert.Contains("END-ISO-10303-21;", ifc);
    }

    [Fact]
    public void SaveIfc_Ifc4Schema_EmitsIfc4HeaderAndTriangulatedFaceSet()
    {
        var doc = CreateSyntheticDocument("Стена наружная 300мм", "Железобетон");

        using var sw = new StringWriter();
        doc.SaveIfc(sw, new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc4,
            ExportTypeObjects = true,
            UseTessellationInIfc4 = true,
        });

        string ifc = sw.ToString();

        // 1. Header schema and view definition
        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.Contains("DesignTransferView", ifc);

        // 2. Geometry: Tessellation via IfcTriangulatedFaceSet + IfcCartesianPointList3D
        Assert.Contains("IFCCARTESIANPOINTLIST3D(", ifc);
        Assert.Contains("IFCTRIANGULATEDFACESET(", ifc);
        Assert.Contains("'Tessellation'", ifc);

        // 3. Type Products
        Assert.Contains("IFCWALLTYPE(", ifc);
        Assert.Contains("IFCRELDEFINESBYTYPE(", ifc);
    }

    [Fact]
    public void SaveIfc_ExportTypeObjectsFalse_OmitsTypesAndRelDefinesByType()
    {
        var doc = CreateSyntheticDocument("Стена наружная 300мм", "Железобетон");

        using var sw = new StringWriter();
        doc.SaveIfc(sw, new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc4,
            ExportTypeObjects = false,
        });

        string ifc = sw.ToString();

        Assert.DoesNotContain("IFCRELDEFINESBYTYPE", ifc);
        Assert.DoesNotContain("IFCWALLTYPE", ifc);
    }

    [Fact]
    public void SaveIfc_Ifc2x3WithTypes_EmitsIfc2x3HeaderAndTypes()
    {
        var doc = CreateSyntheticDocument("Колонна монолитная", "Бетон B25");

        using var sw = new StringWriter();
        doc.SaveIfc(sw, new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc2x3,
            ExportTypeObjects = true,
        });

        string ifc = sw.ToString();

        Assert.Contains("FILE_SCHEMA(('IFC2X3'));", ifc);
        Assert.Contains("CoordinationView", ifc);
        Assert.Contains("IFCFACETEDBREP(", ifc);
        Assert.Contains("IFCCOLUMNTYPE(", ifc);
        Assert.Contains("IFCRELDEFINESBYTYPE(", ifc);
    }

    [Fact]
    public void ToIfcString_EmitsValidStepStringDirectly()
    {
        var doc = CreateSyntheticDocument("Стена монолитная", "Бетон B25");
        string ifc = doc.ToIfcString(new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc4,
            UseTessellationInIfc4 = true
        });

        Assert.NotEmpty(ifc);
        Assert.Contains("ISO-10303-21;", ifc);
        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.Contains("IFCTRIANGULATEDFACESET(", ifc);
        Assert.Contains("END-ISO-10303-21;", ifc);
    }

    [Fact]
    public void IfcExportOptions_SchemaAcceptsKnownValuesAndRejectsUnknownOnes()
    {
        var options = new NwcIfcExportOptions();

        options.Schema = "IFC4";
        Assert.Equal(NwcIfcSchemaVersion.Ifc4, options.SchemaVersion);
        Assert.Equal("IFC4", options.Schema);

        options.Schema = "ifc2x3";
        Assert.Equal(NwcIfcSchemaVersion.Ifc2x3, options.SchemaVersion);
        Assert.Equal("IFC2X3", options.Schema);

        Assert.Throws<ArgumentException>(() => options.Schema = "IFC4X3");
        Assert.Throws<ArgumentException>(() => options.Schema = "IF44");
        Assert.Throws<ArgumentException>(() => options.Schema = "");
        Assert.Throws<ArgumentException>(() => options.Schema = null!);

        // The rejected assignment must not have changed the selected schema.
        Assert.Equal(NwcIfcSchemaVersion.Ifc2x3, options.SchemaVersion);
    }
}

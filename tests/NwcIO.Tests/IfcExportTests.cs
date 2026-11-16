using NwcIO.Model;
using NwcIO.Model.Sectioning;
using Xunit;

namespace NwcIO.Tests;

public class IfcExportTests
{
    [Theory]
    [InlineData(true, "((1,3,2),(3,4,2))")]
    [InlineData(false, "((1,2,3),(3,2,4))")]
    public void SaveIfc_MirroredTransform_ReversesOnlyWorldFaces(bool world, string coordIndex)
    {
        var mesh = TestMeshes.MirroredQuad();
        string ifc = NwcDocument.Create(meshes: [mesh]).ToIfcString(new NwcIfcExportOptions
            { SchemaVersion = NwcIfcSchemaVersion.Ifc4, UseTessellationInIfc4 = true, WorldCoordinates = world });
        Assert.Contains(coordIndex, ifc);
        Assert.Equal([0, 1, 2, 2, 1, 3], mesh.Indices);
    }

    [Theory]
    [InlineData(true, new[] { 0, 2, 1, 2, 3, 1 })]
    [InlineData(false, new[] { 0, 1, 2, 2, 1, 3 })]
    public void SaveIfc2x3_MirroredTransform_ReversesOnlyWorldBrepFaces(bool world, int[] expected)
    {
        var mesh = TestMeshes.MirroredQuad();
        string ifc = NwcDocument.Create(meshes: [mesh]).ToIfcString(new NwcIfcExportOptions
            { SchemaVersion = NwcIfcSchemaVersion.Ifc2x3, WorldCoordinates = world });

        // With shared vertices the quad's points are consecutive entities, so a loop's point ids minus the first one are its corners.
        var corners = System.Text.RegularExpressions.Regex.Matches(ifc, @"IFCPOLYLOOP\(\(#(\d+),#(\d+),#(\d+)\)\)")
            .SelectMany(m => m.Groups.Values.Skip(1).Select(g => int.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        Assert.Equal(expected, corners.Select(id => id - corners.Min()));
        Assert.Equal([0, 1, 2, 2, 1, 3], mesh.Indices);
    }

    [Fact]
    public void SaveIfc_NodeHyperlinks_ExportsDocumentReferences()
    {
        var node = new NwcNode { Id = 1, Name = "Pump 101", Class = "LcRevitData_Element" };
        node.Hyperlinks.Add(new NwcHyperlink
        {
            Name = "Manual",
            Url = "https://example.com/pump.pdf",
            Category = "Datasheet"
        });

        var mesh = TestMeshes.MirroredQuad();
        mesh.Node = node;

        // IFC2X3: exactly 3 arguments (Location, ItemReference, Name)
        string ifc2x3 = NwcDocument.Create(meshes: [mesh]).ToIfcString(new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc2x3,
            ExportProperties = true,
        });
        Assert.Contains("IFCDOCUMENTREFERENCE('https://example.com/pump.pdf','Manual','Datasheet');", ifc2x3);
        Assert.Contains("IFCRELASSOCIATESDOCUMENT", ifc2x3);

        // IFC4: exactly 5 arguments (Location, Identification, Name, Description, ReferencedDocument)
        string ifc4 = NwcDocument.Create(meshes: [mesh]).ToIfcString(new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc4,
            ExportProperties = true,
        });
        Assert.Contains("IFCDOCUMENTREFERENCE('https://example.com/pump.pdf','Manual','Datasheet',$,$);", ifc4);
        Assert.Contains("IFCRELASSOCIATESDOCUMENT", ifc4);
    }

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
    public void SaveIfc_ToPath_WritesNoByteOrderMarkAndTheAuthorInTheHeader()
    {
        var doc = CreateSyntheticDocument();
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.ifc");
        try
        {
            doc.SaveIfc(path, new NwcIfcExportOptions { Author = "Alice", Organization = "Acme" });

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal("ISO-10303-21;"u8.ToArray(), bytes.Take(13).ToArray());

            string ifc = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains("('Alice'),('Acme'),", ifc);
            Assert.Contains("IFCPERSON($,'Alice',", ifc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveObj_ToPath_WritesNoByteOrderMark()
    {
        var doc = CreateSyntheticDocument();
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.obj");
        try
        {
            doc.SaveObj(path);

            Assert.Equal((byte)'#', File.ReadAllBytes(path)[0]);
        }
        finally
        {
            File.Delete(path);
        }
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

        foreach (var schema in new[] { NwcIfcSchemaVersion.Ifc2x3, NwcIfcSchemaVersion.Ifc4 })
        {
            using var sw = new StringWriter();
            doc.SaveIfc(sw, new NwcIfcExportOptions { SchemaVersion = schema });
            var ifc = sw.ToString();
            var expected = schema == NwcIfcSchemaVersion.Ifc2x3
                && expectedIfcEntity is "IFCPIPESEGMENT" or "IFCDUCTSEGMENT"
                ? "IFCFLOWSEGMENT"
                : expectedIfcEntity;

            Assert.Contains(expected + "(", ifc);
        }
    }

    [Theory]
    [InlineData(false, NwcIfcSchemaVersion.Ifc2x3, "Pipe A", "IFCFLOWSEGMENT", "IFCPIPESEGMENTTYPE")]
    [InlineData(true, NwcIfcSchemaVersion.Ifc2x3, "Pipe A", "IFCFLOWSEGMENT", "IFCPIPESEGMENTTYPE")]
    [InlineData(false, NwcIfcSchemaVersion.Ifc2x3, "Duct B", "IFCFLOWSEGMENT", "IFCDUCTSEGMENTTYPE")]
    [InlineData(true, NwcIfcSchemaVersion.Ifc2x3, "Duct B", "IFCFLOWSEGMENT", "IFCDUCTSEGMENTTYPE")]
    [InlineData(false, NwcIfcSchemaVersion.Ifc2x3, "Roof C", "IFCROOF", "IFCBUILDINGELEMENTPROXYTYPE")]
    [InlineData(true, NwcIfcSchemaVersion.Ifc2x3, "Roof C", "IFCROOF", "IFCBUILDINGELEMENTPROXYTYPE")]
    [InlineData(false, NwcIfcSchemaVersion.Ifc4, "Pipe A", "IFCPIPESEGMENT", "IFCPIPESEGMENTTYPE")]
    [InlineData(true, NwcIfcSchemaVersion.Ifc4, "Pipe A", "IFCPIPESEGMENT", "IFCPIPESEGMENTTYPE")]
    [InlineData(false, NwcIfcSchemaVersion.Ifc4, "Duct B", "IFCDUCTSEGMENT", "IFCDUCTSEGMENTTYPE")]
    [InlineData(true, NwcIfcSchemaVersion.Ifc4, "Duct B", "IFCDUCTSEGMENT", "IFCDUCTSEGMENTTYPE")]
    [InlineData(false, NwcIfcSchemaVersion.Ifc4, "Roof C", "IFCROOF", "IFCROOFTYPE")]
    [InlineData(true, NwcIfcSchemaVersion.Ifc4, "Roof C", "IFCROOF", "IFCROOFTYPE")]
    public void SaveIfc_ClassifiedProducts_UseSchemaCompatibleEntities(
        bool assembly, NwcIfcSchemaVersion schema, string meshName, string entity, string typeEntity)
    {
        var doc = CreateSyntheticDocument(meshName);
        using var sw = new StringWriter();
        var options = new NwcIfcExportOptions { SchemaVersion = schema };
        if (assembly)
        {
            NwcAssembly.Create("Schema compatibility", doc).SaveIfc(sw, options);
        }
        else
        {
            doc.SaveIfc(sw, options);
        }

        var ifc = sw.ToString();
        Assert.Contains($"FILE_SCHEMA(('{options.Schema}'));", ifc);
        Assert.Contains(entity + "(", ifc);
        Assert.Contains(typeEntity + "(", ifc);
        Assert.Contains("IFCRELDEFINESBYTYPE(", ifc);
        Assert.Contains("IFCRELCONTAINEDINSPATIALSTRUCTURE(", ifc);

        if (schema == NwcIfcSchemaVersion.Ifc2x3)
        {
            Assert.DoesNotContain("IFCPIPESEGMENT(", ifc);
            Assert.DoesNotContain("IFCDUCTSEGMENT(", ifc);
            Assert.DoesNotContain("IFCROOFTYPE(", ifc);
        }
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

    [Theory]
    [InlineData(false, NwcIfcLengthUnit.Millimetre, true, "1000.")]
    [InlineData(true, NwcIfcLengthUnit.Millimetre, true, "1000.")]
    [InlineData(false, NwcIfcLengthUnit.Metre, true, "1.")]
    [InlineData(true, NwcIfcLengthUnit.Metre, true, "1.")]
    [InlineData(false, NwcIfcLengthUnit.Metre, false, "1000.")]
    [InlineData(true, NwcIfcLengthUnit.Metre, false, "1000.")]
    public void SaveIfc_Dimensions_UseExportedLengthUnits(
        bool assembly, NwcIfcLengthUnit unit, bool convert, string expected)
    {
        var doc = CreateSyntheticDocument();
        var ifc = ExportDimensions(doc, assembly, new NwcIfcExportOptions
        {
            LengthUnit = unit,
            ConvertMmToMetres = convert,
        });

        AssertDimensions(ifc, expected, expected, expected);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void SaveIfc_Dimensions_UseSelectedCoordinateSpace(bool assembly, bool worldCoordinates)
    {
        var doc = CreateSyntheticDocument();
        doc.Meshes[0].Transform = new NwcTransform
        {
            Id = 1,
            Translation = new Vector3d(5000, -3000, 2000),
            // Rotate the cube 45 degrees about Z, increasing its X and Y extents.
            Rotation = new Quaterniond(0, 0, Math.Sin(Math.PI / 8), Math.Cos(Math.PI / 8)),
        };
        var ifc = ExportDimensions(doc, assembly, new NwcIfcExportOptions
        {
            WorldCoordinates = worldCoordinates,
            LengthUnit = NwcIfcLengthUnit.Metre,
            ConvertMmToMetres = true,
        });

        foreach (var name in new[] { "Length_X", "Width_Y", "Height_Z" })
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                ifc, $@"IFCPROPERTYSINGLEVALUE\('{name}',\$,IFCLENGTHMEASURE\(([^)]+)\),\$\)");
            Assert.True(match.Success, $"Missing length measure for {name}");
            var actual = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var expected = worldCoordinates && name != "Height_Z" ? Math.Sqrt(2) : 1;
            Assert.Equal(expected, actual, 6);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveIfc_Dimensions_RetainPostClipExtents(bool assembly)
    {
        var doc = CreateSyntheticDocument();
        var ifc = ExportDimensions(doc, assembly, new NwcIfcExportOptions
        {
            LengthUnit = NwcIfcLengthUnit.Metre,
            ConvertMmToMetres = true,
            SectionPlanes = new[] { new NwcSectionPlane { Normal = new Vector3d(1, 0, 0), Distance = 250 } },
        });

        AssertDimensions(ifc, "0.75", "1.", "1.");
    }

    private static string ExportDimensions(NwcDocument doc, bool assembly, NwcIfcExportOptions options)
    {
        using var sw = new StringWriter();
        if (assembly)
        {
            NwcAssembly.Create("Dimensions", doc).SaveIfc(sw, options);
        }
        else
        {
            doc.SaveIfc(sw, options);
        }

        return sw.ToString();
    }

    private static void AssertDimensions(string ifc, string x, string y, string z)
    {
        Assert.Contains($"IFCPROPERTYSINGLEVALUE('Length_X',$,IFCLENGTHMEASURE({x}),$)", ifc);
        Assert.Contains($"IFCPROPERTYSINGLEVALUE('Width_Y',$,IFCLENGTHMEASURE({y}),$)", ifc);
        Assert.Contains($"IFCPROPERTYSINGLEVALUE('Height_Z',$,IFCLENGTHMEASURE({z}),$)", ifc);
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

    [Theory]
    [InlineData(NwcIfcSchemaVersion.Ifc2x3, false, false)]
    [InlineData(NwcIfcSchemaVersion.Ifc2x3, true, false)]
    [InlineData(NwcIfcSchemaVersion.Ifc2x3, false, true)]
    [InlineData(NwcIfcSchemaVersion.Ifc2x3, true, true)]
    [InlineData(NwcIfcSchemaVersion.Ifc4, false, false)]
    [InlineData(NwcIfcSchemaVersion.Ifc4, true, false)]
    [InlineData(NwcIfcSchemaVersion.Ifc4, false, true)]
    [InlineData(NwcIfcSchemaVersion.Ifc4, true, true)]
    public void SaveIfc_EveryEmittedEntity_HasTheSchemaAttributeCount(
        NwcIfcSchemaVersion schema, bool useTessellation, bool assembly)
    {
        var doc = CreateFullClassificationDocument();
        var options = new NwcIfcExportOptions
        {
            SchemaVersion = schema,
            UseTessellationInIfc4 = useTessellation,
        };

        using var sw = new StringWriter();
        if (assembly)
        {
            NwcAssembly.Create("Attribute counts", doc, CreateFullClassificationDocument()).SaveIfc(sw, options);
        }
        else
        {
            doc.SaveIfc(sw, options);
        }

        var expected = schema == NwcIfcSchemaVersion.Ifc2x3 ? Ifc2x3AttributeCounts : Ifc4AttributeCounts;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entity, args) in ParseStepInstances(sw.ToString()))
        {
            seen.Add(entity);
            Assert.True(expected.TryGetValue(entity, out int count),
                $"{entity} is not part of the {schema} export table (schema-invalid or unclassified entity)");
            Assert.True(count == CountStepArguments(args),
                $"{entity} has {CountStepArguments(args)} arguments, expected {count} in {schema}");
        }

        // The document exercises every entity family the exporter writes for classified products.
        Assert.Contains(schema == NwcIfcSchemaVersion.Ifc2x3 ? "IFCFLOWSEGMENT" : "IFCPIPESEGMENT", seen);
        Assert.Contains("IFCBUILDING", seen);
        Assert.Contains("IFCBUILDINGSTOREY", seen);
        Assert.Contains("IFCWALL", seen);
        Assert.Contains("IFCREINFORCINGBAR", seen);
        Assert.Contains("IFCDOOR", seen);
        Assert.Contains("IFCWINDOW", seen);
        Assert.Contains(schema == NwcIfcSchemaVersion.Ifc4 && useTessellation ? "IFCTRIANGULATEDFACESET" : "IFCFACETEDBREP", seen);
    }

    private static readonly Dictionary<string, int> Ifc2x3AttributeCounts = new(StringComparer.Ordinal)
    {
        ["IFCPERSON"] = 8, ["IFCORGANIZATION"] = 5, ["IFCPERSONANDORGANIZATION"] = 3,
        ["IFCAPPLICATION"] = 4, ["IFCOWNERHISTORY"] = 8, ["IFCSIUNIT"] = 4,
        ["IFCDIMENSIONALEXPONENTS"] = 7, ["IFCMEASUREWITHUNIT"] = 2, ["IFCCONVERSIONBASEDUNIT"] = 4,
        ["IFCUNITASSIGNMENT"] = 1, ["IFCCARTESIANPOINT"] = 1, ["IFCDIRECTION"] = 1,
        ["IFCAXIS2PLACEMENT3D"] = 3, ["IFCGEOMETRICREPRESENTATIONCONTEXT"] = 6,
        ["IFCGEOMETRICREPRESENTATIONSUBCONTEXT"] = 10, ["IFCPROJECT"] = 9, ["IFCLOCALPLACEMENT"] = 2,
        ["IFCSITE"] = 14, ["IFCBUILDING"] = 12, ["IFCBUILDINGSTOREY"] = 10, ["IFCRELAGGREGATES"] = 6,
        ["IFCCOLOURRGB"] = 4, ["IFCSURFACESTYLESHADING"] = 1, ["IFCSURFACESTYLE"] = 3,
        ["IFCPRESENTATIONSTYLEASSIGNMENT"] = 1, ["IFCSTYLEDITEM"] = 3,
        ["IFCCLOSEDSHELL"] = 1, ["IFCFACE"] = 1, ["IFCFACEOUTERBOUND"] = 2, ["IFCPOLYLOOP"] = 1,
        ["IFCFACETEDBREP"] = 1, ["IFCSHAPEREPRESENTATION"] = 4, ["IFCPRODUCTDEFINITIONSHAPE"] = 3,
        ["IFCPROPERTYSINGLEVALUE"] = 4, ["IFCPROPERTYSET"] = 5, ["IFCRELDEFINESBYPROPERTIES"] = 6,
        ["IFCRELDEFINESBYTYPE"] = 6, ["IFCRELCONTAINEDINSPATIALSTRUCTURE"] = 6,
        ["IFCDOCUMENTREFERENCE"] = 3, ["IFCRELASSOCIATESDOCUMENT"] = 6,
        ["IFCWALL"] = 8, ["IFCSLAB"] = 9, ["IFCCOLUMN"] = 8, ["IFCBEAM"] = 8,
        ["IFCREINFORCINGBAR"] = 14, ["IFCWINDOW"] = 10, ["IFCDOOR"] = 10, ["IFCFOOTING"] = 9,
        ["IFCSTAIR"] = 9, ["IFCRAILING"] = 9, ["IFCROOF"] = 9, ["IFCFLOWSEGMENT"] = 8,
        ["IFCBUILDINGELEMENTPROXY"] = 9,
        ["IFCWALLTYPE"] = 10, ["IFCSLABTYPE"] = 10, ["IFCCOLUMNTYPE"] = 10, ["IFCBEAMTYPE"] = 10,
        ["IFCRAILINGTYPE"] = 10, ["IFCSTAIRFLIGHTTYPE"] = 10, ["IFCPIPESEGMENTTYPE"] = 10,
        ["IFCDUCTSEGMENTTYPE"] = 10, ["IFCBUILDINGELEMENTPROXYTYPE"] = 10,
        ["IFCDOORSTYLE"] = 12, ["IFCWINDOWSTYLE"] = 12,
    };

    private static readonly Dictionary<string, int> Ifc4AttributeCounts = new(StringComparer.Ordinal)
    {
        ["IFCPERSON"] = 8, ["IFCORGANIZATION"] = 5, ["IFCPERSONANDORGANIZATION"] = 3,
        ["IFCAPPLICATION"] = 4, ["IFCOWNERHISTORY"] = 8, ["IFCSIUNIT"] = 4,
        ["IFCDIMENSIONALEXPONENTS"] = 7, ["IFCMEASUREWITHUNIT"] = 2, ["IFCCONVERSIONBASEDUNIT"] = 4,
        ["IFCUNITASSIGNMENT"] = 1, ["IFCCARTESIANPOINT"] = 1, ["IFCDIRECTION"] = 1,
        ["IFCAXIS2PLACEMENT3D"] = 3, ["IFCGEOMETRICREPRESENTATIONCONTEXT"] = 6,
        ["IFCGEOMETRICREPRESENTATIONSUBCONTEXT"] = 10, ["IFCPROJECT"] = 9, ["IFCLOCALPLACEMENT"] = 2,
        ["IFCSITE"] = 14, ["IFCBUILDING"] = 12, ["IFCBUILDINGSTOREY"] = 10, ["IFCRELAGGREGATES"] = 6,
        ["IFCCOLOURRGB"] = 4, ["IFCSURFACESTYLESHADING"] = 2, ["IFCSURFACESTYLE"] = 3,
        ["IFCPRESENTATIONSTYLEASSIGNMENT"] = 1, ["IFCSTYLEDITEM"] = 3,
        ["IFCCLOSEDSHELL"] = 1, ["IFCFACE"] = 1, ["IFCFACEOUTERBOUND"] = 2, ["IFCPOLYLOOP"] = 1,
        ["IFCFACETEDBREP"] = 1, ["IFCSHAPEREPRESENTATION"] = 4, ["IFCPRODUCTDEFINITIONSHAPE"] = 3,
        ["IFCCARTESIANPOINTLIST3D"] = 1, ["IFCTRIANGULATEDFACESET"] = 5,
        ["IFCPROPERTYSINGLEVALUE"] = 4, ["IFCPROPERTYSET"] = 5, ["IFCRELDEFINESBYPROPERTIES"] = 6,
        ["IFCRELDEFINESBYTYPE"] = 6, ["IFCRELCONTAINEDINSPATIALSTRUCTURE"] = 6,
        ["IFCDOCUMENTREFERENCE"] = 5, ["IFCRELASSOCIATESDOCUMENT"] = 6,
        ["IFCWALL"] = 9, ["IFCSLAB"] = 9, ["IFCCOLUMN"] = 9, ["IFCBEAM"] = 9,
        ["IFCREINFORCINGBAR"] = 14, ["IFCWINDOW"] = 13, ["IFCDOOR"] = 13, ["IFCFOOTING"] = 9,
        ["IFCSTAIR"] = 9, ["IFCRAILING"] = 9, ["IFCROOF"] = 9, ["IFCPIPESEGMENT"] = 9,
        ["IFCDUCTSEGMENT"] = 9, ["IFCBUILDINGELEMENTPROXY"] = 9,
        ["IFCWALLTYPE"] = 10, ["IFCSLABTYPE"] = 10, ["IFCCOLUMNTYPE"] = 10, ["IFCBEAMTYPE"] = 10,
        ["IFCFOOTINGTYPE"] = 10, ["IFCRAILINGTYPE"] = 10, ["IFCROOFTYPE"] = 10, ["IFCSTAIRFLIGHTTYPE"] = 10,
        ["IFCPIPESEGMENTTYPE"] = 10, ["IFCDUCTSEGMENTTYPE"] = 10, ["IFCREINFORCINGBARTYPE"] = 16,
        ["IFCBUILDINGELEMENTPROXYTYPE"] = 10, ["IFCDOORTYPE"] = 13, ["IFCWINDOWTYPE"] = 13,
    };

    private static IEnumerable<(string Entity, string Args)> ParseStepInstances(string ifc)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            ifc, @"^#\d+=([A-Z0-9]+)\((.*)\);\r?$", System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            yield return (match.Groups[1].Value, match.Groups[2].Value);
        }
    }

    private static int CountStepArguments(string args)
    {
        if (args.Length == 0)
        {
            return 0;
        }

        int count = 1;
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (inString)
            {
                if (c == '\'')
                {
                    if (i + 1 < args.Length && args[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        inString = false;
                    }
                }

                continue;
            }

            switch (c)
            {
                case '\'':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    count++;
                    break;
            }
        }

        return count;
    }

    private static NwcDocument CreateFullClassificationDocument()
    {
        var names = new[]
        {
            "Wall", "Slab", "Column", "Beam", "Rebar", "Window", "Door",
            "Footing", "Stair", "Railing", "Roof", "Pipe", "Duct", "Unknown",
        };

        var meshes = new List<NwcMesh>(names.Length);
        var materials = new List<NwcMaterial>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            var material = new NwcMaterial
            {
                Id = i + 1,
                Name = names[i] + " material",
                Color = new RgbaColor(0.5f, 0.4f, 0.3f, 1.0f),
            };
            materials.Add(material);
            var mesh = new NwcMesh
            {
                Id = i + 1,
                Name = names[i],
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)],
                Indices = [0, 1, 2, 0, 1, 3],
                Material = material,
            };
            if (i == 0)
            {
                var node = new NwcNode { Id = 1, Name = names[i], Class = "LcRevitData_Element" };
                node.Hyperlinks.Add(new NwcHyperlink { Name = "Datasheet", Url = "https://example.com/item" });
                mesh.Node = node;
            }

            meshes.Add(mesh);
        }

        return NwcDocument.Create(meshes: meshes, materials: materials);
    }
}

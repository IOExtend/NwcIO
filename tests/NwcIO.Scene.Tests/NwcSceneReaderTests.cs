using IOScene;
using NwcIO;
using NwcIO.Model;
using NwcIO.Scene;
using NwcIO.Tests;
using Xunit;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;
using BoundingBox3d = IOScene.BoundingBox3d;

namespace NwcIO.Scene.Tests;

public sealed class NwcSceneReaderTests
{
    [Fact]
    public void Read_File_DescribesTheModel()
    {
        using var file = TempFile(RevitSample.Build(), "Tower.nwc");
        var result = Valid(NwcSceneReader.Read(file.Path));
        var model = Assert.Single(result.Document.Models);
        Assert.Equal(("Tower.nwc", "Tower.nwc", file.Path, "nwc", "ft", "lcldrevit", "2026-01-01T00:00:00Z"),
            (model.Key, model.Name, model.Path, model.SourceFormat, model.SourceLengthUnit, model.Application, model.CreatedAt));
        Assert.Matches(@"^NwcIO\.Scene \d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", model.Producer);
        Assert.Equal((NwcLengthUnit.Feet, 0.3048), (result.LengthUnit, result.MetresPerUnit));
    }

    [Fact]
    public void Read_ModelKeyOption_ReplacesTheFileName()
    {
        using var file = TempFile(RevitSample.Build(), "Tower.nwc");
        Assert.Equal("site/tower", Valid(NwcSceneReader.Read(file.Path, new() { ModelKey = "site/tower" })).Document.Models[0].Key);
    }

    [Fact]
    public void Read_EmptyModelKey_Throws()
    {
        using var file = TempFile(RevitSample.Build(), "Tower.nwc");
        Assert.Throws<ArgumentException>(() => NwcSceneReader.Read(file.Path, new() { ModelKey = "" }));
        Assert.Throws<ArgumentException>(() => NwcSceneReader.Read(Doc(RevitSample.Build()), new NwcSceneOptions()));
    }

    [Fact]
    public void Read_DocumentWithoutTree_Throws() =>
        Assert.Contains("BuildSceneTree", Assert.Throws<ArgumentException>(() =>
            NwcSceneReader.Read(Doc(RevitSample.Build(), new NwcReaderOptions { BuildSceneTree = false }), new() { ModelKey = "k" })).Message);

    [Fact]
    public void Read_PartitionTree_ThrowsNotSupportedWithTheAdapterReason()
    {
        // [k=1][0][root][n=1], a class-32 top-level node: [i32 0] + a class-53 body + 16 zero bytes (as HierarchyParserTests.PartitionHierarchy)
        var w = new LcGraphWriter().Int(1).Int(0);
        w.ImplicitRoot(); w.Int(1); w.New(32); w.Int(0).Str("P"); w.New(52); w.Str("P").Str("LcOaPartition");
        w.Int(0).Int(0).Int(0).Int(0).Int(0).Int(0).Int(0).Int(0).Int(1);
        using var file = TempFile(SyntheticNwcFile.Build(w.ToArray(), null, [], []), "Site.nwd");
        var ex = Assert.Throws<NotSupportedException>(() => NwcSceneReader.Read(file.Path));
        Assert.StartsWith("NwcIO.Scene needs the scene tree: .nwd partition trees are not supported yet. ", ex.Message);
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Theory]
    [InlineData("LcNwcLoaderPlugin:lcldrevit", null, NwcLengthUnit.Feet, "ft")]
    [InlineData("LcNwcLoaderPlugin:mock", null, NwcLengthUnit.Unknown, null)]
    [InlineData("LcNwcLoaderPlugin:mock", NwcLengthUnit.Feet, NwcLengthUnit.Feet, "ft")]
    public void Read_Unit_ComesFromTheDocumentOrTheOption(string loader, NwcLengthUnit? option, NwcLengthUnit unit, string? symbol)
    {
        var result = Scene(RevitSample.Build(loader), new() { ModelKey = "k", LengthUnit = option });
        Assert.Equal((unit, symbol), (result.LengthUnit, result.Document.Models[0].SourceLengthUnit));
    }

    [Fact]
    public void Read_RevitSample_MapsTheTree()
    {
        var doc = Scene(RevitSample.Build()).Document;
        Assert.Equal(Enumerable.Range(0, 14).Select(i => (long)i), doc.Elements.Select(e => e.Id));
        Assert.Equal([SceneNodeKind.Model, SceneNodeKind.Level, SceneNodeKind.Group, SceneNodeKind.Group, SceneNodeKind.Group, SceneNodeKind.Element, SceneNodeKind.Element,
                      SceneNodeKind.Element, SceneNodeKind.Element, SceneNodeKind.Element, SceneNodeKind.Element, SceneNodeKind.Group, SceneNodeKind.Group, SceneNodeKind.Element],
                     doc.Elements.Select(e => e.Kind));
        Assert.Equal([null, 0L, 1L, 2L, 3L, 4L, 5L, 4L, 4L, 8L, 8L, 0L, 11L, 12L], doc.Elements.Select(e => e.ParentId));   // 10 is a part of 8
        Assert.Equal(["Tower", "Level 1", "Walls", "Basic Wall", "Generic 200", "Wall A", "Solid", "Wall B", "Door", "Panel", "Door", "No level", "Lines", "Line 1"],
                     doc.Elements.Select(e => e.Name));
    }

    [Fact]
    public void Read_RevitSample_GivesElementsTheirIdentity()
    {
        var doc = Scene(RevitSample.Build()).Document;
        Assert.Equal(
            [(5L, "7b0c5a4e-3f2d-4b1a-9c8e-6d5f4e3ef9b1", "1x35fEForB6foERLzEFlcn"), (7L, "11111111-2222-4333-8444-555555555555", "3Ax9$kQwz0Bf8cYp2LmN4r"),
             (8L, "0f1e2d3c-4b5a-4968-8778-695a4b3c2d1e", "0F7YqyIrf9Q8TuQLfBF2qU"), (13L, "a0b1c2d3-e4f5-4a6b-9c7d-8e9fa0b1c2d3", "2WiSBJvFLAQvnzZf_WiSBJ")],
            doc.Elements.Where(e => e.SourceId != null).Select(e => (e.Id, e.SourceId, e.IfcGuid)));
    }

    [Fact]
    public void Read_RevitSample_SetsCategoryTypeAndLevel()
    {
        var result = Scene(RevitSample.Build());
        var doc = result.Document;
        Assert.All(doc.Elements.Where(e => e.Id is >= 5 and <= 10), e => Assert.Equal(("Walls", "Generic 200"), (e.Category, e.TypeName)));
        Assert.Equal(("Lines", ""), (doc.Elements[13].Category, doc.Elements[13].TypeName));
        Assert.All(doc.Elements.Where(e => e.Kind != SceneNodeKind.Element), e => Assert.Equal(("", ""), (e.Category, e.TypeName)));
        var level = Assert.Single(doc.Levels);
        Assert.Equal((1, 1, "Level 1"), (level.Id, level.ModelId, level.Name));
        Assert.NotNull(level.Elevation);
        Assert.Equal(3.0, level.Elevation.Value, precision: 6);
        Assert.Equal([null, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, null, null, null], doc.Elements.Select(e => e.LevelId));
        Assert.Equal(4, result.ElementCount);
        AssertSampleDiagnostics(result);
    }

    [Theory]
    [InlineData(double.NaN, 6, "lcldrevit_parameter_-1007102", NwcLengthUnit.Feet)]
    [InlineData(double.PositiveInfinity, 6, "lcldrevit_parameter_-1007102", NwcLengthUnit.Feet)]
    [InlineData("9.84", 4, "lcldrevit_parameter_-1007102", NwcLengthUnit.Feet)]    // not a length value
    [InlineData(9.84, 6, "lcldrevit_parameter_-1000010", NwcLengthUnit.Feet)]    // another parameter
    [InlineData(9.84, 6, "lcldrevit_parameter_-1007102", NwcLengthUnit.Unknown)]
    public void Read_RevitSample_LevelElevationIsNullWhenNotAFiniteLength(object elevation, int valueType, string internalName, NwcLengthUnit unit)
    {
        var doc = Scene(RevitSample.Build(levelElevation: elevation, levelElevationType: valueType, levelElevationName: internalName),
            new() { ModelKey = "k", LengthUnit = unit }).Document;
        Assert.Null(Assert.Single(doc.Levels).Elevation);
    }

    [Fact]
    public void Read_RevitSample_ConvertsTheLevelElevationToMetres()
    {
        var doc = Scene(RevitSample.Build(levelElevation: 10.0), new() { ModelKey = "k", LengthUnit = NwcLengthUnit.Meters }).Document;
        Assert.Equal(10.0, Assert.Single(doc.Levels).Elevation!.Value, precision: 6);
    }

    [Theory]   // the second case has a grid, which only a Revit file reads
    [InlineData(-2000023)]
    [InlineData(-2000220)]
    public void Read_RevitExporterLoader_IsARevitFile(int doorCategoryId)
    {
        var expected = Scene(RevitSample.Build(doorCategoryId: doorCategoryId)).Document;
        var result = Scene(RevitSample.Build("nwexportrevit", doorCategoryId: doorCategoryId));
        var doc = result.Document;
        AssertSampleDiagnostics(result);
        Assert.Equal("nwexportrevit", doc.Models[0].Application);
        Assert.Equal(doorCategoryId == -2000220, doc.Grids.Count > 0);
        expected.Models[0].Application = doc.Models[0].Application;
        Assert.Equal(SceneJson.Write(expected), SceneJson.Write(doc));
    }

    [Fact]
    public void Read_OtherLoader_HasNoCategoriesOrLevels()
    {
        var doc = Scene(RevitSample.Build("LcNwcLoaderPlugin:mock")).Document;
        Assert.Empty(doc.Levels);
        Assert.Equal(SceneNodeKind.Group, doc.Elements[1].Kind);
        Assert.All(doc.Elements, e => Assert.Equal("", e.Category));
    }

    [Fact]
    public void Read_LeafOutsideElements_IsAnElementWithoutSourceId()
    {
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var group = new NwcNode { Id = 1, Name = "G", Class = "Group", Tag = 53, Parent = root };
        var leaf = new NwcNode { Id = 2, Name = "L", Class = "Leaf", Tag = 22, Parent = group };
        root.Children.Add(group); group.Children.Add(leaf);
        var result = Valid(NwcSceneReader.Read(NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, rootNode: root), new() { ModelKey = "k" }));
        Assert.Equal((SceneNodeKind.Element, (string?)null), (result.Document.Elements[2].Kind, result.Document.Elements[2].SourceId));
        var d = Assert.Single(result.Diagnostics);
        Assert.Equal((SceneSeverity.Info, "no-source-id"), (d.Severity, d.Code));
        Assert.Equal(1, result.ElementCount);
    }

    [Fact]
    public void Read_PairWithASiblingBetween_MakesTheSecondAPartOfTheFirst()
    {
        var (h, _) = SyntheticTree.Write(1, new TreeNode("T", "LcRevitCollection", Children: [
            new("Grid", "LcRevitComposite", X: 1), new("Other", "LcRevitInstance", X: 3), new("Grid", "LcRevitComposite", X: 1)]));
        var file = SyntheticNwcFile.Build(h, null, [], [], extraStreams: [("LcOpGuidStore", SyntheticNwcFile.GuidStore(RevitSample.G1, RevitSample.G1, RevitSample.G2))]);
        var doc = Scene(file).Document;   // 0 root, 1 T, 2 Grid, 3 Other, 4 Grid
        Assert.Equal([null, 0L, 1L, 1L, 2L], doc.Elements.Select(e => e.ParentId));
        Assert.Equal([null, null, RevitSample.G1.ToString("D"), RevitSample.G2.ToString("D"), null], doc.Elements.Select(e => e.SourceId));
    }

    [Fact]
    public void Read_RevitSample_WritesSetsInSlotOrder()
    {
        var sets = Sets(Scene(RevitSample.Build()).Document, 5);
        Assert.Equal([("Element", "nwc:LcRevitData_Element"), ("Level", null), ("Type", "nwc:LcRevitData_Type"), ("Element ID", "nwc:LcRevitId")],
                     sets.Select(s => (s.Name, s.Key)));
    }

    [Fact]
    public void Read_ValueText_FollowsTheValueType()
    {
        var element = Sets(Scene(RevitSample.Build()).Document, 5)[0];
        Assert.Equal(
            [("Category", "Walls"), ("Category Id", "-2000011"), ("Id", "1001"), ("Area", "10 m²"), ("Volume", "2 m³"), ("Angle", "90 °"), ("Comment", "note"),
             ("Factor", "0.25"), ("Structural", "1"), ("Function", "Exterior"), ("Ident", "ID-9")],
            element.Properties.Select(p => (p.Name, p.Value.Text)));
        Assert.All(element.Properties, p => Assert.Equal((SceneValueKind.Text, (string?)null, (double?)null), (p.Value.Kind, p.Value.Unit, p.Value.Number)));
        Assert.Equal("3000 mm", Sets(Scene(RevitSample.Build()).Document, 5)[1].Properties.Single(p => p.Name == "Elevation").Value.Text);
    }

    [Theory]
    [InlineData(NwcLengthUnit.Meters)]
    [InlineData(NwcLengthUnit.Millimeters)]
    [InlineData(NwcLengthUnit.Inches)]
    public void Read_ValueText_WithDifferentLengthUnits_ConvertsDimensionsAndAngles(NwcLengthUnit unit)
    {
        var doc = Scene(RevitSample.Build(), new NwcSceneOptions { ModelKey = "Tower.nwc", LengthUnit = unit }).Document;
        var element = Sets(doc, 5)[0];
        var propMap = element.Properties.ToDictionary(p => p.Name, p => p.Value.Text);

        Assert.NotNull(propMap["Area"]);
        Assert.EndsWith(" m²", propMap["Area"]);
        Assert.NotNull(propMap["Volume"]);
        Assert.EndsWith(" m³", propMap["Volume"]);
        Assert.Equal("90 °", propMap["Angle"]);

        var elevation = Sets(doc, 5)[1].Properties.Single(p => p.Name == "Elevation").Value.Text;
        Assert.NotNull(elevation);
        Assert.EndsWith(" mm", elevation);
    }

    [Fact]
    public void Read_KeysOnlyForStableInternalNames()
    {
        var doc = Scene(RevitSample.Build()).Document;
        var element = Sets(doc, 5)[0];
        Assert.Equal("nwc:LcRevitData_Element.lcldrevit_parameter_-1000001", element.Properties.Single(p => p.Name == "Area").Key);
        Assert.Equal("nwc:LcRevitData_Element.LcRevitPropertyElementId", element.Properties.Single(p => p.Name == "Id").Key);
        Assert.Null(element.Properties.Single(p => p.Name == "Comment").Key);
        Assert.All(Sets(doc, 5)[1].Properties, p => Assert.Null(p.Key));
        var material = Assert.Single(Sets(doc, 6));
        Assert.Equal(("Concrete", "nwc:LcRevitMaterialProperties"), (material.Name, material.Key));
        Assert.Equal(["nwc:LcRevitMaterialProperties.revit_Color", null], material.Properties.Select(p => p.Key));
        Assert.Equal("nwc:LcRevitId.LcRevitId", Sets(doc, 5)[3].Properties[0].Key);
    }

    [Fact]
    public void Read_SharedAndRepeatedTab_IsOneSetListedOnce()
    {
        var doc = Scene(RevitSample.Build()).Document;
        Assert.Equal(doc.Elements[5].PropertySetIds[1..3], doc.Elements[7].PropertySetIds[1..3]);    // level and type tabs shared by the walls
        Assert.Equal(doc.Elements[8].PropertySetIds[0], doc.Elements[10].PropertySetIds[0]);         // the pair's element tab

        var tab = new NwcPropertyTab
        {
            Name = "T",
            InternalName = "LcRevitData_Type",
            Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = 4, Value = "v" }]
        };
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
        leaf.PropertyTabs.AddRange([tab, tab]); root.Children.Add(leaf);
        Assert.Single(Valid(NwcSceneReader.Read(NwcDocument.Create(rootNode: root), new() { ModelKey = "k" })).Document.Elements[1].PropertySetIds);
    }

    [Fact]
    public void Read_PropertiesOutsideTabs_AreReported()
    {
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
        leaf.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = 4, Value = "v" });
        root.Children.Add(leaf);
        var result = Valid(NwcSceneReader.Read(NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, rootNode: root), new() { ModelKey = "k" }));
        Assert.Empty(result.Document.PropertySets);
        Assert.Contains((SceneSeverity.Warning, "properties-outside-tabs"), result.Diagnostics.Select(d => (d.Severity, d.Code)));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-1", "1")]
    public void Read_BooleanValue_IsZeroOrOne(string stored, string text)
    {
        var tab = new NwcPropertyTab
        {
            Name = "T",
            InternalName = "LcRevitData_Element",
            Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = 3, Value = stored }]
        };
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
        leaf.PropertyTabs.Add(tab); root.Children.Add(leaf);
        var doc = Valid(NwcSceneReader.Read(NwcDocument.Create(rootNode: root), new() { ModelKey = "k" })).Document;
        Assert.Equal(text, Assert.Single(Assert.Single(doc.PropertySets).Properties).Value.Text);
    }

    [Theory]   // guard: before this task no set is written at all
    [InlineData(6, "NaN")]
    [InlineData(6, "x")]
    [InlineData(1, "1,5")]
    [InlineData(4, null)]
    [InlineData(13, "x")]
    public void Read_ValueThatCannotBeShown_IsLeftOut(int type, string? value)
    {
        var tab = new NwcPropertyTab
        {
            Name = "T",
            InternalName = "LcRevitData_Element",
            Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = type, Value = value }]
        };
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
        leaf.PropertyTabs.Add(tab); root.Children.Add(leaf);
        var doc = Valid(NwcSceneReader.Read(NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, rootNode: root), new() { ModelKey = "k" })).Document;
        Assert.Empty(doc.PropertySets);
    }

    [Fact]
    public void Read_RevitSample_SharesGeometryAndPlacesInstances()
    {
        var result = Scene(RevitSample.Build());
        var doc = result.Document;
        Assert.Equal([SceneTopology.Triangles, SceneTopology.Lines, SceneTopology.Lines, SceneTopology.Lines], doc.Geometries.Select(g => g.Topology));
        Assert.Equal([(6L, 1), (7L, 1), (9L, 1), (10L, 2), (13L, 3), (13L, 4)], doc.Instances.Select(i => (i.ElementId, i.GeometryId)));
        Assert.Equal((4, 6, 4), (result.GeometryCount, result.InstanceCount, result.ElementsWithGeometry));
        Assert.Equal(new Vector3d(0.3048, 0.3048, 0), doc.Geometries[0].Vertices[3]);
        Assert.Equal([new Vector3d(0, 0, 0), new Vector3d(0.3048, 0, 0)], doc.Geometries[2].Vertices);   // R7 red group: its own vertices only
        Assert.Equal([0, 1], doc.Geometries[2].Indices);
        Assert.Equal([new Vector3d(0, 0.3048, 0), new Vector3d(0.3048, 0.3048, 0)], doc.Geometries[3].Vertices);   // R7 green group
        Assert.Equal([0, 1], doc.Geometries[3].Indices);
        Assert.All(doc.Geometries.Skip(1), g => Assert.Null(g.Normals));   // lines never carry normals
    }

    [Fact]
    public void Read_RotatedInstance_KeepsTheRowVectorAxes()
    {
        // 90° about Z, translation (1, 2, 3) ft: NwcTransform.ToMatrix4x4 rows are the images of X, Y, Z (row vectors)
        double h = Math.Sqrt(0.5);
        var mesh = new NwcMesh
        {
            Id = 0,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            Transform = new NwcTransform { Id = 0, Rotation = new(0, 0, h, h), Translation = new(1, 2, 3) }
        };
        var result = SceneOf(mesh);
        var t = result.Document.Instances[0].Transform!.Value;
        AssertNear(new Vector3d(0, 1, 0), t.XAxis);
        AssertNear(new Vector3d(-1, 0, 0), t.YAxis);
        AssertNear(new Vector3d(0.3048, 0.6096, 0.9144), t.Translation);
        var w = mesh.GetWorldVertices()[1];
        AssertNear(new Vector3d(w.X * 0.3048, w.Y * 0.3048, w.Z * 0.3048), t.Apply(result.Document.Geometries[0].Vertices[1]));
        Assert.Null(result.Document.Geometries[0].Normals);   // no stored normals
    }

    [Fact]
    public void Read_EmptyMesh_HasNoGeometryAndIsReported()
    {
        var result = SceneOf(new NwcMesh { Id = 0, Vertices = [], Indices = [] });
        Assert.Empty(result.Document.Geometries);
        Assert.Empty(result.Document.Instances);
        Assert.Contains((SceneSeverity.Info, "empty-mesh"), result.Diagnostics.Select(d => (d.Severity, d.Code)));
    }

    [Fact]
    public void Read_RevitSample_KeepsMirrorAndMetreTranslation()
    {
        var doc = Scene(RevitSample.Build()).Document;
        var t0 = doc.Instances[0].Transform!.Value;
        Assert.Equal(new Vector3d(1, 0, 0), t0.XAxis);
        AssertNear(new Vector3d(0.3048, 0.6096, 0.9144), t0.Translation);
        var t1 = doc.Instances[1].Transform!.Value;
        Assert.Equal((new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, -1)), (t1.XAxis, t1.YAxis, t1.ZAxis));
        AssertNear(new Vector3d(3.048, 0, 0), t1.Translation);
    }

    [Fact]
    public void Read_RevitSample_WritesFileMaterialsAndLineColours()
    {
        var doc = Scene(RevitSample.Build()).Document;
        Assert.Equal(
            [("Concrete", new RgbaColor(128, 128, 128, 255)), ("Default", new RgbaColor(128, 128, 128, 255)), ("Glass", new RgbaColor(0, 0, 255, 128)),
             ("Default", new RgbaColor(255, 255, 255, 255))],
            doc.Materials.Select(m => (m.Name, m.Color)));
        Assert.All(doc.Materials, m => Assert.Equal((0.5, 0.1), (m.Roughness, m.Metalness)));   // IOScene defaults, as RvtIO.Scene
        Assert.Equal([null, null, null, new RgbaColor(255, 0, 0, 255), new RgbaColor(255, 0, 0, 255), new RgbaColor(0, 255, 0, 255)], doc.Instances.Select(i => i.Color));
        Assert.Equal([1, 2, 3, 4, 4, 4], doc.Instances.Select(i => i.MaterialId));
    }

    [Fact]
    public void Read_SegmentWithTwoColours_IsDrawnWithTheMaterialAndReported()
    {
        // R7 with vertex 1 green: segment (0,1) joins red and green
        byte[] r7 = FormatVectors.Patched(FormatVectors.R7, 104, [0xFF, 0x00, 0xFF, 0x00]);
        var (h, p) = SyntheticTree.Write(1, new TreeNode("L", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1, Slots: [new PId(5)])]));
        var file = SyntheticNwcFile.Build(h, SyntheticFragments.Build(new Frag(2, NwcIO.Model.Vector3d.Zero, 1, 2, 4, Rec: 1)), [r7], [136],
            extraStreams: [("LcOpGuidStore", SyntheticNwcFile.GuidStore(RevitSample.G1)), ("LcOpDBCache", SyntheticNwcFile.PartitionHeader("a.rvt", 1, "LcNwcLoaderPlugin:lcldrevit"))], partitionProps: p);
        var result = Scene(file);
        Assert.Equal([null, new RgbaColor(0, 255, 0, 255)], result.Document.Instances.Select(i => i.Color));
        Assert.Equal((SceneSeverity.Warning, "line-colour-mixed"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
    }

    private const int MaxLineColourGroups = 64;   // NwcSceneReader.MaxLineColourGroups

    [Fact]
    public void Read_ManyLineColours_StayLinear()
    {
        // 2,000 segments, each of its own colour: one line geometry per colour up to the cap, each with only its two vertices,
        // then one geometry drawn with the material colour for the segments of the further colours
        const int segments = 2000;
        var mesh = new NwcMesh
        {
            Id = 0,
            Indices = [],
            Vertices = [.. Enumerable.Range(0, 2 * segments).Select(v => new NwcIO.Model.Vector3d(v / 2, v % 2, 0))],
            LineIndices = [.. Enumerable.Range(0, 2 * segments)],
            LineColors = [.. Enumerable.Range(0, 2 * segments).Select(v => new NwcIO.Model.RgbaColor(v / 2 % 256 / 255f, v / 2 / 256 / 255f, 0))]
        };
        var result = SceneOf(mesh);
        var doc = result.Document;
        Assert.Equal(MaxLineColourGroups + 1, doc.Geometries.Count);
        Assert.All(doc.Geometries.Take(MaxLineColourGroups), g => Assert.Equal((SceneTopology.Lines, 2, 2), (g.Topology, g.Vertices.Count, g.Indices.Count)));
        Assert.Equal((SceneTopology.Lines, 2 * (segments - MaxLineColourGroups)), (doc.Geometries[^1].Topology, doc.Geometries[^1].Indices.Count));
        Assert.Equal(2 * segments, doc.Geometries.Sum(g => g.Vertices.Count));
        Assert.Equal(MaxLineColourGroups, doc.Instances.Take(MaxLineColourGroups).Select(i => i.Color).OfType<RgbaColor>().Distinct().Count());
        Assert.Null(doc.Instances[^1].Color);
        Assert.Equal(SceneSeverity.Warning, Assert.Single(result.Diagnostics, d => d.Code == "line-colours-capped").Severity);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "line-colour-mixed");
    }

    [Fact]
    public void Read_SharedRecordWithManyColours_InstancesStayLinear()
    {
        // 50 meshes share one line record of MaxLineColourGroups + 10 colours: each places one instance per geometry of the record
        const int meshes = 50, colours = MaxLineColourGroups + 10;
        NwcIO.Model.Vector3d[] vertices = [.. Enumerable.Range(0, 2 * colours).Select(v => new NwcIO.Model.Vector3d(v / 2, v % 2, 0))];
        int[] lineIndices = [.. Enumerable.Range(0, 2 * colours)];
        NwcIO.Model.RgbaColor[] lineColors = [.. Enumerable.Range(0, 2 * colours).Select(v => new NwcIO.Model.RgbaColor(v / 2 / 255f, 0, 0))];
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var list = new List<NwcMesh>();
        for (int i = 0; i < meshes; i++)
        {
            var leaf = new NwcNode { Id = i + 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
            var mesh = new NwcMesh { Id = i, GeometryIndex = 0, Vertices = vertices, Indices = [], LineIndices = lineIndices, LineColors = lineColors, Node = leaf };
            root.Children.Add(leaf); leaf.Meshes.Add(mesh); list.Add(mesh);
        }

        var result = Valid(NwcSceneReader.Read(NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, meshes: list, rootNode: root), new() { ModelKey = "k" }));
        Assert.Equal(MaxLineColourGroups + 1, result.Document.Geometries.Count);
        Assert.Equal(meshes * (MaxLineColourGroups + 1), result.Document.Instances.Count);
        Assert.Single(result.Diagnostics, d => d.Code == "line-colours-capped");
    }

    [Fact]   // guard: the output is the same with the quadratic dedup; only the time differs
    public void Read_ManyTabsOnOneNode_StayLinear()
    {
        const int tabs = 300_000;
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
        for (int i = 0; i < tabs; i++)
        {
            leaf.PropertyTabs.Add(new NwcPropertyTab
            {
                Name = "T",
                Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = 4, Value = "v" }]
            });
        }

        leaf.PropertyTabs.Add(leaf.PropertyTabs[0]);
        root.Children.Add(leaf);
        var doc = NwcSceneReader.Read(NwcDocument.Create(rootNode: root), new() { ModelKey = "k" }).Document;
        Assert.Equal(Enumerable.Range(1, tabs), doc.Elements[1].PropertySetIds);
        Assert.Equal(tabs, doc.PropertySets.Count);
    }

    [Fact]
    public void Read_FileNotReadThroughDbCache_NumbersElementsInPreorder()
    {
        // NwcIO writes no DbCache directory; read back, the nodes keep their stored ids, here 1 twice
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var layer = new NwcNode { Id = 1, Name = "L", Class = "LcRevitLayer", Tag = 52, Parent = root };
        var collection = new NwcNode { Id = 1, Name = "C", Class = "LcRevitCollection", Tag = 52, Parent = layer };
        root.Children.Add(layer); layer.Children.Add(collection);
        using var file = TempFile(NwcWriter.WriteToBytes(NwcDocument.Create(rootNode: root)), "Written.nwc");
        var doc = Valid(NwcSceneReader.Read(file.Path)).Document;
        Assert.Equal([0L, 1L, 2L], doc.Elements.Select(e => e.Id));
        Assert.Equal([null, 0L, 1L], doc.Elements.Select(e => e.ParentId));
    }

    [Fact]
    public void Read_MeshOutsideTheTree_IsUnplaced()
    {
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var stray = new NwcNode { Id = 1, Name = "S", Class = "Leaf", Tag = 22 };   // not under the root
        var mesh = new NwcMesh { Id = 0, Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], Indices = [0, 1, 2], Node = stray };
        stray.Meshes.Add(mesh);
        var result = Valid(NwcSceneReader.Read(NwcDocument.Create(meshes: [mesh], rootNode: root), new() { ModelKey = "k", LengthUnit = NwcLengthUnit.Feet }));
        Assert.Empty(result.Document.Instances);
        Assert.Equal((SceneSeverity.Warning, "unplaced-mesh"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
    }

    [Fact]
    public void Read_ZeroNormals_AreAveragedFromTheTriangles()
    {
        // two triangles of a unit square in z = 0, CCW seen from +Z; vertex 3 stores no normal
        var mesh = new NwcMesh
        {
            Id = 0,
            GeometryIndex = -1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)],
            Indices = [0, 1, 2, 2, 1, 3],
            Normals = [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), NwcIO.Model.Vector3d.Zero]
        };
        Assert.Equal(new Vector3d(0, 0, 1), SceneOf(mesh).Document.Geometries[0].Normals![3]);
    }

    // A Revit-loader document whose root holds one leaf carrying the mesh.
    private static NwcSceneResult SceneOf(NwcMesh mesh)
    {
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = "L", Class = "Leaf", Tag = 22, Parent = root };
        root.Children.Add(leaf); leaf.Meshes.Add(mesh); mesh.Node = leaf;
        return Valid(NwcSceneReader.Read(NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, meshes: [mesh], rootNode: root), new() { ModelKey = "k" }));
    }

    [Fact]
    public void Read_NonRevitLoader_LeavesOutUnitDataAndWarns()
    {
        var result = Scene(RevitSample.Build("LcNwcLoaderPlugin:mock"));
        Assert.Empty(result.Document.Geometries); Assert.Empty(result.Document.Instances); Assert.Empty(result.Document.Materials);
        Assert.DoesNotContain(result.Document.PropertySets.SelectMany(s => s.Properties), p => p.Name is "Area" or "Volume" or "Angle" or "Width" or "Elevation");
        AssertSampleDiagnostics(result, (SceneSeverity.Warning, "unit-unknown"));   // the dimensioned values are `unit-unknown`'s, not counted as skipped
        Assert.Empty(result.Document.Validate());

        var converted = Scene(RevitSample.Build("LcNwcLoaderPlugin:mock"), new() { ModelKey = "k", LengthUnit = NwcLengthUnit.Feet });
        Assert.Equal(6, converted.InstanceCount);
        AssertSampleDiagnostics(converted);
        var texts = converted.Document.PropertySets.SelectMany(s => s.Properties).Select(p => (p.Name, p.Value.Text)).ToList();
        Assert.Contains(("Area", "10 m²"), texts);
        Assert.Contains(("Elevation", "3000 mm"), texts);
        Assert.Equal(new Vector3d(0.3048, 0.3048, 0), converted.Document.Geometries[0].Vertices[3]);
    }

    [Fact]
    public void Read_MeshWithoutNode_IsReportedNotPlaced()
    {
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var doc = NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, rootNode: root,
            meshes: [new NwcMesh { Id = 0, Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], Indices = [0, 1, 2] }]);
        var result = Valid(NwcSceneReader.Read(doc, new() { ModelKey = "k" }));
        Assert.Empty(result.Document.Instances);
        Assert.Equal((SceneSeverity.Warning, "unplaced-mesh"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
    }

    private static ScenePropertySet[] Sets(SceneDocument doc, long element) => [.. doc.Elements[(int)element].PropertySetIds.Select(id => doc.PropertySets[id - 1])];

    // Compares each component with precision 12.
    private static void AssertNear(Vector3d expected, Vector3d actual)
    {
        Assert.Equal(expected.X, actual.X, 12);
        Assert.Equal(expected.Y, actual.Y, 12);
        Assert.Equal(expected.Z, actual.Z, 12);
    }

    // Every converted document must pass Validate() without errors or warnings (design §2, §6.5).
    [Fact]
    public void Read_GridWithOneHorizontalSegment_IsAGridLine()
    {
        var doc = Scene(RevitSample.Build(doorCategoryId: -2000220)).Document;
        var grid = Assert.Single(doc.Grids);
        Assert.Equal((1, "Grids"), (grid.ModelId, grid.Name));
        var line = Assert.Single(grid.Lines);
        Assert.Equal("Door", line.Name);
        AssertNear(new Vector3d(0, 0, 0.9144), line.Start);
        AssertNear(new Vector3d(3.048, 0, 0.9144), line.End);
        Assert.Equal(0.9144, line.Elevation, 12);
        Assert.Equal(14, doc.Elements.Count);
    }

    [Theory]   // guard: no grid is written before this task
    [InlineData(-2000023, -2000051)]   // no grid
    [InlineData(-2000023, -2000220)]   // a grid whose line mesh has two segments
    public void Read_NoUnambiguousGridLine_WritesNoGrid(int door, int line) =>
        Assert.Empty(Scene(RevitSample.Build(doorCategoryId: door, lineCategoryId: line)).Document.Grids);

    [Fact]   // guard (Review Focus 1): no grid is written before this task
    public void Read_GridWithUnknownUnit_WritesNoGrid()
    {
        var result = Scene(RevitSample.Build(doorCategoryId: -2000220), new() { ModelKey = "k", LengthUnit = NwcLengthUnit.Unknown });
        Assert.Empty(result.Document.Grids);
        AssertSampleDiagnostics(result, (SceneSeverity.Warning, "unit-unknown"));
    }

    [Fact]
    public void Read_DocumentWithSavedViewpoints_MapsCameraToSceneViewpoints()
    {
        var id = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var doc = NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = "model.nwc" },
            savedViewpoints:
            [
                new NwcSavedViewpoint
                {
                    Id = id,
                    Name = "Plan",
                    Camera = new NwcCamera
                    {
                        Position = new NwcIO.Model.Vector3d(10, 20, 30),
                        Rotation = Quaterniond.Identity,
                        FocalDistance = 10,
                        FieldOfView = 90,
                        AspectRatio = 1.5,
                        NearDistance = 1,
                        FarDistance = 500,
                    },
                },
                new NwcSavedViewpoint
                {
                    Name = "Elevation",
                    Camera = new NwcCamera
                    {
                        Position = new NwcIO.Model.Vector3d(1, 2, 3),
                        Rotation = Quaterniond.Identity,
                        FocalDistance = 4,
                        Projection = NwcCameraProjection.Orthographic,
                    },
                },
            ],
            rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = NwcSceneReader.Read(doc, new NwcSceneOptions
        {
            ModelKey = "model.nwc",
            LengthUnit = NwcLengthUnit.Meters,
        });

        Assert.Equal(2, result.Document.Viewpoints.Count);
        var plan = result.Document.Viewpoints[0];
        Assert.Equal(id.ToString(), plan.Id);
        Assert.Equal("Plan", plan.Name);
        Assert.Equal(10, plan.Camera.Position.X, 6);
        Assert.Equal(20, plan.Camera.Position.Y, 6);
        Assert.Equal(30, plan.Camera.Position.Z, 6);
        Assert.Equal(Math.PI / 2, plan.Camera.FieldOfView, 6);
        Assert.Equal(1.5, plan.Camera.AspectRatio, 6);
        Assert.Equal(500, plan.Camera.FarDistance, 6);
        Assert.Equal(SceneProjection.Perspective, plan.Camera.Projection);
        Assert.Equal(SceneProjection.Orthographic, result.Document.Viewpoints[1].Camera.Projection);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "viewpoints-skipped");
    }

    [Fact]
    public void Read_Assembly_ConvertsEachDocumentWithUniqueIds()
    {
        var assembly = NwcAssembly.Create("Federation",
            Model("A.nwc", 1, 0),
            Model("B.nwc", 2, 10));

        var result = NwcSceneReader.Read(assembly, new NwcSceneOptions { LengthUnit = NwcLengthUnit.Meters });

        Assert.Equal([1, 2], result.Document.Models.Select(m => m.Id));
        Assert.Equal(["A.nwc", "B.nwc"], result.Document.Models.Select(m => m.Key));
        Assert.Equal(2, result.Document.Geometries.Count);
        Assert.Equal([1, 2], result.Document.Geometries.Select(g => g.Id));
        Assert.Equal(2, result.Document.Instances.Count);
        Assert.Equal([1, 2], result.Document.Materials.Select(m => m.Id));
        Assert.Equal(result.Document.Elements.Count, result.Document.Elements.Select(e => e.Id).Distinct().Count());
        Assert.Equal(2, result.ElementCount);
        Assert.Equal(1, result.LengthUnit.GetMetresPerUnit());
        Assert.Empty(result.Document.Validate());

        static NwcDocument Model(string sourcePath, int meshId, double x)
        {
            var root = new NwcNode { Id = 1, Name = "Root", Class = "Model" };
            var child = new NwcNode { Id = 2, Name = "Child", Class = "Model" };
            root.Children.Add(child);
            var mesh = new NwcMesh
            {
                Id = meshId,
                Vertices = [new NwcIO.Model.Vector3d(x, 0, 0), new NwcIO.Model.Vector3d(x + 1, 0, 0), new NwcIO.Model.Vector3d(x, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new NwcIO.Model.BoundingBox3d(new(x, 0, 0), new(x + 1, 1, 0)),
            };
            mesh.Material = new NwcMaterial { Id = meshId, Name = $"Material {meshId}" };
            mesh.Node = child;
            child.Mesh = mesh;
            return NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = sourcePath },
                meshes: [mesh],
                rootNode: root);
        }
    }

    [Fact]
    public void Read_Assembly_DuplicateFileNamesGetUniqueKeysAndSkipsTheNwfDocument()
    {
        var assembly = NwcAssembly.Create("Federation",
            Model("fed.nwf", 1, 0),
            Model("model.nwc", 2, 10),
            Model("model.nwc", 3, 20));

        var result = NwcSceneReader.Read(assembly, new NwcSceneOptions { LengthUnit = NwcLengthUnit.Meters });

        Assert.Equal(["model.nwc", "model.nwc#2"], result.Document.Models.Select(m => m.Key));
        Assert.DoesNotContain(result.Document.Models, m => m.Key == "fed.nwf");
        Assert.Empty(result.Document.Validate());

        static NwcDocument Model(string sourcePath, int meshId, double x)
        {
            var root = new NwcNode { Id = 1, Name = "Root", Class = "Model" };
            var child = new NwcNode { Id = 2, Name = "Child", Class = "Model" };
            root.Children.Add(child);
            var mesh = new NwcMesh
            {
                Id = meshId,
                Vertices = [new NwcIO.Model.Vector3d(x, 0, 0), new NwcIO.Model.Vector3d(x + 1, 0, 0), new NwcIO.Model.Vector3d(x, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new NwcIO.Model.BoundingBox3d(new(x, 0, 0), new(x + 1, 1, 0)),
            };
            mesh.Material = new NwcMaterial { Id = meshId, Name = $"Material {meshId}" };
            mesh.Node = child;
            child.Mesh = mesh;
            return NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = sourcePath },
                meshes: [mesh],
                rootNode: root);
        }
    }

    [Fact]
    public void Read_LinkedPlacement_BecomesItsOwnSceneModel()
    {
        var root = new NwcNode { Id = 0, Name = "Root", Class = "Model" };
        var element = new NwcNode
        {
            Id = 1,
            Name = "Element",
            Class = "LcRevitData_Element",
            IsElement = true,
            ElementGuid = Guid.Parse("11111111-2222-4333-8444-555555555555"),
        };
        element.Element = element;
        var link = new NwcNode
        {
            Id = 2,
            Name = "Link A",
            Class = "LcOpModelFile",
            Element = element,
            IsLinkPlacement = true,
        };
        root.Children.Add(element);
        root.Children.Add(link);

        var elementMesh = Mesh(1, 0, element);
        var linkMesh = Mesh(2, 10, link);
        var document = NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = "model.nwc" },
            meshes: [elementMesh, linkMesh],
            rootNode: root);

        var result = NwcSceneReader.Read(document, new NwcSceneOptions
        {
            ModelKey = "model.nwc",
            LengthUnit = NwcLengthUnit.Meters,
        });

        Assert.Equal(2, result.Document.Models.Count);
        Assert.Equal(2, result.Document.Models[1].Id);
        Assert.Equal("Link A", result.Document.Models[1].Key);
        Assert.Equal(2, result.Document.Instances.Count);
        Assert.Equal(2, result.Document.Elements.Select(e => e.ModelId).Distinct().Count());
        Assert.All(result.Document.Elements.Where(e => e.ModelId == 2),
            e => Assert.Equal("11111111-2222-4333-8444-555555555555", e.SourceId));
        Assert.Empty(result.Document.Validate());

        static NwcMesh Mesh(int id, double x, NwcNode node)
        {
            var mesh = new NwcMesh
            {
                Id = id,
                Vertices = [new NwcIO.Model.Vector3d(x, 0, 0), new NwcIO.Model.Vector3d(x + 1, 0, 0), new NwcIO.Model.Vector3d(x, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new NwcIO.Model.BoundingBox3d(new(x, 0, 0), new(x + 1, 1, 0)),
            };
            mesh.Node = node;
            node.Mesh = mesh;
            return mesh;
        }
    }

    [Fact]
    public void Read_SharedLinkedSubtree_BecomesOneModel()
    {
        var root = new NwcNode { Id = 0, Name = "Root", Class = "Model" };
        var first = new NwcNode { Id = 1, Name = "First", Class = "LcRevitData_Element", IsElement = true };
        first.Element = first;
        var shared = new NwcNode { Id = 2, Name = "Shared model", Class = "LcOpModelFile", IsLinkRoot = true };
        for (int i = 0; i < 3; i++)
        {
            shared.Children.Add(new NwcNode
            {
                Id = 10 + i,
                Name = $"Part {i + 1}",
                Class = "LcRevitData_Element",
                Element = first,
                IsLinkPlacement = true,
            });
        }

        root.Children.Add(first);
        root.Children.Add(shared);

        var result = Valid(NwcSceneReader.Read(NwcDocument.Create(rootNode: root), new NwcSceneOptions
        {
            ModelKey = "model.nwc",
            LengthUnit = NwcLengthUnit.Meters,
        }));

        Assert.Equal(2, result.Document.Models.Count);
        var linkModel = result.Document.Models[1];
        Assert.Equal("Shared model", linkModel.Key);
        Assert.All(result.Document.Elements.Where(element => element.Name.StartsWith("Part ", StringComparison.Ordinal)),
            element => Assert.Equal(linkModel.Id, element.ModelId));
    }

    [Fact]
    public void Read_GeometryUnderALinkedPlacement_CountsThePlacementNotItsElement()
    {
        var root = new NwcNode { Id = 0, Name = "Root", Class = "Model" };
        var first = new NwcNode { Id = 1, Name = "First", Class = "LcRevitData_Element", IsElement = true, Parent = root };
        first.Element = first;
        var link = new NwcNode { Id = 2, Name = "Link", Class = "LcOpModelFile", Element = first, IsLinkPlacement = true, IsLinkRoot = true, Parent = root };
        var part = new NwcNode { Id = 3, Name = "Part", Class = "LcRevitData_Element", Element = first, Parent = link };
        link.Children.Add(part);
        root.Children.Add(first);
        root.Children.Add(link);

        var result = Valid(NwcSceneReader.Read(
            NwcDocument.Create(meshes: [Mesh(1, 0, first), Mesh(2, 10, part)], rootNode: root),
            new NwcSceneOptions { ModelKey = "model.nwc", LengthUnit = NwcLengthUnit.Meters }));

        Assert.Equal(2, result.ElementCount);
        Assert.Equal(2, result.ElementsWithGeometry);

        static NwcMesh Mesh(int id, double x, NwcNode node)
        {
            var mesh = new NwcMesh
            {
                Id = id,
                Vertices = [new NwcIO.Model.Vector3d(x, 0, 0), new NwcIO.Model.Vector3d(x + 1, 0, 0), new NwcIO.Model.Vector3d(x, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new NwcIO.Model.BoundingBox3d(new(x, 0, 0), new(x + 1, 1, 0)),
            };
            mesh.Node = node;
            node.Mesh = mesh;
            return mesh;
        }
    }

    [Fact]
    public void Read_MoreLinkedPlacementsThanTheModelLimit_Throws()
    {
        var root = new NwcNode { Id = 0, Name = "Root", Class = "Model" };
        var first = new NwcNode { Id = 1, Name = "First", Class = "LcRevitData_Element", IsElement = true, Parent = root };
        first.Element = first;
        root.Children.Add(first);
        for (int i = 0; i < 100_000; i++)
        {
            root.Children.Add(new NwcNode { Id = i + 2, Name = "Link", Class = "LcOpModelFile", Element = first, IsLinkPlacement = true, IsLinkRoot = true, Parent = root });
        }

        var ex = Assert.Throws<InvalidDataException>(() => NwcSceneReader.Read(
            NwcDocument.Create(rootNode: root),
            new NwcSceneOptions { ModelKey = "model.nwc", LengthUnit = NwcLengthUnit.Meters }));
        Assert.Contains("100000", ex.Message);
    }

    [Fact]
    public void Read_LinkedSubtreesWithTheSameName_GetOneModelPerRoot()
    {
        var root = new NwcNode { Id = 0, Name = "Root", Class = "Model" };
        for (int i = 1; i <= 2; i++)
        {
            var link = new NwcNode
            {
                Id = i,
                Name = "Linked model",
                Class = "LcOpModelFile",
                IsLinkPlacement = true,
            };
            var child = new NwcNode
            {
                Id = 10 + i,
                Name = $"Part {i}",
                Class = "LcRevitData_Element",
                IsElement = true,
                Element = link,
            };
            link.Children.Add(child);
            root.Children.Add(link);
        }

        var result = Valid(NwcSceneReader.Read(NwcDocument.Create(rootNode: root), new NwcSceneOptions
        {
            ModelKey = "model.nwc",
            LengthUnit = NwcLengthUnit.Meters,
        }));

        Assert.Equal(3, result.Document.Models.Count);
        Assert.Equal(["model.nwc", "Linked model", "Linked model#2"],
            result.Document.Models.Select(model => model.Key));
        Assert.Equal(2, result.Document.Elements.Single(element => element.Name == "Part 1").ModelId);
        Assert.Equal(3, result.Document.Elements.Single(element => element.Name == "Part 2").ModelId);
    }

    [Fact]
    public void Read_AssemblyLoadedFromDirectory_UsesFileNamesForKeysAndPaths()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nwcscene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = new NwcNode { Id = 1, Name = "Root", Class = "Model" };
            var fileInfo = new NwcFileInfo { SourcePath = "Tower.rvt", LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" };
            NwcDocument.Create(fileInfo, rootNode: root).Save(Path.Combine(directory, "Arch.nwc"));
            NwcDocument.Create(fileInfo, rootNode: root).Save(Path.Combine(directory, "Struct.nwc"));

            var assembly = NwcAssembly.LoadFromDirectory(directory);
            var result = Valid(NwcSceneReader.Read(assembly));

            Assert.Equal(["Arch.nwc", "Struct.nwc"],
                result.Document.Models.Select(model => model.Key).OrderBy(key => key, StringComparer.Ordinal));
            Assert.All(result.Document.Models, model =>
            {
                Assert.NotNull(model.Path);
                Assert.Equal(model.Key, Path.GetFileName(model.Path));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Read_AssemblyOpenedFromNwf_SkipsTheShellAndReportsUnresolvedAndNotLoadedReferences()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nwcscene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = new NwcNode { Id = 1, Name = "Root", Class = "Model" };
            NwcDocument.Create(new NwcFileInfo { SourcePath = "Tower.rvt", LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, rootNode: root)
                .Save(Path.Combine(directory, "Arch.nwc"));
            File.WriteAllText(Path.Combine(directory, "tex.png"), "not a model");
            string nwf = Path.Combine(directory, "fed.nwf");
            File.WriteAllBytes(nwf, XRefFile("Arch.nwc", "gone/Missing.nwc", "tex.png"));

            var assembly = NwcAssembly.OpenNwf(nwf);
            var result = Valid(NwcSceneReader.Read(assembly));

            Assert.Equal(["Arch.nwc"], result.Document.Models.Select(m => m.Key));   // the shell is not a model
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "xrefs-skipped");
            var unresolved = Assert.Single(result.Diagnostics, d => d.Code == "xrefs-unresolved");
            Assert.Equal(SceneSeverity.Warning, unresolved.Severity);
            Assert.StartsWith("1 external references were not found", unresolved.Message);
            Assert.Contains("gone/Missing.nwc", unresolved.Message);
            var notLoaded = Assert.Single(result.Diagnostics, d => d.Code == "xrefs-not-loaded");
            Assert.Equal(SceneSeverity.Warning, notLoaded.Severity);
            Assert.Contains("tex.png", notLoaded.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Read_AssemblyWithoutNwfReferences_GetsNoXRefDiagnostics()
    {
        var assembly = NwcAssembly.Create("Federation", ElementWithEmptyMesh("A.nwc", Guid.NewGuid()));
        var result = NwcSceneReader.Read(assembly, new NwcSceneOptions { LengthUnit = NwcLengthUnit.Meters });
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("xrefs-", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_Assembly_KeepsTheSourceIdOfAModelDiagnostic()
    {
        var guid = Guid.NewGuid();
        var assembly = NwcAssembly.Create("Federation", ElementWithEmptyMesh("A.nwc", guid));

        var result = NwcSceneReader.Read(assembly, new NwcSceneOptions { LengthUnit = NwcLengthUnit.Meters });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "empty-mesh");
        Assert.StartsWith("A.nwc: ", diagnostic.Message);
        Assert.Equal(guid.ToString("D"), diagnostic.SourceId);
    }

    private static NwcDocument ElementWithEmptyMesh(string sourcePath, Guid guid)
    {
        var root = new NwcNode { Id = 1, Name = "Root", Class = "Model" };
        var child = new NwcNode { Id = 2, Name = "Child", Class = "Model", IsElement = true, ElementGuid = guid };
        child.Element = child;
        root.Children.Add(child);
        var mesh = new NwcMesh { Id = 1, Vertices = [], Indices = [], Node = child };
        child.Mesh = mesh;
        return NwcDocument.Create(new NwcFileInfo { SourcePath = sourcePath }, meshes: [mesh], rootNode: root);
    }

    // A .nwf-like file whose XRef table lists the given paths.
    private static byte[] XRefFile(params string[] paths) => SyntheticNwcFile.Build(null, null, [], [], extraStreams:
        [("LcOpXRefTable", System.Text.Encoding.UTF8.GetBytes(
            "{\"Type\":\"XRefTable\",\"Version\":1,\"SavedFilename\":\"fed.nwf\",\"Table\":["
            + string.Join(",", paths.Select(p => $"{{\"OriginalPath\":\"{p}\",\"RemappedPath\":\"\"}}")) + "]}"))]);

    [Fact]
    public void Read_SavedViewpointInFeet_ScalesPositionsAndKeepsUpUnitLength()
    {
        var viewpoint = new NwcSavedViewpoint
        {
            Id = Guid.NewGuid(),
            Name = "Feet",
            Camera = new NwcCamera
            {
                Position = new NwcIO.Model.Vector3d(1, 2, 3),
                Rotation = Quaterniond.Identity,
                FocalDistance = 10,
                FieldOfView = 90,
                AspectRatio = 1.5,
                NearDistance = 1,
                FarDistance = 500,
            },
        };
        var document = NwcDocument.Create(
            savedViewpoints: [viewpoint],
            rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions
        {
            ModelKey = "feet",
            LengthUnit = NwcLengthUnit.Feet,
        }));

        var camera = Assert.Single(result.Document.Viewpoints).Camera;
        Assert.Equal(0.3048, camera.Position.X, 6);
        Assert.Equal(0.6096, camera.Position.Y, 6);
        Assert.Equal(0.9144, camera.Position.Z, 6);
        Assert.Equal(0.3048, camera.Target.X, 6);
        Assert.Equal(0.6096, camera.Target.Y, 6);
        Assert.Equal(-2.1336, camera.Target.Z, 6);
        Assert.Equal(0, camera.Up.X, 6);
        Assert.Equal(1, camera.Up.Y, 6);
        Assert.Equal(0, camera.Up.Z, 6);
        Assert.Equal(0.3048, camera.NearDistance, 6);
        Assert.Equal(152.4, camera.FarDistance, 6);
        Assert.Equal(Math.PI / 2, camera.FieldOfView, 6);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "viewpoint-camera-approximated");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "viewpoint-content-skipped");
    }

    [Fact]
    public void Read_FileSavedViewpoint_ReportsAnApproximatedCamera()
    {
        var document = NwcDocument.Create(savedViewpoints:
        [
            new NwcSavedViewpoint
            {
                Id = null,
                IsCameraApproximated = true,
                Name = "From file",
                Camera = new NwcCamera
                {
                    Position = new NwcIO.Model.Vector3d(1, 2, 3),
                    Rotation = Quaterniond.Identity,
                },
            },
        ], rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions
        {
            ModelKey = "file",
            LengthUnit = NwcLengthUnit.Meters,
        }));

        Assert.Contains(result.Diagnostics,
            d => d.Code == "viewpoint-camera-approximated" && d.Severity == SceneSeverity.Info);
    }

    [Fact]
    public void Read_SavedViewpointWithoutIdAndWithDecodedCamera_IsNotReportedAsApproximated()
    {
        // An XML viewpoint without a guid has no Id but a real camera.
        var document = NwcDocument.Create(savedViewpoints:
        [
            new NwcSavedViewpoint
            {
                Id = null,
                Name = "No guid",
                Camera = new NwcCamera { Position = new NwcIO.Model.Vector3d(1, 2, 3), Rotation = Quaterniond.Identity },
            },
        ], rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions { ModelKey = "k", LengthUnit = NwcLengthUnit.Meters }));

        Assert.Single(result.Document.Viewpoints);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "viewpoint-camera-approximated");
    }

    private static NwcSavedViewpoint ViewpointWithFov(string name, double fov) => new()
    {
        Name = name,
        Camera = new NwcCamera { Position = new NwcIO.Model.Vector3d(1, 2, 3), Rotation = Quaterniond.Identity, FieldOfView = fov },
    };

    [Fact]
    public void Read_SavedViewpointWithUnconvertibleCamera_IsSkippedAndCounted()
    {
        var document = NwcDocument.Create(savedViewpoints:
        [
            ViewpointWithFov("Good", 60),
            ViewpointWithFov("Bad", 180),
            ViewpointWithFov("Nan", double.NaN),
        ], rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions { ModelKey = "k", LengthUnit = NwcLengthUnit.Meters }));

        Assert.Equal("Good", Assert.Single(result.Document.Viewpoints).Name);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "viewpoints-skipped");
        Assert.Equal(SceneSeverity.Warning, diagnostic.Severity);
        Assert.Equal("2 saved viewpoints could not be converted.", diagnostic.Message);
    }

    [Fact]
    public void Read_SavedViewpointsWithUnknownUnit_AreAllSkipped()
    {
        var document = NwcDocument.Create(savedViewpoints: [ViewpointWithFov("A", 60), ViewpointWithFov("B", 60)],
            rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions { ModelKey = "k", LengthUnit = NwcLengthUnit.Unknown }));

        Assert.Empty(result.Document.Viewpoints);
        Assert.Contains(result.Diagnostics, d => d.Code == "viewpoints-skipped" && d.Message == "2 saved viewpoints could not be converted.");
    }

    [Fact]
    public void Read_SavedViewpointsWithSectionPlanesOrBox_ReportSkippedContent()
    {
        var camera = new NwcCamera { Position = new NwcIO.Model.Vector3d(1, 2, 3), Rotation = Quaterniond.Identity };
        var document = NwcDocument.Create(savedViewpoints:
        [
            new NwcSavedViewpoint { Name = "Planes", Camera = camera, SectionPlanes = [new NwcIO.Model.Sectioning.NwcSectionPlane { Normal = new NwcIO.Model.Vector3d(1, 0, 0) }] },
            new NwcSavedViewpoint { Name = "Box", Camera = camera, SectionBox = new NwcIO.Model.Sectioning.NwcSectionBox { Bounds = new NwcIO.Model.BoundingBox3d(new NwcIO.Model.Vector3d(0, 0, 0), new NwcIO.Model.Vector3d(1, 1, 1)), IsEnabled = true } },
            new NwcSavedViewpoint { Name = "Plain", Camera = camera },
        ], rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions { ModelKey = "k", LengthUnit = NwcLengthUnit.Meters }));

        Assert.Equal(3, result.Document.Viewpoints.Count);
        Assert.Contains(result.Diagnostics, d => d.Code == "viewpoint-content-skipped" && d.Message.StartsWith("2 saved viewpoints", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_SavedViewpointWithComments_ReportsSkippedContent()
    {
        var document = NwcDocument.Create(savedViewpoints:
        [
            new NwcSavedViewpoint
            {
                Name = "Review",
                Comments = ["Check the opening"],
                Camera = new NwcCamera
                {
                    Position = new NwcIO.Model.Vector3d(1, 2, 3),
                    Rotation = Quaterniond.Identity,
                },
            },
        ], rootNode: new NwcNode { Id = 1, Name = "Root", Class = "Model" });

        var result = Valid(NwcSceneReader.Read(document, new NwcSceneOptions
        {
            ModelKey = "comments",
            LengthUnit = NwcLengthUnit.Meters,
        }));

        Assert.Contains(result.Diagnostics,
            d => d.Code == "viewpoint-content-skipped" && d.Severity == SceneSeverity.Info);
    }

    [Fact]
    public void Read_FileWithSavedViews_SkipsThemWithAWarning()
    {
        var result = Scene(RevitSample.Build(extraStreams: [("LcOpSavedViewsElement", new byte[8])]));
        Assert.Empty(result.Document.Viewpoints);
        AssertSampleDiagnostics(result, (SceneSeverity.Warning, "viewpoints-skipped"));
    }

    // RevitSample holds two values the adapter leaves out for a reason other than the unit (value types 0 and 5), so every conversion of it
    // reports them in one `properties-skipped` entry, after the other diagnostics the test expects.
    private static void AssertSampleDiagnostics(NwcSceneResult result, params (SceneSeverity Severity, string Code)[] others)
    {
        Assert.Equal([.. others, (SceneSeverity.Info, "properties-skipped")], result.Diagnostics.Select(d => (d.Severity, d.Code)));
        Assert.StartsWith("2 property values", result.Diagnostics[^1].Message);
    }

    // The XRef table of a .nwf-like file with no model segments, as in ModelGraphReaderTests: the document opens with its references and no meshes.
    private static byte[] XRefOnlyFile(int references) => SyntheticNwcFile.Build(null, null, [], [], extraStreams:
        [("LcOpXRefTable", System.Text.Encoding.UTF8.GetBytes(
            "{\"Type\":\"XRefTable\",\"Version\":1,\"SavedFilename\":\"fed.nwf\",\"Table\":["
            + string.Join(",", Enumerable.Range(0, references).Select(i => $"{{\"OriginalPath\":\"m{i}.nwc\",\"RemappedPath\":\"\"}}")) + "]}"))]);

    [Fact]
    public void Read_FileWithXRefs_ConvertsItAndReportsTheReferencesInBothOverloads()
    {
        byte[] file = XRefOnlyFile(2);
        Assert.Equal(2, Doc(file).XRefs!.References.Count);
        using var temp = TempFile(file, "fed.nwf");
        foreach (var result in new[] { NwcSceneReader.Read(temp.Path), Scene(file, new() { ModelKey = "fed.nwf" }) })
        {
            Assert.Empty(result.Document.Instances);
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "xrefs-skipped");   // the loader names no unit, so `unit-unknown` is there too
            Assert.Equal(SceneSeverity.Warning, diagnostic.Severity);
            Assert.Contains("lists 2 external references", diagnostic.Message);
        }
    }

    [Fact]
    public void Read_EmptyXRefTable_ReportsNoXRefs() =>
        Assert.DoesNotContain(Scene(XRefOnlyFile(0), new() { ModelKey = "k" }).Diagnostics, d => d.Code == "xrefs-skipped");

    // A document whose tree is a root and one group (tag 53: a group never counts as a leaf outside elements), so that a
    // document without the content under test converts with no diagnostic when the loader names a unit.
    private static NwcDocument DocumentWith(Action<NwcDocument>? edit = null, IEnumerable<Model.Sets.NwcSetItem>? selectionSets = null,
        Model.TimeLiner.NwcTimeLinerSchedule? timeLiner = null, NwcXRefTable? xRefs = null, string loader = "LcNwcLoaderPlugin:lcldrevit",
        params NwcPropertyTab[] tabs)
    {
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        var group = new NwcNode { Id = 1, Name = "G", Class = "Group", Tag = 53, Parent = root };
        group.PropertyTabs.AddRange(tabs); root.Children.Add(group);
        var document = NwcDocument.Create(new NwcFileInfo { LoaderPlugin = loader }, rootNode: root,
            selectionSets: selectionSets, timeLiner: timeLiner, xRefs: xRefs);
        edit?.Invoke(document);
        return document;
    }

    private static NwcSceneResult Convert(NwcDocument document) => Valid(NwcSceneReader.Read(document, new() { ModelKey = "k" }));

    [Fact]
    public void Read_DocumentWithoutSkippedContent_GetsNoneOfTheSkippedCodes()
    {
        var emptyTable = new NwcXRefTable("XRefTable", 1, "x.nwf", []);
        Assert.Empty(Convert(DocumentWith(xRefs: emptyTable, selectionSets: [])).Diagnostics);
        Assert.DoesNotContain(Scene(RevitSample.Build()).Diagnostics, d => d.Code is "xrefs-skipped" or "selection-sets-skipped" or "timeliner-skipped" or "clash-tests-skipped");
    }

    [Fact]
    public void Read_DocumentWithSelectionSets_ReportsThem()
    {
        var result = Convert(DocumentWith(selectionSets: [new Model.Sets.NwcSelectionSetFolder("F")]));
        Assert.Equal((SceneSeverity.Warning, "selection-sets-skipped"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
    }

    [Fact]
    public void Read_DocumentWithTimeLiner_ReportsIt()
    {
        var result = Convert(DocumentWith(timeLiner: new Model.TimeLiner.NwcTimeLinerSchedule("S")));
        Assert.Equal((SceneSeverity.Warning, "timeliner-skipped"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
    }

    [Fact]
    public void Read_DocumentWithClashTests_ReportsThem()
    {
        var result = Convert(DocumentWith(document => document.ClashTests.Add(new Model.Clash.NwcClashTest())));
        Assert.Equal((SceneSeverity.Warning, "clash-tests-skipped"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
    }

    [Fact]
    public void Read_DocumentWithEveryKindOfSkippedContent_ReportsEachCodeOnce()
    {
        var table = new NwcXRefTable("XRefTable", 1, "x.nwf", [new NwcXRef("a.nwc", "")]);
        var document = DocumentWith(document => document.ClashTests.Add(new Model.Clash.NwcClashTest()),
            [new Model.Sets.NwcSelectionSetFolder("F")], new Model.TimeLiner.NwcTimeLinerSchedule("S"), table, tabs: Tab(("N", 13, "x")));
        Assert.Equal(["xrefs-skipped", "selection-sets-skipped", "timeliner-skipped", "clash-tests-skipped", "properties-skipped"],
            Convert(document).Diagnostics.Select(d => d.Code));
    }

    private static NwcPropertyTab Tab(params (string Name, int Type, string? Value)[] values) => new()
    {
        Name = "T",
        InternalName = "LcRevitData_Element",
        Properties = [.. values.Select(v => new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = v.Name, Class = "T", ValueType = v.Type, Value = v.Value })],
    };

    [Theory]   // every way a value is left out for a reason other than the unit
    [InlineData(13, "x")]     // unknown value type
    [InlineData(4, null)]     // no text
    [InlineData(1, "1,5")]    // numeric text that does not parse
    [InlineData(6, "NaN")]    // not finite
    [InlineData(10, "x")]
    [InlineData(11, "x")]
    [InlineData(7, "x")]
    public void Read_ValueLeftOut_ReportsAnAggregatedInfo(int type, string? value)
    {
        var result = Convert(DocumentWith(tabs: Tab(("N", type, value), ("Kept", 4, "v"))));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((SceneSeverity.Info, "properties-skipped"), (diagnostic.Severity, diagnostic.Code));
        Assert.StartsWith("1 property values", diagnostic.Message);
    }

    [Fact]
    public void Read_ValuesLeftOut_AreCountedOncePerTab()
    {
        var shared = Tab(("A", 13, "x"), ("B", 1, "x"), ("Kept", 4, "v"));
        var other = Tab(("C", 6, "NaN"));
        var root = new NwcNode { Id = 0, Name = "M", Class = "Model" };
        for (int i = 1; i <= 3; i++)
        {
            var group = new NwcNode { Id = i, Name = "G" + i, Class = "Group", Tag = 53, Parent = root };
            group.PropertyTabs.AddRange([shared, other]);
            root.Children.Add(group);
        }

        var result = Convert(NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }, rootNode: root));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("properties-skipped", diagnostic.Code);
        Assert.StartsWith("3 property values", diagnostic.Message);
        Assert.Single(result.Document.PropertySets);   // the tab holding only the NaN has no set, and three nodes share it
    }

    [Theory]   // a dimensioned value in an unknown unit is `unit-unknown`'s, not `properties-skipped`'s, whatever its text
    [InlineData(6, "1")]
    [InlineData(7, "1")]
    [InlineData(10, "1")]
    [InlineData(11, "1")]
    [InlineData(6, "NaN")]
    public void Read_DimensionedValueInUnknownUnit_IsNotCountedAsSkipped(int type, string text)
    {
        var result = Convert(DocumentWith(loader: "LcNwcLoaderPlugin:mock", tabs: Tab(("N", type, text), ("Kept", 4, "v"))));
        Assert.Equal("unit-unknown", Assert.Single(result.Diagnostics).Code);
    }

    private static NwcSceneResult Valid(NwcSceneResult r)
    {
        Assert.Empty(r.Document.Validate());
        return r;
    }

    private static NwcDocument Doc(byte[] file, NwcReaderOptions? o = null) => NwcDocument.Open(new MemoryStream(file), o);

    private static NwcSceneResult Scene(byte[] file, NwcSceneOptions? o = null) =>
        Valid(NwcSceneReader.Read(Doc(file), o ?? new() { ModelKey = "Tower.nwc" }));

    private static TempDirectoryFile TempFile(byte[] bytes, string name) => new(bytes, name);

    // A file in a fresh directory under the temp path; Dispose deletes the directory.
    private sealed class TempDirectoryFile : IDisposable
    {
        private readonly string _directory;

        public TempDirectoryFile(byte[] bytes, string name)
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nwcio-scene-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, name);
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}

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
        Assert.StartsWith("NwcIO.Scene needs the scene tree: .nwd partition trees and Revit links are not supported yet. ", ex.Message);
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
        Assert.Equal((1, 1, "Level 1", (double?)null), (level.Id, level.ModelId, level.Name, level.Elevation));
        Assert.Equal([null, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, null, null, null], doc.Elements.Select(e => e.LevelId));
        Assert.Equal(4, result.ElementCount);
        Assert.Empty(result.Diagnostics);
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

        var tab = new NwcPropertyTab { Name = "T", InternalName = "LcRevitData_Type",
            Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = 4, Value = "v" }] };
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

    [Theory]   // guard: before this task no set is written at all
    [InlineData(6, "NaN")]
    [InlineData(6, "x")]
    [InlineData(1, "1,5")]
    [InlineData(4, null)]
    [InlineData(13, "x")]
    public void Read_ValueThatCannotBeShown_IsLeftOut(int type, string? value)
    {
        var tab = new NwcPropertyTab { Name = "T", InternalName = "LcRevitData_Element",
            Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = "N", Class = "T", ValueType = type, Value = value }] };
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
        Assert.Equal([0, 1], doc.Geometries[2].Indices);
        Assert.Equal([2, 3], doc.Geometries[3].Indices);
        Assert.All(doc.Geometries.Skip(1), g => Assert.Null(g.Normals));   // lines never carry normals
    }

    [Fact]
    public void Read_RotatedInstance_KeepsTheRowVectorAxes()
    {
        // 90° about Z, translation (1, 2, 3) ft: NwcTransform.ToMatrix4x4 rows are the images of X, Y, Z (row vectors)
        double h = Math.Sqrt(0.5);
        var mesh = new NwcMesh { Id = 0, Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], Indices = [0, 1, 2],
            Transform = new NwcTransform { Id = 0, Rotation = new(0, 0, h, h), Translation = new(1, 2, 3) } };
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

    [Fact]
    public void Read_ZeroNormals_AreAveragedFromTheTriangles()
    {
        // two triangles of a unit square in z = 0, CCW seen from +Z; vertex 3 stores no normal
        var mesh = new NwcMesh { Id = 0, GeometryIndex = -1, Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)], Indices = [0, 1, 2, 2, 1, 3],
            Normals = [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), NwcIO.Model.Vector3d.Zero] };
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
        Assert.Equal((SceneSeverity.Warning, "unit-unknown"), result.Diagnostics.Select(d => (d.Severity, d.Code)).Single());
        Assert.Empty(result.Document.Validate());

        var converted = Scene(RevitSample.Build("LcNwcLoaderPlugin:mock"), new() { ModelKey = "k", LengthUnit = NwcLengthUnit.Feet });
        Assert.Equal(6, converted.InstanceCount);
        Assert.DoesNotContain(converted.Diagnostics, d => d.Code == "unit-unknown");
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

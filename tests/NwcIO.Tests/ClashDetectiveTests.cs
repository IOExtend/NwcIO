using System.IO.Compression;
using System.Xml.Linq;
using NwcIO.Model;
using NwcIO.Model.Clash;
using NwcIO.Model.Appearance;
using NwcIO.Model.Sectioning;
using NwcIO.Model.Sets;
using Xunit;

namespace NwcIO.Tests;

public sealed class ClashDetectiveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClashBcfExport_ValidatesAgainstBcf21Schemas(bool includeIfcGuids)
    {
        var clash = new NwcClashResult
        {
            Item1 = new NwcClashObject("Pipe", includeIfcGuids ? 1 : null),
            Item2 = new NwcClashObject("Wall", includeIfcGuids ? 2 : null),
            AssignedTo = "coordinator@example.org",
            Description = "Pipe intersects wall",
            Distance = -0.02,
            GridLocation = "A-1",
        };
        clash.AddComment("Add a sleeve", "Reviewer");

        using var stream = new MemoryStream();
        NwcClashBcfExporter.Write(stream, [clash]);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        BcfSchemaAssert.ValidArchive(archive, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupBcfExport_ValidatesAgainstBcf21Schemas(bool includeIfcGuids)
    {
        var clash = new NwcClashResult
        {
            Item1 = new NwcClashObject("Pipe", includeIfcGuids ? 1 : null),
            Item2 = new NwcClashObject("Wall", includeIfcGuids ? 2 : null),
        };
        clash.AddComment("Child comment", "Reviewer");
        var group = new NwcClashGroup("Pipe clashes");
        group.Clashes.Add(clash);
        group.Comments.Add(new NwcClashComment("Group comment", "Coordinator"));
        var test = new NwcClashTest("Coordination");
        test.Groups.Add(group);
        test.Results.Add(new NwcClashResult());

        using var stream = new MemoryStream();
        NwcClashBcfExporter.Write(stream, test);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        BcfSchemaAssert.ValidArchive(archive, 2);

        using var markup = archive.GetEntry($"{group.GroupGuid:D}/markup.bcf")!.Open();
        var root = XDocument.Load(markup).Root!;
        Assert.Collection(root.Elements("Comment"),
            c => Assert.Equal("Group comment", c.Element("Comment")!.Value),
            c => Assert.Equal("Child comment", c.Element("Comment")!.Value));
        Assert.Equal("viewpoint.bcfv", root.Element("Viewpoints")?.Element("Viewpoint")?.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshClashDetectionPreservesSharedVertexForNearlyParallelTriangles(bool swapMeshes)
    {
        var v = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(0.01, 0, 0), new(0, 0.01, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(0.01, 0.01, 0)),
        };
        var u = new NwcMesh
        {
            Id = 2,
            Vertices = [new(0, 0, 0), new(1, 1, 0.00000018), new(1, 1.001, 0.00000018009)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1.001, 0.00000018009)),
        };
        var doc = new NwcDocumentBuilder()
            .AddMesh(swapMeshes ? u : v)
            .AddMesh(swapMeshes ? v : u)
            .Build();
        var test = new NwcClashTest("Nearly parallel shared vertex", NwcClashTestType.Hard);

        NwcClashDetector.Run(doc, test);

        var clash = Assert.Single(test.Results);
        Assert.Equal(Vector3d.Zero, clash.ClashPoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshClashDetectionPreservesIntersectionsWhenTriangleSizesDiffer(bool swapMeshes)
    {
        var large = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(100000000, 0, 0), new(0, 100000000, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(100000000, 100000000, 0)),
        };
        var small = new NwcMesh
        {
            Id = 2,
            Vertices = [new(99999999, 3, -1), new(99999999, 3, 1), new(99999999, -1, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(99999999, -1, -1), new(99999999, 3, 1)),
        };
        var doc = new NwcDocumentBuilder()
            .AddMesh(swapMeshes ? small : large)
            .AddMesh(swapMeshes ? large : small)
            .Build();
        var test = new NwcClashTest("Different triangle sizes", NwcClashTestType.Hard);

        NwcClashDetector.Run(doc, test);

        // (99999999, 0.5, 0) lies strictly inside both triangles.
        Assert.Single(test.Results);
    }

    [Theory]
    [InlineData(0.001, 0.008, false)]
    [InlineData(1, 0.008, false)]
    [InlineData(1000, 0.008, false)]
    [InlineData(0.001, 0.002, true)]
    [InlineData(1, 0.002, true)]
    [InlineData(1000, 0.002, true)]
    [InlineData(0.001, 0.005, true)]
    [InlineData(1, 0.005, true)]
    [InlineData(1000, 0.005, true)]
    public void TriangleIntersectionPerpendicularPairsAreScaleIndependent(double scale, double y, bool expected)
    {
        var v0 = Vector3d.Zero;
        var v1 = new Vector3d(0.01, 0, 0) * scale;
        var v2 = new Vector3d(0, 0.01, 0) * scale;
        var u0 = new Vector3d(0.005, y, -0.005) * scale;
        var u1 = new Vector3d(0.005, y, 0.005) * scale;
        var u2 = new Vector3d(0.005, 0.02, 0) * scale;

        Assert.Equal(expected, NwcTriangleIntersection.TrianglesIntersect(v0, v1, v2, u0, u1, u2, out _));
        Assert.Equal(expected, NwcTriangleIntersection.TrianglesIntersect(u0, u1, u2, v0, v2, v1, out _));
    }

    [Theory]
    [InlineData(0.001, 0)]
    [InlineData(1, 0)]
    [InlineData(1000, 0)]
    [InlineData(0.001, 1)]
    [InlineData(1, 1)]
    [InlineData(1000, 1)]
    [InlineData(0.001, 2)]
    [InlineData(1, 2)]
    [InlineData(1000, 2)]
    public void TriangleIntersectionCoplanarPairsUseProjectedOverlap(double scale, int axis)
    {
        Vector3d Point(double x, double y)
        {
            return (axis == 0 ? new Vector3d(0, x, y) :
                axis == 1 ? new Vector3d(x, 0, y) : new Vector3d(x, y, 0)) * scale;
        }

        var v0 = Point(0, 0);
        var v1 = Point(1, 0);
        var v2 = Point(0, 1);
        var pairs = new[]
        {
            (Point(1, 1), Point(0.6, 1), Point(1, 0.6), false), // Disjoint despite AABB overlap.
            (Point(0.2, 0.2), Point(1, 0.2), Point(0.2, 1), true), // Crossing edges.
            (Point(0.1, 0.1), Point(0.2, 0.1), Point(0.1, 0.2), true), // Containment.
            (Point(1, 0), Point(0, 1), Point(1, 1), true), // Shared edge.
            (Point(1, 0), Point(2, 0), Point(1, 1), true), // Shared vertex.
        };

        foreach (var (u0, u1, u2, expected) in pairs)
        {
            Assert.Equal(expected, NwcTriangleIntersection.TrianglesIntersect(v0, v1, v2, u0, u1, u2, out _));
            Assert.Equal(expected, NwcTriangleIntersection.TrianglesIntersect(u0, u2, u1, v0, v1, v2, out _));
        }
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1)]
    [InlineData(1000)]
    public void TriangleIntersectionRejectsZeroAreaTriangles(double scale)
    {
        var a = Vector3d.Zero;
        var b = new Vector3d(1, 0, 0) * scale;
        var c = new Vector3d(0, 1, 0) * scale;
        foreach (var end in new[] { a, b * 0.5 })
        {
            Assert.False(NwcTriangleIntersection.TrianglesIntersect(a, b, c, a, b, end, out _));
            Assert.False(NwcTriangleIntersection.TrianglesIntersect(a, b, end, a, b, c, out _));
        }
    }

    [Fact]
    public void TriangleIntersectionDetectsCrossingTriangles()
    {
        // Triangle 1 on XY plane: z = 0
        var v0 = new Vector3d(-2, 0, 0);
        var v1 = new Vector3d(2, -2, 0);
        var v2 = new Vector3d(2, 2, 0);

        // Triangle 2 vertical crossing through z = 0
        var u0 = new Vector3d(0, 0, -2);
        var u1 = new Vector3d(0, 0, 2);
        var u2 = new Vector3d(1, 0, 0);

        bool intersects = NwcTriangleIntersection.TrianglesIntersect(v0, v1, v2, u0, u1, u2, out var pt);

        Assert.True(intersects);
        Assert.True(Math.Abs(pt.Z) < 0.5); // Intersection point should be near z = 0
    }

    [Fact]
    public void TriangleIntersectionReturnsFalseForDisjointTriangles()
    {
        var v0 = new Vector3d(0, 0, 0);
        var v1 = new Vector3d(1, 0, 0);
        var v2 = new Vector3d(0, 1, 0);

        var u0 = new Vector3d(10, 10, 10);
        var u1 = new Vector3d(11, 10, 10);
        var u2 = new Vector3d(10, 11, 10);

        bool intersects = NwcTriangleIntersection.TrianglesIntersect(v0, v1, v2, u0, u1, u2, out _);

        Assert.False(intersects);
    }

    [Fact]
    public void MeshClashDetectionIdentifiesInterpenetratingElements()
    {
        // Structural Beam: Box centered at origin, 10m long along X, 0.4m wide, 0.6m high
        var beamMesh = CreateBoxMesh(1, new Vector3d(-5, -0.2, -0.3), new Vector3d(5, 0.2, 0.3));
        var beamNode = new NwcNode { Id = 101, Name = "Beam W12x26", Class = "LcOaNode", Mesh = beamMesh };
        beamMesh.Node = beamNode;

        // HVAC Duct: Box crossing perpendicular through the beam along Y at X=0
        var ductMesh = CreateBoxMesh(2, new Vector3d(-0.3, -2, -0.2), new Vector3d(0.3, 2, 0.2));
        var ductNode = new NwcNode { Id = 201, Name = "Supply Air Duct", Class = "LcOaNode", Mesh = ductMesh };
        ductMesh.Node = ductNode;

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("CommercialCenter.rvt")
            .AddMesh(beamMesh)
            .AddMesh(ductMesh)
            .Build();

        var test = new NwcClashTest("Structural vs HVAC", NwcClashTestType.Hard, tolerance: 0.001);
        doc.RunClashDetection(test);

        Assert.Single(test.Results);
        var clash = test.Results[0];

        Assert.Equal("Clash1", clash.Name);
        Assert.Equal(NwcClashStatus.New, clash.Status);
        Assert.True(clash.Distance < 0); // Penetration depth is negative
        Assert.NotNull(clash.Viewpoint);
        Assert.NotNull(clash.Item1.IfcGuid);
        Assert.NotNull(clash.Item2.IfcGuid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ClashDetection_OverlappingSelectionsReportsEachPairOnce(bool federated, bool includeSharedPair)
    {
        var mesh1 = CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2));
        var mesh2 = CreateBoxMesh(2, new(1.5, 0.5, 0.5), new(3.5, 1.5, 1.5));
        var mesh3 = CreateBoxMesh(3, new(3, 0, 0), new(5, 2, 2));
        var builder = new NwcDocumentBuilder().SetFileInfo("Architecture.nwc")
            .AddMesh(mesh1)
            .AddMesh(mesh2);
        if (!federated)
        {
            builder.AddMesh(mesh3);
        }
        var doc = builder.Build();
        var test = new NwcClashTest("Overlapping selections");
        test.SelectionA.ExplicitNodeIds.AddRange([1, 2]);
        test.SelectionB.ExplicitNodeIds.AddRange(includeSharedPair ? [1, 2, 3] : [1, 3]);

        if (federated)
        {
            var otherDoc = new NwcDocumentBuilder().SetFileInfo("MEP.nwc").AddMesh(mesh3).Build();
            NwcClashDetector.Run(NwcAssembly.Create("Federated", doc, otherDoc), test);
        }
        else
        {
            doc.RunClashDetection(test);
        }

        var pairs = test.Results.Select(clash => (
            Math.Min(clash.Item1.Mesh!.Id, clash.Item2.Mesh!.Id),
            Math.Max(clash.Item1.Mesh!.Id, clash.Item2.Mesh!.Id))).OrderBy(pair => pair.Item1).ToArray();
        Assert.Equal([(1, 2), (2, 3)], pairs);
    }

    [Fact]
    public void AssemblyClashDetection_AllVsAllPreservesPairsWithRepeatedMeshIds()
    {
        var architecture = new NwcDocumentBuilder().SetFileInfo("Architecture.nwc")
            .AddMesh(CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2)))
            .Build();
        var mep = new NwcDocumentBuilder().SetFileInfo("MEP.nwc")
            .AddMesh(CreateBoxMesh(1, new(1, 1, 1), new(3, 3, 3)))
            .Build();

        var test = NwcAssembly.Create("Federated", architecture, mep).RunClashDetection();

        var clash = Assert.Single(test.Results);
        Assert.NotEqual(clash.Item1.ModelName, clash.Item2.ModelName);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 1)]
    public void AssemblyClashDetection_ReportsOnlyUniqueCrossModelPairs(bool intersect, bool distinctSelections, int expectedCount)
    {
        var architecture = new NwcDocumentBuilder().SetFileInfo("Architecture.nwc")
            .AddMesh(CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2)))
            .AddMesh(CreateBoxMesh(2, new(1, 0, 0), new(3, 2, 2)))
            .Build();
        double x = intersect ? 1.5 : 100;
        var mep = new NwcDocumentBuilder().SetFileInfo("MEP.nwc")
            .AddMesh(CreateBoxMesh(3, new(x, -1, 0.5), new(x + 1, 3, 1.5)))
            .Build();
        var test = new NwcClashTest("Architecture vs MEP", NwcClashTestType.Hard, tolerance: 0.01);
        test.Rules.Add(NwcClashRule.SameFile());
        if (distinctSelections)
        {
            test.SelectionA.ExplicitNodeIds.Add(2);
            test.SelectionB.ExplicitNodeIds.Add(3);
        }

        NwcClashDetector.Run(NwcAssembly.Create("Federated", architecture, mep), test);

        Assert.Equal(expectedCount, test.TotalClashCount);
        Assert.All(test.Results, clash => Assert.NotEqual(clash.Item1.ModelName, clash.Item2.ModelName));
    }

    private static NwcDocument CreateClashingDocument(string name, params Model.Sets.NwcSetItem[] sets)
    {
        var builder = new NwcDocumentBuilder().SetFileInfo(name)
            .AddMesh(CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2)))
            .AddMesh(CreateBoxMesh(2, new(1, 0, 0), new(3, 2, 2)));
        foreach (var set in sets)
        {
            builder.AddSelectionSet(set);
        }

        return builder.Build();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SetSelection_ExplicitNodeIds_OverrideMissingSet(bool useAssembly, bool selectionB)
    {
        var doc = CreateClashingDocument("Model.nwc");
        var test = new NwcClashTest("Explicit IDs", NwcClashTestType.Hard, tolerance: 0.01);
        var selection = selectionB ? test.SelectionB : test.SelectionA;
        selection.ExplicitNodeIds.Add(1);
        selection.SetNames.Add("unused missing set");

        if (useAssembly)
        {
            NwcClashDetector.Run(NwcAssembly.Create("Federated", doc), test);
        }
        else
        {
            NwcClashDetector.Run(doc, test);
        }

        Assert.Single(test.Results);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SetSelection_FilterCondition_OverridesMissingSet(bool useAssembly, bool selectionB)
    {
        var meshA = CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2));
        var node = new NwcNode { Id = 1, Name = "Selected", Class = "LcOaNode", Mesh = meshA };
        meshA.Node = node;
        var doc = new NwcDocumentBuilder().SetFileInfo("Model.nwc")
            .SetRootNode(node)
            .AddMesh(meshA)
            .AddMesh(CreateBoxMesh(2, new(1, 0, 0), new(3, 2, 2)))
            .Build();
        var test = new NwcClashTest("Filter", NwcClashTestType.Hard, tolerance: 0.01);
        var selection = selectionB ? test.SelectionB : test.SelectionA;
        selection.FilterCondition = new Model.Sets.NwcSearchCondition { Value = "Selected" };
        selection.SetNames.Add("unused missing set");

        if (useAssembly)
        {
            NwcClashDetector.Run(NwcAssembly.Create("Federated", doc), test);
        }
        else
        {
            NwcClashDetector.Run(doc, test);
        }

        Assert.Single(test.Results);
    }

    [Fact]
    public void SetSelection_UnknownSetName_Throws()
    {
        var doc = CreateClashingDocument("Model.nwc");
        var test = new NwcClashTest("Typo", NwcClashTestType.Hard, tolerance: 0.01)
        {
            SelectionA = NwcClashSelection.FromSet("NoSuchSet"),
            SelectionB = NwcClashSelection.All(),
        };

        var ex = Assert.Throws<ArgumentException>(() => NwcClashDetector.Run(doc, test));
        Assert.Contains("NoSuchSet", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetSelection_SetWithoutMeshes_SelectsNothing()
    {
        var doc = CreateClashingDocument("Model.nwc", new Model.Sets.NwcSelectionSet("Empty", [999]));
        var test = new NwcClashTest("Empty set", NwcClashTestType.Hard, tolerance: 0.01)
        {
            SelectionA = NwcClashSelection.FromSet("Empty"),
            SelectionB = NwcClashSelection.All(),
        };

        NwcClashDetector.Run(doc, test);

        Assert.Equal(0, test.TotalClashCount);
    }

    [Fact]
    public void SetSelection_SetInOneModelOnly_SelectsNothingFromTheOthers()
    {
        var architecture = CreateClashingDocument("Architecture.nwc", new Model.Sets.NwcSelectionSet("Empty", [999]));
        var mep = CreateClashingDocument("MEP.nwc");
        var test = new NwcClashTest("Federated", NwcClashTestType.Hard, tolerance: 0.01)
        {
            SelectionA = NwcClashSelection.FromSet("Empty"),
            SelectionB = NwcClashSelection.All(),
        };

        NwcClashDetector.Run(NwcAssembly.Create("Federated", architecture, mep), test);

        Assert.Equal(0, test.TotalClashCount);
    }

    [Fact]
    public void SetSelection_SetMissingFromEveryModel_Throws()
    {
        var assembly = NwcAssembly.Create("Federated", CreateClashingDocument("A.nwc"), CreateClashingDocument("B.nwc"));
        var test = new NwcClashTest("Typo", NwcClashTestType.Hard, tolerance: 0.01)
        {
            SelectionA = NwcClashSelection.All(),
            SelectionB = NwcClashSelection.FromSet("NoSuchSet"),
        };

        var ex = Assert.Throws<ArgumentException>(() => NwcClashDetector.Run(assembly, test));
        Assert.Contains("NoSuchSet", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoreSameLayerRuleFiltersOutSameLayerClashes()
    {
        var layerNode = new NwcNode { Id = 10, Name = "Level 1", Class = "LcOaLayer" };

        var meshA = CreateBoxMesh(1, new Vector3d(0, 0, 0), new Vector3d(2, 2, 2));
        var nodeA = new NwcNode { Id = 11, Name = "Element A", Class = "LcOaNode", Parent = layerNode, Mesh = meshA };
        meshA.Node = nodeA;

        var meshB = CreateBoxMesh(2, new Vector3d(1, 1, 1), new Vector3d(3, 3, 3));
        var nodeB = new NwcNode { Id = 12, Name = "Element B", Class = "LcOaNode", Parent = layerNode, Mesh = meshB };
        meshB.Node = nodeB;

        var doc = new NwcDocumentBuilder()
            .AddMesh(meshA)
            .AddMesh(meshB)
            .Build();

        var testWithRule = new NwcClashTest("Layer Test", NwcClashTestType.Hard);
        testWithRule.Rules.Add(NwcClashRule.SameLayer(enabled: true));

        doc.RunClashDetection(testWithRule);

        // Clashes between elements on the same layer must be ignored
        Assert.Empty(testWithRule.Results);
    }

    [Fact]
    public void ClashReportXmlSaveAndReadRoundtrip()
    {
        string tempXml = Path.Combine(Path.GetTempPath(), $"NwcClash_{Guid.NewGuid():N}.xml");
        try
        {
            var test = new NwcClashTest("MEP vs Structure", NwcClashTestType.Hard, tolerance: 0.005);
            test.Rules.Add(NwcClashRule.SameLayer());

            var obj1 = new NwcClashObject("Duct 500x300", 1001, "HVAC.nwc/Level 2/Duct 500x300")
            {
                Layer = "Level 2",
                IfcGuid = "2O2Fr$t4X7Zf8NOew3FL9r",
            };
            var obj2 = new NwcClashObject("Column C-1", 2002, "STR.nwc/Level 2/Column C-1")
            {
                Layer = "Level 2",
                IfcGuid = "3O2Fr$t4X7Zf8NOew3FL9s",
            };

            var result = new NwcClashResult("Clash1", obj1, obj2, new Vector3d(14.2, -6.8, 4.1), -0.045, NwcClashStatus.Active)
            {
                GridLocation = "Grid B-4, Level 2",
                AssignedTo = "mep-contractor@bim.org",
                Description = "Duct penetrates structural column",
            };
            result.AddComment("Reviewed on site. Need penetration sleeve detail.", "lead-architect@bim.org");

            test.AddResult(result);

            NwcXmlClashSerializer.Save(tempXml, [test]);
            Assert.True(File.Exists(tempXml));

            var loaded = NwcXmlClashSerializer.Open(tempXml);
            Assert.Single(loaded);
            var loadedTest = loaded[0];

            Assert.Equal("MEP vs Structure", loadedTest.Name);
            Assert.Equal(NwcClashTestType.Hard, loadedTest.TestType);
            Assert.Equal(0.005, loadedTest.Tolerance);
            Assert.Single(loadedTest.Rules);
            Assert.Single(loadedTest.Results);

            var loadedClash = loadedTest.Results[0];
            Assert.Equal("Clash1", loadedClash.Name);
            Assert.Equal(NwcClashStatus.Active, loadedClash.Status);
            Assert.Equal(-0.045, loadedClash.Distance, 3);
            Assert.Equal(14.2, loadedClash.ClashPoint.X, 1);
            Assert.Equal(-6.8, loadedClash.ClashPoint.Y, 1);
            Assert.Equal(4.1, loadedClash.ClashPoint.Z, 1);
            Assert.Equal("Grid B-4, Level 2", loadedClash.GridLocation);
            Assert.Equal("mep-contractor@bim.org", loadedClash.AssignedTo);

            Assert.Equal("Duct 500x300", loadedClash.Item1.Name);
            Assert.Equal(1001, loadedClash.Item1.ElementId);
            Assert.Equal("2O2Fr$t4X7Zf8NOew3FL9r", loadedClash.Item1.IfcGuid);

            Assert.Single(loadedClash.Comments);
            Assert.Equal("lead-architect@bim.org", loadedClash.Comments[0].Author);
            Assert.Contains("sleeve detail", loadedClash.Comments[0].Body);
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
        }
    }

    [Fact]
    public void ClashBcfZipExportGeneratesValidBcfArchive()
    {
        string tempBcf = Path.Combine(Path.GetTempPath(), $"NwcClashBcf_{Guid.NewGuid():N}.bcfzip");
        try
        {
            var obj1 = new NwcClashObject("Pipe 100mm", 501) { IfcGuid = "1O2Fr$t4X7Zf8NOew3FL9a" };
            var obj2 = new NwcClashObject("Wall 200mm", 601) { IfcGuid = "1O2Fr$t4X7Zf8NOew3FL9b" };

            var clash = new NwcClashResult("Clash_Pipe_Wall", obj1, obj2, new Vector3d(5.0, 10.0, 2.5), -0.02, NwcClashStatus.New)
            {
                GridLocation = "Grid A-1",
            };
            clash.AddComment("Pipe sleeves required", "bim-manager@bim.org");

            NwcClashBcfExporter.Save(tempBcf, [clash], "Coordination Project");
            Assert.True(File.Exists(tempBcf));

            using var archive = ZipFile.OpenRead(tempBcf);
            var versionEntry = archive.GetEntry("bcf.version");
            Assert.NotNull(versionEntry);

            string folder = clash.ResultGuid.ToString("D");
            var markupEntry = archive.GetEntry($"{folder}/markup.bcf");
            Assert.NotNull(markupEntry);

            using (var s = markupEntry.Open())
            {
                var doc = XDocument.Load(s);
                var topic = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Topic", StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(topic);
                Assert.Equal("Clash", (string?)topic.Attribute("TopicType"));
                Assert.Contains("Pipe", (string?)topic.Element(topic.Name.Namespace + "Title") ?? (string?)topic.Elements().FirstOrDefault(e => e.Name.LocalName == "Title"));
            }

            var vpEntry = archive.GetEntry($"{folder}/viewpoint.bcfv");
            Assert.NotNull(vpEntry);

            using (var s = vpEntry.Open())
            {
                var doc = XDocument.Load(s);
                var root = doc.Root;
                Assert.NotNull(root);
                Assert.Equal("VisualizationInfo", root.Name.LocalName);

                var cam = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("PerspectiveCamera", StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(cam);

                var selection = doc.Descendants().Where(e => e.Name.LocalName.Equals("Component", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.Equal(2, selection.Count);
                Assert.Contains(selection, c => (string?)c.Attribute("IfcGuid") == "1O2Fr$t4X7Zf8NOew3FL9a");
                Assert.Contains(selection, c => (string?)c.Attribute("IfcGuid") == "1O2Fr$t4X7Zf8NOew3FL9b");
            }
        }
        finally
        {
            if (File.Exists(tempBcf)) File.Delete(tempBcf);
        }
    }

    [Fact]
    public void NwfManifestSaveAndReadRoundtrip()
    {
        string tempNwf = Path.Combine(Path.GetTempPath(), $"Federation_{Guid.NewGuid():N}.nwf.xml");
        try
        {
            var manifest = new NwcNwfManifest("Hospital Block A Coordination");
            manifest.ModelPaths.Add("AR_Model.nwc");
            manifest.ModelPaths.Add("STR_Model.nwc");
            manifest.ModelPaths.Add("MEP_Model.nwc");

            manifest.ClashTests.Add(new NwcClashTest("AR vs MEP", NwcClashTestType.Hard, 0.002));
            manifest.ClashTests.Add(new NwcClashTest("STR vs MEP", NwcClashTestType.Hard, 0.001));

            manifest.Save(tempNwf);
            Assert.True(File.Exists(tempNwf));

            var loaded = NwcNwfManifest.Open(tempNwf);
            Assert.Equal("Hospital Block A Coordination", loaded.ProjectName);
            Assert.Equal(3, loaded.ModelPaths.Count);
            Assert.Contains("STR_Model.nwc", loaded.ModelPaths);
            Assert.Equal(2, loaded.ClashTests.Count);
            Assert.Equal(0.002, loaded.ClashTests[0].Tolerance);
        }
        finally
        {
            if (File.Exists(tempNwf)) File.Delete(tempNwf);
        }
    }

    [Fact]
    public void NwfManifestRead_PairsLegacyCameraWithItsOwnElement()
    {
        const string xml = """
            <NavisworksFederation>
              <Viewpoints>
                <exchange><viewpoints><viewpoint name="Modern">
                  <camera><position><pos3f x="10" y="20" z="30" /></position></camera>
                </viewpoint></viewpoints></exchange>
                <Viewpoint name="Legacy" cameraX="1" cameraY="2" cameraZ="3" />
              </Viewpoints>
            </NavisworksFederation>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var manifest = NwcNwfManifest.Read(stream);

        Assert.Collection(manifest.SavedViewpoints,
            modern =>
            {
                Assert.Equal("Modern", modern.Name);
                Assert.Equal(new Vector3d(10, 20, 30), modern.Camera.Position);
            },
            legacy =>
            {
                Assert.Equal("Legacy", legacy.Name);
                Assert.Equal(new Vector3d(1, 2, 3), legacy.Camera.Position);
            });
    }

    [Theory]
    [InlineData("2026-01-01T09:00:00.1234567", 0)]
    [InlineData("2026-01-01T09:00:00.1234567Z", 0)]
    [InlineData("2026-01-01T12:00:00.1234567+03:00", 3)]
    public void NwfManifestRead_AssumesUtcForDatesWithoutOffset(string date, int offsetHours)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            $"<NavisworksFederation date=\"{date}\" />"));

        var manifest = NwcNwfManifest.Read(stream);

        var expected = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        Assert.Equal(expected, manifest.CreatedDate);
        Assert.Equal(TimeSpan.FromHours(offsetHours), manifest.CreatedDate.Offset);
        using var output = new MemoryStream();
        manifest.Write(output);
        output.Position = 0;
        Assert.Equal("2026-01-01T09:00:00.1234567Z", (string?)XDocument.Load(output).Root!.Attribute("date"));
        output.Position = 0;
        Assert.Equal(expected, NwcNwfManifest.Read(output).CreatedDate);
    }

    [Fact]
    public void NwfManifestRead_PreservesLegacyViewpointsAndAppearanceRules()
    {
        const string xml = """
            <NavisworksFederation project="Legacy">
              <Viewpoints><Viewpoint name="Review" cameraX="1.25" cameraY="2" cameraZ="3" /></Viewpoints>
              <AppearanceRules><Rule name="Red" category="Item" property="Name" value="Wall" color="rgba(255, 0, 0, 1.00)" /></AppearanceRules>
              <ClashDetective><Test name="Old test" tolerance="0.01" /></ClashDetective>
            </NavisworksFederation>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var manifest = NwcNwfManifest.Read(stream);

        var viewpoint = Assert.Single(manifest.SavedViewpoints);
        Assert.Equal("Review", viewpoint.Name);
        Assert.Equal(new Vector3d(1.25, 2, 3), viewpoint.Camera.Position);
        var rule = Assert.Single(manifest.AppearanceProfiler.Rules);
        Assert.Equal(new RgbaColor(1, 0, 0), rule.Color);
        Assert.Equal("Wall", rule.Condition.Value);
        Assert.Equal(NwcClashTestType.Hard, Assert.Single(manifest.ClashTests).TestType);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("viewpoints")]
    [InlineData("sets")]
    [InlineData("appearance")]
    [InlineData("type")]
    public void NwfManifestRoundtripPreservesCoordinationData(string aspect)
    {
        var manifest = new NwcNwfManifest("Coordination")
        {
            CreatedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(3)).AddTicks(1234567),
        };
        manifest.ModelPaths.Add("a.nwc");
        var bounds = new BoundingBox3d(new(0, 0, 0), new(1, 2, 3));
        manifest.SavedViewpoints.Add(NwcSavedViewpoint.CreateDefault(bounds, "Overview"));
        manifest.SavedViewpoints.Add(NwcSavedViewpoint.CreateTop(bounds, "Top"));
        var folder = new NwcSelectionSetFolder("Review");
        var selection = new NwcSelectionSet("Selected", [7, 9]) { Comment = "Check these" };
        selection.NodePaths.Add("Model/Wall");
        var search = new NwcSearchSet("Walls") { MatchAllConditions = false };
        search.Conditions.Add(new NwcSearchCondition("Item", "Name", NwcSearchOperator.Contains, "Wall"));
        folder.Children.AddRange([selection, search]);
        manifest.Sets.Add(folder);
        var rule = new NwcAppearanceRule("Highlight", search.Conditions[0], new RgbaColor(0.25f, 0.5f, 0.75f), 0.4f)
        {
            Enabled = false,
        };
        manifest.AppearanceProfiler.Rules.Add(rule);
        manifest.ClashTests.Add(new NwcClashTest("Clearance", NwcClashTestType.Clearance, 0.05));

        using var stream = new MemoryStream();
        manifest.Write(stream);
        stream.Position = 0;
        var xml = XDocument.Load(stream);
        stream.Position = 0;
        var loaded = NwcNwfManifest.Read(stream);

        switch (aspect)
        {
            case "date":
                Assert.Equal("2026-01-01T09:00:00.1234567Z", (string?)xml.Root!.Attribute("date"));
                Assert.Equal(manifest.CreatedDate, loaded.CreatedDate);
                break;
            case "viewpoints":
                Assert.Equal(2, loaded.SavedViewpoints.Count);
                for (var i = 0; i < 2; i++)
                {
                    var expected = manifest.SavedViewpoints[i];
                    var actual = loaded.SavedViewpoints[i];
                    Assert.Equal(expected.Id, actual.Id);
                    Assert.Equal(expected.Name, actual.Name);
                    Assert.Equal(expected.Camera.Position, actual.Camera.Position);
                    Assert.Equal(expected.Camera.Rotation, actual.Camera.Rotation);
                    Assert.Equal(expected.Camera.Projection, actual.Camera.Projection);
                }
                break;
            case "sets":
                var loadedFolder = Assert.IsType<NwcSelectionSetFolder>(Assert.Single(loaded.Sets));
                Assert.Equal(folder.SetGuid, loadedFolder.SetGuid);
                var loadedSelection = Assert.IsType<NwcSelectionSet>(loadedFolder.Children[0]);
                Assert.Equal(selection.NodeIds, loadedSelection.NodeIds);
                Assert.Equal(selection.NodePaths, loadedSelection.NodePaths);
                Assert.Equal(selection.Comment, loadedSelection.Comment);
                var loadedSearch = Assert.IsType<NwcSearchSet>(loadedFolder.Children[1]);
                Assert.False(loadedSearch.MatchAllConditions);
                Assert.Equal(search.Conditions[0].Operator, Assert.Single(loadedSearch.Conditions).Operator);
                Assert.Equal("Wall", loadedSearch.Conditions[0].Value);
                break;
            case "appearance":
                var loadedRule = Assert.Single(loaded.AppearanceProfiler.Rules);
                Assert.Equal(rule.Name, loadedRule.Name);
                Assert.Equal(rule.Enabled, loadedRule.Enabled);
                Assert.Equal(rule.Color, loadedRule.Color);
                Assert.Equal(rule.Transparency, loadedRule.Transparency);
                Assert.Equal(rule.Condition.Operator, loadedRule.Condition.Operator);
                Assert.Equal(rule.Condition.Value, loadedRule.Condition.Value);
                break;
            case "type":
                Assert.Equal(NwcClashTestType.Clearance, Assert.Single(loaded.ClashTests).TestType);
                Assert.Equal(0.05, loaded.ClashTests[0].Tolerance);
                break;
        }
    }

    private static NwcMesh CreateBoxMesh(int id, Vector3d min, Vector3d max)
    {
        var vertices = new Vector3d[]
        {
            new(min.X, min.Y, min.Z), // 0
            new(max.X, min.Y, min.Z), // 1
            new(max.X, max.Y, min.Z), // 2
            new(min.X, max.Y, min.Z), // 3
            new(min.X, min.Y, max.Z), // 4
            new(max.X, min.Y, max.Z), // 5
            new(max.X, max.Y, max.Z), // 6
            new(min.X, max.Y, max.Z), // 7
        };

        var indices = new int[]
        {
            0, 2, 1, 0, 3, 2, // bottom
            4, 5, 6, 4, 6, 7, // top
            0, 1, 5, 0, 5, 4, // front
            1, 2, 6, 1, 6, 5, // right
            2, 3, 7, 2, 7, 6, // back
            3, 0, 4, 3, 4, 7  // left
        };

        return new NwcMesh
        {
            Id = id,
            Vertices = vertices,
            Indices = indices,
            BoundingBox = new BoundingBox3d(min, max),
        };
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    public void ClashDetection_FragmentsOfOneElement_DoNotClash(bool federated, bool sharedNode, int expectedCount)
    {
        var meshA = CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2));
        var meshB = CreateBoxMesh(2, new(1, 1, 1), new(3, 3, 3));
        meshA.Node = new NwcNode { Id = 10, Name = "Wall", Class = "LcOaNode" };
        meshB.Node = sharedNode ? meshA.Node : new NwcNode { Id = 11, Name = "Wall", Class = "LcOaNode" };
        var doc = new NwcDocumentBuilder().SetFileInfo("Model.nwc").AddMesh(meshA).AddMesh(meshB).Build();
        var test = new NwcClashTest("Fragments", NwcClashTestType.Hard);

        if (federated)
        {
            NwcClashDetector.Run(NwcAssembly.Create("Federated", doc), test);
        }
        else
        {
            NwcClashDetector.Run(doc, test);
        }

        Assert.Equal(expectedCount, test.TotalClashCount);
    }

    [Theory]
    [InlineData("Level 2", 1)]
    [InlineData("Level 1", 0)]
    public void AssemblyClashDetection_IgnoreSameLayer_ComparesParentLayers(string secondLayer, int expectedCount)
    {
        var meshA = CreateBoxMesh(1, new(0, 0, 0), new(2, 2, 2));
        meshA.Node = new NwcNode
        {
            Id = 10, Name = "Basic Wall", Class = "LcOaNode",
            Parent = new NwcNode { Id = 9, Name = "Level 1", Class = "LcOaLayer" },
        };
        var meshB = CreateBoxMesh(2, new(1, 1, 1), new(3, 3, 3));
        meshB.Node = new NwcNode
        {
            Id = 20, Name = "Basic Wall", Class = "LcOaNode",
            Parent = new NwcNode { Id = 19, Name = secondLayer, Class = "LcOaLayer" },
        };
        var architecture = new NwcDocumentBuilder().SetFileInfo("Architecture.nwc").AddMesh(meshA).Build();
        var structure = new NwcDocumentBuilder().SetFileInfo("Structure.nwc").AddMesh(meshB).Build();
        var test = new NwcClashTest("Layers", NwcClashTestType.Hard);
        test.Rules.Add(NwcClashRule.SameLayer(enabled: true));

        NwcClashDetector.Run(NwcAssembly.Create("Federated", architecture, structure), test);

        Assert.Equal(expectedCount, test.TotalClashCount);
        Assert.All(test.Results, clash =>
        {
            Assert.Equal("Level 1", clash.Item1.Layer);
            Assert.Equal(secondLayer, clash.Item2.Layer);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitNodeIds_MatchNodeIdsAndFallBackToMeshIds(bool federated)
    {
        // The fragment's mesh id 5 equals the other element's node id; only node ids select meshes with nodes.
        var fragment = CreateBoxMesh(5, new(0, 0, 0), new(2, 2, 2));
        fragment.Node = new NwcNode { Id = 100, Name = "Pipe", Class = "LcOaNode" };
        var other = CreateBoxMesh(6, new(1, 1, 1), new(3, 3, 3));
        other.Node = new NwcNode { Id = 5, Name = "Duct", Class = "LcOaNode" };
        var loose = CreateBoxMesh(7, new(1.5, 1.5, 1.5), new(4, 4, 4));
        var doc = new NwcDocumentBuilder().SetFileInfo("Model.nwc").AddMesh(fragment).AddMesh(other).AddMesh(loose).Build();
        var test = new NwcClashTest("Explicit");
        test.SelectionA.ExplicitNodeIds.Add(100);
        test.SelectionB.ExplicitNodeIds.Add(7);

        if (federated)
        {
            NwcClashDetector.Run(NwcAssembly.Create("Federated", doc), test);
        }
        else
        {
            NwcClashDetector.Run(doc, test);
        }

        var clash = Assert.Single(test.Results);
        Assert.Same(fragment, clash.Item1.Mesh);
        Assert.Same(loose, clash.Item2.Mesh);

        test.Results.Clear();
        test.SelectionA.ExplicitNodeIds[0] = 5;
        NwcClashDetector.Run(doc, test);
        Assert.Same(other, Assert.Single(test.Results).Item1.Mesh);
    }

    // Two points take the bounding-box fallback; three would be read as one default triangle that crosses the box.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshClashDetection_LineMeshCrossingBox_ReportsNoClash(bool threeVertices)
    {
        var box = CreateBoxMesh(1, new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
        var line = threeVertices
            ? new NwcMesh { Id = 2, Vertices = [new(-1, 0.5, 0.5), new(2, 0.5, 0.5), new(0.5, 0.5, 2)], Indices = [], LineIndices = [0, 1, 1, 2] }
            : new NwcMesh { Id = 2, Vertices = [new(-1, 0.5, 0.5), new(2, 0.5, 0.5)], Indices = [], LineIndices = [0, 1] };
        var doc = new NwcDocumentBuilder().AddMesh(box).AddMesh(line).Build();
        var test = new NwcClashTest("Lines", NwcClashTestType.Hard, tolerance: 0.001);
        doc.RunClashDetection(test);
        Assert.Empty(test.Results);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Clash_SiblingFragmentsOfOneLeaf_NoClashWithOrWithoutTree(bool tree)
    {
        var doc = NwcDocument.Open(new MemoryStream(QuadFile(2, 2)), new NwcReaderOptions { BuildSceneTree = tree });
        var test = new NwcClashTest("Siblings", NwcClashTestType.Hard);
        NwcClashDetector.Run(doc, test);
        Assert.Empty(test.Results);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Clash_FragmentsOfDifferentLeaves_ClashWithOrWithoutTree(bool tree)
    {
        var doc = NwcDocument.Open(new MemoryStream(QuadFile(2, 3)), new NwcReaderOptions { BuildSceneTree = tree });
        var test = new NwcClashTest("Leaves", NwcClashTestType.Hard);
        NwcClashDetector.Run(doc, test);
        Assert.Single(test.Results);
    }

    [Fact]
    public void Clash_ClippedMeshKeepsElementKey_NoClashWithSiblingWithoutTree()
    {
        var options = new NwcReaderOptions { BuildSceneTree = false };
        var plane = new NwcSectionPlane { Normal = new Vector3d(1, 0, 0), Distance = -1e6, IsEnabled = true };

        var siblings = NwcDocument.Open(new MemoryStream(QuadFile(2, 2)), options).Meshes;
        var sameLeaf = NwcDocument.Create(meshes: [NwcMeshClipper.Clip(siblings[0], [plane]), siblings[1]]);
        var sameTest = new NwcClashTest("Clipped sibling", NwcClashTestType.Hard);
        NwcClashDetector.Run(sameLeaf, sameTest);
        Assert.Empty(sameTest.Results);

        var leaves = NwcDocument.Open(new MemoryStream(QuadFile(2, 3)), options).Meshes;
        var otherLeaf = NwcDocument.Create(meshes: [NwcMeshClipper.Clip(leaves[0], [plane]), leaves[1]]);
        var otherTest = new NwcClashTest("Clipped other leaf", NwcClashTestType.Hard);
        NwcClashDetector.Run(otherLeaf, otherTest);
        Assert.Single(otherTest.Results);
    }

    [Fact]
    public void FederatedClash_SameLeafSeqInTwoDocuments_Clash()
    {
        byte[] file = QuadFile(2, 2);
        var options = new NwcReaderOptions { BuildSceneTree = false };
        var first = NwcDocument.Open(new MemoryStream(file), options);
        var second = NwcDocument.Open(new MemoryStream(file), options);
        var test = new NwcClashTest("Federated", NwcClashTestType.Hard);
        NwcClashDetector.Run(NwcAssembly.Create("Federated", first, second), test);

        // Every pair across the documents clashes; the sibling pair inside each document does not.
        Assert.Equal(4, test.Results.Count);
        Assert.All(test.Results, clash => Assert.NotEqual(first.Meshes.Contains(clash.Item1.Mesh!), first.Meshes.Contains(clash.Item2.Mesh!)));
    }

    /// <summary>H1 + [R1] with one identity-posed class-15 fragment per seq, so every two of its quads overlap exactly.</summary>
    internal static byte[] QuadFile(params int[] seqs) => SyntheticNwcFile.Build(
        FormatVectors.H1,
        SyntheticFragments.Build(seqs.Select(seq => new Frag(seq, Vector3d.Zero, 1, 2, 4, Rec: 1)).ToArray()),
        [FormatVectors.R1],
        [120]);
}

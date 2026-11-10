using System.IO.Compression;
using System.Xml.Linq;
using NwcIO.Model;
using NwcIO.Model.Clash;
using Xunit;

namespace NwcIO.Tests;

public sealed class ClashDetectiveTests
{
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
}

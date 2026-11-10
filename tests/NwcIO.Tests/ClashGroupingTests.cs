using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using NwcIO.Model;
using NwcIO.Model.Clash;
using Xunit;

namespace NwcIO.Tests;

public sealed class ClashGroupingTests
{
    [Fact]
    public void GroupByProximity_ClustersNearbyClashesIntoGroups()
    {
        var test = new NwcClashTest("ProximityTest");

        // Cluster 1 near origin
        var c1 = CreateClash("Clash 1", new Vector3d(0, 0, 0), -0.05);
        var c2 = CreateClash("Clash 2", new Vector3d(0.2, 0.1, 0), -0.08);

        // Cluster 2 far away near (50, 50, 10)
        var c3 = CreateClash("Clash 3", new Vector3d(50, 50, 10), -0.12);
        var c4 = CreateClash("Clash 4", new Vector3d(50.3, 49.8, 10.1), -0.04);

        // Standalone clash
        var c5 = CreateClash("Clash 5", new Vector3d(200, 200, 0), -0.02);

        test.Results.AddRange([c1, c2, c3, c4, c5]);

        int groupsCreated = NwcClashGrouper.GroupByProximity(test, clusterDistance: 2.0, minClusterSize: 2);

        Assert.Equal(2, groupsCreated);
        Assert.Equal(2, test.Groups.Count);
        Assert.Single(test.Results); // c5 remains ungrouped
        Assert.Equal("Clash 5", test.Results[0].Name);
        Assert.Equal(5, test.TotalClashCount);

        // Verify group 1
        var g1 = test.Groups[0];
        Assert.Equal(2, g1.Clashes.Count);
        Assert.NotNull(g1.Viewpoint);
        Assert.True(Math.Abs(g1.ClashPoint.X - 0.1) < 0.2);
        Assert.Equal(-0.08, g1.Distance); // Worst penetration

        // Verify group 2
        var g2 = test.Groups[1];
        Assert.Equal(2, g2.Clashes.Count);
        Assert.NotNull(g2.Viewpoint);
        Assert.True(Math.Abs(g2.ClashPoint.X - 50.15) < 0.2);
        Assert.Equal(-0.12, g2.Distance); // Worst penetration
    }

    [Fact]
    public void GroupByPrimaryElement_BundlesByItem1()
    {
        var test = new NwcClashTest("ElementTest");

        var c1 = CreateClash("C1", new Vector3d(0, 0, 0), -0.01, item1Name: "Pipe-101", guid1: "GUID-PIPE-101");
        var c2 = CreateClash("C2", new Vector3d(1, 0, 0), -0.02, item1Name: "Pipe-101", guid1: "GUID-PIPE-101");
        var c3 = CreateClash("C3", new Vector3d(2, 0, 0), -0.03, item1Name: "Pipe-101", guid1: "GUID-PIPE-101");
        var c4 = CreateClash("C4", new Vector3d(5, 5, 0), -0.01, item1Name: "Duct-202", guid1: "GUID-DUCT-202");

        test.Results.AddRange([c1, c2, c3, c4]);

        int count = NwcClashGrouper.GroupByPrimaryElement(test, minGroupSize: 2);

        Assert.Equal(1, count);
        Assert.Single(test.Groups);
        Assert.Equal(3, test.Groups[0].Clashes.Count);
        Assert.Single(test.Results);
        Assert.Equal("C4", test.Results[0].Name);
    }

    [Fact]
    public void GroupByGrid_BundlesByGridLocation()
    {
        var test = new NwcClashTest("GridTest");

        var c1 = CreateClash("C1", new Vector3d(0, 0, 0), -0.01);
        c1.GridLocation = "A-1";
        var c2 = CreateClash("C2", new Vector3d(0.5, 0, 0), -0.02);
        c2.GridLocation = "A-1";

        var c3 = CreateClash("C3", new Vector3d(20, 0, 0), -0.01);
        c3.GridLocation = "B-2";
        var c4 = CreateClash("C4", new Vector3d(20.5, 0, 0), -0.02);
        c4.GridLocation = "B-2";

        test.Results.AddRange([c1, c2, c3, c4]);

        int count = NwcClashGrouper.GroupByGrid(test, minGroupSize: 2);

        Assert.Equal(2, count);
        Assert.Equal(2, test.Groups.Count);
        Assert.Empty(test.Results);
    }

    [Fact]
    public void GroupByLayer_BundlesByLayerPairs()
    {
        var test = new NwcClashTest("LayerTest");

        var c1 = CreateClash("C1", new Vector3d(0, 0, 0), -0.01, item1Layer: "HVAC", item2Layer: "Structure");
        var c2 = CreateClash("C2", new Vector3d(1, 0, 0), -0.02, item1Layer: "HVAC", item2Layer: "Structure");
        var c3 = CreateClash("C3", new Vector3d(2, 0, 0), -0.03, item1Layer: "Plumbing", item2Layer: "Structure");

        test.Results.AddRange([c1, c2, c3]);

        int count = NwcClashGrouper.GroupByLayer(test, minGroupSize: 2);

        Assert.Equal(1, count);
        Assert.Single(test.Groups);
        Assert.Equal(2, test.Groups[0].Clashes.Count);
        Assert.Single(test.Results);
        Assert.Equal("C3", test.Results[0].Name);
    }

    [Fact]
    public void UngroupAll_RestoresAllClashesToRootResults()
    {
        var test = new NwcClashTest("UngroupTest");
        var c1 = CreateClash("C1", new Vector3d(0, 0, 0), -0.01);
        var c2 = CreateClash("C2", new Vector3d(0.1, 0, 0), -0.02);
        var c3 = CreateClash("C3", new Vector3d(50, 0, 0), -0.03);
        test.Results.AddRange([c1, c2, c3]);

        NwcClashGrouper.GroupByProximity(test, clusterDistance: 1.0);
        Assert.Single(test.Groups);
        Assert.Single(test.Results);

        NwcClashGrouper.UngroupAll(test);

        Assert.Empty(test.Groups);
        Assert.Equal(3, test.Results.Count);
    }

    [Fact]
    public void NwcXmlClashSerializer_RoundtripsGroupsCorrectly()
    {
        var originalTest = new NwcClashTest("XmlGroupTest");
        var group = new NwcClashGroup("Cluster Alpha", NwcClashStatus.Active)
        {
            Description = "Test group description",
        };

        var c1 = CreateClash("Clash Alpha 1", new Vector3d(10.0, 20.0, 5.0), -0.10);
        var c2 = CreateClash("Clash Alpha 2", new Vector3d(11.0, 20.5, 5.0), -0.15);
        group.AddClash(c1);
        group.AddClash(c2);

        originalTest.Groups.Add(group);

        var standalone = CreateClash("Standalone Clash", new Vector3d(100, 100, 0), -0.01);
        originalTest.Results.Add(standalone);

        // Serialize to XML
        using var ms = new MemoryStream();
        NwcXmlClashSerializer.Write(ms, [originalTest]);
        byte[] xmlBytes = ms.ToArray();
        string xmlText = Encoding.UTF8.GetString(xmlBytes);

        Assert.Contains("<clashgroup", xmlText, StringComparison.Ordinal);
        Assert.Contains("name=\"Cluster Alpha\"", xmlText, StringComparison.Ordinal);
        Assert.Contains("name=\"Clash Alpha 1\"", xmlText, StringComparison.Ordinal);
        Assert.Contains("name=\"Standalone Clash\"", xmlText, StringComparison.Ordinal);

        // Deserialize back
        using var readMs = new MemoryStream(xmlBytes);
        var loadedTests = NwcXmlClashSerializer.Read(readMs);

        Assert.Single(loadedTests);
        var loadedTest = loadedTests[0];
        Assert.Equal("XmlGroupTest", loadedTest.Name);
        Assert.Single(loadedTest.Groups);
        Assert.Single(loadedTest.Results);

        var loadedGroup = loadedTest.Groups[0];
        Assert.Equal("Cluster Alpha", loadedGroup.Name);
        Assert.Equal("Test group description", loadedGroup.Description);
        Assert.Equal(2, loadedGroup.Clashes.Count);
        Assert.Equal("Clash Alpha 1", loadedGroup.Clashes[0].Name);
        Assert.Equal("Clash Alpha 2", loadedGroup.Clashes[1].Name);
        Assert.Equal(3, loadedTest.TotalClashCount);
    }

    [Fact]
    public void NwcClashBcfExporter_ExportsGroupedClashesToBcfZip()
    {
        var test = new NwcClashTest("BcfGroupTest");
        var group = new NwcClashGroup("HVAC vs Pipes", NwcClashStatus.Active)
        {
            Description = "Clash cluster along corridor A",
        };

        var c1 = CreateClash("Clash 1", new Vector3d(5.1, 10.0, 3.0), -0.05, guid1: "03X9Wk_0f8pQ123456789A", guid2: "15Y9Wk_0f8pQ123456789B");
        var c2 = CreateClash("Clash 2", new Vector3d(4.9, 10.2, 3.1), -0.075, guid1: "03X9Wk_0f8pQ123456789A", guid2: "22Z9Wk_0f8pQ123456789C");
        group.AddClash(c1);
        group.AddClash(c2);
        test.Groups.Add(group);

        using var ms = new MemoryStream();
        NwcClashBcfExporter.Write(ms, test);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var bcfVersionEntry = zip.GetEntry("bcf.version");
        Assert.NotNull(bcfVersionEntry);

        var markupEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(markupEntry);

        using var reader = new StreamReader(markupEntry.Open());
        string markupXml = reader.ReadToEnd();
        Assert.Contains("TopicType=\"ClashGroup\"", markupXml, StringComparison.Ordinal);
        Assert.Contains("[Group] HVAC vs Pipes (2 items)", markupXml, StringComparison.Ordinal);
        Assert.Contains("Clash Group: HVAC vs Pipes with 2 collisions", markupXml, StringComparison.Ordinal);

        var viewpointEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("viewpoint.bcfv", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(viewpointEntry);
    }

    private static NwcClashResult CreateClash(
        string name,
        Vector3d point,
        double distance,
        string item1Name = "Element A",
        string item2Name = "Element B",
        string guid1 = "1234567890123456789012",
        string guid2 = "9876543210987654321098",
        string item1Layer = "Layer1",
        string item2Layer = "Layer2")
    {
        return new NwcClashResult(
            name: name,
            item1: new NwcClashObject
            {
                Name = item1Name,
                Path = $"Root/Layer1/{item1Name}",
                IfcGuid = guid1,
                Layer = item1Layer,
            },
            item2: new NwcClashObject
            {
                Name = item2Name,
                Path = $"Root/Layer2/{item2Name}",
                IfcGuid = guid2,
                Layer = item2Layer,
            },
            clashPoint: point,
            distance: distance,
            status: NwcClashStatus.Active
        );
    }

    [Fact]
    public void Cli_ClashGrouping_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"nwc_cli_clash_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string nwcPath = Path.Combine(tempDir, "input.nwc");
            string jsonPath = Path.Combine(tempDir, "output.json");
            string clashXmlPath = Path.Combine(tempDir, "clashes.xml");

            var meshA = new NwcMesh
            {
                Id = 1,
                Name = "Beam1",
                Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(2, 2, 0)),
            };
            var meshB = new NwcMesh
            {
                Id = 2,
                Name = "Pipe1",
                Vertices = [new(0.5, 0.5, -1), new(0.5, 0.5, 1), new(1, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0.5, 0.5, -1), new(1, 1, 1)),
            };

            var doc = NwcDocument.Create(meshes: [meshA, meshB]);
            doc.Save(nwcPath);

            int exitCode = NwcIO.Cli.Program.Main([
                nwcPath,
                jsonPath,
                "--clash-xml", clashXmlPath,
                "--clash-group", "proximity",
                "--clash-radius", "5.0",
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(clashXmlPath));
            string xml = File.ReadAllText(clashXmlPath);
            Assert.Contains("<?xml", xml, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

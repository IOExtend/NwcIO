using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class ClashReportTests
{
    [Fact]
    public void NwcClash_CalculatesDerivedPropertiesCorrectly()
    {
        var meshA = new NwcMesh
        {
            Id = 101,
            Vertices = [],
            Indices = [],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(4, 4, 2)),
        };
        var meshB = new NwcMesh
        {
            Id = 202,
            Vertices = [],
            Indices = [],
            BoundingBox = new BoundingBox3d(new(2, 2, 0), new(6, 6, 2)),
        };

        var docA = CreateMockDocument("Architecture.rvt", [meshA]);
        var docB = CreateMockDocument("Structure.rvt", [meshB]);

        var intersection = new BoundingBox3d(new(2, 2, 0), new(4, 4, 2));
        var clash = new NwcClash(docA, meshA, docB, meshB, intersection);

        Assert.Equal(8.0, clash.Volume); // 2 * 2 * 2
        Assert.Equal(new Vector3d(3, 3, 1), clash.Center);
        Assert.Equal("Architecture", clash.ModelAName);
        Assert.Equal("Structure", clash.ModelBName);
        Assert.Equal(22, clash.IfcGuidA.Length);
        Assert.Equal(22, clash.IfcGuidB.Length);
        Assert.Contains("Architecture (#101)", clash.Title, StringComparison.Ordinal);
        Assert.Contains("Structure (#202)", clash.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void BcfExport_CreatesValidZipWithVersionMarkupAndViewpoint()
    {
        var meshA = new NwcMesh
        {
            Id = 10,
            Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(2, 2, 2)),
        };
        var meshB = new NwcMesh
        {
            Id = 20,
            Vertices = [new(1, 1, 1), new(3, 1, 1), new(1, 3, 1)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(1, 1, 1), new(3, 3, 3)),
        };

        var docA = CreateMockDocument("ArchModel.rvt", [meshA]);
        var docB = CreateMockDocument("StrModel.rvt", [meshB]);
        var assembly = NwcAssembly.Create("HospitalCampus", docA, docB);

        var clashes = assembly.FindClashes();
        Assert.Single(clashes);

        using var ms = new MemoryStream();
        assembly.SaveBcf(ms, clashes);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // 1. Check bcf.version
        var versionEntry = zip.GetEntry("bcf.version");
        Assert.NotNull(versionEntry);
        using (var reader = new StreamReader(versionEntry.Open(), Encoding.UTF8))
        {
            string versionContent = reader.ReadToEnd();
            Assert.Contains("VersionId=\"2.1\"", versionContent, StringComparison.Ordinal);
        }

        // 2. Check topic folder
        string topicGuid = clashes[0].Id.ToString("D");
        var markupEntry = zip.GetEntry($"{topicGuid}/markup.bcf");
        Assert.NotNull(markupEntry);

        using (var s = markupEntry.Open())
        {
            var xml = XDocument.Load(s);
            var topic = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Topic");
            Assert.NotNull(topic);
            Assert.Equal("Clash", topic.Attribute("TopicType")?.Value);
            Assert.Equal("Active", topic.Attribute("TopicStatus")?.Value);
            Assert.Equal(topicGuid, topic.Attribute("Guid")?.Value);

            var title = topic.Element("Title");
            Assert.NotNull(title);
            Assert.Contains("ArchModel", title.Value, StringComparison.Ordinal);

            var author = topic.Element("CreationAuthor");
            Assert.NotNull(author);
            Assert.Equal("NwcIO", author.Value);
        }

        // 3. Check viewpoint.bcfv
        var viewpointEntry = zip.GetEntry($"{topicGuid}/viewpoint.bcfv");
        Assert.NotNull(viewpointEntry);

        using (var s = viewpointEntry.Open())
        {
            var xml = XDocument.Load(s);
            var components = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Components");
            Assert.NotNull(components);

            var selection = components.Element("Selection");
            Assert.NotNull(selection);
            var selectedGuids = selection.Elements("Component")
                .Select(c => c.Attribute("IfcGuid")?.Value)
                .ToList();

            Assert.Contains(clashes[0].IfcGuidA, selectedGuids);
            Assert.Contains(clashes[0].IfcGuidB, selectedGuids);

            var cam = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "PerspectiveCamera");
            Assert.NotNull(cam);
            Assert.NotNull(cam.Element("CameraViewPoint"));
            Assert.NotNull(cam.Element("CameraDirection"));
            Assert.NotNull(cam.Element("CameraUpVector"));
        }
    }

    [Fact]
    public void HtmlClashReport_GeneratesInteractiveReportWithMatrix()
    {
        var meshA = new NwcMesh { Id = 1, Vertices = [], Indices = [], BoundingBox = new BoundingBox3d(new(0, 0, 0), new(5, 5, 3)) };
        var meshB = new NwcMesh { Id = 2, Vertices = [], Indices = [], BoundingBox = new BoundingBox3d(new(2, 2, 0), new(7, 7, 3)) };
        var meshC = new NwcMesh { Id = 3, Vertices = [], Indices = [], BoundingBox = new BoundingBox3d(new(4, 4, 0), new(9, 9, 3)) };

        var docA = CreateMockDocument("Architecture.rvt", [meshA]);
        var docB = CreateMockDocument("Structure.rvt", [meshB]);
        var docC = CreateMockDocument("MEP.rvt", [meshC]);

        var assembly = NwcAssembly.Create("MultiDisciplineProject", docA, docB, docC);
        var clashes = assembly.FindClashes();

        using var sw = new StringWriter();
        assembly.SaveClashReportHtml(sw, clashes);
        string html = sw.ToString();

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("NwcIO Clash Detection Report", html, StringComparison.Ordinal);
        Assert.Contains("MultiDisciplineProject", html, StringComparison.Ordinal);
        Assert.Contains("Discipline Cross-Clash Matrix", html, StringComparison.Ordinal);
        Assert.Contains("Architecture", html, StringComparison.Ordinal);
        Assert.Contains("Structure", html, StringComparison.Ordinal);
        Assert.Contains("MEP", html, StringComparison.Ordinal);
        Assert.Contains("clash-badge", html, StringComparison.Ordinal);
        Assert.Contains("applyFilters()", html, StringComparison.Ordinal);
        Assert.Contains("sortTable(", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlClashReport_MatrixKeepsModelNamesOutOfScript()
    {
        const string nameA = "a');alert(1);('";
        const string nameB = "O\"Brien <&>";
        var meshA = new NwcMesh { Id = 1, Vertices = [], Indices = [], BoundingBox = new BoundingBox3d(new(0, 0, 0), new(5, 5, 3)) };
        var meshB = new NwcMesh { Id = 2, Vertices = [], Indices = [], BoundingBox = new BoundingBox3d(new(2, 2, 0), new(7, 7, 3)) };
        var assembly = NwcAssembly.Create("Project",
            CreateMockDocument(nameA + ".rvt", [meshA]),
            CreateMockDocument(nameB + ".rvt", [meshB]));

        using var sw = new StringWriter();
        assembly.SaveClashReportHtml(sw, assembly.FindClashes());
        string html = sw.ToString();

        var cells = html.Split('\n').Where(l => l.Contains("class=\"cell-clash\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell =>
        {
            Assert.Contains("onclick=\"filterPair(this.dataset.modelA, this.dataset.modelB)\"", cell, StringComparison.Ordinal);
            Assert.DoesNotContain("alert", cell.Substring(cell.IndexOf("onclick=", StringComparison.Ordinal), 60), StringComparison.Ordinal);
        });
        Assert.Contains(cells, cell => cell.Contains($"data-model-a=\"{WebUtility.HtmlEncode(nameA)}\" data-model-b=\"{WebUtility.HtmlEncode(nameB)}\"", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains($"data-model-a=\"{WebUtility.HtmlEncode(nameB)}\" data-model-b=\"{WebUtility.HtmlEncode(nameA)}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyClashes_GeneratesValidOutputsWithoutCrashing()
    {
        var docA = CreateMockDocument("EmptyModelA.rvt", []);
        var docB = CreateMockDocument("EmptyModelB.rvt", []);
        var assembly = NwcAssembly.Create("ClearProject", docA, docB);

        // BCF with 0 clashes
        using var ms = new MemoryStream();
        assembly.SaveBcf(ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Single(zip.Entries); // Only bcf.version
        Assert.Equal("bcf.version", zip.Entries[0].FullName);

        // HTML with 0 clashes
        using var sw = new StringWriter();
        assembly.SaveClashReportHtml(sw);
        string html = sw.ToString();
        Assert.Contains("No geometric clashes detected", html, StringComparison.Ordinal);
    }

    private static NwcDocument CreateMockDocument(string sourceName, IReadOnlyList<NwcMesh> meshes)
    {
        using var ms = new MemoryStream();
        ms.Write("#LcUStream-V1.1 binary double release C lichunk-008\n"u8);

        using var zlibMs = new MemoryStream();
        using (var deflater = new ZLibStream(zlibMs, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            using var w = new BinaryWriter(deflater);
            w.Write((long)7);
            byte[] nameBytes = Encoding.UTF8.GetBytes(sourceName);
            w.Write(nameBytes.Length);
            w.Write(nameBytes);
            w.Write(-1);
            w.Write((long)1692371995);
            w.Write((long)1024);
            byte[] pluginBytes = Encoding.UTF8.GetBytes("LcNwcLoaderPlugin:mock");
            w.Write(pluginBytes.Length);
            w.Write(pluginBytes);
        }

        ms.Write(zlibMs.ToArray());
        byte[] containerBytes = ms.ToArray();

        string tempPath = Path.Combine(Path.GetTempPath(), $"mock_{Guid.NewGuid():N}.nwc");
        try
        {
            File.WriteAllBytes(tempPath, containerBytes);
            var doc = NwcDocument.Open(tempPath, new NwcReaderOptions { ReadMeshes = false });

            var backingField = typeof(NwcDocument).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name.Contains("Meshes", StringComparison.OrdinalIgnoreCase));
            if (backingField is not null)
            {
                backingField.SetValue(doc, meshes);
            }

            return doc;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

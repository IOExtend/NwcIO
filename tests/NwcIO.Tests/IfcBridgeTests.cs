using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NwcIO.Internal.Export;
using NwcIO.Model;
using NwcIO.Model.Redline;
using NwcIO.Model.Sectioning;
using Xunit;

namespace NwcIO.Tests;

public class IfcBridgeTests
{
    private static NwcDocument CreateCubeDocument()
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

        var indices = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        };

        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "CubeMesh",
            Vertices = vertices,
            Indices = indices,
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(1000, 1000, 1000)),
        };

        return NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = "Test.nwc" },
            propertyRecords: new List<NwcPropertyRecord>(),
            meshes: new List<NwcMesh> { mesh },
            materials: new List<NwcMaterial>());
    }

    [Fact]
    public void SaveIfc_WithSectionBox_ClipsGeometry()
    {
        var doc = CreateCubeDocument();

        using var sw = new StringWriter();
        var options = new NwcIfcExportOptions
        {
            SectionBox = new NwcSectionBox
            {
                Bounds = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(500, 1000, 1000)),
                IsEnabled = true,
            },
            ClipGeometryWithSectionPlanes = true,
        };

        doc.SaveIfc(sw, options);
        string ifc = sw.ToString();

        Assert.Contains("IFCFACETEDBREP(", ifc);

        var polyloopMatches = Regex.Matches(ifc, @"IFCPOLYLOOP\(\(([^)]+)\)\)");
        Assert.NotEmpty(polyloopMatches);

        var pointIds = new HashSet<string>();
        foreach (Match m in polyloopMatches)
        {
            foreach (var ptRef in m.Groups[1].Value.Split(','))
            {
                pointIds.Add(ptRef.Trim());
            }
        }

        Assert.NotEmpty(pointIds);

        foreach (var ptId in pointIds)
        {
            var ptMatch = Regex.Match(ifc, $@"{Regex.Escape(ptId)}=IFCCARTESIANPOINT\(\(([^)]+)\)\);");
            Assert.True(ptMatch.Success, $"Point {ptId} definition not found in IFC");
            var coords = ptMatch.Groups[1].Value.Split(',');
            Assert.True(double.TryParse(coords[0], System.Globalization.CultureInfo.InvariantCulture, out double x));
            Assert.True(x <= 500.01, $"Point {ptId} coordinate X={x} exceeded section box bound 500");
        }
    }

    [Fact]
    public void SaveIfc_WithSectionPlane_ClipsGeometry()
    {
        var doc = CreateCubeDocument();

        using var sw = new StringWriter();
        var options = new NwcIfcExportOptions
        {
            SectionPlanes = new List<NwcSectionPlane>
            {
                new()
                {
                    Normal = new Vector3d(1, 0, 0),
                    Distance = 300.0,
                    IsEnabled = true,
                },
            },
            ClipGeometryWithSectionPlanes = true,
        };

        doc.SaveIfc(sw, options);
        string ifc = sw.ToString();

        Assert.Contains("IFCFACETEDBREP(", ifc);

        var polyloopMatches = Regex.Matches(ifc, @"IFCPOLYLOOP\(\(([^)]+)\)\)");
        Assert.NotEmpty(polyloopMatches);

        var pointIds = new HashSet<string>();
        foreach (Match m in polyloopMatches)
        {
            foreach (var ptRef in m.Groups[1].Value.Split(','))
            {
                pointIds.Add(ptRef.Trim());
            }
        }

        Assert.NotEmpty(pointIds);

        foreach (var ptId in pointIds)
        {
            var ptMatch = Regex.Match(ifc, $@"{Regex.Escape(ptId)}=IFCCARTESIANPOINT\(\(([^)]+)\)\);");
            Assert.True(ptMatch.Success, $"Point {ptId} definition not found in IFC");
            var coords = ptMatch.Groups[1].Value.Split(',');
            Assert.True(double.TryParse(coords[0], System.Globalization.CultureInfo.InvariantCulture, out double x));
            Assert.True(x >= 299.99, $"Point {ptId} coordinate X={x} was less than section plane bound 300");
        }
    }

    [Fact]
    public void BcfExporter_Write_GeneratesValidBcfZipWithClippingAndRedlines()
    {
        var vp = new NwcSavedViewpoint
        {
            Id = Guid.NewGuid(),
            Name = "Clash Viewpoint",
            Camera = new NwcCamera
            {
                Position = new Vector3d(10, 20, 30),
                Rotation = new Quaterniond(0, 0, 0, 1),
                FocalDistance = 15.0,
                FieldOfView = 60.0,
                AspectRatio = 1.6,
                Projection = NwcCameraProjection.Perspective,
            },
            SectionPlanes =
            [
                new NwcSectionPlane
                {
                    Normal = new Vector3d(0, 0, 1),
                    Distance = 2.5,
                    IsEnabled = true,
                }
            ],
            Redlines =
            [
                new NwcRedlineLine
                {
                    StartPoint = new Vector3d(0, 0, 0),
                    EndPoint = new Vector3d(5, 5, 5),
                    Color = new RgbaColor(1, 0, 0, 1),
                },
                new NwcRedlineTag
                {
                    TagNumber = 1,
                    Position = new Vector3d(5, 5, 5),
                    Comment = "Review clash here",
                    Author = "Lead Engineer",
                }
            ],
        };

        using var ms = new MemoryStream();
        NwcViewpointBcfExporter.Write(ms, new[] { vp }, "Project Alpha");

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        // 1. Root bcf.version
        var versionEntry = archive.GetEntry("bcf.version");
        Assert.NotNull(versionEntry);
        using (var reader = new StreamReader(versionEntry.Open()))
        {
            string content = reader.ReadToEnd();
            Assert.Contains("VersionId=\"2.1\"", content);
        }

        // 2. Topic folder
        string topicFolder = vp.Id.ToString("D");
        var markupEntry = archive.GetEntry($"{topicFolder}/markup.bcf");
        Assert.NotNull(markupEntry);
        using (var reader = new StreamReader(markupEntry.Open()))
        {
            var doc = XDocument.Load(reader);
            var title = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Title")?.Value;
            Assert.Equal("Clash Viewpoint", title);

            var comment = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Comment")?.Value;
            Assert.NotNull(comment);
            Assert.Contains("Review clash here", comment);
        }

        var vpEntry = archive.GetEntry($"{topicFolder}/viewpoint.bcfv");
        Assert.NotNull(vpEntry);
        using (var reader = new StreamReader(vpEntry.Open()))
        {
            var doc = XDocument.Load(reader);
            var clippingPlanes = doc.Descendants().Where(e => e.Name.LocalName == "ClippingPlane").ToList();
            Assert.Single(clippingPlanes);

            var lines = doc.Descendants().Where(e => e.Name.LocalName == "Line").ToList();
            Assert.Single(lines);
        }
    }
}

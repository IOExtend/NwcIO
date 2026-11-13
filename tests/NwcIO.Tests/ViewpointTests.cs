using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using NwcIO.Internal.Container;
using NwcIO.Internal.Export;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class ViewpointTests
{
    [Fact]
    public void BcfExporter_WriteClashes_ValidatesAgainstBcf21Schemas()
    {
        var model = new NwcDocumentBuilder().SetFileInfo("Model.nwc").Build();
        var clash = new NwcClash(model, new NwcMesh { Id = 1, Vertices = [], Indices = [] },
            model, new NwcMesh { Id = 2, Vertices = [], Indices = [] },
            new BoundingBox3d(Vector3d.Zero, new Vector3d(1, 1, 1)));

        using var stream = new MemoryStream();
        BcfExporter.Write([clash], stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        BcfSchemaAssert.ValidArchive(archive, 1);

        using var markup = archive.GetEntry($"{clash.Id:D}/markup.bcf")!.Open();
        var file = XDocument.Load(markup).Root!.Element("Header")!.Element("File")!;
        Assert.Null(file.Attribute("IfcProject"));
        Assert.Equal("true", file.Attribute("isExternal")?.Value);
    }

    [Fact]
    public void Camera_DirectionAndUp_AreOrthogonal()
    {
        var camera = new NwcCamera
        {
            Position = new Vector3d(10, 20, 30),
            Rotation = new Quaterniond(0, 0, 0, 1), // Identity
            FocalDistance = 15.0,
            FieldOfView = 60.0,
            AspectRatio = 1.6,
            Projection = NwcCameraProjection.Perspective,
        };

        // For identity quaternion:
        // Dir = (0, 0, -1)
        // Up = (0, 1, 0)
        Assert.Equal(new Vector3d(0, 0, -1), camera.Direction);
        Assert.Equal(new Vector3d(0, 1, 0), camera.Up);
        Assert.Equal(new Vector3d(10, 20, 15), camera.Target);

        double dot = Vector3d.Dot(camera.Direction, camera.Up);
        Assert.True(Math.Abs(dot) < 1e-6);
    }

    [Fact]
    public void SavedViewpoint_CreateDefault_GeneratesValidCameraFacingTarget()
    {
        var bounds = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(100, 50, 20));
        var vp = NwcSavedViewpoint.CreateDefault(bounds, "General View");

        Assert.Equal("General View", vp.Name);
        Assert.Equal(NwcCameraProjection.Perspective, vp.Camera.Projection);
        Assert.True(vp.Camera.FocalDistance > 0);
        Assert.True(vp.Camera.Rotation.IsNormalized());

        // Target should match the bounding box center
        var center = bounds.Center;
        Assert.InRange(vp.Camera.Target.X, center.X - 0.1, center.X + 0.1);
        Assert.InRange(vp.Camera.Target.Y, center.Y - 0.1, center.Y + 0.1);
        Assert.InRange(vp.Camera.Target.Z, center.Z - 0.1, center.Z + 0.1);

        // Direction and Up must be orthogonal
        double dot = Vector3d.Dot(vp.Camera.Direction, vp.Camera.Up);
        Assert.True(Math.Abs(dot) < 1e-4);
    }

    [Fact]
    public void SavedViewpoint_CreateTop_GeneratesTopDownView()
    {
        var bounds = new BoundingBox3d(new Vector3d(-10, -10, 0), new Vector3d(10, 10, 5));
        var vp = NwcSavedViewpoint.CreateTop(bounds, "Plan View");

        Assert.Equal("Plan View", vp.Name);
        Assert.Equal(NwcCameraProjection.Orthographic, vp.Camera.Projection);
        Assert.True(vp.Camera.Position.Z > bounds.Max.Z);

        // Direction should point downwards (towards -Z)
        Assert.True(vp.Camera.Direction.Z < -0.9);
    }

    [Fact]
    public void ViewpointParser_DecodesSyntheticStream()
    {
        // Build synthetic stream buffer with viewpoint tag 517
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Write name length and string: "Overview Viewpoint"
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes("Overview Viewpoint");
        bw.Write(nameBytes.Length);
        bw.Write(nameBytes);

        // Align or pad to 4 bytes
        while (ms.Position % 4 != 0)
        {
            bw.Write((byte)0);
        }

        // Tag 517: 517, 1, 0
        bw.Write((int)517);
        bw.Write((int)1);
        bw.Write((int)0);

        // Position: X=12.5, Y=34.5, Z=56.5
        bw.Write((double)12.5);
        bw.Write((double)34.5);
        bw.Write((double)56.5);

        // Quaternion: Identity (0, 0, 0, 1)
        bw.Write((double)0.0);
        bw.Write((double)0.0);
        bw.Write((double)0.0);
        bw.Write((double)1.0);

        // Aspect=1.777, FocalDistance=50.0, Height=10.0, Near=0.1
        bw.Write((double)1.777);
        bw.Write((double)50.0);
        bw.Write((double)10.0);
        bw.Write((double)0.1);

        byte[] streamData = ms.ToArray();
        var rawStream = new LcRawStream
        {
            Index = 1,
            FileOffset = 0,
            CompressedLength = streamData.Length,
            GapBefore = 0,
            SectionIndex = 0,
            Kind = NwcStreamKind.SavedViews,
            Data = streamData,
        };

        var viewpoints = ViewpointParser.Parse(rawStream);

        Assert.Single(viewpoints);
        var vp = viewpoints[0];
        Assert.Equal("Overview Viewpoint", vp.Name);
        Assert.Equal(12.5, vp.Camera.Position.X);
        Assert.Equal(34.5, vp.Camera.Position.Y);
        Assert.Equal(56.5, vp.Camera.Position.Z);
        Assert.Equal(50.0, vp.Camera.FocalDistance);
        Assert.InRange(vp.Camera.AspectRatio, 1.77, 1.78);
        Assert.Equal(new Vector3d(0, 0, -1), vp.Camera.Direction);
    }

    [Fact]
    public void GlbExport_WithSavedViewpoints_ExportsCamerasAndNodes()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Cube",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var vp1 = new NwcSavedViewpoint
        {
            Name = "Camera 1",
            Camera = new NwcCamera
            {
                Position = new Vector3d(5, 5, 5),
                Rotation = new Quaterniond(0, 0, 0, 1),
                FocalDistance = 10.0,
                FieldOfView = 45.0,
                AspectRatio = 1.6,
                Projection = NwcCameraProjection.Perspective,
            },
        };

        using var ms = new MemoryStream();
        GlbExporter.Write([mesh], [], ms, worldCoordinates: true, generatorName: "NwcIO Test", viewpoints: [vp1]);
        byte[] glb = ms.ToArray();

        // Extract JSON chunk from GLB
        uint jsonChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        byte[] jsonBytes = glb.AsSpan(20, (int)jsonChunkLength).ToArray();
        string json = System.Text.Encoding.UTF8.GetString(jsonBytes).Trim();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify cameras array exists
        Assert.True(root.TryGetProperty("cameras", out var camerasEl));
        Assert.Equal(1, camerasEl.GetArrayLength());
        var camEl = camerasEl[0];
        Assert.Equal("perspective", camEl.GetProperty("type").GetString());
        Assert.True(camEl.GetProperty("perspective").GetProperty("yfov").GetDouble() > 0);

        // Verify nodes array contains camera node
        Assert.True(root.TryGetProperty("nodes", out var nodesEl));
        bool foundCameraNode = false;
        foreach (var node in nodesEl.EnumerateArray())
        {
            if (node.TryGetProperty("camera", out var camIdx) && camIdx.GetInt32() == 0)
            {
                foundCameraNode = true;
                Assert.Equal("Camera 1", node.GetProperty("name").GetString());
                Assert.True(node.TryGetProperty("translation", out var trans));
                Assert.Equal(3, trans.GetArrayLength());
                break;
            }
        }

        Assert.True(foundCameraNode, "GLB nodes must contain a node referencing the camera");
    }

    [Theory]
    [InlineData(NwcCameraProjection.Perspective)]
    [InlineData(NwcCameraProjection.Orthographic)]
    public void BcfExporter_WriteViewpoints_CreatesValidBcfZip(NwcCameraProjection projection)
    {
        var vp = new NwcSavedViewpoint
        {
            Name = "Section Perspective",
            Camera = new NwcCamera
            {
                Position = new Vector3d(20, -30, 15),
                Rotation = new Quaterniond(0, 0, 0, 1),
                FocalDistance = 25.0,
                FieldOfView = 60.0,
                AspectRatio = 1.77,
                Projection = projection,
            },
            Comments = ["Check clearance under floor beams"],
        };

        using var ms = new MemoryStream();
        BcfExporter.WriteViewpoints([vp], ms, "TestAssembly");

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        BcfSchemaAssert.ValidArchive(zip, 1);

        // 1. bcf.version
        var versionEntry = zip.GetEntry("bcf.version");
        Assert.NotNull(versionEntry);
        using (var reader = new StreamReader(versionEntry.Open()))
        {
            string content = reader.ReadToEnd();
            Assert.Contains("2.1", content);
        }

        // 2. Locate topic folder
        var markupEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/markup.bcf", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(markupEntry);

        using (var stream = markupEntry.Open())
        {
            var doc = XDocument.Load(stream);
            Assert.Null(doc.Root!.Element("Header")!.Element("File")!.Attribute("IfcProject"));
            var topic = doc.Descendants("Topic").FirstOrDefault();
            Assert.NotNull(topic);
            Assert.Equal("Section Perspective", topic.Element("Title")?.Value);
            Assert.Equal("SavedViewpoint", topic.Attribute("TopicType")?.Value);
            Assert.Contains("clearance under floor beams", topic.Element("Description")?.Value);
        }

        // 3. Locate viewpoint.bcfv
        var viewpointEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/viewpoint.bcfv", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(viewpointEntry);

        using (var stream = viewpointEntry.Open())
        {
            var doc = XDocument.Load(stream);
            var perspective = doc.Descendants(projection == NwcCameraProjection.Perspective ? "PerspectiveCamera" : "OrthogonalCamera").FirstOrDefault();
            Assert.NotNull(perspective);
            var pos = perspective.Element("CameraViewPoint");
            Assert.NotNull(pos);
            Assert.Equal("20.0000", pos.Element("X")?.Value);
            Assert.Equal("-30.0000", pos.Element("Y")?.Value);
            Assert.Equal("15.0000", pos.Element("Z")?.Value);
        }
    }
}

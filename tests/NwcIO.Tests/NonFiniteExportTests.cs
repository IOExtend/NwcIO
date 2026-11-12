using System.Buffers.Binary;
using System.Text.Json;
using NwcIO.Internal.Export;
using NwcIO.Model;
using NwcIO.Model.Viewer;
using Xunit;

namespace NwcIO.Tests;

public sealed class NonFiniteExportTests
{
    private static NwcSavedViewpoint Viewpoint(string name, NwcCamera camera) => new() { Name = name, Camera = camera };

    private static NwcCamera FiniteCamera() => new() { Position = new Vector3d(1, 2, 3), Rotation = Quaterniond.Identity };

    [Fact]
    public void Glb_LeavesOutCamerasThatCantBePlaced_AndFallsBackForNonFiniteParameters()
    {
        var viewpoints = new[]
        {
            Viewpoint("nan-position", new NwcCamera { Position = new Vector3d(double.NaN, 0, 0) }),
            Viewpoint("nan-rotation", new NwcCamera { Rotation = new Quaterniond(double.NaN, 0, 0, 1) }),
            Viewpoint("ortho", new NwcCamera
            {
                Projection = NwcCameraProjection.Orthographic,
                FocalDistance = double.NaN,
                AspectRatio = double.PositiveInfinity,
                FarDistance = double.PositiveInfinity,
            }),
            Viewpoint("perspective", new NwcCamera { FieldOfView = double.PositiveInfinity, NearDistance = double.NaN }),
        };

        using var ms = new MemoryStream();
        GlbExporter.Write(new List<NwcMesh>(), new List<NwcMaterial>(), ms, true, null, viewpoints);

        using var gltf = ReadGlbJson(ms.ToArray());
        var cameras = gltf.RootElement.GetProperty("cameras");
        Assert.Equal(["ortho", "perspective"], cameras.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(2, gltf.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(1, gltf.RootElement.GetProperty("nodes")[1].GetProperty("camera").GetInt32());

        var ortho = cameras[0].GetProperty("orthographic");
        Assert.Equal(1.0, ortho.GetProperty("xmag").GetDouble());
        Assert.Equal(1.0, ortho.GetProperty("ymag").GetDouble());
        Assert.Equal(10000.0, ortho.GetProperty("zfar").GetDouble());
        var perspective = cameras[1].GetProperty("perspective");
        Assert.Equal(1.04719755, perspective.GetProperty("yfov").GetDouble());
        Assert.Equal(0.1, perspective.GetProperty("znear").GetDouble());
    }

    [Fact]
    public void Json_WritesNonFiniteViewpointCameraFieldsAsNull()
    {
        var doc = new NwcDocumentBuilder()
            .AddSavedViewpoint(Viewpoint("v", new NwcCamera
            {
                Position = new Vector3d(double.NaN, 2, 3),
                FarDistance = double.PositiveInfinity,
            }))
            .Build();

        using var json = JsonDocument.Parse(doc.ToJson());
        var camera = json.RootElement.GetProperty("savedViewpoints")[0].GetProperty("camera");

        Assert.Equal(JsonValueKind.Null, camera.GetProperty("position").GetProperty("x").ValueKind);
        Assert.Equal(2.0, camera.GetProperty("position").GetProperty("y").GetDouble());
        Assert.Equal(JsonValueKind.Null, camera.GetProperty("farDistance").ValueKind);
    }

    [Fact]
    public void HtmlViewer_EmptyBoundsNonFiniteVertexAndCamera_WritesThePage()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(double.NaN, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };
        var viewpoints = new[]
        {
            Viewpoint("broken", new NwcCamera { Position = new Vector3d(0, double.PositiveInfinity, 0) }),
            Viewpoint("fine", FiniteCamera()),
        };

        var sw = new StringWriter();
        NwcHtmlViewerExporter.Write(sw, [mesh], viewpoints, [], BoundingBox3d.Empty, new NwcHtmlViewerOptions());
        string html = sw.ToString();

        Assert.DoesNotContain("\"boundingBox\"", html, StringComparison.Ordinal);
        Assert.Contains("\"vertices\":[0,0,0,1,0,0,0,1,0]", html, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"broken\"}", html, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"fine\",\"camera\":{\"position\":[1,2,3]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Exports_DocumentWithoutGeometry_Complete()
    {
        var doc = new NwcDocumentBuilder().AddSavedViewpoint(Viewpoint("v", FiniteCamera())).Build();
        Assert.True(doc.BoundingBox.IsEmpty);

        string path = Path.Combine(Path.GetTempPath(), $"nwcio_{Guid.NewGuid():N}.html");
        try
        {
            doc.SaveHtmlViewer(path);
            Assert.DoesNotContain("\"boundingBox\"", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }

        using var glb = new MemoryStream();
        doc.SaveGlb(glb);
        using var gltf = ReadGlbJson(glb.ToArray());
        Assert.Single(gltf.RootElement.GetProperty("cameras").EnumerateArray());

        using var json = JsonDocument.Parse(doc.ToJson());
        Assert.Single(json.RootElement.GetProperty("savedViewpoints").EnumerateArray());
    }

    private static JsonDocument ReadGlbJson(byte[] glb)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        return JsonDocument.Parse(glb.AsMemory(20, length));
    }
}

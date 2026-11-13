using NwcIO.Internal.Export;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class ObjExportTests
{
    [Theory]
    [InlineData(true, "f 1 3 2", "f 3 4 2")]
    [InlineData(false, "f 1 2 3", "f 3 2 4")]
    public void Write_MirroredTransform_ReversesOnlyWorldFaces(bool world, string first, string second)
    {
        var mesh = TestMeshes.MirroredQuad();
        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: world);
        Assert.Contains(first, sw.ToString());
        Assert.Contains(second, sw.ToString());
        Assert.Equal([0, 1, 2, 2, 1, 3], mesh.Indices);
    }

    [Fact]
    public void Write_SingleMesh_ProducesValidWavefrontObj()
    {
        var vertices = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(10, 0, 0),
            new Vector3d(0, 10, 0),
        };

        var mesh = new NwcMesh
        {
            Id = 0,
            Name = "Triangle",
            Vertices = vertices,
            Indices = [0, 1, 2],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: false, sourceDocument: "test.rvt");
        string obj = sw.ToString();

        Assert.Contains("# Wavefront OBJ exported by NwcIO", obj);
        Assert.Contains("# Source: test.rvt", obj);
        Assert.Contains("o Triangle", obj);
        Assert.Contains("v 0.0000 0.0000 0.0000", obj);
        Assert.Contains("v 10.0000 0.0000 0.0000", obj);
        Assert.Contains("v 0.0000 10.0000 0.0000", obj);
        Assert.Contains("f 1 2 3", obj);
    }

    [Fact]
    public void Write_MultipleMeshes_UsesGlobal1BasedIndexOffsets()
    {
        var mesh1 = new NwcMesh
        {
            Id = 1,
            Name = "MeshA",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var mesh2 = new NwcMesh
        {
            Id = 2,
            Name = "MeshB",
            Vertices = [new Vector3d(2, 0, 0), new Vector3d(3, 0, 0), new Vector3d(2, 1, 0)],
            Indices = [0, 1, 2],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh1, mesh2], sw, worldCoordinates: false);
        string obj = sw.ToString();

        // MeshA has vertices 1..3, faces f 1 2 3
        Assert.Contains("o MeshA", obj);
        Assert.Contains("f 1 2 3", obj);

        // MeshB has vertices 4..6, faces f 4 5 6
        Assert.Contains("o MeshB", obj);
        Assert.Contains("f 4 5 6", obj);
    }

    [Fact]
    public void Write_WithWorldCoordinates_AppliesTransformMatrix()
    {
        var transform = new NwcTransform
        {
            Id = 1,
            Translation = new Vector3d(100, 200, 300),
            Rotation = Quaterniond.Identity,
        };

        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new Vector3d(5, 5, 5), new Vector3d(6, 5, 5), new Vector3d(5, 6, 5)],
            Indices = [0, 1, 2],
            Transform = transform,
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: true);
        string obj = sw.ToString();

        Assert.Contains("v 105.0000 205.0000 305.0000", obj);
    }
}

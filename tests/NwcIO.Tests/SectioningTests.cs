using NwcIO.Model;
using NwcIO.Model.Sectioning;
using Xunit;

namespace NwcIO.Tests;

public class SectioningTests
{
    [Fact]
    public void NwcSectionPlane_SignedDistanceTo_CalculatesCorrectly()
    {
        // Plane passing through (0, 0, 10) with normal (0, 0, 1) -> n.p - d = 0, so d = 10
        var plane = new NwcSectionPlane
        {
            Normal = new Vector3d(0, 0, 1),
            Distance = 10.0,
            IsEnabled = true,
        };

        // Point at (0, 0, 15) -> dot(n, p) - 10 = 5
        Assert.Equal(5.0, plane.SignedDistanceTo(new Vector3d(0, 0, 15)), 6);

        // Point at (0, 0, 10) -> on the plane
        Assert.Equal(0.0, plane.SignedDistanceTo(new Vector3d(0, 0, 10)), 6);

        // Point at (0, 0, 2) -> -8
        Assert.Equal(-8.0, plane.SignedDistanceTo(new Vector3d(0, 0, 2)), 6);
    }

    [Fact]
    public void NwcSectionPlane_ContainsPoint_EvaluatesCorrectly()
    {
        var plane = new NwcSectionPlane
        {
            Normal = new Vector3d(1, 0, 0),
            Distance = 5.0,
            IsEnabled = true,
        };

        // Retains points where SignedDistance >= 0 (i.e. X >= 5)
        Assert.True(plane.ContainsPoint(new Vector3d(6, 0, 0)));
        Assert.True(plane.ContainsPoint(new Vector3d(5, 0, 0)));
        Assert.False(plane.ContainsPoint(new Vector3d(4, 0, 0)));
    }

    [Fact]
    public void NwcSectionBox_ToSectionPlanes_ProducesSixPlanes()
    {
        var box = new NwcSectionBox
        {
            Bounds = new BoundingBox3d(new Vector3d(-10, -20, -30), new Vector3d(10, 20, 30)),
            IsEnabled = true,
        };

        var planes = box.ToSectionPlanes();
        Assert.Equal(6, planes.Count);

        // All planes should be enabled
        Assert.All(planes, p => Assert.True(p.IsEnabled));

        // Left plane (Xmin): normal +X, distance -10
        var leftPlane = planes.First(p => p.Alignment == NwcSectionAlignment.Left);
        Assert.Equal(1.0, leftPlane.Normal.X, 5);
        Assert.Equal(-10.0, leftPlane.Distance, 5);

        // Right plane (Xmax): normal -X, distance -10 (which means -X - (-10) >= 0 => X <= 10)
        var rightPlane = planes.First(p => p.Alignment == NwcSectionAlignment.Right);
        Assert.Equal(-1.0, rightPlane.Normal.X, 5);
    }

    [Fact]
    public void NwcSectionBox_Contains_CorrectlyClassifiesPoints()
    {
        var box = new NwcSectionBox
        {
            Bounds = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(10, 10, 10)),
            IsEnabled = true,
        };

        Assert.True(box.Contains(new Vector3d(5, 5, 5)));
        Assert.True(box.Contains(new Vector3d(0, 0, 0)));
        Assert.True(box.Contains(new Vector3d(10, 10, 10)));

        Assert.False(box.Contains(new Vector3d(-1, 5, 5)));
        Assert.False(box.Contains(new Vector3d(11, 5, 5)));
        Assert.False(box.Contains(new Vector3d(5, 12, 5)));
    }

    [Fact]
    public void NwcMeshClipper_Clip_SinglePlane_CutsCube()
    {
        // 1x1x1 cube at [0, 10] in all axes
        var mesh = CreateCubeMesh(new Vector3d(0, 0, 0), new Vector3d(10, 10, 10));

        // Plane cutting at X = 5, normal = (1, 0, 0), retaining X >= 5
        var plane = new NwcSectionPlane
        {
            Normal = new Vector3d(1, 0, 0),
            Distance = 5.0,
            IsEnabled = true,
        };

        var clipped = NwcMeshClipper.Clip(mesh, new[] { plane });

        Assert.NotNull(clipped);
        Assert.True(clipped.Vertices.Count > 0);
        Assert.True(clipped.Indices.Count > 0);

        // Every vertex in clipped mesh must have X >= 5.0 (with slight tolerance)
        foreach (var v in clipped.Vertices)
        {
            Assert.True(v.X >= 5.0 - 1e-4, $"Vertex X {v.X} is less than clipping threshold 5.0");
        }
    }

    [Fact]
    public void NwcMeshClipper_Clip_SectionBox_ClipsAllSixFaces()
    {
        // Cube [0, 10]^3
        var mesh = CreateCubeMesh(new Vector3d(0, 0, 0), new Vector3d(10, 10, 10));

        // Section box [-2, 5]^3 cutting through the cube faces
        var box = new NwcSectionBox
        {
            Bounds = new BoundingBox3d(new Vector3d(-2, -2, -2), new Vector3d(5, 5, 5)),
            IsEnabled = true,
        };

        var clipped = NwcMeshClipper.Clip(mesh, box);

        Assert.NotNull(clipped);
        Assert.True(clipped.Vertices.Count > 0);

        foreach (var v in clipped.Vertices)
        {
            Assert.InRange(v.X, -2.0001, 5.0001);
            Assert.InRange(v.Y, -2.0001, 5.0001);
            Assert.InRange(v.Z, -2.0001, 5.0001);
        }
    }

    [Fact]
    public void NwcMeshClipper_Clip_DisabledPlane_LeavesMeshUnchanged()
    {
        var mesh = CreateCubeMesh(new Vector3d(0, 0, 0), new Vector3d(10, 10, 10));
        var plane = new NwcSectionPlane
        {
            Normal = new Vector3d(1, 0, 0),
            Distance = 5.0,
            IsEnabled = false, // Disabled
        };

        var clipped = NwcMeshClipper.Clip(mesh, new[] { plane });
        Assert.Equal(mesh.Vertices.Count, clipped.Vertices.Count);
        Assert.Equal(mesh.Indices.Count, clipped.Indices.Count);
    }

    private static NwcMesh CreateCubeMesh(Vector3d min, Vector3d max)
    {
        var vertices = new[]
        {
            new Vector3d(min.X, min.Y, min.Z), // 0
            new Vector3d(max.X, min.Y, min.Z), // 1
            new Vector3d(max.X, max.Y, min.Z), // 2
            new Vector3d(min.X, max.Y, min.Z), // 3
            new Vector3d(min.X, min.Y, max.Z), // 4
            new Vector3d(max.X, min.Y, max.Z), // 5
            new Vector3d(max.X, max.Y, max.Z), // 6
            new Vector3d(min.X, max.Y, max.Z), // 7
        };

        var indices = new[]
        {
            // Bottom (Z = min)
            0, 2, 1, 0, 3, 2,
            // Top (Z = max)
            4, 5, 6, 4, 6, 7,
            // Front (Y = min)
            0, 1, 5, 0, 5, 4,
            // Right (X = max)
            1, 2, 6, 1, 6, 5,
            // Back (Y = max)
            2, 3, 7, 2, 7, 6,
            // Left (X = min)
            3, 0, 4, 3, 4, 7,
        };

        return new NwcMesh
        {
            Id = 1,
            Name = "Cube",
            Vertices = vertices,
            Indices = indices,
        };
    }
}

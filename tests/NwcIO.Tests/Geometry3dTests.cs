using NwcIO.Model;
using NwcIO.Model.Sectioning;
using Xunit;

namespace NwcIO.Tests;

public class Geometry3dTests
{
    [Fact]
    public void Normalized_NonUnitQuaternion_ReturnsUnitQuaternion()
    {
        var q = new Quaterniond(0.5, 0, 0, 1.5);

        var normalized = q.Normalized();

        Assert.True(normalized.IsNormalized());
        Assert.Equal(0.5 / Math.Sqrt(2.5), normalized.X, 12);
        Assert.Equal(1.5 / Math.Sqrt(2.5), normalized.W, 12);
    }

    [Fact]
    public void Normalized_ZeroQuaternion_ReturnsIdentity()
    {
        Assert.Equal(Quaterniond.Identity, new Quaterniond(0, 0, 0, 0).Normalized());
    }

    [Fact]
    public void ToMatrix4x4_NonUnitRotation_ProducesOrthonormalRows()
    {
        var transform = new NwcTransform { Id = 1, Rotation = new Quaterniond(0.5, 0, 0, 1.5) };

        double[] m = transform.ToMatrix4x4();

        // Each row of the 3x3 part must be unit length, whatever the quaternion scale.
        for (int row = 0; row < 3; row++)
        {
            double length = Math.Sqrt(
                m[row * 4] * m[row * 4] +
                m[row * 4 + 1] * m[row * 4 + 1] +
                m[row * 4 + 2] * m[row * 4 + 2]);
            Assert.Equal(1.0, length, 10);
        }
    }

    [Fact]
    public void ToMatrix4x4_NonUnitRotation_MatchesNormalizedRotationMatrix()
    {
        var nonUnit = new Quaterniond(0.5, 0, 0, 1.5);
        double[] scaled = new NwcTransform { Id = 1, Rotation = nonUnit }.ToMatrix4x4();
        double[] normalized = new NwcTransform { Id = 1, Rotation = nonUnit.Normalized() }.ToMatrix4x4();

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(normalized[i], scaled[i], 10);
        }
    }

    [Fact]
    public void ToMatrix4x4_ZeroRotation_FallsBackToIdentityMatrix()
    {
        var transform = new NwcTransform { Id = 1, Rotation = new Quaterniond(0, 0, 0, 0) };

        double[] m = transform.ToMatrix4x4();

        double[] identity =
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
        ];
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(identity[i], m[i], 10);
        }
    }

    [Fact]
    public void Camera_NonUnitRotation_DirectionAndUpAreUnitVectors()
    {
        var camera = new NwcCamera { Rotation = new Quaterniond(0.5, 0, 0, 1.5) };

        Assert.Equal(1.0, camera.Direction.Length, 10);
        Assert.Equal(1.0, camera.Up.Length, 10);
    }

    [Fact]
    public void Camera_NonUnitRotation_DirectionMatchesNormalizedRotation()
    {
        var nonUnit = new Quaterniond(0.5, 0, 0, 1.5);
        var scaled = new NwcCamera { Rotation = nonUnit };
        var normalized = new NwcCamera { Rotation = nonUnit.Normalized() };

        Assert.Equal(normalized.Direction.X, scaled.Direction.X, 10);
        Assert.Equal(normalized.Direction.Y, scaled.Direction.Y, 10);
        Assert.Equal(normalized.Direction.Z, scaled.Direction.Z, 10);
        Assert.Equal(normalized.Up.X, scaled.Up.X, 10);
        Assert.Equal(normalized.Up.Y, scaled.Up.Y, 10);
        Assert.Equal(normalized.Up.Z, scaled.Up.Z, 10);
    }

    [Fact]
    public void XmlViewpoint_RoundtripWithNonUnitRotation_CameraVectorsStayUnit()
    {
        var xml = NwcXmlViewpointSerializer.Serialize([
            new NwcSavedViewpoint
            {
                Name = "v",
                Camera = new NwcCamera { Rotation = new Quaterniond(0.5, 0, 0, 1.5) },
            },
        ]);

        var camera = NwcXmlViewpointSerializer.Deserialize(xml)[0].Camera;

        Assert.Equal(1.0, camera.Direction.Length, 10);
        Assert.Equal(1.0, camera.Up.Length, 10);
    }
}

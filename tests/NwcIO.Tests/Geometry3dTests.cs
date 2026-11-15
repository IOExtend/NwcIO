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
    public void Normalized_SmallQuaternionWithNormAboveThreshold_Normalizes()
    {
        // Full norm is 7.5e-10 * sqrt(2) ≈ 1.06e-9 > 1e-9, so this must normalize
        // even though the largest component alone is below the threshold.
        var q = new Quaterniond(7.5e-10, 0, 0, 7.5e-10);

        var normalized = q.Normalized();

        Assert.True(normalized.IsNormalized());
        Assert.Equal(Math.Sqrt(0.5), normalized.X, 12);
        Assert.Equal(Math.Sqrt(0.5), normalized.W, 12);
    }

    [Fact]
    public void Normalized_SmallQuaternionWithNormBelowThreshold_ReturnsIdentity()
    {
        // Norm is 0.5e-9 < 1e-9: effectively zero, so fall back to Identity.
        Assert.Equal(Quaterniond.Identity, new Quaterniond(0.5e-9, 0, 0, 0).Normalized());
    }

    [Fact]
    public void Normalized_LargeFiniteComponents_DoNotOverflowToZero()
    {
        // x*x + w*w overflows to Infinity for components near 1e154; the naive
        // normalization would return (0,0,0,0) and lose the rotation entirely.
        var q = new Quaterniond(1e154, 0, 0, 1e154);

        var normalized = q.Normalized();

        Assert.True(normalized.IsNormalized());
        Assert.Equal(Math.Sqrt(0.5), normalized.X, 12);
        Assert.Equal(0.0, normalized.Y, 12);
        Assert.Equal(0.0, normalized.Z, 12);
        Assert.Equal(Math.Sqrt(0.5), normalized.W, 12);
    }

    [Fact]
    public void Normalized_NonFiniteComponent_ReturnsIdentity()
    {
        Assert.Equal(Quaterniond.Identity, new Quaterniond(double.PositiveInfinity, 0, 0, 1).Normalized());
        Assert.Equal(Quaterniond.Identity, new Quaterniond(0, double.NegativeInfinity, 0, 1).Normalized());
        Assert.Equal(Quaterniond.Identity, new Quaterniond(0, 0, double.NaN, 1).Normalized());
    }

    [Fact]
    public void Camera_LargeRotation_PreservesOrientation()
    {
        // A 180-degree rotation about X scaled by 1e160 must keep flipping the view
        // direction instead of collapsing to the identity rotation via overflow.
        var huge = new NwcCamera { Rotation = new Quaterniond(1e160, 0, 0, 0) };
        var unit = new NwcCamera { Rotation = new Quaterniond(1, 0, 0, 0) };

        Assert.Equal(unit.Direction.X, huge.Direction.X, 10);
        Assert.Equal(unit.Direction.Y, huge.Direction.Y, 10);
        Assert.Equal(unit.Direction.Z, huge.Direction.Z, 10);
        Assert.Equal(unit.Up.X, huge.Up.X, 10);
        Assert.Equal(unit.Up.Y, huge.Up.Y, 10);
        Assert.Equal(unit.Up.Z, huge.Up.Z, 10);
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

    [Theory]
    [InlineData(-1.0)]
    [InlineData(100.0)]
    public void ToMatrix4x4_Scale_ScalesRotationRowsOnly(double scale)
    {
        double h = Math.Sqrt(0.5); // 90° about Z
        var t = new NwcTransform { Id = 1, Translation = new(1, 2, 3), Rotation = new(0, 0, h, h), Scale = scale };
        double[] expected = [0, scale, 0, 0, -scale, 0, 0, 0, 0, 0, scale, 0, 1, 2, 3, 1];
        double[] m = t.ToMatrix4x4();
        for (int i = 0; i < 16; i++) Assert.Equal(expected[i], m[i], 12);
    }

    [Fact]
    public void Quaterniond_FromRotationMatrix_RoundtripsIdentityAndAxisRotations()
    {
        // Identity matrix -> Identity quaternion
        var qIdent = Quaterniond.FromRotationMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1);
        Assert.True(qIdent.IsNormalized());
        Assert.Equal(0, qIdent.X, 10);
        Assert.Equal(0, qIdent.Y, 10);
        Assert.Equal(0, qIdent.Z, 10);
        Assert.Equal(1, qIdent.W, 10);

        // 90 degrees around Z axis in row-vector convention ([1,0,0]*R = [0,1,0]):
        // [ 0  1  0 ]
        // [ -1 0  0 ]
        // [ 0  0  1 ]
        var qZ = Quaterniond.FromRotationMatrix(0, 1, 0, -1, 0, 0, 0, 0, 1);
        Assert.True(qZ.IsNormalized());
        double expectedH = Math.Sqrt(0.5);
        Assert.Equal(0, qZ.X, 10);
        Assert.Equal(0, qZ.Y, 10);
        Assert.Equal(expectedH, qZ.Z, 10);
        Assert.Equal(expectedH, qZ.W, 10);

        // Verify ToMatrix4x4 roundtrip
        var xformZ = new NwcTransform { Id = 1, Rotation = qZ };
        double[] mZ = xformZ.ToMatrix4x4();
        Assert.Equal(0, mZ[0], 10);
        Assert.Equal(1, mZ[1], 10);
        Assert.Equal(0, mZ[2], 10);
        Assert.Equal(-1, mZ[4], 10);
        Assert.Equal(0, mZ[5], 10);
        Assert.Equal(0, mZ[6], 10);
        Assert.Equal(0, mZ[8], 10);
        Assert.Equal(0, mZ[9], 10);
        Assert.Equal(1, mZ[10], 10);

        // 180 degrees around X axis:
        // [ 1  0  0 ]
        // [ 0 -1  0 ]
        // [ 0  0 -1 ]
        var qX = Quaterniond.FromRotationMatrix(1, 0, 0, 0, -1, 0, 0, 0, -1);
        Assert.True(qX.IsNormalized());
        Assert.Equal(1, Math.Abs(qX.X), 10);
        Assert.Equal(0, qX.Y, 10);
        Assert.Equal(0, qX.Z, 10);
        Assert.Equal(0, qX.W, 10);

        // 180 degrees around Y axis:
        // [ -1  0  0 ]
        // [  0  1  0 ]
        // [  0  0 -1 ]
        var qY = Quaterniond.FromRotationMatrix(-1, 0, 0, 0, 1, 0, 0, 0, -1);
        Assert.True(qY.IsNormalized());
        Assert.Equal(0, qY.X, 10);
        Assert.Equal(1, Math.Abs(qY.Y), 10);
        Assert.Equal(0, qY.Z, 10);
        Assert.Equal(0, qY.W, 10);
    }
}

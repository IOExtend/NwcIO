namespace NwcIO.Model;

/// <summary>
/// A 3D spatial transformation (pose, translation, rotation quaternion and bounding box)
/// parsed from Navisworks <c>Transforms</c> streams.
/// </summary>
public sealed class NwcTransform
{
    public required int Id { get; init; }

    /// <summary>Translation in 3D world space (typically in millimeters or document units).</summary>
    public Vector3d Translation { get; init; } = Vector3d.Zero;

    /// <summary>Unit rotation quaternion (X, Y, Z, W).</summary>
    public Quaterniond Rotation { get; init; } = Quaterniond.Identity;

    /// <summary>Axis-aligned bounding box of the element geometry in local space.</summary>
    public BoundingBox3d? BoundingBox { get; init; }

    /// <summary>
    /// Uniform scale applied before the rotation: world = Scale·R(Rotation)·v + Translation.
    /// A negative value mirrors; world-space consumers must reverse triangle winding.
    /// </summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// Computes the 4x4 affine transformation matrix in row-major order:
    /// <code>
    /// [ R00, R01, R02, 0 ]
    /// [ R10, R11, R12, 0 ]
    /// [ R20, R21, R22, 0 ]
    /// [ Tx,  Ty,  Tz,  1 ]
    /// </code>
    /// </summary>
    public double[] ToMatrix4x4()
    {
        // The formulas below assume a unit quaternion; a non-unit Rotation must not
        // scale or shear the result, so normalize first (zero falls back to identity).
        Quaterniond r = Rotation.Normalized();
        double x = r.X;
        double y = r.Y;
        double z = r.Z;
        double w = r.W;

        double xx = x * x;
        double yy = y * y;
        double zz = z * z;
        double xy = x * y;
        double xz = x * z;
        double yz = y * z;
        double wx = w * x;
        double wy = w * y;
        double wz = w * z;
        double s = Scale;

        return
        [
            s * (1.0 - 2.0 * (yy + zz)), s * (2.0 * (xy + wz)),       s * (2.0 * (xz - wy)),       0.0,
            s * (2.0 * (xy - wz)),       s * (1.0 - 2.0 * (xx + zz)), s * (2.0 * (yz + wx)),       0.0,
            s * (2.0 * (xz + wy)),       s * (2.0 * (yz - wx)),       s * (1.0 - 2.0 * (xx + yy)), 0.0,
            Translation.X,         Translation.Y,         Translation.Z,         1.0,
        ];
    }
}

namespace NwcIO.Model;

/// <summary>Projection type of a 3D camera.</summary>
public enum NwcCameraProjection
{
    Perspective,
    Orthographic,
}

/// <summary>
/// Represents a 3D camera with double-precision position, orientation quaternion,
/// computed viewing direction and up vectors, focal distance, and field of view.
/// </summary>
public sealed record NwcCamera
{
    /// <summary>Eye position of the camera in 3D world coordinates.</summary>
    public Vector3d Position { get; init; } = Vector3d.Zero;

    /// <summary>Unit rotation quaternion representing the camera orientation.</summary>
    public Quaterniond Rotation { get; init; } = Quaterniond.Identity;

    /// <summary>Distance from the camera eye to the focal/target point.</summary>
    public double FocalDistance { get; init; } = 1.0;

    /// <summary>Vertical field of view in degrees (for perspective projection).</summary>
    public double FieldOfView { get; init; } = 60.0;

    /// <summary>Aspect ratio (width / height) of the camera viewport.</summary>
    public double AspectRatio { get; init; } = 1.0;

    /// <summary>Near clipping plane distance.</summary>
    public double NearDistance { get; init; } = 0.1;

    /// <summary>Far clipping plane distance.</summary>
    public double FarDistance { get; init; } = 10000.0;

    /// <summary>Projection mode (perspective or orthographic).</summary>
    public NwcCameraProjection Projection { get; init; } = NwcCameraProjection.Perspective;

    /// <summary>
    /// Gets the normalized viewing direction vector computed by rotating canonical (0, 0, -1) by the camera quaternion.
    /// </summary>
    public Vector3d Direction => Rotate(Rotation, new Vector3d(0, 0, -1));

    /// <summary>
    /// Gets the normalized camera up vector computed by rotating canonical (0, 1, 0) by the camera quaternion.
    /// </summary>
    public Vector3d Up => Rotate(Rotation, new Vector3d(0, 1, 0));

    /// <summary>
    /// Gets the focal target / look-at point in 3D world space (Position + Direction * FocalDistance).
    /// </summary>
    public Vector3d Target => Position + Direction * (FocalDistance > 0 ? FocalDistance : 1.0);

    private static Vector3d Rotate(Quaterniond q, Vector3d v)
    {
        // The rotation formula is only exact for a unit quaternion; normalize so that
        // Direction and Up keep their documented unit length for any Rotation scale.
        q = q.Normalized();
        var qv = new Vector3d(q.X, q.Y, q.Z);
        var uv = Vector3d.Cross(qv, v);
        var uuv = Vector3d.Cross(qv, uv);
        return v + ((uv * q.W) + uuv) * 2.0;
    }
}

using NwcIO.Model.Redline;
using NwcIO.Model.Sectioning;

namespace NwcIO.Model;

/// <summary>
/// Represents a saved viewpoint / camera view in a Navisworks document or federated assembly.
/// </summary>
public sealed record NwcSavedViewpoint
{
    /// <summary>Unique identifier of this saved viewpoint.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name of the saved viewpoint.</summary>
    public required string Name { get; init; }

    /// <summary>Camera orientation and projection parameters.</summary>
    public required NwcCamera Camera { get; init; }

    /// <summary>Convenience accessor for the 3D position of the camera.</summary>
    public Vector3d? CameraPosition => Camera?.Position;

    /// <summary>Optional user comments or review notes associated with this viewpoint.</summary>
    public IReadOnlyList<string> Comments { get; init; } = Array.Empty<string>();

    /// <summary>Dynamic clipping section planes active in this viewpoint (up to 6 planes).</summary>
    public IReadOnlyList<NwcSectionPlane> SectionPlanes { get; init; } = Array.Empty<NwcSectionPlane>();

    /// <summary>Optional 3D bounding section box cropping visible geometry in this viewpoint.</summary>
    public NwcSectionBox? SectionBox { get; init; }

    /// <summary>Review redline markup annotations (arrows, text, clouds, tags) drawn on this viewpoint.</summary>
    public IReadOnlyList<NwcRedline> Redlines { get; init; } = Array.Empty<NwcRedline>();

    /// <summary>
    /// Creates a default perspective viewpoint framing the provided bounding box.
    /// </summary>
    public static NwcSavedViewpoint CreateDefault(BoundingBox3d bounds, string name = "Default View")
    {
        if (bounds.IsEmpty)
        {
            return new NwcSavedViewpoint
            {
                Name = name,
                Camera = new NwcCamera
                {
                    Position = new Vector3d(10, -10, 10),
                    Rotation = Quaterniond.Identity,
                    FocalDistance = 15.0,
                }
            };
        }

        var center = bounds.Center;
        var size = bounds.Size;
        double maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));
        double dist = maxDim > 0.1 ? maxDim * 2.0 : 10.0;

        // Isometric offset (SW to NE, elevated)
        var offset = new Vector3d(1.0, -1.0, 0.75).Normalize() * dist;
        var eye = center + offset;
        var dir = (center - eye).Normalize();

        var quat = LookRotation(dir, new Vector3d(0, 0, 1));

        return new NwcSavedViewpoint
        {
            Name = name,
            Camera = new NwcCamera
            {
                Position = eye,
                Rotation = quat,
                FocalDistance = dist,
                FieldOfView = 60.0,
                AspectRatio = 1.0,
            }
        };
    }

    /// <summary>
    /// Creates an orthographic top (plan) view looking down along -Z.
    /// </summary>
    public static NwcSavedViewpoint CreateTop(BoundingBox3d bounds, string name = "Top View")
    {
        var center = bounds.IsEmpty ? Vector3d.Zero : bounds.Center;
        double maxDim = bounds.IsEmpty ? 10.0 : Math.Max(bounds.Size.X, bounds.Size.Y);
        double dist = bounds.IsEmpty ? 20.0 : (bounds.Size.Z * 0.5 + maxDim * 1.5);

        return new NwcSavedViewpoint
        {
            Name = name,
            Camera = new NwcCamera
            {
                Position = center + new Vector3d(0, 0, dist),
                Rotation = LookRotation(new Vector3d(0, 0, -1), new Vector3d(0, 1, 0)),
                FocalDistance = dist,
                Projection = NwcCameraProjection.Orthographic,
            }
        };
    }

    /// <summary>
    /// Computes a rotation quaternion looking along direction with the given up vector.
    /// </summary>
    public static Quaterniond LookRotation(Vector3d forward, Vector3d up)
    {
        forward = forward.Normalize();
        if (forward.LengthSquared < 1e-6) forward = new Vector3d(0, 0, -1);

        var right = Vector3d.Cross(forward, up).Normalize();
        if (right.LengthSquared < 1e-6)
        {
            up = Math.Abs(forward.Z) > 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(0, 0, 1);
            right = Vector3d.Cross(forward, up).Normalize();
        }
        var actualUp = Vector3d.Cross(right, forward).Normalize();

        // Convert camera rotation matrix [right, actualUp, -forward] to quaternion
        // Camera looks along canonical (0, 0, -1) in camera local space, so -forward is col 2
        double m00 = right.X, m01 = actualUp.X, m02 = -forward.X;
        double m10 = right.Y, m11 = actualUp.Y, m12 = -forward.Y;
        double m20 = right.Z, m21 = actualUp.Z, m22 = -forward.Z;

        double tr = m00 + m11 + m22;
        double qw, qx, qy, qz;
        if (tr > 0)
        {
            double s = Math.Sqrt(tr + 1.0) * 2.0;
            qw = 0.25 * s;
            qx = (m21 - m12) / s;
            qy = (m02 - m20) / s;
            qz = (m10 - m01) / s;
        }
        else if ((m00 > m11) && (m00 > m22))
        {
            double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
            qw = (m21 - m12) / s;
            qx = 0.25 * s;
            qy = (m01 + m10) / s;
            qz = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
            qw = (m02 - m20) / s;
            qx = (m01 + m10) / s;
            qy = 0.25 * s;
            qz = (m12 + m21) / s;
        }
        else
        {
            double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
            qw = (m10 - m01) / s;
            qx = (m02 + m20) / s;
            qy = (m12 + m21) / s;
            qz = 0.25 * s;
        }

        var res = new Quaterniond(qx, qy, qz, qw);
        return res.IsNormalized() ? res : Quaterniond.Identity;
    }
}

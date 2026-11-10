namespace NwcIO.Model.Sectioning;

/// <summary>
/// Predefined section plane alignment conforming to Navisworks sectioning modes.
/// </summary>
public enum NwcSectionAlignment
{
    Custom = 0,
    Top = 1,
    Bottom = 2,
    Front = 3,
    Back = 4,
    Left = 5,
    Right = 6,
    View = 7,
}

/// <summary>
/// A dynamic clipping / cutting section plane in a Navisworks viewpoint.
/// Supports up to 6 simultaneous section planes conforming to Navisworks sectioning tools.
/// </summary>
public sealed record NwcSectionPlane
{
    /// <summary>Index of the section plane (typically 0 to 5 in Navisworks).</summary>
    public int PlaneIndex { get; init; }

    /// <summary>Whether this section plane is currently active and clipping geometry.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Surface normal vector pointing towards the retained half-space volume.</summary>
    public Vector3d Normal { get; init; } = new(0, 0, 1);

    /// <summary>Distance parameter along the normal vector from the origin (plane equation: N . P - d = 0).</summary>
    public double Distance { get; init; }

    /// <summary>Standard alignment of the section plane.</summary>
    public NwcSectionAlignment Alignment { get; init; } = NwcSectionAlignment.Custom;

    public NwcSectionPlane() { }

    public NwcSectionPlane(
        int planeIndex,
        Vector3d normal,
        double distance,
        bool isEnabled = true,
        NwcSectionAlignment alignment = NwcSectionAlignment.Custom)
    {
        PlaneIndex = planeIndex;
        Normal = normal.Normalize();
        Distance = distance;
        IsEnabled = isEnabled;
        Alignment = alignment;
    }

    /// <summary>
    /// Computes the signed perpendicular distance from the point to this clipping plane.
    /// Positive if on the retained side of the plane, negative if clipped.
    /// </summary>
    public double SignedDistanceTo(Vector3d point)
    {
        var n = Normal.Normalize();
        return Vector3d.Dot(n, point) - Distance;
    }

    /// <summary>
    /// Evaluates whether the given 3D point is on the visible (retained) side of this clipping plane.
    /// </summary>
    public bool ContainsPoint(Vector3d point, double tolerance = 1e-7) =>
        SignedDistanceTo(point) >= -tolerance;

    public static NwcSectionPlane CreateTop(double zElevation, int index = 0, bool enabled = true) =>
        new(index, new Vector3d(0, 0, -1), -zElevation, enabled, NwcSectionAlignment.Top);

    public static NwcSectionPlane CreateBottom(double zElevation, int index = 1, bool enabled = true) =>
        new(index, new Vector3d(0, 0, 1), zElevation, enabled, NwcSectionAlignment.Bottom);

    public static NwcSectionPlane CreateFront(double yDistance, int index = 2, bool enabled = true) =>
        new(index, new Vector3d(0, 1, 0), yDistance, enabled, NwcSectionAlignment.Front);

    public static NwcSectionPlane CreateBack(double yDistance, int index = 3, bool enabled = true) =>
        new(index, new Vector3d(0, -1, 0), -yDistance, enabled, NwcSectionAlignment.Back);

    public static NwcSectionPlane CreateLeft(double xDistance, int index = 4, bool enabled = true) =>
        new(index, new Vector3d(1, 0, 0), xDistance, enabled, NwcSectionAlignment.Left);

    public static NwcSectionPlane CreateRight(double xDistance, int index = 5, bool enabled = true) =>
        new(index, new Vector3d(-1, 0, 0), -xDistance, enabled, NwcSectionAlignment.Right);

    public static NwcSectionPlane CreateCustom(Vector3d normal, double distance, int index = 0, bool enabled = true) =>
        new(index, normal, distance, enabled, NwcSectionAlignment.Custom);
}

/// <summary>
/// A 3D section box (oriented or axis-aligned bounding volume) used for model isolation and clash review.
/// </summary>
public sealed record NwcSectionBox
{
    public bool IsEnabled { get; init; } = true;
    public BoundingBox3d Bounds { get; init; }

    public NwcSectionBox() { }

    public NwcSectionBox(BoundingBox3d bounds, bool isEnabled = true)
    {
        Bounds = bounds;
        IsEnabled = isEnabled;
    }

    /// <summary>Returns true if the point lies inside the section box volume.</summary>
    public bool Contains(Vector3d point) => IsEnabled ? Bounds.Contains(point) : true;

    /// <summary>Returns true if the target bounding box intersects the section box.</summary>
    public bool Intersects(BoundingBox3d other) => IsEnabled ? Bounds.Intersects(other) : true;

    /// <summary>
    /// Converts this section box into 6 bounding <see cref="NwcSectionPlane"/> instances.
    /// </summary>
    public IReadOnlyList<NwcSectionPlane> ToSectionPlanes()
    {
        if (Bounds.IsEmpty) return Array.Empty<NwcSectionPlane>();

        return
        [
            NwcSectionPlane.CreateLeft(Bounds.Min.X, 0, IsEnabled),
            NwcSectionPlane.CreateRight(Bounds.Max.X, 1, IsEnabled),
            NwcSectionPlane.CreateFront(Bounds.Min.Y, 2, IsEnabled),
            NwcSectionPlane.CreateBack(Bounds.Max.Y, 3, IsEnabled),
            NwcSectionPlane.CreateBottom(Bounds.Min.Z, 4, IsEnabled),
            NwcSectionPlane.CreateTop(Bounds.Max.Z, 5, IsEnabled),
        ];
    }
}

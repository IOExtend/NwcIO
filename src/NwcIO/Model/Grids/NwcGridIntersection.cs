using System.Globalization;

namespace NwcIO.Model.Grids;

/// <summary>
/// A 2D/3D intersection between two crossing grid lines (e.g. "A / 1").
/// </summary>
public sealed class NwcGridIntersection
{
    public required string Line1Name { get; init; }

    public required string Line2Name { get; init; }

    public required Vector3d IntersectionPoint { get; init; }

    public double Elevation => IntersectionPoint.Z;

    public string? SystemName { get; init; }

    public string DisplayName => $"{Line1Name} / {Line2Name}";

    public double DistanceTo(Vector3d point) => Vector3d.Distance(IntersectionPoint, point);

    public double DistanceTo2D(Vector3d point)
    {
        double dx = IntersectionPoint.X - point.X;
        double dy = IntersectionPoint.Y - point.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{DisplayName} at ({IntersectionPoint.X:F2}, {IntersectionPoint.Y:F2}, {IntersectionPoint.Z:F2})");
}

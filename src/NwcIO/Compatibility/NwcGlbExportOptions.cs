namespace NwcIO;

/// <summary>Coordinate and camera options for the binary glTF 2.0 exporter.</summary>
public sealed class NwcGlbExportOptions
{
    private static readonly NwcGlbExportOptions World = new();
    private static readonly NwcGlbExportOptions Local = new() { WorldCoordinates = false };

    /// <summary>Bakes mesh placements into world coordinates. The default is true.</summary>
    public bool WorldCoordinates { get; init; } = true;

    /// <summary>Rotates positions, normals and saved cameras from +Z up to +Y up. The default is false.</summary>
    public bool ConvertZUpToYUp { get; init; }

    /// <summary>Positive finite multiplier for geometry and camera lengths. The default is 1.</summary>
    public double UnitScale { get; init; } = 1.0;

    /// <summary>Subtracts the mesh bounds' center before packing float positions and restores it on a root node.</summary>
    public bool RecenterToOrigin { get; init; }

    /// <summary>Includes cameras from saved viewpoints. The default is true.</summary>
    public bool ExportCameras { get; init; } = true;

    internal static NwcGlbExportOptions ForCoordinates(bool worldCoordinates) => worldCoordinates ? World : Local;
}

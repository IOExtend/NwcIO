namespace NwcIO;

/// <summary>Options controlling glTF 2.0 Binary (.glb) export.</summary>
public sealed class NwcGlbExportOptions
{
    public static NwcGlbExportOptions Default { get; } = new();

    /// <summary>Export world-space vertices and indices (default: true).</summary>
    public bool WorldCoordinates { get; set; } = true;

    /// <summary>
    /// Convert the document's Z-up frame to glTF's Y-up frame with (x, y, z) -> (x, z, -y) for positions
    /// and normals and rotate cameras to match (default: false, which keeps the document frame).
    /// </summary>
    public bool ConvertZUpToYUp { get; set; }

    /// <summary>Uniform scale applied to positions, camera placement and clipping distances (default: 1.0).</summary>
    public double UnitScale { get; set; } = 1.0;

    /// <summary>
    /// Subtract the model bounding-box center in double precision before the float cast and add it back as the
    /// scene root translation (default: false). Keeps large georeferenced coordinates precise.
    /// </summary>
    public bool RecenterToOrigin { get; set; }

    /// <summary>Export saved viewpoints as glTF cameras and camera nodes (default: true).</summary>
    public bool ExportCameras { get; set; } = true;
}

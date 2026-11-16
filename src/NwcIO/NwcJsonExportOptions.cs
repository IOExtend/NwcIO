namespace NwcIO;

/// <summary>Options for the JSON export.</summary>
public sealed class NwcJsonExportOptions
{
    public static NwcJsonExportOptions Default { get; } = new();

    /// <summary>Indent the output (default: true).</summary>
    public bool Indented { get; set; } = true;

    /// <summary>Include 3D transforms in the JSON export (default: true).</summary>
    public bool IncludeTransforms { get; set; } = true;

    /// <summary>Include hierarchical scene graph tree in the JSON export (default: true).</summary>
    public bool IncludeSceneTree { get; set; } = true;

    /// <summary>Include 3D mesh geometry summaries in the JSON export (default: false).</summary>
    public bool IncludeMeshes { get; set; }

    /// <summary>Include materials and surface colors in the JSON export (default: true).</summary>
    public bool IncludeMaterials { get; set; } = true;

    /// <summary>Include saved viewpoints and cameras in the JSON export (default: true).</summary>
    public bool IncludeSavedViewpoints { get; set; } = true;

    /// <summary>Include the per-stream container catalog in the output (default: false).</summary>
    public bool IncludeStreamCatalog { get; set; }
}

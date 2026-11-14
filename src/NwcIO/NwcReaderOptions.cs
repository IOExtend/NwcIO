namespace NwcIO;

/// <summary>Options controlling what <see cref="NwcDocument.Open"/> reads and keeps.</summary>
public sealed class NwcReaderOptions
{
    public static NwcReaderOptions Default { get; } = new();

    /// <summary>Parse the scene-tree/property streams (default: true).</summary>
    public bool ReadPropertyRecords { get; set; } = true;

    /// <summary>
    /// Parse GUID tables into <see cref="NwcDocument.Guids"/> (default: true); on files read without a DbCache directory the scene
    /// tree takes its element GUIDs from them too. On a model file read through its DbCache directory, element GUIDs
    /// (<see cref="Model.NwcNode.ElementGuid"/>) are read from <c>LcOpGuidStore</c> whenever the scene tree is built, whatever this
    /// option says, so a malformed store throws <see cref="InvalidDataException"/> with it off as well.
    /// </summary>
    public bool ReadGuids { get; set; } = true;

    /// <summary>Parse 3D transformation streams (default: true).</summary>
    public bool ReadTransforms { get; set; } = true;

    /// <summary>Build the hierarchical scene graph tree (default: true).</summary>
    public bool BuildSceneTree { get; set; } = true;

    /// <summary>Parse 3D meshes from geometry streams (default: true).</summary>
    public bool ReadMeshes { get; set; } = true;

    /// <summary>Extract materials, RGBA colors, and surface parameters (default: true).</summary>
    public bool ReadMaterials { get; set; } = true;

    /// <summary>Parse saved viewpoints and camera orientation records (default: true).</summary>
    public bool ReadSavedViewpoints { get; set; } = true;

    /// <summary>Parse selection sets and search sets (default: true).</summary>
    public bool ReadSelectionSets { get; set; } = true;

    /// <summary>Parse 4D TimeLiner construction schedule (default: true).</summary>
    public bool ReadTimeLiner { get; set; } = true;

    /// <summary>Keep the stream catalog (offsets, sizes, kinds) in the document (default: true).</summary>
    public bool KeepStreamCatalog { get; set; } = true;
}

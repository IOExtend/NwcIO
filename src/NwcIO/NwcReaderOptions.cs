namespace NwcIO;

/// <summary>Options controlling what <see cref="NwcDocument.Open"/> reads and keeps.</summary>
public sealed class NwcReaderOptions
{
    private int _maxExpandedPropertyReferences = Internal.Parsing.HierarchyParser.DefaultMaxExpandedPropertyReferences;

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

    /// <summary>
    /// Override the document length unit when the source unit is known. Null (default) infers feet for the Revit
    /// loader/exporter and leaves the unit Unknown for everything else. Exposed as <see cref="NwcDocument.LengthUnit"/>.
    /// </summary>
    public NwcLengthUnit? LengthUnit { get; set; }

    /// <summary>
    /// Cap on the decompressed size of one container stream (default: 512 MiB). Exceeding it makes
    /// <see cref="NwcDocument.Open(string, NwcReaderOptions?)"/> throw <see cref="InvalidDataException"/>.
    /// </summary>
    public long MaxDecompressedStreamBytes { get; set; } = Internal.Container.NwcContainerReader.DefaultMaxDecompressedStreamBytes;

    /// <summary>
    /// Cap on the decompressed size of all container streams together (default: 2 GiB). Exceeding it makes
    /// <see cref="NwcDocument.Open(string, NwcReaderOptions?)"/> throw <see cref="InvalidDataException"/>.
    /// </summary>
    public long MaxTotalDecompressedBytes { get; set; } = Internal.Container.NwcContainerReader.DefaultMaxTotalDecompressedBytes;

    /// <summary>
    /// Positive cap on the total property records and property tabs referenced by nodes in the expanded DbCache
    /// scene tree (default: 134,217,728). Exceeding it throws <see cref="InvalidDataException"/> before expansion.
    /// Raising the cap can increase memory use.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public int MaxExpandedPropertyReferences
    {
        get => _maxExpandedPropertyReferences;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxExpandedPropertyReferences = value;
        }
    }

    /// <summary>
    /// Cap on the vertices of one geometry record on the lenient reader path (default: 200,000). Larger records are skipped.
    /// Model files read through their DbCache directory use the strict geometry parser and its own bounds.
    /// </summary>
    public int MaxMeshVertices { get; set; } = Internal.Parsing.MeshParser.MaxPlausibleVertices;

    /// <summary>
    /// Throw <see cref="InvalidDataException"/> when the reader records an error-level diagnostic
    /// (default: false, diagnostics are only recorded).
    /// </summary>
    public bool Strict { get; set; }
}

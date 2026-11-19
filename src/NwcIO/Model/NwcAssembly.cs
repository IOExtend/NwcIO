using NwcIO.Internal;
using NwcIO.Internal.Json;

namespace NwcIO.Model;

/// <summary>
/// A federated multi-model BIM assembly combining multiple Navisworks documents
/// (.nwc, .nwd, or .nwf external references) in a unified coordinate system.
/// </summary>
public sealed partial class NwcAssembly
{
    private readonly Dictionary<NwcMesh, int> _meshModelIndexes;

    public string Name { get; }
    public IReadOnlyList<NwcDocument> Models { get; }

    /// <summary>True when the contributing models report different length units; the assembly does not rescale them.</summary>
    public bool HasMixedLengthUnits =>
        Models.Select(model => model.LengthUnit)
            .Where(unit => unit != NwcLengthUnit.Unknown)
            .Distinct()
            .Skip(1)
            .Any();

    internal NwcLengthUnit CommonLengthUnit => HasMixedLengthUnits
        ? NwcLengthUnit.Unknown
        : Models.Select(model => model.LengthUnit).FirstOrDefault(unit => unit.GetMetresPerUnit().HasValue);

    /// <summary>
    /// Synthetic assembly root. Its id is negative so it cannot collide with the
    /// document roots (id 0); node ids are otherwise unique only within a document.
    /// </summary>
    public NwcNode RootNode { get; }
    public IReadOnlyList<NwcMesh> AllMeshes { get; }
    public IReadOnlyList<NwcMaterial> AllMaterials { get; }
    public BoundingBox3d BoundingBox { get; }

    /// <summary>Saved viewpoints aggregated from contributing models or generated for the assembly.</summary>
    public IReadOnlyList<NwcSavedViewpoint> SavedViewpoints { get; }

    /// <summary>Gets the primary/default viewpoint of the federated assembly.</summary>
    public NwcSavedViewpoint? DefaultViewpoint =>
        SavedViewpoints.Count > 0 ? SavedViewpoints[0] : (!BoundingBox.IsEmpty ? NwcSavedViewpoint.CreateDefault(BoundingBox, $"{Name} Overview") : null);

    /// <summary>4D TimeLiner construction simulation schedule.</summary>
    public TimeLiner.NwcTimeLinerSchedule? TimeLiner { get; set; }

    /// <summary>Selection Sets and Search Sets defined in the federated assembly.</summary>
    public List<Sets.NwcSetItem> SelectionSets { get; } = [];

    /// <summary>Diagnostics from contributing models plus assembly-level issues such as unresolved references.</summary>
    public IReadOnlyList<NwcDiagnostic> Diagnostics { get; internal set; } = [];

    /// <summary>
    /// XRef entries of the opened federation that could not be resolved to an existing file,
    /// so the corresponding component model is missing from <see cref="Models"/>.
    /// Populated by <see cref="OpenNwf"/>; empty for assemblies built otherwise.
    /// </summary>
    public IReadOnlyList<NwcXRef> UnresolvedXRefs { get; internal set; } = [];

    /// <summary>
    /// XRef entries that resolved to an existing file which this loader does not append
    /// (anything other than .nwc/.nwd, including nested .nwf files). Populated by
    /// <see cref="OpenNwf"/>; empty for assemblies built otherwise.
    /// </summary>
    public IReadOnlyList<NwcXRef> SkippedXRefs { get; internal set; } = [];

    private NwcAssembly(
        string name,
        IReadOnlyList<NwcDocument> models,
        NwcNode rootNode,
        IReadOnlyList<NwcMesh> allMeshes,
        IReadOnlyList<NwcMaterial> allMaterials,
        BoundingBox3d boundingBox,
        IReadOnlyList<NwcSavedViewpoint> savedViewpoints,
        Dictionary<NwcMesh, int> meshModelIndexes)
    {
        Name = name;
        Models = models;
        RootNode = rootNode;
        AllMeshes = allMeshes;
        AllMaterials = allMaterials;
        BoundingBox = boundingBox;
        SavedViewpoints = savedViewpoints;
        _meshModelIndexes = meshModelIndexes;
    }

    /// <summary>
    /// Returns the zero-based index of the source model that owns <paramref name="mesh"/>,
    /// or -1 when the mesh does not belong to this assembly.
    /// </summary>
    public int GetModelIndex(NwcMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return _meshModelIndexes.TryGetValue(mesh, out int index) ? index : -1;
    }

    /// <summary>
    /// Returns the source model that owns <paramref name="mesh"/>, or null when the
    /// mesh does not belong to this assembly.
    /// </summary>
    public NwcDocument? GetModelOf(NwcMesh mesh)
    {
        int index = GetModelIndex(mesh);
        return index >= 0 && index < Models.Count ? Models[index] : null;
    }

    /// <summary>
    /// Creates a federated assembly from an arbitrary collection of opened Navisworks documents.
    /// </summary>
    public static NwcAssembly Create(string name, params NwcDocument[] models) =>
        Create(name, (IEnumerable<NwcDocument>)models);

    /// <summary>
    /// Creates a federated assembly from an arbitrary collection of opened Navisworks documents.
    /// </summary>
    public static NwcAssembly Create(string name, IEnumerable<NwcDocument> models)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(models);

        var modelList = models.ToList();
        var allMeshes = new List<NwcMesh>();
        var meshModelIndexes = new Dictionary<NwcMesh, int>();
        // Dedupe by reference, NOT by name: disciplines routinely define their own
        // material with a shared name (Concrete, Steel) and different colours, and a
        // by-name map would silently drop the later ones from AllMaterials.
        var matSet = new HashSet<NwcMaterial>();

        var rootChildren = new List<NwcNode>();
        var totalBox = BoundingBox3d.Empty;

        for (int m = 0; m < modelList.Count; m++)
        {
            var doc = modelList[m];
            foreach (var mesh in doc.Meshes)
            {
                allMeshes.Add(mesh);
                meshModelIndexes[mesh] = m;
            }

            foreach (var mat in doc.Materials)
            {
                matSet.Add(mat);
            }

            foreach (var mesh in doc.Meshes)
            {
                totalBox = totalBox.Union(mesh.GetWorldBoundingBox());
            }

            if (doc.RootNode is not null)
            {
                rootChildren.Add(doc.RootNode);
            }
            else
            {
                string docName = Path.GetFileNameWithoutExtension(doc.FileInfo.SourcePath ?? $"Model_{m + 1}");
                var fallbackNode = new NwcNode
                {
                    Id = -(m + 2),
                    Name = docName,
                    Class = "LcOpModelFile",
                };
                fallbackNode.Properties.AddRange(doc.PropertyRecords);
                rootChildren.Add(fallbackNode);
            }
        }

        var allViewpoints = new List<NwcSavedViewpoint>();
        foreach (var doc in modelList)
        {
            allViewpoints.AddRange(doc.SavedViewpoints);
        }

        if (allViewpoints.Count == 0 && !totalBox.IsEmpty)
        {
            allViewpoints.Add(NwcSavedViewpoint.CreateDefault(totalBox, $"{name} Overview"));
            allViewpoints.Add(NwcSavedViewpoint.CreateTop(totalBox, $"{name} Top View"));
        }

        var rootNode = new NwcNode
        {
            Id = -1,
            Name = name,
            Class = "LcOpModelAssembly",
        };
        rootNode.Children.AddRange(rootChildren);

        var assembly = new NwcAssembly(name, modelList, rootNode, allMeshes, [.. matSet], totalBox, allViewpoints, meshModelIndexes);
        var diagnostics = modelList.SelectMany(m => m.Diagnostics).ToList();
        if (assembly.HasMixedLengthUnits)
        {
            diagnostics.Add(new NwcDiagnostic(NwcDiagnosticSeverity.Warning, "MixedLengthUnits",
                "The assembly contains models with different length units. Their original coordinates were retained."));
        }
        assembly.Diagnostics = diagnostics;
        return assembly;
    }

    /// <summary>
    /// Discovers and loads all .nwc models in a folder into a single federated assembly.
    /// </summary>
    /// <exception cref="InvalidDataException">A file is not an LcUStream container, or its DbCache directory names model segments that break the format.</exception>
    public static NwcAssembly LoadFromDirectory(
        string directoryPath,
        string searchPattern = "*.nwc",
        NwcReaderOptions? options = null) =>
        LoadFromDirectory(directoryPath, [searchPattern], recursive: false, options);

    /// <summary>
    /// Discovers and loads files matching any of <paramref name="searchPatterns"/> into a
    /// single federated assembly, optionally recursing into subfolders. Files are opened
    /// in a deterministic case-insensitive path order.
    /// </summary>
    public static NwcAssembly LoadFromDirectory(
        string directoryPath,
        IReadOnlyList<string> searchPatterns,
        bool recursive = false,
        NwcReaderOptions? options = null)
        => LoadFromDirectory(directoryPath, searchPatterns, recursive, options, null, default);

    /// <summary>
    /// Loads a folder of models with cancellation and per-model progress.
    /// </summary>
    public static NwcAssembly LoadFromDirectory(
        string directoryPath,
        string searchPattern,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
        => LoadFromDirectory(directoryPath, [searchPattern], recursive: false, options, progress, cancellationToken);

    /// <summary>
    /// Loads files matching any of <paramref name="searchPatterns"/> with cancellation and per-model progress.
    /// </summary>
    public static NwcAssembly LoadFromDirectory(
        string directoryPath,
        IReadOnlyList<string> searchPatterns,
        bool recursive,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
        => LoadFromDirectoryCore(directoryPath, searchPatterns, recursive, options, progress, asynchronous: false, cancellationToken)
            .GetSynchronousResult();

    private static async ValueTask<NwcAssembly> LoadFromDirectoryCore(
        string directoryPath,
        IReadOnlyList<string> searchPatterns,
        bool recursive,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        bool asynchronous,
        CancellationToken cancellationToken)
    {
        if (asynchronous) cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(searchPatterns);
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }
        if (searchPatterns.Count == 0)
        {
            throw new ArgumentException("At least one search pattern is required.", nameof(searchPatterns));
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = searchPatterns
            .SelectMany(pattern => Directory.GetFiles(directoryPath, pattern, searchOption))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new FileNotFoundException(
                $"No files matching '{string.Join(";", searchPatterns)}' found in {directoryPath}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var docs = new List<NwcDocument>(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            docs.Add(await OpenModel(files[i], options, asynchronous, cancellationToken).ConfigureAwait(false));
            progress?.Report(new NwcProgress("load-models", i + 1, files.Length));
        }

        string assemblyName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = Create(assemblyName, docs);
        cancellationToken.ThrowIfCancellationRequested();
        return assembly;
    }

    /// <summary>
    /// Opens a Navisworks File (.nwf) assembly, automatically discovering and loading its external references.
    /// </summary>
    /// <exception cref="InvalidDataException">A file is not an LcUStream container, or its DbCache directory names model segments that break the format.</exception>
    public static NwcAssembly OpenNwf(string nwfPath, NwcReaderOptions? options = null)
        => OpenNwf(nwfPath, options, null, default);

    /// <summary>
    /// Opens a .nwf assembly with cancellation and progress for each loaded model.
    /// </summary>
    public static NwcAssembly OpenNwf(
        string nwfPath,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
        => OpenNwfCore(nwfPath, options, progress, asynchronous: false, cancellationToken).GetSynchronousResult();

    private static async ValueTask<NwcAssembly> OpenNwfCore(
        string nwfPath,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        bool asynchronous,
        CancellationToken cancellationToken)
    {
        if (asynchronous) cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(nwfPath))
        {
            throw new FileNotFoundException($"NWF file not found: {nwfPath}", nwfPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var primaryDoc = await OpenModel(nwfPath, options, asynchronous, cancellationToken).ConfigureAwait(false);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(nwfPath)) ?? ".";

        var loadedDocs = new List<NwcDocument> { primaryDoc };
        var loadedPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        var unresolved = new List<NwcXRef>();
        var skipped = new List<NwcXRef>();
        int total = 1 + (primaryDoc.XRefs?.References.Count ?? 0);
        progress?.Report(new NwcProgress("load-models", 1, total));

        if (primaryDoc.XRefs is not null && primaryDoc.XRefs.References.Count > 0)
        {
            int processed = 1;
            foreach (var xref in primaryDoc.XRefs.References)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!xref.TryResolve(baseDir, out string resolved))
                {
                    unresolved.Add(xref);
                }
                else if (loadedPaths.Add(Path.GetFullPath(resolved)))
                {
                    // Load every resolved file once, whatever the stored path spelled like.
                    if (resolved.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) ||
                        resolved.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase))
                    {
                        loadedDocs.Add(await OpenModel(resolved, options, asynchronous, cancellationToken).ConfigureAwait(false));
                    }
                    else
                    {
                        skipped.Add(xref);
                    }
                }

                // Every reference counts, including entries that did not load a model.
                processed++;
                progress?.Report(new NwcProgress("load-models", processed, total));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        string assemblyName = Path.GetFileNameWithoutExtension(nwfPath);
        var assembly = Create(assemblyName, loadedDocs);
        assembly.UnresolvedXRefs = unresolved;
        assembly.SkippedXRefs = skipped;
        if (unresolved.Count > 0 || skipped.Count > 0)
        {
            var diagnostics = assembly.Diagnostics.ToList();
            diagnostics.AddRange(unresolved.Select(x => new NwcDiagnostic(
                NwcDiagnosticSeverity.Warning,
                "NwfUnresolvedXref",
                $"Unresolved NWF reference: '{x.EffectivePath}'.")));
            diagnostics.AddRange(skipped.Select(x => new NwcDiagnostic(
                NwcDiagnosticSeverity.Warning,
                "NwfSkippedXref",
                $"Skipped unsupported NWF reference: '{x.EffectivePath}'.")));
            assembly.Diagnostics = diagnostics;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return assembly;
    }

    private static ValueTask<NwcDocument> OpenModel(
        string path, NwcReaderOptions? options, bool asynchronous, CancellationToken cancellationToken) =>
        asynchronous
            ? new ValueTask<NwcDocument>(NwcDocument.OpenAsync(path, options, cancellationToken: cancellationToken))
            : ValueTask.FromResult(NwcDocument.Open(path, options, progress: null, cancellationToken));

    /// <summary>Loads documents in a folder using asynchronous file reads.</summary>
    public static Task<NwcAssembly> LoadFromDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.nwc",
        NwcReaderOptions? options = null,
        IProgress<NwcProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => LoadFromDirectoryCore(directoryPath, [searchPattern], recursive: false, options, progress,
            asynchronous: true, cancellationToken).AsTask();

    /// <summary>Opens a .nwf and its referenced models using asynchronous file reads.</summary>
    public static Task<NwcAssembly> OpenNwfAsync(
        string nwfPath,
        NwcReaderOptions? options = null,
        IProgress<NwcProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => OpenNwfCore(nwfPath, options, progress, asynchronous: true, cancellationToken).AsTask();

    /// <summary>
    /// Builds a single document that carries the assembly scene tree, meshes with their transforms,
    /// viewpoints, GUIDs, the assembly selection sets and the assembly TimeLiner schedule.
    /// The file info is neutral and does not claim a Revit origin.
    /// </summary>
    /// <remarks>
    /// Node ids of different models overlap (every document root is 0), so the exported tree is a copy
    /// numbered depth-first from 0 (the assembly root); the assembly itself is not changed. Selection sets
    /// and TimeLiner tasks stored in the source models bind to the ids of their own tree and are not
    /// carried over; only <see cref="SelectionSets"/> and <see cref="TimeLiner"/> of the assembly are
    /// written, unchanged. Materials are part of the returned document but the .nwc writer does not store them.
    /// </remarks>
    public NwcDocument ToDocument()
    {
        int nextId = 0;
        NwcNode Renumber(NwcNode source, NwcNode? parent)
        {
            var copy = new NwcNode { Id = nextId++, Name = source.Name, Class = source.Class, Parent = parent };
            copy.Properties.AddRange(source.Properties);
            foreach (var child in source.Children)
            {
                copy.Children.Add(Renumber(child, copy));
            }
            return copy;
        }

        return NwcDocument.CreateNative(
            fileInfo: new NwcFileInfo
            {
                SourcePath = $"{Name}.nwc",
                LoaderPlugin = "LcOpNwcIOAssembly",
            },
            meshes: AllMeshes,
            savedViewpoints: SavedViewpoints,
            guids: Models.SelectMany(m => m.Guids),
            materials: AllMaterials,
            rootNode: Renumber(RootNode, null),
            selectionSets: SelectionSets,
            timeLiner: TimeLiner);
    }

    /// <summary>Exports the federated assembly as a single .nwc document.</summary>
    public void SaveNwc(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ToDocument().Save(path);
    }

    /// <summary>Exports a structured summary of the federated assembly to a stream in JSON format.</summary>
    public void SaveJson(Stream stream, NwcJsonExportOptions? options = null)
        => SaveJson(stream, options, null, default);

    /// <summary>Exports the assembly JSON to a stream with cancellation and progress.</summary>
    public void SaveJson(Stream stream, NwcJsonExportOptions? options, IProgress<NwcProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 0, 1));
        cancellationToken.ThrowIfCancellationRequested();
        SaveJsonCore(stream, options);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 1, 1));
    }

    private void SaveJsonCore(Stream stream, NwcJsonExportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= NwcJsonExportOptions.Default;
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions
        {
            Indented = options.Indented
        });

        writer.WriteStartObject();
        writer.WriteString("assemblyName", Name);
        writer.WriteNumber("modelCount", Models.Count);
        writer.WriteNumber("totalMeshes", AllMeshes.Count);
        writer.WriteNumber("totalMaterials", AllMaterials.Count);

        writer.WriteStartObject("boundingBox");
        writer.WriteString("min", BoundingBox.Min.ToString());
        writer.WriteString("max", BoundingBox.Max.ToString());
        writer.WriteString("size", BoundingBox.Size.ToString());
        writer.WriteEndObject();

        if (Diagnostics.Count > 0)
        {
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in Diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("severity", diagnostic.Severity.ToString());
                writer.WriteString("code", diagnostic.Code);
                writer.WriteString("message", diagnostic.Message);
                if (diagnostic.StreamIndex is int streamIndex)
                {
                    writer.WriteNumber("streamIndex", streamIndex);
                }
                if (diagnostic.ByteOffset is long byteOffset)
                {
                    writer.WriteNumber("byteOffset", byteOffset);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteStartArray("models");
        foreach (var m in Models)
        {
            writer.WriteStartObject();
            writer.WriteString("sourcePath", m.FileInfo.SourcePath ?? "");
            writer.WriteNumber("meshes", m.Meshes.Count);
            writer.WriteNumber("properties", m.PropertyRecords.Count);
            writer.WriteNumber("transforms", m.Transforms.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        if (SavedViewpoints.Count > 0)
        {
            writer.WriteStartArray("savedViewpoints");
            foreach (var vp in SavedViewpoints)
            {
                writer.WriteStartObject();
                writer.WriteString("id", vp.Id?.ToString("D"));
                writer.WriteString("name", vp.Name);
                writer.WriteString("projection", vp.Camera.Projection.ToString());
                writer.WriteNumber("fieldOfView", vp.Camera.FieldOfView);
                writer.WriteNumber("focalDistance", vp.Camera.FocalDistance);
                writer.WriteString("position", vp.Camera.Position.ToString());
                writer.WriteString("direction", vp.Camera.Direction.ToString());
                writer.WriteString("target", vp.Camera.Target.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>Exports a structured summary of the federated assembly to a JSON file.</summary>
    public void SaveJson(string path, NwcJsonExportOptions? options = null)
        => SaveJson(path, options, null, default);

    /// <summary>Exports the assembly JSON to a file with cancellation and progress.</summary>
    public void SaveJson(string path, NwcJsonExportOptions? options, IProgress<NwcProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        cancellationToken.ThrowIfCancellationRequested();
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = File.Create(temp))
            {
                SaveJson(fs, options, progress, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}

using NwcIO.Internal;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using NwcIO.Model;

namespace NwcIO;

/// <summary>
/// An opened Navisworks document (.nwc cache, .nwd document) parsed without Navisworks installed.
/// </summary>
public sealed partial class NwcDocument
{
    private NwcDocument(
        NwcFileInfo fileInfo,
        IReadOnlyList<NwcPropertyRecord> records,
        IReadOnlyList<Guid> guids,
        IReadOnlyList<NwcTransform> transforms,
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcMaterial> materials,
        NwcNode? rootNode,
        NwcXRefTable? xRefs,
        NwcDbCacheDirectory? dbCache,
        IReadOnlyList<NwcSavedViewpoint> savedViewpoints,
        IReadOnlyList<Model.Sets.NwcSetItem> selectionSets,
        Model.TimeLiner.NwcTimeLinerSchedule? timeLiner,
        NwcContainer container)
    {
        FileInfo = fileInfo;
        PropertyRecords = records;
        Guids = guids;
        Transforms = transforms;
        Meshes = meshes;
        Materials = materials;
        RootNode = rootNode;
        XRefs = xRefs;
        DbCache = dbCache;
        SavedViewpoints = savedViewpoints;
        SelectionSets = selectionSets;
        TimeLiner = timeLiner;
        Container = container;
    }

    public NwcFileInfo FileInfo { get; }

    /// <summary>Length unit of the document coordinates and raw length values, inferred or overridden.</summary>
    public NwcLengthUnit LengthUnit => FileInfo.LengthUnit;

    /// <summary>
    /// True when the loader plugin is exactly (ordinal) the Revit loader <c>LcNwcLoaderPlugin:lcldrevit</c> (Navisworks reading a
    /// <c>.rvt</c>) or the Revit NWC exporter <c>nwexportrevit</c>. Computed from <see cref="FileInfo"/>.
    /// </summary>
    public bool IsRevitSource => FileInfo.LoaderPlugin is "LcNwcLoaderPlugin:lcldrevit" or "nwexportrevit";

    /// <summary>Metres in one <see cref="LengthUnit"/> (0.3048 for feet); null when the unit is unknown.</summary>
    public double? MetresPerUnit => LengthUnit.GetMetresPerUnit();

    /// <summary>Flat records parsed from the scene-tree/property streams.</summary>
    public IReadOnlyList<NwcPropertyRecord> PropertyRecords { get; }

    /// <summary>GUIDs from the GUID table streams, in file order; for model files read through their DbCache directory, the <c>LcOpGuidStore</c> table.</summary>
    public IReadOnlyList<Guid> Guids { get; }

    /// <summary>3D spatial transformations and bounding boxes parsed from transform streams.</summary>
    public IReadOnlyList<NwcTransform> Transforms { get; }

    /// <summary>3D polygon meshes extracted from geometry streams and TOC tables.</summary>
    public IReadOnlyList<NwcMesh> Meshes { get; }

    /// <summary>Surface materials, RGBA colors and PBR properties: for model files read through their DbCache directory, the materials decoded from the fragments' appearances (none without the scene tree); for other files, the materials named in property records with their `revit_Color` colours (neutral and `IsInferred` when none) plus one inferred default for meshes without a material.</summary>
    public IReadOnlyList<NwcMaterial> Materials { get; }

    /// <summary>Root of the hierarchical scene graph tree (matching Navisworks selection tree).</summary>
    public NwcNode? RootNode { get; }

    /// <summary>External references (XRefs) parsed from XRefJson streams.</summary>
    public NwcXRefTable? XRefs { get; }

    /// <summary>Database cache directory parsed from LcOpDBCache streams.</summary>
    public NwcDbCacheDirectory? DbCache { get; }

    /// <summary>Saved viewpoints and camera views extracted from container viewpoint streams.</summary>
    public IReadOnlyList<NwcSavedViewpoint> SavedViewpoints { get; }

    /// <summary>Selection Sets and Search Sets defined in the document.</summary>
    public IReadOnlyList<Model.Sets.NwcSetItem> SelectionSets { get; }

    /// <summary>4D TimeLiner construction simulation schedule.</summary>
    public Model.TimeLiner.NwcTimeLinerSchedule? TimeLiner { get; set; }

    /// <summary>Reader diagnostics for data that was skipped or substituted.</summary>
    public IReadOnlyList<NwcDiagnostic> Diagnostics { get; internal set; } = [];

    /// <summary>Gets the total 3D bounding box over all meshes in the document.</summary>
    public BoundingBox3d BoundingBox
    {
        get
        {
            BoundingBox3d? indexedBounds = null;
            GetIndexedBounds(ref indexedBounds);
            return indexedBounds ?? (_bounds ??= Meshes.Aggregate(BoundingBox3d.Empty,
                static (bounds, mesh) => bounds.Union(mesh.GetWorldBoundingBox())));
        }
    }

    /// <summary>Gets the primary/default viewpoint of the document.</summary>
    public NwcSavedViewpoint? DefaultViewpoint =>
        SavedViewpoints.Count > 0 ? SavedViewpoints[0] : (!BoundingBox.IsEmpty ? NwcSavedViewpoint.CreateDefault(BoundingBox) : null);

    private BoundingBox3d? _bounds;
    partial void GetIndexedBounds(ref BoundingBox3d? bounds);

    internal NwcContainer Container { get; }

    private string? _loadedPath;

    /// <summary>Returns the full path passed to the file-based reader, or <see langword="null"/> for a stream or an in-memory document.</summary>
    /// <remarks>This is the location of the Navisworks file. <see cref="NwcFileInfo.SourcePath"/> comes from the file's metadata.</remarks>
    public string? GetLoadedPath() => _loadedPath;

    /// <summary>Opens and parses a Navisworks file from disk.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not an LcUStream container, a stream exceeds <see cref="NwcReaderOptions.MaxDecompressedStreamBytes"/> or <see cref="NwcReaderOptions.MaxTotalDecompressedBytes"/>, or its DbCache directory names model segments that break the format.</exception>
    public static NwcDocument Open(string path, NwcReaderOptions? options = null)
        => Open(path, options, null, default);

    /// <summary>
    /// Opens and parses a Navisworks file from disk with cancellation and progress reporting.
    /// </summary>
    public static NwcDocument Open(
        string path,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Navisworks file not found: {path}", path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        var document = Open(stream, options, Path.GetFileNameWithoutExtension(path), progress, cancellationToken);
        document._loadedPath = Path.GetFullPath(path);
        return document;
    }

    /// <summary>Opens and parses a Navisworks file from a stream.</summary>
    /// <exception cref="InvalidDataException">The stream is not an LcUStream container, a stream exceeds <see cref="NwcReaderOptions.MaxDecompressedStreamBytes"/> or <see cref="NwcReaderOptions.MaxTotalDecompressedBytes"/>, or its DbCache directory names model segments that break the format.</exception>
    public static NwcDocument Open(Stream stream, NwcReaderOptions? options = null, string? fallbackName = null)
        => Open(stream, options, fallbackName, null, default);

    /// <summary>
    /// Opens and parses a Navisworks file from a stream with cancellation and progress reporting.
    /// </summary>
    public static NwcDocument Open(
        Stream stream,
        NwcReaderOptions? options,
        string? fallbackName,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= NwcReaderOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();

        bool keepStreamCatalog = options.KeepStreamCatalog;
        return NwcContainerReader.Read(stream, options.MaxDecompressedStreamBytes, options.MaxTotalDecompressedBytes,
            deferPayloads: !keepStreamCatalog,
            container => ParseContainer(container, options, keepStreamCatalog, fallbackName, progress, cancellationToken).GetSynchronousResult(),
            cancellationToken);
    }

    private static async ValueTask<NwcDocument> ParseContainer(
        NwcContainer container,
        NwcReaderOptions options,
        bool keepStreamCatalog,
        string? fallbackName,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("read-container", 1, 1));
        await StreamClassifier.ClassifyAllAsync(container.Streams, cancellationToken).ConfigureAwait(false);
        var segments = await ModelGraphReader.LocateAsync(container).ConfigureAwait(false);

        var fileInfo = new NwcFileInfo();
        var headerStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.PartitionHeader);
        if (headerStream is not null)
        {
            fileInfo = PartitionHeaderParser.Parse(await headerStream.LoadAsync().ConfigureAwait(false));
        }

        fileInfo.FormatVersion ??= container.Sections.Select(s => s.InternalVersion).FirstOrDefault(v => v is not null);
        bool linkedWriterCache = segments is null && fileInfo.Options.Contains(WriterSceneLinks.MetreCacheOption);
        fileInfo.LengthUnit = options.LengthUnit ?? (linkedWriterCache ? NwcLengthUnit.Meters : InferLengthUnit(fileInfo.LoaderPlugin));
        fileInfo.Encoding ??= container.Sections.Select(s => s.Encoding).FirstOrDefault(e => e is not null);
        fileInfo.ChunkMarker ??= container.Sections.Select(s => s.Marker).FirstOrDefault(m => m is not null);

        string rootName = Path.GetFileNameWithoutExtension(fileInfo.SourcePath ?? fallbackName ?? "Model");

        var diagnostics = new List<NwcDiagnostic>();
        var records = new List<NwcPropertyRecord>();
        cancellationToken.ThrowIfCancellationRequested();
        if (options.ReadPropertyRecords)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.SceneTree))
            {
                var scanner = new PropertyRecordScanner(await s.GetDataAsync().ConfigureAwait(false));
                records.AddRange(scanner.Scan());
                if (segments is null && scanner.FailureReason is not null)
                {
                    // The lenient scanner searches forward; a rejected probe is not a lost record.
                    diagnostics.Add(new NwcDiagnostic(
                        NwcDiagnosticSeverity.Warning,
                        "property-scan-failed",
                        scanner.FailureReason,
                        s.Index,
                        scanner.FailureOffset));
                }
            }
        }

        var guids = new List<Guid>();
        if (options.ReadGuids && segments is null)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.Guids))
            {
                if (GuidTableParser.TryParse(await s.LoadAsync().ConfigureAwait(false), out var parsed))
                {
                    guids.AddRange(parsed);
                }
            }
        }

        NwcXRefTable? xRefs = null;
        var xrefStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.XRefJson);
        if (xrefStream is not null)
        {
            xRefs = XRefTableParser.Parse(await xrefStream.LoadAsync().ConfigureAwait(false));
        }

        var transforms = new List<NwcTransform>();
        var meshes = new List<NwcMesh>();
        int skippedMeshRecords = 0;
        var materials = new List<NwcMaterial>();
        NwcNode? rootNode = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (segments is not null)
        {
            var graph = await ModelGraphReader.ReadAsync(segments, options, rootName, xRefs, cancellationToken).ConfigureAwait(false);
            transforms.AddRange(graph.Transforms);
            meshes.AddRange(graph.Meshes);
            materials.AddRange(graph.Materials);
            rootNode = graph.Root;
            if (options.ReadGuids)
            {
                guids.AddRange(graph.Guids);
            }
        }
        else
        {
            if (options.ReadTransforms)
            {
                foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.Transforms))
                {
                    transforms.AddRange(TransformParser.Parse(await s.LoadAsync().ConfigureAwait(false)));
                }
            }

            if (options.ReadMeshes)
            {
                IReadOnlyList<int>? transformIndices = null;
                var linksStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.TransformLinks);
                if (linksStream is not null)
                {
                    transformIndices = TransformLinkParser.TryParse(await linksStream.LoadAsync().ConfigureAwait(false));
                }

                var meshResult = await MeshParser.ParseAsync(container, transforms, options.MaxMeshVertices, transformIndices, cancellationToken).ConfigureAwait(false);
                meshes.AddRange(meshResult.Meshes);
                skippedMeshRecords = meshResult.SkippedRecords;
            }

            if (options.BuildSceneTree)
            {
                rootNode = SceneTreeBuilder.Build(rootName, records, transforms, guids);
                if (linkedWriterCache)
                    WriterSceneLinks.Attach(rootNode, meshes, options.ReadMeshes, diagnostics);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("read-geometry", 1, 1));

        // The name and colour heuristics apply only to files not read through their DbCache directory.
        if (segments is null && options.ReadMaterials)
        {
            materials.AddRange(MaterialParser.Parse(records, meshes, rootNode));
        }

        var dbCache = segments?.Directory;
        var dbCacheStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.DbCache);
        if (dbCache is null && dbCacheStream is not null)
        {
            dbCache = DbCacheParser.Parse(await dbCacheStream.LoadAsync().ConfigureAwait(false));
        }

        var savedViewpoints = new List<NwcSavedViewpoint>();
        if (options.ReadSavedViewpoints)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.SavedViews))
            {
                savedViewpoints.AddRange(ViewpointParser.Parse(await s.LoadAsync().ConfigureAwait(false)));
            }
        }

        var selectionSets = new List<Model.Sets.NwcSetItem>();
        if (options.ReadSelectionSets)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.SelectionSets))
            {
                selectionSets.AddRange(SelectionSetParser.Parse(await s.LoadAsync().ConfigureAwait(false)));
            }
        }

        Model.TimeLiner.NwcTimeLinerSchedule? timeLiner = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (options.ReadTimeLiner)
        {
            var tlStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.TimeLiner);
            if (tlStream is not null)
            {
                timeLiner = TimeLinerParser.Parse(await tlStream.LoadAsync().ConfigureAwait(false));
            }
        }

        if (!keepStreamCatalog)
        {
            container = container with { Streams = [] };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (skippedMeshRecords > 0)
        {
            diagnostics.Add(new NwcDiagnostic(
                NwcDiagnosticSeverity.Warning,
                "meshes-skipped",
                $"{skippedMeshRecords} geometry records were skipped: their vertex count is outside " +
                $"{MeshParser.MinMeshVertices}..{options.MaxMeshVertices} or the record is malformed."));
        }

        if (container.TrailingGap > 0)
        {
            diagnostics.Add(new NwcDiagnostic(
                NwcDiagnosticSeverity.Warning,
                "ContainerTrailingGap",
                $"Unrecognized {container.TrailingGap} byte tail after the last stream.",
                ByteOffset: container.FileLength - container.TrailingGap));
        }

        if (options.Strict)
        {
            var errors = diagnostics.Where(d => d.Severity == NwcDiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join(" ", errors.Select(d => d.Message)));
            }
        }

        progress?.Report(new NwcProgress("open", 1, 1));
        var document = new NwcDocument(fileInfo, records, guids, transforms, meshes, materials, rootNode, xRefs, dbCache, savedViewpoints, selectionSets, timeLiner, container);
        document.Diagnostics = diagnostics;

        return document;
    }

    internal static NwcDocument CreateNative(
        NwcFileInfo? fileInfo = null,
        IEnumerable<NwcMesh>? meshes = null,
        IEnumerable<NwcTransform>? transforms = null,
        IEnumerable<NwcSavedViewpoint>? savedViewpoints = null,
        IEnumerable<Guid>? guids = null,
        IEnumerable<NwcPropertyRecord>? propertyRecords = null,
        IEnumerable<NwcMaterial>? materials = null,
        NwcNode? rootNode = null,
        NwcXRefTable? xRefs = null,
        NwcDbCacheDirectory? dbCache = null,
        IEnumerable<Model.Sets.NwcSetItem>? selectionSets = null,
        Model.TimeLiner.NwcTimeLinerSchedule? timeLiner = null)
    {
        var fi = fileInfo ?? new NwcFileInfo { SourcePath = "Model.rvt", LoaderPlugin = "LcOpRevitExport", SourceTimestampUtc = DateTime.UtcNow };
        if (fi.LengthUnit == NwcLengthUnit.Unknown)
        {
            fi.LengthUnit = InferLengthUnit(fi.LoaderPlugin);
        }
        var mList = meshes?.ToList() ?? [];
        var tList = transforms?.ToList() ?? [];
        var vList = savedViewpoints?.ToList() ?? [];
        var gList = guids?.ToList() ?? [];
        var pList = propertyRecords?.ToList() ?? [];
        var matList = materials?.ToList() ?? [];
        var sList = selectionSets?.ToList() ?? [];

        var container = new NwcContainer
        {
            Sections = [],
            Streams = [],
            FileLength = 0,
            TrailingGap = 0,
        };

        return new NwcDocument(fi, pList, gList, tList, mList, matList, rootNode, xRefs, dbCache, vList, sList, timeLiner, container);
    }

    /// <summary>
    /// Saves the document to a .nwc file in NwcIO's own layout, not verified to open in Autodesk Navisworks. When <see cref="PropertyRecords"/> is non-empty it is the
    /// source of truth for the scene tree; <see cref="RootNode"/> is serialized only when no records exist, so edits to the
    /// tree of an opened document are not persisted unless the records are cleared first.
    /// </summary>
    public void Save(string path, string? header = null)
        => Save(path, header, null, default);

    /// <summary>Saves the document to an .nwc file with cancellation and progress.</summary>
    public void Save(string path, string? header, IProgress<NwcProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-nwc", 0, 1));
        NwcWriter.Write(this, path, header, cancellationToken);
        progress?.Report(new NwcProgress("export-nwc", 1, 1));
    }

    /// <summary>Saves the document to an output stream in NwcIO's .nwc layout.</summary>
    public void Save(Stream stream, string? header = null)
        => Save(stream, header, null, default);

    /// <summary>Saves the document to an .nwc stream with cancellation and progress.</summary>
    public void Save(Stream stream, string? header, IProgress<NwcProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-nwc", 0, 1));
        NwcWriter.Write(this, stream, header, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-nwc", 1, 1));
    }

    /// <summary>
    /// Serializes the document to a byte array in NwcIO's .nwc layout. See <see cref="Save(string, string?)"/>
    /// for the <see cref="PropertyRecords"/> / <see cref="RootNode"/> precedence.
    /// </summary>
    public byte[] ToByteArray(string? header = null)
    {
        return NwcWriter.WriteToBytes(this, header);
    }

    /// <summary>Exports the document to a JSON file.</summary>
    public void SaveJson(string path, NwcJsonExportOptions? options = null)
        => SaveJson(path, options, null, default);

    /// <summary>Exports the document to JSON with cancellation and progress.</summary>
    public void SaveJson(string path, NwcJsonExportOptions? options, IProgress<NwcProgress>? progress, CancellationToken cancellationToken)
    {
        options ??= NwcJsonExportOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 0, 1));
        cancellationToken.ThrowIfCancellationRequested();
        Internal.Json.JsonExporter.WriteFile(this, path, options, cancellationToken);
        progress?.Report(new NwcProgress("export-json", 1, 1));
    }

    /// <summary>Exports the document to a JSON stream.</summary>
    public void SaveJson(Stream stream, NwcJsonExportOptions? options = null)
        => SaveJson(stream, options, null, default);

    /// <summary>Exports the document to a JSON stream with cancellation and progress.</summary>
    public void SaveJson(Stream stream, NwcJsonExportOptions? options, IProgress<NwcProgress>? progress, CancellationToken cancellationToken)
    {
        options ??= NwcJsonExportOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 0, 1));
        cancellationToken.ThrowIfCancellationRequested();
        Internal.Json.JsonExporter.Write(this, stream, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 1, 1));
    }

    /// <summary>Serializes the document to a JSON string.</summary>
    public string ToJson(NwcJsonExportOptions? options = null)
    {
        using var ms = new MemoryStream();
        SaveJson(ms, options);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Opens and parses a file using asynchronous I/O.</summary>
    public static async Task<NwcDocument> OpenAsync(
        string path,
        NwcReaderOptions? options = null,
        IProgress<NwcProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Navisworks file not found: {path}", path);
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            var document = await OpenAsync(stream, options, Path.GetFileNameWithoutExtension(path), progress, cancellationToken).ConfigureAwait(false);
            document._loadedPath = Path.GetFullPath(path);
            return document;
        }
    }

    /// <summary>Opens and parses a stream using asynchronous I/O.</summary>
    public static async Task<NwcDocument> OpenAsync(
        Stream stream,
        NwcReaderOptions? options = null,
        string? fallbackName = null,
        IProgress<NwcProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stream);
        options ??= NwcReaderOptions.Default;

        bool keepStreamCatalog = options.KeepStreamCatalog;
        return await NwcContainerReader.ReadAsync(stream, options.MaxDecompressedStreamBytes, options.MaxTotalDecompressedBytes,
            deferPayloads: !keepStreamCatalog,
            container => ParseContainer(container, options, keepStreamCatalog, fallbackName, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves the document to a file using asynchronous I/O.</summary>
    public Task SaveAsync(string path, string? header = null, CancellationToken cancellationToken = default)
        => NwcWriter.WriteAsync(this, path, header, cancellationToken);

    /// <summary>Saves the document to a stream asynchronously.</summary>
    public Task SaveAsync(Stream stream, string? header = null, CancellationToken cancellationToken = default)
        => NwcWriter.WriteAsync(this, stream, header, cancellationToken);

    /// <summary>Exports the document to a JSON file using asynchronous I/O.</summary>
    public async Task SaveJsonAsync(
        string path,
        NwcJsonExportOptions? options = null,
        IProgress<NwcProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 0, 1));
        await Internal.AtomicFileWriter.WriteAsync(path, output =>
            Internal.Json.JsonExporter.WriteAsync(this, output, options ?? NwcJsonExportOptions.Default, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        progress?.Report(new NwcProgress("export-json", 1, 1));
    }

    /// <summary>Exports the document to a JSON stream using asynchronous I/O.</summary>
    public async Task SaveJsonAsync(
        Stream stream,
        NwcJsonExportOptions? options = null,
        IProgress<NwcProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 0, 1));
        await Internal.Json.JsonExporter.WriteAsync(this, stream, options ?? NwcJsonExportOptions.Default, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new NwcProgress("export-json", 1, 1));
    }

    private static NwcLengthUnit InferLengthUnit(string? loaderPlugin) =>
        loaderPlugin is "LcNwcLoaderPlugin:lcldrevit" or "nwexportrevit"
            ? NwcLengthUnit.Feet
            : NwcLengthUnit.Unknown;
}

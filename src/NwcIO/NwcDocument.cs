using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using NwcIO.Model;

namespace NwcIO;

/// <summary>
/// An opened Navisworks document (.nwc cache, .nwd document) parsed without Navisworks installed.
/// </summary>
public sealed class NwcDocument
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

    /// <summary>Flat records parsed from the scene-tree/property streams.</summary>
    public IReadOnlyList<NwcPropertyRecord> PropertyRecords { get; }

    /// <summary>GUIDs from the GUID table streams, in file order.</summary>
    public IReadOnlyList<Guid> Guids { get; }

    /// <summary>3D spatial transformations and bounding boxes parsed from transform streams.</summary>
    public IReadOnlyList<NwcTransform> Transforms { get; }

    /// <summary>3D polygon meshes extracted from geometry streams and TOC tables.</summary>
    public IReadOnlyList<NwcMesh> Meshes { get; }

    /// <summary>Surface materials, RGBA colors and PBR properties extracted from properties and categories.</summary>
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
    public Model.TimeLiner.NwcTimeLinerSchedule? TimeLiner { get; }

    /// <summary>Navisworks Appearance Profiler color/transparency rules associated with the document.</summary>
    public Model.Appearance.NwcAppearanceProfiler? AppearanceProfiler { get; set; }

    /// <summary>Configured Clash Detective tests and results for this document.</summary>
    public List<Model.Clash.NwcClashTest> ClashTests { get; } = [];

    /// <summary>Spatial grid system (axes and levels) associated with the document.</summary>
    public Model.Grids.NwcGridSystem? GridSystem { get; set; }

    /// <summary>Gets the total 3D bounding box over all meshes in the document.</summary>
    public BoundingBox3d BoundingBox => SpatialIndex.Bounds;

    /// <summary>Gets the primary/default viewpoint of the document.</summary>
    public NwcSavedViewpoint? DefaultViewpoint =>
        SavedViewpoints.Count > 0 ? SavedViewpoints[0] : (!BoundingBox.IsEmpty ? NwcSavedViewpoint.CreateDefault(BoundingBox) : null);

    private Model.Spatial.NwcSpatialIndex? _spatialIndex;

    /// <summary>3D spatial acceleration index (BVH) over meshes for raycasting and spatial selection.</summary>
    public Model.Spatial.NwcSpatialIndex SpatialIndex => _spatialIndex ??= new Model.Spatial.NwcSpatialIndex(Meshes);

    internal NwcContainer Container { get; }

    /// <summary>Opens and parses a Navisworks file from disk.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not an LcUStream container.</exception>
    public static NwcDocument Open(string path, NwcReaderOptions? options = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Navisworks file not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        return Open(stream, options, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Opens and parses a Navisworks file from a stream.</summary>
    /// <exception cref="InvalidDataException">The stream is not an LcUStream container.</exception>
    public static NwcDocument Open(Stream stream, NwcReaderOptions? options = null, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= NwcReaderOptions.Default;

        var container = NwcContainerReader.Read(stream);
        StreamClassifier.ClassifyAll(container.Streams);

        var fileInfo = new NwcFileInfo();
        var headerStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.PartitionHeader);
        if (headerStream is not null)
        {
            fileInfo = PartitionHeaderParser.Parse(headerStream);
        }

        var records = new List<NwcPropertyRecord>();
        if (options.ReadPropertyRecords)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.SceneTree))
            {
                var scanner = new PropertyRecordScanner(s.Data);
                records.AddRange(scanner.Scan());
            }
        }

        var guids = new List<Guid>();
        if (options.ReadGuids)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.Guids))
            {
                if (GuidTableParser.TryParse(s, out var parsed))
                {
                    guids.AddRange(parsed);
                }
            }
        }

        var transforms = new List<NwcTransform>();
        if (options.ReadTransforms)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.Transforms))
            {
                transforms.AddRange(TransformParser.Parse(s));
            }
        }

        var meshes = new List<NwcMesh>();
        if (options.ReadMeshes)
        {
            meshes.AddRange(MeshParser.Parse(container, transforms));
        }

        NwcNode? rootNode = null;
        if (options.BuildSceneTree)
        {
            string rootName = Path.GetFileNameWithoutExtension(fileInfo.SourcePath ?? fallbackName ?? "Model");
            rootNode = SceneTreeBuilder.Build(rootName, records, transforms, guids);
        }

        var materials = new List<NwcMaterial>();
        if (options.ReadMaterials)
        {
            materials.AddRange(MaterialParser.Parse(records, meshes, rootNode));
        }

        NwcXRefTable? xRefs = null;
        var xrefStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.XRefJson);
        if (xrefStream is not null)
        {
            xRefs = XRefTableParser.Parse(xrefStream);
        }

        NwcDbCacheDirectory? dbCache = null;
        var dbCacheStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.DbCache);
        if (dbCacheStream is not null)
        {
            dbCache = DbCacheParser.Parse(dbCacheStream);
        }

        var savedViewpoints = new List<NwcSavedViewpoint>();
        if (options.ReadSavedViewpoints)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.SavedViews))
            {
                savedViewpoints.AddRange(ViewpointParser.Parse(s));
            }
        }

        var selectionSets = new List<Model.Sets.NwcSetItem>();
        if (options.ReadSelectionSets)
        {
            foreach (var s in container.Streams.Where(s => s.Kind == NwcStreamKind.SelectionSets))
            {
                selectionSets.AddRange(SelectionSetParser.Parse(s));
            }
        }

        Model.TimeLiner.NwcTimeLinerSchedule? timeLiner = null;
        if (options.ReadTimeLiner)
        {
            var tlStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.TimeLiner);
            if (tlStream is not null)
            {
                timeLiner = TimeLinerParser.Parse(tlStream);
            }
        }

        if (!options.KeepStreamCatalog)
        {
            container = container with { Streams = [] };
        }

        return new NwcDocument(fileInfo, records, guids, transforms, meshes, materials, rootNode, xRefs, dbCache, savedViewpoints, selectionSets, timeLiner, container);
    }

    /// <summary>
    /// Creates a new <see cref="NwcDocument"/> programmatically from components.
    /// </summary>
    public static NwcDocument Create(
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
        Model.TimeLiner.NwcTimeLinerSchedule? timeLiner = null,
        Model.Appearance.NwcAppearanceProfiler? appearanceProfiler = null)
    {
        var fi = fileInfo ?? new NwcFileInfo { SourcePath = "Model.rvt", LoaderPlugin = "LcOpRevitExport", SourceTimestampUtc = DateTime.UtcNow };
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

        var doc = new NwcDocument(fi, pList, gList, tList, mList, matList, rootNode, xRefs, dbCache, vList, sList, timeLiner, container);
        if (appearanceProfiler is not null)
        {
            doc.ApplyAppearanceProfiler(appearanceProfiler);
        }
        return doc;
    }

    /// <summary>Applies an appearance profiler to colorize the document based on property rules.</summary>
    /// <returns>The total number of scene nodes and meshes whose appearance was overridden.</returns>
    public int ApplyAppearanceProfiler(Model.Appearance.NwcAppearanceProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        AppearanceProfiler = profiler;
        return profiler.Apply(this);
    }

    /// <summary>Exports the document appearance profiler rules to a Navisworks XML format (.xml).</summary>
    public void ExportAppearanceProfilerXml(string path)
    {
        if (AppearanceProfiler is null)
            throw new InvalidOperationException("Document does not contain an Appearance Profiler.");
        Model.Appearance.NwcXmlAppearanceProfilerSerializer.Save(AppearanceProfiler, path);
    }

    /// <summary>Exports the document appearance profiler rules to an XML stream.</summary>
    public void ExportAppearanceProfilerXml(Stream stream)
    {
        if (AppearanceProfiler is null)
            throw new InvalidOperationException("Document does not contain an Appearance Profiler.");
        Model.Appearance.NwcXmlAppearanceProfilerSerializer.Write(AppearanceProfiler, stream);
    }

    /// <summary>Exports the document selection sets and search sets to Navisworks XML format.</summary>
    public void ExportSelectionSetsXml(string path) => Model.Sets.NwcXmlSetSerializer.Save(SelectionSets, path);

    /// <summary>Exports the document selection sets and search sets to an XML stream.</summary>
    public void ExportSelectionSetsXml(Stream stream) => Model.Sets.NwcXmlSetSerializer.Write(SelectionSets, stream);

    /// <summary>Finds a selection set or search set by name in the document sets hierarchy.</summary>
    public Model.Sets.NwcSetItem? FindSet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return FindSetRecursive(SelectionSets, name);
    }

    private static Model.Sets.NwcSetItem? FindSetRecursive(IEnumerable<Model.Sets.NwcSetItem> items, string name)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
            if (item is Model.Sets.NwcSelectionSetFolder folder)
            {
                var found = FindSetRecursive(folder.Children, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>Finds all scene graph nodes matching a search condition.</summary>
    public List<NwcNode> FindNodes(Model.Sets.NwcSearchCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var result = new List<NwcNode>();
        if (RootNode is null) return result;

        void Walk(NwcNode node)
        {
            if (condition.Evaluate(node)) result.Add(node);
            for (int i = 0; i < node.Children.Count; i++) Walk(node.Children[i]);
        }

        Walk(RootNode);
        return result;
    }

    /// <summary>Exports the 4D TimeLiner schedule to CSV format.</summary>
    public void ExportTimeLinerCsv(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerCsvSerializer.Save(TimeLiner, path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to a CSV stream.</summary>
    public void ExportTimeLinerCsv(Stream stream)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerCsvSerializer.Write(TimeLiner, stream);
    }

    /// <summary>
    /// Executes clash detection across the document using the specified or default test configuration.
    /// </summary>
    public Model.Clash.NwcClashTest RunClashDetection(Model.Clash.NwcClashTest? test = null)
    {
        test ??= new Model.Clash.NwcClashTest("Clash Test");
        Model.Clash.NwcClashDetector.Run(this, test);
        if (!ClashTests.Contains(test)) ClashTests.Add(test);
        return test;
    }

    /// <summary>Exports detected clash results to Autodesk Navisworks Clash Report XML format.</summary>
    public void ExportClashReportXml(string path, string? testName = null)
    {
        var tests = string.IsNullOrEmpty(testName)
            ? ClashTests
            : ClashTests.Where(t => string.Equals(t.Name, testName, StringComparison.OrdinalIgnoreCase)).ToList();
        Model.Clash.NwcXmlClashSerializer.Save(path, tests);
    }

    /// <summary>Exports detected clash results to an XML stream.</summary>
    public void ExportClashReportXml(Stream stream, string? testName = null)
    {
        var tests = string.IsNullOrEmpty(testName)
            ? ClashTests
            : ClashTests.Where(t => string.Equals(t.Name, testName, StringComparison.OrdinalIgnoreCase)).ToList();
        Model.Clash.NwcXmlClashSerializer.Write(stream, tests);
    }

    /// <summary>Exports detected clash results to a buildingSMART BCF 2.1 zip archive (.bcfzip).</summary>
    public void ExportClashBcf(string path, string? testName = null)
    {
        var tests = string.IsNullOrEmpty(testName)
            ? ClashTests
            : ClashTests.Where(t => string.Equals(t.Name, testName, StringComparison.OrdinalIgnoreCase));
        var clashes = tests.SelectMany(t => t.Results);
        Model.Clash.NwcClashBcfExporter.Save(path, clashes, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"));
    }

    /// <summary>Exports detected clash results to a BCF 2.1 stream.</summary>
    public void ExportClashBcf(Stream stream, string? testName = null)
    {
        var tests = string.IsNullOrEmpty(testName)
            ? ClashTests
            : ClashTests.Where(t => string.Equals(t.Name, testName, StringComparison.OrdinalIgnoreCase));
        var clashes = tests.SelectMany(t => t.Results);
        Model.Clash.NwcClashBcfExporter.Write(stream, clashes, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"));
    }

    /// <summary>Loads clash detective tests and results from a Navisworks Clash Report XML file.</summary>
    public List<Model.Clash.NwcClashTest> LoadClashReportXml(string path)
    {
        var tests = Model.Clash.NwcXmlClashSerializer.Open(path);
        ClashTests.AddRange(tests);
        return tests;
    }

    /// <summary>Saves the document to an Autodesk Navisworks (.nwc) file.</summary>
    public void Save(string path, string? header = null)
    {
        NwcWriter.Write(this, path, header);
    }

    /// <summary>Saves the document to an output stream in Autodesk Navisworks (.nwc) format.</summary>
    public void Save(Stream stream, string? header = null)
    {
        NwcWriter.Write(this, stream, header);
    }

    /// <summary>Serializes the document to a byte array in Autodesk Navisworks (.nwc) format.</summary>
    public byte[] ToByteArray(string? header = null)
    {
        return NwcWriter.WriteToBytes(this, header);
    }

    /// <summary>Exports 3D mesh geometry to a Wavefront OBJ file.</summary>
    public void SaveObj(string path, bool worldCoordinates = true)
    {
        Internal.Export.ObjExporter.WriteFile(Meshes, path, worldCoordinates, FileInfo.SourcePath);
    }

    /// <summary>Exports 3D mesh geometry to a <see cref="TextWriter"/> in Wavefront OBJ format.</summary>
    public void SaveObj(TextWriter writer, bool worldCoordinates = true)
    {
        Internal.Export.ObjExporter.Write(Meshes, writer, worldCoordinates, FileInfo.SourcePath);
    }

    /// <summary>Exports 3D mesh geometry, materials, and scene hierarchy to a binary glTF 2.0 file (.glb).</summary>
    public void SaveGlb(string path, bool worldCoordinates = true)
    {
        Internal.Export.GlbExporter.WriteFile(Meshes, Materials, path, worldCoordinates, FileInfo.LoaderPlugin, SavedViewpoints);
    }

    /// <summary>Exports 3D mesh geometry, materials, and scene hierarchy to a binary glTF 2.0 stream (.glb).</summary>
    public void SaveGlb(Stream stream, bool worldCoordinates = true)
    {
        Internal.Export.GlbExporter.Write(Meshes, Materials, stream, worldCoordinates, FileInfo.LoaderPlugin, SavedViewpoints);
    }

    /// <summary>Exports 3D mesh geometry, materials, spatial structure, and properties to an IFC STEP physical file (.ifc).</summary>
    public void SaveIfc(string path, NwcIfcExportOptions? options = null)
    {
        Internal.Export.IfcExporter.WriteFile(this, path, options);
    }

    /// <summary>Exports 3D mesh geometry, materials, spatial structure, and properties to a <see cref="TextWriter"/> in IFC format.</summary>
    public void SaveIfc(TextWriter writer, NwcIfcExportOptions? options = null)
    {
        Internal.Export.IfcExporter.Write(this, writer, options);
    }

    /// <summary>Serializes 3D mesh geometry, materials, spatial structure, and properties to an IFC STEP physical string.</summary>
    public string ToIfcString(NwcIfcExportOptions? options = null)
    {
        using var sw = new StringWriter();
        SaveIfc(sw, options);
        return sw.ToString();
    }

    /// <summary>Exports saved viewpoints to a buildingSMART BCF 2.1 zip archive (.bcfzip).</summary>
    public void SaveViewpointsBcf(string path)
    {
        Internal.Export.BcfExporter.WriteViewpointsFile(SavedViewpoints, path, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"));
    }

    /// <summary>Exports saved viewpoints to a buildingSMART BCF 2.1 zip stream.</summary>
    public void SaveViewpointsBcf(Stream stream)
    {
        Internal.Export.BcfExporter.WriteViewpoints(SavedViewpoints, stream, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"));
    }

    /// <summary>Exports saved viewpoints, cameras, clipping planes, and redlines to an Autodesk Navisworks XML viewpoints file.</summary>
    public void SaveViewpointsXml(string path, string units = "m")
    {
        Model.Sectioning.NwcXmlViewpointSerializer.Save(path, SavedViewpoints, units);
    }

    /// <summary>Serializes saved viewpoints, cameras, clipping planes, and redlines to an Autodesk Navisworks XML viewpoints string.</summary>
    public string ToViewpointsXml(string units = "m")
    {
        return Model.Sectioning.NwcXmlViewpointSerializer.Serialize(SavedViewpoints, units);
    }

    /// <summary>Exports the document to a JSON file.</summary>
    public void SaveJson(string path, NwcJsonExportOptions? options = null)
    {
        options ??= NwcJsonExportOptions.Default;
        Internal.Json.JsonExporter.WriteFile(this, path, options);
    }

    /// <summary>Exports the document to a JSON stream.</summary>
    public void SaveJson(Stream stream, NwcJsonExportOptions? options = null)
    {
        options ??= NwcJsonExportOptions.Default;
        Internal.Json.JsonExporter.Write(this, stream, options);
    }

    /// <summary>Serializes the document to a JSON string.</summary>
    public string ToJson(NwcJsonExportOptions? options = null)
    {
        using var ms = new MemoryStream();
        SaveJson(ms, options);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Exports the document to a standalone interactive 3D WebGL HTML viewer (.html) with zero external dependencies.
    /// </summary>
    public void SaveHtmlViewer(string path, Model.Viewer.NwcHtmlViewerOptions? options = null)
    {
        Model.Viewer.NwcHtmlViewerExporter.Save(path, this, options);
    }

    /// <summary>
    /// Compares this document (as baseline) against a revision document, detecting additions, deletions, coordinate shifts, and property changes.
    /// </summary>
    public Model.Diff.NwcDiffReport Compare(NwcDocument revision, Model.Diff.NwcDiffOptions? options = null) =>
        Model.Diff.NwcDiffEngine.Compare(this, revision, options);

    /// <summary>
    /// Populates clash grid location tags across all clash detective tests in this document using the provided or attached grid system.
    /// </summary>
    public int AssignGridLocations(Model.Grids.NwcGridSystem? gridSystem = null)
    {
        var grids = gridSystem ?? GridSystem ?? ExtractGridSystem();
        GridSystem = grids;
        return grids.AssignGridLocations(this);
    }

    /// <summary>
    /// Automatically extracts grid lines from scene hierarchy nodes and metadata.
    /// </summary>
    public Model.Grids.NwcGridSystem ExtractGridSystem() =>
        Model.Grids.NwcGridSystem.ExtractFromDocument(this);
}

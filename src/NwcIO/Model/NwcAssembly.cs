using NwcIO.Internal.Export;
using NwcIO.Internal.Json;
using NwcIO.Model.Spatial;

namespace NwcIO.Model;

/// <summary>
/// A federated multi-model BIM assembly combining multiple Navisworks documents
/// (.nwc, .nwd, or .nwf external references) in a unified coordinate system.
/// </summary>
public sealed class NwcAssembly
{
    private NwcSpatialIndex? _spatialIndex;

    public string Name { get; }
    public IReadOnlyList<NwcDocument> Models { get; }
    public NwcNode RootNode { get; }
    public IReadOnlyList<NwcMesh> AllMeshes { get; }
    public IReadOnlyList<NwcMaterial> AllMaterials { get; }
    public BoundingBox3d BoundingBox { get; }

    /// <summary>Saved viewpoints aggregated from contributing models or generated for the assembly.</summary>
    public IReadOnlyList<NwcSavedViewpoint> SavedViewpoints { get; }

    /// <summary>Gets the primary/default viewpoint of the federated assembly.</summary>
    public NwcSavedViewpoint? DefaultViewpoint =>
        SavedViewpoints.Count > 0 ? SavedViewpoints[0] : (!BoundingBox.IsEmpty ? NwcSavedViewpoint.CreateDefault(BoundingBox, $"{Name} Overview") : null);

    /// <summary>
    /// Gets the spatial acceleration index (BVH) over all meshes across all contributing models.
    /// </summary>
    public NwcSpatialIndex SpatialIndex => _spatialIndex ??= new NwcSpatialIndex(AllMeshes);

    private NwcAssembly(
        string name,
        IReadOnlyList<NwcDocument> models,
        NwcNode rootNode,
        IReadOnlyList<NwcMesh> allMeshes,
        IReadOnlyList<NwcMaterial> allMaterials,
        BoundingBox3d boundingBox,
        IReadOnlyList<NwcSavedViewpoint> savedViewpoints)
    {
        Name = name;
        Models = models;
        RootNode = rootNode;
        AllMeshes = allMeshes;
        AllMaterials = allMaterials;
        BoundingBox = boundingBox;
        SavedViewpoints = savedViewpoints;
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
        var matMap = new Dictionary<string, NwcMaterial>(StringComparer.OrdinalIgnoreCase);

        var rootChildren = new List<NwcNode>();
        var totalBox = BoundingBox3d.Empty;

        for (int m = 0; m < modelList.Count; m++)
        {
            var doc = modelList[m];
            allMeshes.AddRange(doc.Meshes);

            foreach (var mat in doc.Materials)
            {
                if (!matMap.ContainsKey(mat.Name))
                {
                    matMap[mat.Name] = mat;
                }
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
                    Id = m + 1,
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
            Id = 0,
            Name = name,
            Class = "LcOpModelAssembly",
        };
        rootNode.Children.AddRange(rootChildren);

        return new NwcAssembly(name, modelList, rootNode, allMeshes, matMap.Values.ToList(), totalBox, allViewpoints);
    }

    /// <summary>
    /// Discovers and loads all .nwc models in a folder into a single federated assembly.
    /// </summary>
    public static NwcAssembly LoadFromDirectory(
        string directoryPath,
        string searchPattern = "*.nwc",
        NwcReaderOptions? options = null)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var files = Directory.GetFiles(directoryPath, searchPattern);
        if (files.Length == 0)
        {
            throw new FileNotFoundException($"No files matching '{searchPattern}' found in {directoryPath}");
        }

        var docs = new List<NwcDocument>(files.Length);
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            docs.Add(NwcDocument.Open(file, options));
        }

        string assemblyName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Create(assemblyName, docs);
    }

    /// <summary>
    /// Opens a Navisworks File (.nwf) assembly, automatically discovering and loading its external references.
    /// </summary>
    public static NwcAssembly OpenNwf(string nwfPath, NwcReaderOptions? options = null)
    {
        if (!File.Exists(nwfPath))
        {
            throw new FileNotFoundException($"NWF file not found: {nwfPath}", nwfPath);
        }

        var primaryDoc = NwcDocument.Open(nwfPath, options);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(nwfPath)) ?? ".";

        var loadedDocs = new List<NwcDocument> { primaryDoc };

        if (primaryDoc.XRefs is not null && primaryDoc.XRefs.References.Count > 0)
        {
            foreach (var xref in primaryDoc.XRefs.References)
            {
                string candidate = xref.EffectivePath;
                if (!File.Exists(candidate))
                {
                    candidate = Path.Combine(baseDir, Path.GetFileName(candidate));
                }

                if (File.Exists(candidate) &&
                    (candidate.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) ||
                     candidate.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase)))
                {
                    loadedDocs.Add(NwcDocument.Open(candidate, options));
                }
            }
        }

        string assemblyName = Path.GetFileNameWithoutExtension(nwfPath);
        return Create(assemblyName, loadedDocs);
    }

    /// <summary>
    /// Applies an appearance profiler across all contributing models in the federated assembly.
    /// </summary>
    /// <returns>The total number of scene nodes and meshes whose appearance was overridden.</returns>
    public int ApplyAppearanceProfiler(Appearance.NwcAppearanceProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        int total = 0;
        for (int i = 0; i < Models.Count; i++)
        {
            total += Models[i].ApplyAppearanceProfiler(profiler);
        }
        return total;
    }

    /// <summary>
    /// Detects spatial geometric clashes (bounding box overlaps) between meshes belonging to different models.
    /// </summary>
    public IReadOnlyList<NwcClash> FindClashes(double tolerance = 0.0)
    {
        if (Models.Count < 2)
        {
            return Array.Empty<NwcClash>();
        }

        var clashes = new List<NwcClash>();

        // Build per-model spatial indexes
        var modelIndexes = new (NwcDocument Doc, NwcSpatialIndex Index)[Models.Count];
        for (int i = 0; i < Models.Count; i++)
        {
            modelIndexes[i] = (Models[i], Models[i].SpatialIndex);
        }

        // Cross-compare each pair of models (i < j)
        for (int i = 0; i < Models.Count; i++)
        {
            var (docA, _) = modelIndexes[i];

            for (int j = i + 1; j < Models.Count; j++)
            {
                var (docB, indexB) = modelIndexes[j];

                for (int m = 0; m < docA.Meshes.Count; m++)
                {
                    var meshA = docA.Meshes[m];
                    var boxA = meshA.GetWorldBoundingBox();
                    if (boxA.IsEmpty) continue;

                    var expanded = tolerance > 0
                        ? new BoundingBox3d(
                            boxA.Min - new Vector3d(tolerance, tolerance, tolerance),
                            boxA.Max + new Vector3d(tolerance, tolerance, tolerance))
                        : boxA;

                    var candidatesB = indexB.QueryBox(expanded);
                    for (int n = 0; n < candidatesB.Count; n++)
                    {
                        var meshB = candidatesB[n];
                        var boxB = meshB.GetWorldBoundingBox();
                        if (boxA.Intersects(boxB))
                        {
                            var overlap = new BoundingBox3d(
                                new Vector3d(
                                    Math.Max(boxA.Min.X, boxB.Min.X),
                                    Math.Max(boxA.Min.Y, boxB.Min.Y),
                                    Math.Max(boxA.Min.Z, boxB.Min.Z)),
                                new Vector3d(
                                    Math.Min(boxA.Max.X, boxB.Max.X),
                                    Math.Min(boxA.Max.Y, boxB.Max.Y),
                                    Math.Min(boxA.Max.Z, boxB.Max.Z)));

                            clashes.Add(new NwcClash(docA, meshA, docB, meshB, overlap));
                        }
                    }
                }
            }
        }

        return clashes;
    }

    /// <summary>Configured Clash Detective tests and results for this federated assembly.</summary>
    public List<Clash.NwcClashTest> ClashTests { get; } = [];

    /// <summary>
    /// Executes full mesh-level triangle collision detection across all contributing models of the assembly.
    /// </summary>
    public Clash.NwcClashTest RunClashDetection(string testName = "Federated Clash Test", double tolerance = 0.001)
    {
        var test = new Clash.NwcClashTest(testName, Clash.NwcClashTestType.Hard, tolerance);
        Clash.NwcClashDetector.Run(this, test);
        ClashTests.Add(test);
        return test;
    }

    /// <summary>Exports detected clash results to Autodesk Navisworks Clash Report XML format.</summary>
    public void ExportClashReportXml(string path)
    {
        Clash.NwcXmlClashSerializer.Save(path, ClashTests, Name);
    }

    /// <summary>Exports detected clash results to a buildingSMART BCF 2.1 zip archive (.bcfzip).</summary>
    public void ExportClashBcf(string path)
    {
        Clash.NwcClashBcfExporter.Save(path, ClashTests.SelectMany(t => t.Results), Name);
    }

    /// <summary>Creates a Navisworks Federated Project Manifest (NWF) representation of this assembly.</summary>
    public Clash.NwcNwfManifest ToNwfManifest()
    {
        var manifest = new Clash.NwcNwfManifest(Name);
        for (int i = 0; i < Models.Count; i++)
        {
            var p = Models[i].FileInfo.SourcePath;
            if (!string.IsNullOrEmpty(p))
            {
                manifest.ModelPaths.Add(p);
            }
        }
        manifest.SavedViewpoints.AddRange(SavedViewpoints);
        manifest.ClashTests.AddRange(ClashTests);
        return manifest;
    }

    /// <summary>Saves the assembly coordination manifest as a Navisworks NWF coordination file (.nwf.xml).</summary>
    public void SaveNwf(string path)
    {
        ToNwfManifest().Save(path);
    }

    /// <summary>Exports the entire federated assembly to a binary glTF 2.0 file (.glb).</summary>
    public void SaveGlb(string path, bool worldCoordinates = true)
    {
        GlbExporter.WriteFile(AllMeshes, AllMaterials, path, worldCoordinates, "NwcIO Assembly", SavedViewpoints);
    }

    /// <summary>Exports the entire federated assembly to a Wavefront OBJ file.</summary>
    public void SaveObj(string path, bool worldCoordinates = true)
    {
        ObjExporter.WriteFile(AllMeshes, path, worldCoordinates, Name);
    }

    /// <summary>Exports the entire federated assembly with geometry, materials, and spatial hierarchy to an IFC2x3 STEP file (.ifc).</summary>
    public void SaveIfc(string path, NwcIfcExportOptions? options = null)
    {
        IfcExporter.WriteAssembly(this, path, options);
    }

    /// <summary>Exports the entire federated assembly with geometry, materials, and spatial hierarchy to a <see cref="TextWriter"/> in IFC2x3 format.</summary>
    public void SaveIfc(TextWriter writer, NwcIfcExportOptions? options = null)
    {
        IfcExporter.WriteAssembly(this, writer, options);
    }

    /// <summary>
    /// Exports detected clashes across models in the assembly to a standard buildingSMART BCF 2.1 zip archive (.bcfzip).
    /// </summary>
    /// <param name="path">Destination .bcf or .bcfzip file path.</param>
    /// <param name="clashes">Optional pre-computed clash list; if null, <see cref="FindClashes"/> is evaluated.</param>
    public void SaveBcf(string path, IReadOnlyList<NwcClash>? clashes = null)
    {
        clashes ??= FindClashes();
        BcfExporter.WriteFile(clashes, path, Name);
    }

    /// <summary>
    /// Exports detected clashes across models in the assembly to a standard buildingSMART BCF 2.1 zip stream.
    /// </summary>
    public void SaveBcf(Stream stream, IReadOnlyList<NwcClash>? clashes = null)
    {
        clashes ??= FindClashes();
        BcfExporter.Write(clashes, stream, Name);
    }

    /// <summary>
    /// Exports saved viewpoints of the assembly to a buildingSMART BCF 2.1 zip archive (.bcfzip).
    /// </summary>
    /// <param name="path">Destination .bcf or .bcfzip file path.</param>
    /// <param name="viewpoints">Optional pre-computed viewpoints list; if null, <see cref="SavedViewpoints"/> is used.</param>
    public void SaveViewpointsBcf(string path, IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        viewpoints ??= SavedViewpoints;
        BcfExporter.WriteViewpointsFile(viewpoints, path, Name);
    }

    /// <summary>
    /// Exports saved viewpoints of the assembly to a buildingSMART BCF 2.1 zip stream.
    /// </summary>
    public void SaveViewpointsBcf(Stream stream, IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        viewpoints ??= SavedViewpoints;
        BcfExporter.WriteViewpoints(viewpoints, stream, Name);
    }

    /// <summary>
    /// Exports an interactive, self-contained HTML clash detection report with a discipline clash matrix and filterable clash table.
    /// </summary>
    /// <param name="path">Destination .html file path.</param>
    /// <param name="clashes">Optional pre-computed clash list; if null, <see cref="FindClashes"/> is evaluated.</param>
    public void SaveClashReportHtml(string path, IReadOnlyList<NwcClash>? clashes = null)
    {
        clashes ??= FindClashes();
        HtmlClashReportExporter.WriteFile(clashes, path, Name, Models.Count, AllMeshes.Count);
    }

    /// <summary>
    /// Exports an interactive, self-contained HTML clash detection report to a <see cref="TextWriter"/>.
    /// </summary>
    public void SaveClashReportHtml(TextWriter writer, IReadOnlyList<NwcClash>? clashes = null)
    {
        clashes ??= FindClashes();
        HtmlClashReportExporter.Write(clashes, writer, Name, Models.Count, AllMeshes.Count);
    }

    /// <summary>Exports a structured summary of the federated assembly to a JSON file.</summary>
    public void SaveJson(string path, NwcJsonExportOptions? options = null)
    {
        options ??= NwcJsonExportOptions.Default;
        using var fs = File.Create(path);
        using var writer = new System.Text.Json.Utf8JsonWriter(fs, new System.Text.Json.JsonWriterOptions
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
                writer.WriteString("id", vp.Id.ToString("D"));
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
}

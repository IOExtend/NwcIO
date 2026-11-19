using System.Text;
using NwcIO.Internal.Export;
using NwcIO.Model.Spatial;

namespace NwcIO.Model;

public sealed partial class NwcAssembly
{
    private NwcSpatialIndex? _spatialIndex;

    /// <summary>
    /// Gets the spatial acceleration index (BVH) over all meshes across all contributing models.
    /// </summary>
    public NwcSpatialIndex SpatialIndex => _spatialIndex ??= new NwcSpatialIndex(AllMeshes);

    /// <summary>Multi-discipline clash coordination matrix associated with the assembly.</summary>
    public Clash.NwcClashMatrix? ClashMatrix { get; set; }

    /// <summary>
    /// Opens a federation manifest (.nwf.xml) and loads the .nwc/.nwd models it lists.
    /// Relative paths are resolved under the manifest folder; an absolute path is tried as written first
    /// and then by its trailing segments under the manifest folder. Backslashes in stored paths are read as
    /// directory separators on every platform. Only the model list is read: viewpoints, sets, appearance
    /// rules and clash tests of the manifest are not loaded.
    /// Missing and unsupported entries are recorded on <see cref="UnresolvedXRefs"/>
    /// and <see cref="SkippedXRefs"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The manifest is missing, or none of its models could be resolved.</exception>
    public static NwcAssembly OpenManifest(string manifestPath, NwcReaderOptions? options = null)
        => OpenManifest(manifestPath, options, null, default);

    /// <summary>
    /// Opens a federation manifest with cancellation and progress for each listed model.
    /// </summary>
    public static NwcAssembly OpenManifest(
        string manifestPath,
        NwcReaderOptions? options,
        IProgress<NwcProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Manifest file not found: {manifestPath}", manifestPath);
        }

        var manifest = Clash.NwcNwfManifest.Open(manifestPath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? ".";
        var loadedDocs = new List<NwcDocument>();
        var loadedPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        var unresolved = new List<NwcXRef>();
        var skipped = new List<NwcXRef>();

        int processed = 0;
        foreach (var modelPath in manifest.ModelPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new NwcProgress("load-models", processed++, manifest.ModelPaths.Count));
            var xref = new NwcXRef(modelPath.Replace('\\', Path.DirectorySeparatorChar), "");
            if (!xref.TryResolve(baseDir, out string resolved))
            {
                unresolved.Add(xref);
                continue;
            }

            if (!resolved.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) &&
                !resolved.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(xref);
                continue;
            }

            if (loadedPaths.Add(Path.GetFullPath(resolved)))
            {
                loadedDocs.Add(NwcDocument.Open(resolved, options, null, cancellationToken));
            }
        }

        if (loadedDocs.Count == 0)
        {
            throw new FileNotFoundException(
                $"No .nwc or .nwd models listed in manifest '{manifestPath}' could be resolved.");
        }

        progress?.Report(new NwcProgress("load-models", manifest.ModelPaths.Count, manifest.ModelPaths.Count));
        string fileName = Path.GetFileNameWithoutExtension(manifestPath);
        string assemblyName = fileName.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
        var assembly = Create(assemblyName, loadedDocs);
        assembly.UnresolvedXRefs = unresolved;
        assembly.SkippedXRefs = skipped;
        return assembly;
    }

    /// <summary>Creates a Navisworks Federated Project Manifest (NWF) representation of this assembly.</summary>
    public Clash.NwcNwfManifest ToNwfManifest()
    {
        var manifest = new Clash.NwcNwfManifest(Name);
        for (int i = 0; i < Models.Count; i++)
        {
            var p = Models[i].GetLoadedPath();
            if (string.IsNullOrEmpty(p))
            {
                p = Models[i].FileInfo.SourcePath;
            }
            if (!string.IsNullOrEmpty(p))
            {
                manifest.ModelPaths.Add(p);
            }
        }
        manifest.SavedViewpoints.AddRange(SavedViewpoints);
        manifest.Sets.AddRange(SelectionSets);
        foreach (var model in Models)
        {
            if (model.AppearanceProfiler is not null)
            {
                manifest.AppearanceProfiler.Rules.AddRange(model.AppearanceProfiler.Rules);
            }
        }
        manifest.ClashTests.AddRange(ClashTests);
        return manifest;
    }

    /// <summary>Saves the assembly coordination manifest as a Navisworks NWF coordination file (.nwf.xml).</summary>
    public void SaveNwf(string path)
    {
        var manifest = ToNwfManifest();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        for (var i = 0; i < manifest.ModelPaths.Count; i++)
        {
            manifest.ModelPaths[i] = Path.GetRelativePath(directory, manifest.ModelPaths[i]);
        }
        manifest.Save(path);
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
    /// Detects spatial clashes between meshes belonging to different models. Meshes whose world bounding boxes
    /// come within <paramref name="tolerance"/> of each other are reported; the overlap box is empty when they
    /// only come close.
    /// </summary>
    public IReadOnlyList<NwcClash> FindClashes(double tolerance = 0.0)
    {
        if (Models.Count < 2)
        {
            return Array.Empty<NwcClash>();
        }

        var clashes = new List<NwcClash>();
        var modelKeys = NwcElementGuid.ModelKeys(Models);

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
                        if (expanded.Intersects(boxB))
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
                            if (overlap.IsEmpty)
                            {
                                // Near miss: point at the middle of the gap (or of the overlap on axes that overlap).
                                var point = new Vector3d(
                                    (Math.Max(boxA.Min.X, boxB.Min.X) + Math.Min(boxA.Max.X, boxB.Max.X)) * 0.5,
                                    (Math.Max(boxA.Min.Y, boxB.Min.Y) + Math.Min(boxA.Max.Y, boxB.Max.Y)) * 0.5,
                                    (Math.Max(boxA.Min.Z, boxB.Min.Z) + Math.Min(boxA.Max.Z, boxB.Max.Z)) * 0.5);
                                overlap = new BoundingBox3d(point, point);
                            }

                            clashes.Add(new NwcClash(docA, meshA, docB, meshB, overlap)
                            {
                                ModelKeyA = modelKeys[i],
                                ModelKeyB = modelKeys[j],
                            });
                        }
                    }
                }
            }
        }

        return clashes;
    }

    /// <summary>Configured Clash Detective tests and results for this federated assembly.</summary>
    public List<Clash.NwcClashTest> ClashTests { get; } = [];

    /// <summary>Spatial grid system (axes and levels) associated with the federated assembly.</summary>
    public Grids.NwcGridSystem? GridSystem { get; set; }

    /// <summary>
    /// Executes full mesh-level triangle collision detection across all contributing models of the assembly.
    /// </summary>
    public Clash.NwcClashTest RunClashDetection(string testName = "Federated Clash Test", double tolerance = 0.001) =>
        RunClashDetection(testName, tolerance, null, default);

    /// <summary>Runs a hard clash test across all models with cancellation and progress.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was cancelled.</exception>
    public Clash.NwcClashTest RunClashDetection(
        string testName, double tolerance, IProgress<NwcProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var test = new Clash.NwcClashTest(testName, Clash.NwcClashTestType.Hard, tolerance);
        Clash.NwcClashDetector.Run(this, test, progress, cancellationToken);
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
        Clash.NwcClashBcfExporter.SaveTests(path, ClashTests, Name, CommonLengthUnit);
    }

    /// <summary>Exports the 4D TimeLiner schedule to Navisworks XML exchange format (.xml).</summary>
    public void ExportTimeLinerXml(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Assembly does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerXmlSerializer.Save(TimeLiner, path);
    }

    /// <summary>Loads or imports a 4D TimeLiner schedule from Navisworks XML exchange format (.xml).</summary>
    public void ImportTimeLinerXml(string path)
    {
        TimeLiner = Model.TimeLiner.NwcTimeLinerXmlSerializer.Open(path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to Microsoft Project XML format (.xml).</summary>
    public void ExportTimeLinerMsProject(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Assembly does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerMsProjectXmlSerializer.Save(TimeLiner, path);
    }

    /// <summary>Loads or imports a 4D TimeLiner schedule from Microsoft Project XML format (.xml).</summary>
    public void ImportTimeLinerMsProject(string path)
    {
        TimeLiner = Model.TimeLiner.NwcTimeLinerMsProjectXmlSerializer.Open(path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to Primavera P6 XER format (.xer).</summary>
    public void ExportTimeLinerPrimavera(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Assembly does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerPrimaveraXerSerializer.Save(TimeLiner, path);
    }

    /// <summary>Loads or imports a 4D TimeLiner schedule from Primavera P6 XER format (.xer).</summary>
    public void ImportTimeLinerPrimavera(string path)
    {
        TimeLiner = Model.TimeLiner.NwcTimeLinerPrimaveraXerSerializer.Open(path);
    }

    /// <summary>Exports the coordination clash matrix to JSON format.</summary>
    public void ExportClashMatrixJson(string path, bool indent = true)
    {
        if (ClashMatrix is null) throw new InvalidOperationException("Assembly does not have a configured Clash Matrix.");
        ClashMatrix.SaveJson(path, indent);
    }

    /// <summary>Exports the coordination clash matrix summary to a Markdown table.</summary>
    public void ExportClashMatrixMarkdown(string path)
    {
        if (ClashMatrix is null) throw new InvalidOperationException("Assembly does not have a configured Clash Matrix.");
        File.WriteAllText(path, ClashMatrix.ToMarkdownTable(), Encoding.UTF8);
    }

    /// <summary>Exports the coordination clash matrix conflicts to a buildingSMART BCF 2.1 zip archive.</summary>
    public void ExportClashMatrixBcf(string path)
    {
        if (ClashMatrix is null) throw new InvalidOperationException("Assembly does not have a configured Clash Matrix.");
        ClashMatrix.SaveBcf(path);
    }

    /// <summary>Exports the entire federated assembly to a binary glTF 2.0 file (.glb).</summary>
    public void SaveGlb(string path, bool worldCoordinates = true)
    {
        GlbExporter.WriteFile(AllMeshes, AllMaterials, path, worldCoordinates, "NwcIO Assembly", SavedViewpoints);
    }

    /// <summary>Exports the entire federated assembly to a Wavefront OBJ file.</summary>
    public void SaveObj(string path, bool worldCoordinates = true)
    {
        ObjExporter.WriteFile(AllMeshes, path, worldCoordinates, Name, GetObjModelLabel);
    }

    private string? GetObjModelLabel(NwcMesh mesh)
    {
        int modelIndex = GetModelIndex(mesh);
        if (modelIndex < 0)
        {
            return null;
        }

        var model = Models[modelIndex];
        string modelName = Path.GetFileNameWithoutExtension(model.FileInfo.SourcePath ?? string.Empty);
        if (string.IsNullOrEmpty(modelName))
        {
            modelName = $"Model_{modelIndex + 1}";
        }
        return $"M{modelIndex + 1}_{modelName}";
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
        BcfExporter.WriteViewpointsFile(viewpoints, path, Name, CommonLengthUnit.GetMetresPerUnit() ?? 1.0);
    }

    /// <summary>
    /// Exports saved viewpoints of the assembly to a buildingSMART BCF 2.1 zip stream.
    /// </summary>
    public void SaveViewpointsBcf(Stream stream, IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        viewpoints ??= SavedViewpoints;
        BcfExporter.WriteViewpoints(viewpoints, stream, Name, CommonLengthUnit.GetMetresPerUnit() ?? 1.0);
    }

    /// <summary>
    /// Exports saved viewpoints of the assembly to an Autodesk Navisworks XML viewpoints file.
    /// </summary>
    public void SaveViewpointsXml(string path, IReadOnlyList<NwcSavedViewpoint>? viewpoints = null, string? units = null)
    {
        viewpoints ??= SavedViewpoints;
        Sectioning.NwcXmlViewpointSerializer.Save(path, viewpoints, units ?? CommonLengthUnit.GetSymbol() ?? "m");
    }

    /// <summary>
    /// Serializes saved viewpoints of the assembly to an Autodesk Navisworks XML viewpoints string.
    /// </summary>
    public string ToViewpointsXml(IReadOnlyList<NwcSavedViewpoint>? viewpoints = null, string? units = null)
    {
        viewpoints ??= SavedViewpoints;
        return Sectioning.NwcXmlViewpointSerializer.Serialize(viewpoints, units ?? CommonLengthUnit.GetSymbol() ?? "m");
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

    /// <summary>
    /// Exports the federated assembly to a standalone interactive 3D WebGL HTML viewer (.html) with zero external dependencies.
    /// </summary>
    public void SaveHtmlViewer(string path, Viewer.NwcHtmlViewerOptions? options = null)
    {
        Viewer.NwcHtmlViewerExporter.Save(path, this, options);
    }

    /// <summary>
    /// Compares this federated assembly (as baseline) against a revision assembly, detecting additions, deletions, coordinate shifts, and property changes.
    /// </summary>
    public Diff.NwcDiffReport Compare(NwcAssembly revision, Diff.NwcDiffOptions? options = null) =>
        Diff.NwcDiffEngine.Compare(this, revision, options);

    /// <summary>
    /// Populates clash grid location tags across all clash tests in this assembly and its contributing models using the provided or attached grid system.
    /// </summary>
    public int AssignGridLocations(Grids.NwcGridSystem? gridSystem = null)
    {
        var grids = gridSystem ?? GridSystem;
        if (grids is null)
        {
            foreach (var m in Models)
            {
                var extracted = m.ExtractGridSystem();
                if (extracted.Lines.Count > 0)
                {
                    grids = extracted;
                    break;
                }
            }
        }
        grids ??= new Grids.NwcGridSystem("Assembly Grids");
        GridSystem = grids;

        var lengthUnit = grids.LengthUnit == NwcLengthUnit.Unknown ? CommonLengthUnit : grids.LengthUnit;
        int count = 0;
        foreach (var test in ClashTests)
        {
            count += grids.AssignGridLocations(test, lengthUnit);
        }
        foreach (var m in Models)
        {
            count += grids.AssignGridLocations(m);
        }
        return count;
    }
}

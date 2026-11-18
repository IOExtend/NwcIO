using NwcIO.Model;

namespace NwcIO;

public sealed partial class NwcDocument
{
    private Model.Spatial.NwcSpatialIndex? _spatialIndex;

    /// <summary>3D spatial acceleration index (BVH) over meshes for raycasting and spatial selection.</summary>
    public Model.Spatial.NwcSpatialIndex SpatialIndex => _spatialIndex ??= new Model.Spatial.NwcSpatialIndex(Meshes);

    // Use the same snapshot as SpatialIndex when both APIs are available.
    partial void GetIndexedBounds(ref BoundingBox3d? bounds) => bounds = SpatialIndex.Bounds;

    /// <summary>Configured Clash Detective tests and results for this document.</summary>
    public List<Model.Clash.NwcClashTest> ClashTests { get; } = [];

    /// <summary>Spatial grid system (axes and levels) associated with the document.</summary>
    public Model.Grids.NwcGridSystem? GridSystem { get; set; }

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

    /// <summary>Exports the 4D TimeLiner schedule to Navisworks XML exchange format (.xml).</summary>
    public void ExportTimeLinerXml(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerXmlSerializer.Save(TimeLiner, path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to a Navisworks XML exchange stream.</summary>
    public void ExportTimeLinerXml(Stream stream)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerXmlSerializer.Write(TimeLiner, stream);
    }

    /// <summary>Loads or imports a 4D TimeLiner schedule from Navisworks XML exchange format (.xml).</summary>
    public void ImportTimeLinerXml(string path)
    {
        TimeLiner = Model.TimeLiner.NwcTimeLinerXmlSerializer.Open(path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to Microsoft Project XML format (.xml).</summary>
    public void ExportTimeLinerMsProject(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerMsProjectXmlSerializer.Save(TimeLiner, path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to a Microsoft Project XML stream.</summary>
    public void ExportTimeLinerMsProject(Stream stream)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerMsProjectXmlSerializer.Write(TimeLiner, stream);
    }

    /// <summary>Loads or imports a 4D TimeLiner schedule from Microsoft Project XML format (.xml).</summary>
    public void ImportTimeLinerMsProject(string path)
    {
        TimeLiner = Model.TimeLiner.NwcTimeLinerMsProjectXmlSerializer.Open(path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to Primavera P6 XER format (.xer).</summary>
    public void ExportTimeLinerPrimavera(string path)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerPrimaveraXerSerializer.Save(TimeLiner, path);
    }

    /// <summary>Exports the 4D TimeLiner schedule to a Primavera P6 XER stream.</summary>
    public void ExportTimeLinerPrimavera(Stream stream)
    {
        if (TimeLiner is null) throw new InvalidOperationException("Document does not contain a TimeLiner schedule.");
        Model.TimeLiner.NwcTimeLinerPrimaveraXerSerializer.Write(TimeLiner, stream);
    }

    /// <summary>Loads or imports a 4D TimeLiner schedule from Primavera P6 XER format (.xer).</summary>
    public void ImportTimeLinerPrimavera(string path)
    {
        TimeLiner = Model.TimeLiner.NwcTimeLinerPrimaveraXerSerializer.Open(path);
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
        Model.Clash.NwcClashBcfExporter.SaveTests(path, tests, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"));
    }

    /// <summary>Exports detected clash results to a BCF 2.1 stream.</summary>
    public void ExportClashBcf(Stream stream, string? testName = null)
    {
        var tests = string.IsNullOrEmpty(testName)
            ? ClashTests
            : ClashTests.Where(t => string.Equals(t.Name, testName, StringComparison.OrdinalIgnoreCase));
        Model.Clash.NwcClashBcfExporter.WriteTests(stream, tests, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"));
    }

    /// <summary>Loads clash detective tests and results from a Navisworks Clash Report XML file.</summary>
    public List<Model.Clash.NwcClashTest> LoadClashReportXml(string path)
    {
        var tests = Model.Clash.NwcXmlClashSerializer.Open(path);
        ClashTests.AddRange(tests);
        return tests;
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

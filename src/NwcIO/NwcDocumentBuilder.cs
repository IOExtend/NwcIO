using NwcIO.Model;

namespace NwcIO;

/// <summary>
/// A fluent builder for programmatically creating new <see cref="NwcDocument"/> instances.
/// </summary>
public sealed class NwcDocumentBuilder
{
    private readonly NwcFileInfo _fileInfo = new();
    private readonly List<NwcMesh> _meshes = [];
    private readonly List<NwcTransform> _transforms = [];
    private readonly List<NwcSavedViewpoint> _viewpoints = [];
    private readonly List<Guid> _guids = [];
    private readonly List<NwcPropertyRecord> _propertyRecords = [];
    private readonly List<NwcMaterial> _materials = [];
    private readonly List<Model.Sets.NwcSetItem> _selectionSets = [];
    private readonly List<Model.Clash.NwcClashTest> _clashTests = [];
    private Model.TimeLiner.NwcTimeLinerSchedule? _timeLiner;
    private Model.Appearance.NwcAppearanceProfiler? _appearanceProfiler;
    private NwcNode? _rootNode;

    /// <summary>
    /// Sets the source path, loader plugin, and options for the document.
    /// </summary>
    public NwcDocumentBuilder SetFileInfo(string sourcePath, string? loaderPlugin = "LcOpRevitExport", DateTime? timestampUtc = null)
    {
        _fileInfo.SourcePath = sourcePath;
        _fileInfo.LoaderPlugin = loaderPlugin;
        _fileInfo.SourceTimestampUtc = timestampUtc ?? DateTime.UtcNow;
        return this;
    }

    /// <summary>
    /// Adds a 3D polygon mesh to the document.
    /// </summary>
    public NwcDocumentBuilder AddMesh(NwcMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _meshes.Add(mesh);
        return this;
    }

    /// <summary>
    /// Adds a spatial transform to the document.
    /// </summary>
    public NwcDocumentBuilder AddTransform(NwcTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        _transforms.Add(transform);
        return this;
    }

    /// <summary>
    /// Adds a saved viewpoint and camera to the document.
    /// </summary>
    public NwcDocumentBuilder AddSavedViewpoint(NwcSavedViewpoint viewpoint)
    {
        ArgumentNullException.ThrowIfNull(viewpoint);
        _viewpoints.Add(viewpoint);
        return this;
    }

    /// <summary>
    /// Adds an element GUID to the document.
    /// </summary>
    public NwcDocumentBuilder AddGuid(Guid id)
    {
        _guids.Add(id);
        return this;
    }

    /// <summary>
    /// Sets the root scene hierarchy node.
    /// </summary>
    public NwcDocumentBuilder SetRootNode(NwcNode rootNode)
    {
        _rootNode = rootNode;
        return this;
    }

    /// <summary>
    /// Adds a surface material to the document.
    /// </summary>
    public NwcDocumentBuilder AddMaterial(NwcMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        _materials.Add(material);
        return this;
    }

    /// <summary>
    /// Adds a property record to the document.
    /// </summary>
    public NwcDocumentBuilder AddPropertyRecord(NwcPropertyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _propertyRecords.Add(record);
        return this;
    }

    /// <summary>
    /// Adds a selection set, search set, or set folder to the document.
    /// </summary>
    public NwcDocumentBuilder AddSelectionSet(Model.Sets.NwcSetItem setItem)
    {
        ArgumentNullException.ThrowIfNull(setItem);
        _selectionSets.Add(setItem);
        return this;
    }

    /// <summary>
    /// Sets the 4D TimeLiner construction schedule for the document.
    /// </summary>
    public NwcDocumentBuilder SetTimeLiner(Model.TimeLiner.NwcTimeLinerSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _timeLiner = schedule;
        return this;
    }

    /// <summary>
    /// Sets the Navisworks Appearance Profiler for colorizing the model based on rules.
    /// </summary>
    public NwcDocumentBuilder SetAppearanceProfiler(Model.Appearance.NwcAppearanceProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        _appearanceProfiler = profiler;
        return this;
    }

    /// <summary>
    /// Adds an appearance profiler rule to colorize matching scene graph elements.
    /// </summary>
    public NwcDocumentBuilder AddAppearanceRule(Model.Appearance.NwcAppearanceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _appearanceProfiler ??= new Model.Appearance.NwcAppearanceProfiler();
        _appearanceProfiler.Rules.Add(rule);
        return this;
    }

    /// <summary>
    /// Adds a Clash Detective test to the document.
    /// </summary>
    public NwcDocumentBuilder AddClashTest(Model.Clash.NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        _clashTests.Add(test);
        return this;
    }

    /// <summary>
    /// Builds and returns the constructed <see cref="NwcDocument"/>.
    /// </summary>
    public NwcDocument Build()
    {
        var doc = NwcDocument.Create(
            fileInfo: _fileInfo,
            meshes: _meshes,
            transforms: _transforms,
            savedViewpoints: _viewpoints,
            guids: _guids,
            propertyRecords: _propertyRecords,
            materials: _materials,
            rootNode: _rootNode,
            selectionSets: _selectionSets,
            timeLiner: _timeLiner,
            appearanceProfiler: _appearanceProfiler);
        doc.ClashTests.AddRange(_clashTests);
        return doc;
    }
}

using System.Globalization;
using System.Reflection;
using IOScene;
using NwcIO.Model;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;
using BoundingBox3d = IOScene.BoundingBox3d;

namespace NwcIO.Scene;

/// <summary>Converts a Navisworks model file, or an <see cref="NwcDocument"/> read by NwcIO, into a <see cref="SceneDocument"/>.</summary>
/// <remarks>
/// The scene has one <see cref="SceneModel"/>: <c>Key</c> and <c>Name</c> are <see cref="NwcSceneOptions.ModelKey"/>, else the file
/// name; <c>SourceFormat</c> is <c>nwc</c>; <c>Application</c> is the loader plugin after its last <c>:</c> (for example
/// <c>lcldrevit</c>); <c>CreatedAt</c> is the source timestamp in UTC; <c>SourceLengthUnit</c> is <c>ft</c> when the unit used is feet.
/// </remarks>
public static partial class NwcSceneReader
{
    private const int ModelId = 1;

    private static readonly string Producer = "NwcIO.Scene "
        + typeof(NwcSceneReader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    /// <summary>Reads the model file at <paramref name="path"/> with <see cref="NwcDocument.Open(string, NwcReaderOptions?)"/> and converts it.</summary>
    /// <param name="path">The model file.</param>
    /// <param name="options">Conversion options; null uses the defaults.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> or <see cref="NwcSceneOptions.ModelKey"/> is empty.</exception>
    /// <exception cref="NotSupportedException">The file's scene tree holds <c>.nwd</c> partition nodes or a Revit link.</exception>
    public static NwcSceneResult Read(string path, NwcSceneOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string key = options?.ModelKey ?? Path.GetFileName(path);
        if (key.Length == 0)
        {
            throw new ArgumentException("NwcSceneOptions.ModelKey must not be empty; leave it null to use the file name.", nameof(options));
        }

        NwcDocument document;
        try
        {
            document = NwcDocument.Open(path);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException("NwcIO.Scene needs the scene tree: .nwd partition trees and Revit links are not supported yet. " + ex.Message, ex);
        }

        return Convert(document, key, path, options);
    }

    /// <summary>
    /// Converts <paramref name="document"/>, which has no file name, so <see cref="NwcSceneOptions.ModelKey"/> is required.
    /// Give a document opened from a model file with the default <see cref="NwcReaderOptions"/>. With
    /// <see cref="NwcReaderOptions.ReadPropertyRecords"/>, <see cref="NwcReaderOptions.ReadMeshes"/> or
    /// <see cref="NwcReaderOptions.ReadMaterials"/> off the scene lacks property sets and levels, geometry, or materials, without a
    /// diagnostic (the document does not record its options); a document not read through the file's DbCache directory has meshes
    /// without tree nodes and properties outside tabs, both reported and left out.
    /// </summary>
    /// <param name="document">The document, for example from <see cref="NwcDocument.Open(Stream, NwcReaderOptions?, string?)"/>.</param>
    /// <param name="options">Conversion options with <see cref="NwcSceneOptions.ModelKey"/>.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="NwcSceneOptions.ModelKey"/> is null or empty, or the document has no scene tree (<see cref="NwcDocument.RootNode"/> is null).
    /// </exception>
    public static NwcSceneResult Read(NwcDocument document, NwcSceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.ModelKey))
        {
            throw new ArgumentException("A document converted from memory needs NwcSceneOptions.ModelKey.", nameof(options));
        }

        if (document.RootNode is null)
        {
            throw new ArgumentException("The document has no scene tree: open it with NwcReaderOptions.BuildSceneTree on.", nameof(document));
        }

        return Convert(document, options.ModelKey, null, options);
    }

    private static NwcSceneResult Convert(NwcDocument document, string key, string? path, NwcSceneOptions? options)
    {
        var unit = options?.LengthUnit ?? document.LengthUnit;
        string? loader = document.FileInfo.LoaderPlugin;
        var scene = new SceneDocument { Title = key };
        scene.Models.Add(new SceneModel
        {
            Id = ModelId,
            Key = key,
            Name = key,
            Path = path,
            SourceFormat = "nwc",
            Producer = Producer,
            SourceLengthUnit = unit == NwcLengthUnit.Feet ? "ft" : null,
            Application = loader?[(loader.LastIndexOf(':') + 1)..],
            CreatedAt = document.FileInfo.SourceTimestampUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });

        var root = document.RootNode!;   // Open with the default BuildSceneTree always builds one; Read(document) rejects a document without one

        // Element ids are preorder indices in the order of the walk below; NwcNode.Id is one only on the strict path (a file read
        // without its DbCache directory keeps stored ints, which can repeat).
        var ids = new Dictionary<NwcNode, long>(ReferenceEqualityComparer.Instance);
        var order = new Stack<NwcNode>();
        order.Push(root);
        while (order.TryPop(out var next))
        {
            ids[next] = ids.Count;
            for (int i = next.Children.Count - 1; i >= 0; i--)
            {
                order.Push(next.Children[i]);
            }
        }

        double? metresPerUnit = unit == NwcLengthUnit.Feet ? 0.3048 : null;
        var diagnostics = new List<SceneDiagnostic>();
        HashSet<NwcNode> placed;
        if (metresPerUnit is double metres)
        {
            placed = AddGeometry(document, scene, metres, ids, diagnostics);
        }
        else
        {
            placed = [];
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "unit-unknown",
                $"The length unit is unknown (loader {loader ?? "none"}): geometry, grid lines and length, area, volume and angle values are left out. Set NwcSceneOptions.LengthUnit to convert them."));
        }

        bool revit = loader == "LcNwcLoaderPlugin:lcldrevit";
        var levelElementIds = revit ? LevelElementIds(root) : [];
        int elementCount = 0;
        int leavesOutsideElements = 0;
        int elementsWithGeometry = 0;
        int nodesWithPropertiesOutsideTabs = 0;
        var tabSets = new Dictionary<NwcPropertyTab, int?>(ReferenceEqualityComparer.Instance);
        var lineMeshes = new Dictionary<NwcNode, List<NwcMesh>>(ReferenceEqualityComparer.Instance);   // by element
        if (revit && metresPerUnit is not null)
        {
            foreach (var mesh in document.Meshes)
            {
                if (mesh.LineIndices.Count > 0 && mesh.Node?.Element is { } owner)
                {
                    (lineMeshes.TryGetValue(owner, out var list) ? list : lineMeshes[owner] = []).Add(mesh);
                }
            }
        }

        // Each item carries the names at depth 2 and 4 on its path and the depth of the node's element, for Category and TypeName.
        var pending = new Stack<(NwcNode Node, NwcNode? Parent, int Depth, int? LevelId, int ElementDepth, string AtDepth2, string AtDepth4)>();
        pending.Push((root, null, 0, null, 0, "", ""));
        while (pending.TryPop(out var item))
        {
            var (node, parent, depth, levelId, elementDepth, atDepth2, atDepth4) = item;
            atDepth2 = depth == 2 ? node.Name : atDepth2;
            atDepth4 = depth == 4 ? node.Name : atDepth4;

            // A part keeps the depth of its element (an ancestor); an element, the second node of a pair (a sibling of its element)
            // and a node outside elements take their own.
            if (node.Element is null || node.Element == node || node.Element != parent?.Element)
            {
                elementDepth = depth;
            }

            SceneNodeKind kind;
            if (parent is null)
            {
                kind = SceneNodeKind.Model;
            }
            else if (depth == 1 && IsLevel(node, levelElementIds))
            {
                kind = SceneNodeKind.Level;
                levelId = scene.Levels.Count + 1;
                scene.Levels.Add(new SceneLevel { Id = levelId.Value, ModelId = ModelId, Name = node.Name });
            }
            else if (node.IsElement || node.Element is not null)
            {
                kind = SceneNodeKind.Element;
            }
            else if (node.Children.Count == 0 && node.Tag != 53)
            {
                kind = SceneNodeKind.Element;
                leavesOutsideElements++;
                elementsWithGeometry += placed.Contains(node) ? 1 : 0;
            }
            else
            {
                kind = SceneNodeKind.Group;
            }

            if (node.IsElement)
            {
                if (metresPerUnit is double m && lineMeshes.TryGetValue(node, out var meshes) && IsGrid(node) && GridLine(node, meshes, m) is { } gridLine)
                {
                    if (scene.Grids.Count == 0)
                    {
                        scene.Grids.Add(new SceneGridSystem { ModelId = ModelId, Name = "Grids" });
                    }

                    scene.Grids[0].Lines.Add(gridLine);
                }

                elementCount++;
                elementsWithGeometry += placed.Contains(node) ? 1 : 0;
            }

            if (node.PropertyTabs.Count == 0 && node.Properties.Count > 0)
            {
                nodesWithPropertiesOutsideTabs++;
            }

            // Category is the name at depth 2 on the path from the root (depth 0) to the element; TypeName the name at depth 4 when
            // the element is at depth 5. The element's path shares the node's names except its own, at depth 2 for a pair's second node.
            var (category, typeName) = !revit || kind != SceneNodeKind.Element ? ("", "")
                : (elementDepth == 2 ? (node.Element ?? node).Name : elementDepth > 2 ? atDepth2 : "", elementDepth == 5 ? atDepth4 : "");

            // The second node of a sibling pair is a part of the first.
            long? parentId = parent is null ? null
                : node.Element is { } owner && owner != node && owner != parent.Element ? ids.TryGetValue(owner, out long o) ? o : ids[parent] : ids[parent];
            scene.Elements.Add(new SceneElement
            {
                Id = ids[node],
                ModelId = ModelId,
                ParentId = parentId,
                Kind = kind,
                Name = node.Name,
                SourceId = node.IsElement ? node.ElementGuid?.ToString("D") : null,
                IfcGuid = node.IsElement ? node.IfcGlobalId : null,
                Category = category,
                TypeName = typeName,
                LevelId = levelId,
                PropertySetIds = PropertySetIds(node, scene, tabSets, unit == NwcLengthUnit.Feet),
            });

            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push((node.Children[i], node, depth + 1, levelId, elementDepth, atDepth2, atDepth4));
            }
        }

        if (leavesOutsideElements > 0)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Info, "no-source-id",
                $"{leavesOutsideElements} leaf nodes outside any Revit element have no SourceId: they cannot be identified outside this scene."));
        }

        if (nodesWithPropertiesOutsideTabs > 0)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "properties-outside-tabs",
                $"{nodesWithPropertiesOutsideTabs} nodes carry properties outside property tabs (the file was not read through its DbCache directory); they are not in the scene."));
        }

        if (document.SavedViewpoints.Count > 0 || document.DbCache?.Find("LcOpSavedViewsElement") is not null)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "viewpoints-skipped",
                "Saved viewpoints are not converted: Navisworks stores no stable viewpoint id."));
        }

        return new NwcSceneResult
        {
            Document = scene,
            Diagnostics = diagnostics,
            ElementCount = elementCount + leavesOutsideElements,
            ElementsWithGeometry = elementsWithGeometry,
            GeometryCount = scene.Geometries.Count,
            InstanceCount = scene.Instances.Count,
            LengthUnit = unit,
            MetresPerUnit = metresPerUnit,
        };
    }

    // OST_Grids (-2000220) in the element's own LcRevitData_Element tab.
    private static bool IsGrid(NwcNode node) =>
        node.PropertyTabs.Any(t => t.InternalName == "LcRevitData_Element"
            && t.Properties.Any(p => p.InternalName == "LcRevitPropertyElementCategoryId" && p.Value == "-2000220"));

    // A grid is a line only when it has one line mesh of one segment that is horizontal within 1e-6 m; otherwise it stays an element.
    private static SceneGridLine? GridLine(NwcNode node, List<NwcMesh> meshes, double metresPerUnit)
    {
        if (meshes.Count != 1 || meshes[0].LineIndices.Count != 2)
        {
            return null;
        }

        var world = meshes[0].GetWorldVertices();
        var a = world[meshes[0].LineIndices[0]];
        var b = world[meshes[0].LineIndices[1]];
        var start = new Vector3d(a.X, a.Y, a.Z) * metresPerUnit;
        var end = new Vector3d(b.X, b.Y, b.Z) * metresPerUnit;
        return Math.Abs(start.Z - end.Z) <= 1e-6
            ? new SceneGridLine { Name = node.Name, Start = start, End = end, Elevation = start.Z }
            : null;
    }

    // The LcRevitPropertyElementId values of every LcRevitData_Parameter tab of category OST_Levels (-2000240): the element ids of the levels.
    private static HashSet<string> LevelElementIds(NwcNode root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<NwcNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            foreach (var tab in node.PropertyTabs)
            {
                if (tab.InternalName == "LcRevitData_Parameter"
                    && tab.Properties.Any(p => p.InternalName == "LcRevitPropertyElementCategoryId" && p.Value == "-2000240"))
                {
                    foreach (var p in tab.Properties)
                    {
                        if (p.InternalName == "LcRevitPropertyElementId" && p.Value is { } id)
                        {
                            ids.Add(id);
                        }
                    }
                }
            }

            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return ids;
    }

    // A layer is a level when its LcRevitId tab holds the element id of a level.
    private static bool IsLevel(NwcNode node, HashSet<string> levelElementIds) =>
        node.PropertyTabs.Any(t => t.InternalName == "LcRevitId" && t.Properties.Any(p => p.Value is { } id && levelElementIds.Contains(id)));
}

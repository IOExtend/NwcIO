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
public static class NwcSceneReader
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
        bool revit = loader == "LcNwcLoaderPlugin:lcldrevit";
        var levelElementIds = revit ? LevelElementIds(root) : [];
        int elementCount = 0;
        int leavesOutsideElements = 0;
        var pending = new Stack<(NwcNode Node, NwcNode? Parent, int Depth, int? LevelId)>();
        pending.Push((root, null, 0, null));
        while (pending.TryPop(out var item))
        {
            var (node, parent, depth, levelId) = item;
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
            }
            else
            {
                kind = SceneNodeKind.Group;
            }

            if (node.IsElement)
            {
                elementCount++;
            }

            var (category, typeName) = revit && kind == SceneNodeKind.Element ? CategoryAndType(node.Element ?? node) : ("", "");

            // The second node of a sibling pair is a part of the first.
            long? parentId = parent is null ? null
                : node.Element is { } owner && owner != node && owner != parent.Element ? owner.Id : parent.Id;
            scene.Elements.Add(new SceneElement
            {
                Id = node.Id,
                ModelId = ModelId,
                ParentId = parentId,
                Kind = kind,
                Name = node.Name,
                SourceId = node.IsElement ? node.ElementGuid?.ToString("D") : null,
                IfcGuid = node.IsElement ? node.IfcGlobalId : null,
                Category = category,
                TypeName = typeName,
                LevelId = levelId,
            });

            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push((node.Children[i], node, depth + 1, levelId));
            }
        }

        var diagnostics = new List<SceneDiagnostic>();
        if (leavesOutsideElements > 0)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Info, "no-source-id",
                $"{leavesOutsideElements} leaf nodes outside any Revit element have no SourceId: they cannot be identified outside this scene."));
        }

        return new NwcSceneResult
        {
            Document = scene,
            Diagnostics = diagnostics,
            ElementCount = elementCount + leavesOutsideElements,
            LengthUnit = unit,
            MetresPerUnit = unit == NwcLengthUnit.Feet ? 0.3048 : null,
        };
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

    // Category is the name at depth 2 on the path from the root (depth 0) to the element; TypeName the name at depth 4 when the element is at depth 5.
    private static (string Category, string TypeName) CategoryAndType(NwcNode element)
    {
        var names = new List<string>();   // names[i] is at depth (depth - i)
        for (var n = element; n is not null; n = n.Parent)
        {
            names.Add(n.Name);
        }

        int depth = names.Count - 1;
        return (depth >= 2 ? names[depth - 2] : "", depth == 5 ? names[1] : "");
    }
}

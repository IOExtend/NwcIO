using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Assembles flat <see cref="NwcPropertyRecord"/>s, transforms and GUIDs into a
/// hierarchical <see cref="NwcNode"/> scene graph (matching the Navisworks selection tree:
/// Model -&gt; Layers -&gt; Categories -&gt; Families -&gt; Types -&gt; Composites/Elements).
/// </summary>
/// <remarks>
/// This builder is the lenient fallback for containers without a DbCache directory; model files are read
/// through <see cref="HierarchyParser"/>. A flat record scan does not carry parent links, so the nesting
/// level of a collection comes from its stored name (the localized Category/Family/Type markers the
/// exporters write) and, for names not in that table, from the order of the records. Node classes are
/// matched by suffix (<c>*Layer</c>, <c>*Collection</c>/<c>*Group</c>, <c>*Element</c>/<c>*Composite</c>/<c>*Node</c>
/// and the observed <c>LcRevit*</c> names), so loader-specific class names create nodes too.
/// </remarks>
public static class SceneTreeBuilder
{
    private const int CategoryLevel = 0;
    private const int FamilyLevel = 1;
    private const int TypeLevel = 2;

    // The hierarchy markers are UI strings written by the exporter. Cover the languages Navisworks ships in;
    // names outside the table are nested by record order (see Build).
    private static readonly Dictionary<string, int> CollectionLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["категория"] = CategoryLevel,
        ["category"] = CategoryLevel,
        ["categories"] = CategoryLevel,
        ["kategorie"] = CategoryLevel,
        ["catégorie"] = CategoryLevel,
        ["categoría"] = CategoryLevel,
        ["categoria"] = CategoryLevel,
        ["kategoria"] = CategoryLevel,
        ["カテゴリ"] = CategoryLevel,
        ["类别"] = CategoryLevel,
        ["類別"] = CategoryLevel,
        ["카테고리"] = CategoryLevel,
        ["семейство"] = FamilyLevel,
        ["family"] = FamilyLevel,
        ["familie"] = FamilyLevel,
        ["famille"] = FamilyLevel,
        ["familia"] = FamilyLevel,
        ["famiglia"] = FamilyLevel,
        ["família"] = FamilyLevel,
        ["rodzina"] = FamilyLevel,
        ["ファミリ"] = FamilyLevel,
        ["族"] = FamilyLevel,
        ["패밀리"] = FamilyLevel,
        ["тип"] = TypeLevel,
        ["type"] = TypeLevel,
        ["typ"] = TypeLevel,
        ["tipo"] = TypeLevel,
        ["タイプ"] = TypeLevel,
        ["类型"] = TypeLevel,
        ["類型"] = TypeLevel,
        ["유형"] = TypeLevel,
    };

    public static NwcNode Build(
        string rootName,
        IReadOnlyList<NwcPropertyRecord> records,
        IReadOnlyList<NwcTransform>? transforms = null,
        IReadOnlyList<Guid>? guids = null)
    {
        var root = new NwcNode
        {
            Id = 0,
            Name = rootName,
            Class = "Model",
            Tag = 0,
        };

        NwcNode? currentLayer = null;
        NwcNode? currentCategory = null;
        NwcNode? currentFamily = null;
        NwcNode? currentType = null;
        NwcNode currentActive = root;
        var nodeMap = new Dictionary<int, NwcNode>();
        bool elementSinceCollection = false;
        var markerLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        string? pendingNodeName = null;
        int? pendingParentId = null;

        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];

            if (record.Tag == 53)
            {
                pendingNodeName = record.Name;
                pendingParentId = record.ParentId;
                continue;
            }

            if (record.Tag != 52)
            {
                continue;
            }

            string name = !string.IsNullOrEmpty(pendingNodeName) ? pendingNodeName : record.Name;
            pendingNodeName = null;
            int? parentId = pendingParentId;
            pendingParentId = null;

            if (parentId == 0 && record.FirstInt == 0)
            {
                root.Name = name;
                if (!string.IsNullOrEmpty(record.Class))
                {
                    root.Class = record.Class;
                }

                nodeMap[0] = root;
                currentActive = root;
                continue;
            }

            if (parentId is { } explicitParentId)
            {
                NwcNode? explicitParent = nodeMap.TryGetValue(explicitParentId, out var mappedParent)
                    ? mappedParent
                    : explicitParentId == 0 ? root : null;
                if (explicitParent is not null)
                {
                    var treeNode = new NwcNode
                    {
                        Id = record.FirstInt,
                        Name = name,
                        Class = record.Class,
                        Tag = record.Tag,
                        Value = record.Value,
                        Parent = explicitParent,
                    };
                    explicitParent.Children.Add(treeNode);
                    nodeMap[treeNode.Id] = treeNode;
                    currentActive = treeNode;
                    continue;
                }
            }

            if (IsLayerClass(record.Class))
            {
                var layer = new NwcNode
                {
                    Id = record.FirstInt,
                    Name = name,
                    Class = record.Class,
                    Tag = record.Tag,
                    Parent = root,
                };
                root.Children.Add(layer);
                nodeMap[layer.Id] = layer;
                currentLayer = layer;
                currentCategory = null;
                currentFamily = null;
                currentType = null;
                markerLevels.Clear();
                currentActive = layer;
                elementSinceCollection = false;
            }
            else if (IsCollectionClass(record.Class))
            {
                var collection = new NwcNode
                {
                    Id = record.FirstInt,
                    Name = name,
                    Class = record.Class,
                    Tag = record.Tag,
                };

                int level = ResolveCollectionLevel(
                    record.Name,
                    currentCategory,
                    currentFamily,
                    currentType,
                    elementSinceCollection,
                    markerLevels);

                switch (level)
                {
                    case CategoryLevel:
                        AddCollection(collection, currentLayer ?? root);
                        currentCategory = collection;
                        currentFamily = null;
                        currentType = null;
                        break;
                    case FamilyLevel:
                        AddCollection(collection, currentCategory ?? currentLayer ?? root);
                        currentFamily = collection;
                        currentType = null;
                        break;
                    default:
                        AddCollection(collection, currentFamily ?? currentCategory ?? currentLayer ?? root);
                        currentType = collection;
                        break;
                }

                currentActive = collection;
                nodeMap[collection.Id] = collection;
                elementSinceCollection = false;
            }
            else if (IsElementClass(record.Class) && !IsMaterialRecord(record))
            {
                var parent = currentType ?? currentFamily ?? currentCategory ?? currentLayer ?? root;
                var elemNode = new NwcNode
                {
                    Id = record.FirstInt,
                    Name = record.Name,
                    Class = record.Class,
                    Tag = record.Tag,
                    Value = record.Value,
                    Parent = parent,
                };
                parent.Children.Add(elemNode);
                nodeMap[elemNode.Id] = elemNode;
                currentActive = elemNode;
                elementSinceCollection = true;
            }
            else
            {
                // Property attached to current node
                currentActive.Properties.Add(record);
            }
        }

        // Bound the tree before any recursive consumer (transforms, GUIDs, materials) walks it.
        EnsureBoundedDepth(root);

        // Attach transforms by ID if available, or to root/elements
        if (transforms is { Count: > 0 })
        {
            var transformMap = new Dictionary<int, NwcTransform>(transforms.Count);
            foreach (var t in transforms)
            {
                transformMap.TryAdd(t.Id, t);
            }

            AttachTransformsRecursive(root, transformMap);
        }

        // Attach GUIDs to element nodes if available
        if (guids is { Count: > 0 })
        {
            AttachGuidsRecursive(root, guids, 0);
        }

        return root;
    }

    private static void AddCollection(NwcNode collection, NwcNode parent)
    {
        collection.Parent = parent;
        parent.Children.Add(collection);
    }

    /// <summary>
    /// Chooses the nesting level of a collection. A stored Category/Family/Type marker wins; otherwise the
    /// record order decides: fill Category, then Family, then Type, and start a new Category once an element
    /// has been placed under the current Type.
    /// </summary>
    private static int ResolveCollectionLevel(
        string? storedName,
        NwcNode? currentCategory,
        NwcNode? currentFamily,
        NwcNode? currentType,
        bool elementSinceCollection,
        Dictionary<string, int> markerLevels)
    {
        string marker = storedName?.Trim() ?? string.Empty;
        int level;
        if (marker.Length > 0 && CollectionLevels.TryGetValue(marker, out level))
        {
            // Remember it so an unknown marker that follows is placed below the levels already taken.
            markerLevels[marker] = level;
            return level;
        }

        // Real files repeat the stored marker for every collection of one level. Reuse the level of a
        // marker seen before; a new marker takes the level below the deepest one seen in this layer.
        if (marker.Length > 0 && markerLevels.TryGetValue(marker, out level))
        {
            return level;
        }

        if (marker.Length > 0)
        {
            level = markerLevels.Count == 0 ? CategoryLevel : markerLevels.Values.Max() + 1;
            if (level <= TypeLevel)
            {
                markerLevels[marker] = level;
                return level;
            }
        }

        if (currentCategory is null)
        {
            return CategoryLevel;
        }

        if (currentFamily is null)
        {
            return FamilyLevel;
        }

        if (currentType is null)
        {
            return TypeLevel;
        }

        return elementSinceCollection ? CategoryLevel : TypeLevel;
    }

    // Material definitions use LcRevitElement but are properties, not scene nodes; creating a node for
    // them shifts every following element GUID by one.
    private static bool IsMaterialRecord(NwcPropertyRecord record) =>
        record.Class.Equals("LcRevitElement", StringComparison.OrdinalIgnoreCase) &&
        record.Name.StartsWith("Material \"", StringComparison.Ordinal);

    private static bool IsLayerClass(string className) =>
        className.Equals("LcRevitLayer", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Layer", StringComparison.OrdinalIgnoreCase);

    private static bool IsCollectionClass(string className) =>
        className.Equals("LcRevitCollection", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Collection", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Group", StringComparison.OrdinalIgnoreCase);

    private static bool IsElementClass(string className) =>
        className.Equals("LcRevitComposite", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("LcRevitData_Element", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Element", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Composite", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Instance", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Node", StringComparison.OrdinalIgnoreCase) ||
        className.EndsWith("Object", StringComparison.OrdinalIgnoreCase);

    private static void AttachTransformsRecursive(NwcNode node, Dictionary<int, NwcTransform> transformMap)
    {
        if (transformMap.TryGetValue(node.Id, out var transform))
        {
            node.Transform = transform;
        }

        foreach (var child in node.Children)
        {
            AttachTransformsRecursive(child, transformMap);
        }
    }

    private static int AttachGuidsRecursive(NwcNode node, IReadOnlyList<Guid> guids, int nextIndex)
    {
        if (node.Id != 0 && IsElementClass(node.Class) && nextIndex < guids.Count)
        {
            node.ElementGuid = guids[nextIndex++];
        }

        foreach (var child in node.Children)
        {
            nextIndex = AttachGuidsRecursive(child, guids, nextIndex);
        }

        return nextIndex;
    }

    /// <summary>
    /// An explicit parent marker can chain nodes as deep as the stream wants. Every recursive consumer of
    /// the tree (materials, transforms, GUIDs, exporters) needs the same bound as the strict hierarchy reader.
    /// </summary>
    private static void EnsureBoundedDepth(NwcNode root)
    {
        var stack = new Stack<(NwcNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (depth > LcObjectReader.MaxNestingDepth)
            {
                throw new InvalidDataException(
                    $"Scene tree depth exceeds {LcObjectReader.MaxNestingDepth} at node '{node.Name}'.");
            }

            foreach (var child in node.Children)
            {
                stack.Push((child, depth + 1));
            }
        }
    }
}

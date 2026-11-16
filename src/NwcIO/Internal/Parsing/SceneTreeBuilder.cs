using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Assembles flat <see cref="NwcPropertyRecord"/>s, transforms and GUIDs into a
/// hierarchical <see cref="NwcNode"/> scene graph (matching the Navisworks selection tree:
/// Model -&gt; Layers -&gt; Categories -&gt; Families -&gt; Types -&gt; Composites/Elements).
/// </summary>
public static class SceneTreeBuilder
{
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

            if (record.Tag == 52)
            {
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

                if (record.Class == "LcRevitLayer")
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
                    currentActive = layer;
                }
                else if (record.Class == "LcRevitCollection")
                {
                    var collection = new NwcNode
                    {
                        Id = record.FirstInt,
                        Name = name,
                        Class = record.Class,
                        Tag = record.Tag,
                    };

                    if (record.Name.Equals("Категория", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = currentLayer ?? root;
                        collection.Parent = parent;
                        parent.Children.Add(collection);
                        currentCategory = collection;
                        currentFamily = null;
                        currentType = null;
                    }
                    else if (record.Name.Equals("Семейство", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = currentCategory ?? currentLayer ?? root;
                        collection.Parent = parent;
                        parent.Children.Add(collection);
                        currentFamily = collection;
                        currentType = null;
                    }
                    else if (record.Name.Equals("Тип", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = currentFamily ?? currentCategory ?? currentLayer ?? root;
                        collection.Parent = parent;
                        parent.Children.Add(collection);
                        currentType = collection;
                    }
                    else
                    {
                        var parent = currentType ?? currentFamily ?? currentCategory ?? currentLayer ?? root;
                        collection.Parent = parent;
                        parent.Children.Add(collection);
                    }

                    currentActive = collection;
                    nodeMap[collection.Id] = collection;
                }
                else if (record.Class == "LcRevitComposite")
                {
                    var parent = currentType ?? currentFamily ?? currentCategory ?? currentLayer ?? root;
                    var composite = new NwcNode
                    {
                        Id = record.FirstInt,
                        Name = record.Name,
                        Class = record.Class,
                        Tag = record.Tag,
                        Value = record.Value,
                        Parent = parent,
                    };
                    parent.Children.Add(composite);
                    nodeMap[composite.Id] = composite;
                    currentActive = composite;
                }
                else if (record.Class == "LcRevitData_Element")
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
                }
                else
                {
                    // Property attached to current node
                    currentActive.Properties.Add(record);
                }
            }
        }

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
        if (node.Class is "LcRevitData_Element" or "LcRevitComposite" && nextIndex < guids.Count)
        {
            node.ElementGuid = guids[nextIndex++];
        }

        foreach (var child in node.Children)
        {
            nextIndex = AttachGuidsRecursive(child, guids, nextIndex);
        }

        return nextIndex;
    }
}

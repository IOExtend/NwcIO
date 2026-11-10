using System.Globalization;
using NwcIO.Model;

namespace NwcIO.Model.Diff;

/// <summary>
/// Engine for comparing Navisworks models or federated assemblies, detecting additions,
/// deletions, geometric coordinate shifts, and property record modifications.
/// </summary>
public static class NwcDiffEngine
{
    private sealed class DiffItem
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public string? ClassName { get; init; }
        public Guid? ElementGuid { get; init; }
        public NwcNode? Node { get; init; }
        public NwcMesh? Mesh { get; init; }
        public NwcTransform? Transform { get; init; }
        public IReadOnlyList<NwcPropertyRecord> Properties { get; init; } = [];
        public BoundingBox3d WorldBounds { get; set; } = BoundingBox3d.Empty;
        public string MatchKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Compares two <see cref="NwcDocument"/> instances and produces a revision difference report.
    /// </summary>
    public static NwcDiffReport Compare(NwcDocument baseline, NwcDocument revision, NwcDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(revision);
        options ??= new NwcDiffOptions();

        string baseName = Path.GetFileNameWithoutExtension(baseline.FileInfo.SourcePath ?? "Baseline");
        string revName = Path.GetFileNameWithoutExtension(revision.FileInfo.SourcePath ?? "Revision");

        var report = new NwcDiffReport(baseName, revName);

        var baseItems = CollectItems(baseline);
        var revItems = CollectItems(revision);

        PerformDiff(report, baseItems, revItems, options);
        return report;
    }

    /// <summary>
    /// Compares two federated <see cref="NwcAssembly"/> instances and produces a revision difference report.
    /// </summary>
    public static NwcDiffReport Compare(NwcAssembly baseline, NwcAssembly revision, NwcDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(revision);
        options ??= new NwcDiffOptions();

        var report = new NwcDiffReport(baseline.Name, revision.Name);

        var baseItems = CollectAssemblyItems(baseline);
        var revItems = CollectAssemblyItems(revision);

        PerformDiff(report, baseItems, revItems, options);
        return report;
    }

    private static void PerformDiff(
        NwcDiffReport report,
        List<DiffItem> baseItems,
        List<DiffItem> revItems,
        NwcDiffOptions options)
    {
        // 1. Build lookup tables: by Guid first, then by composite MatchKey
        var baseByGuid = new Dictionary<Guid, DiffItem>();
        var baseByKey = new Dictionary<string, DiffItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in baseItems)
        {
            if (item.ElementGuid.HasValue && item.ElementGuid.Value != Guid.Empty)
            {
                baseByGuid[item.ElementGuid.Value] = item;
            }
            else
            {
                baseByKey[item.MatchKey] = item;
            }
        }

        var matchedBase = new HashSet<DiffItem>();

        // 2. Iterate revision items to find matches or additions
        foreach (var rev in revItems)
        {
            if (!PassesFilter(rev, options.NameFilter))
            {
                continue;
            }

            DiffItem? baseItem = null;
            if (rev.ElementGuid.HasValue && rev.ElementGuid.Value != Guid.Empty)
            {
                baseByGuid.TryGetValue(rev.ElementGuid.Value, out baseItem);
            }
            if (baseItem is null && !string.IsNullOrEmpty(rev.MatchKey))
            {
                baseByKey.TryGetValue(rev.MatchKey, out baseItem);
            }

            if (baseItem is null)
            {
                // Added element
                report.Added.Add(new NwcElementDiff
                {
                    NodeId = rev.Id,
                    Name = rev.Name,
                    ClassName = rev.ClassName,
                    ElementGuid = rev.ElementGuid,
                    ChangeKind = NwcChangeKind.Added,
                    RevisionNode = rev.Node,
                    RevisionMesh = rev.Mesh,
                });
            }
            else
            {
                matchedBase.Add(baseItem);

                // Compare geometry and properties
                bool geomChanged = false;
                var delta = Vector3d.Zero;

                // Geometric comparison
                if (!baseItem.WorldBounds.IsEmpty && !rev.WorldBounds.IsEmpty)
                {
                    delta = rev.WorldBounds.Center - baseItem.WorldBounds.Center;
                    if (delta.Length > options.GeometricTolerance)
                    {
                        geomChanged = true;
                    }
                    else
                    {
                        var sizeDelta = rev.WorldBounds.Size - baseItem.WorldBounds.Size;
                        if (Math.Abs(sizeDelta.X) > options.GeometricTolerance ||
                            Math.Abs(sizeDelta.Y) > options.GeometricTolerance ||
                            Math.Abs(sizeDelta.Z) > options.GeometricTolerance)
                        {
                            geomChanged = true;
                        }
                    }
                }
                else if (options.CompareTransforms && baseItem.Transform is not null && rev.Transform is not null)
                {
                    delta = rev.Transform.Translation - baseItem.Transform.Translation;
                    if (delta.Length > options.GeometricTolerance)
                    {
                        geomChanged = true;
                    }
                }

                if (baseItem.Mesh is not null && rev.Mesh is not null)
                {
                    if (baseItem.Mesh.Vertices.Count != rev.Mesh.Vertices.Count ||
                        baseItem.Mesh.Indices.Count != rev.Mesh.Indices.Count)
                    {
                        geomChanged = true;
                    }
                }
                else if ((baseItem.Mesh is null) != (rev.Mesh is null))
                {
                    geomChanged = true;
                }

                // Property comparison
                var propChanges = new List<NwcPropertyChange>();
                if (options.CompareProperties)
                {
                    CompareProperties(baseItem.Properties, rev.Properties, propChanges);
                }

                var diff = new NwcElementDiff
                {
                    NodeId = rev.Id,
                    Name = rev.Name,
                    ClassName = rev.ClassName ?? baseItem.ClassName,
                    ElementGuid = rev.ElementGuid ?? baseItem.ElementGuid,
                    GeometryChanged = geomChanged,
                    TranslationDelta = delta,
                    BaselineNode = baseItem.Node,
                    RevisionNode = rev.Node,
                    BaselineMesh = baseItem.Mesh,
                    RevisionMesh = rev.Mesh,
                };
                diff.PropertyChanges.AddRange(propChanges);

                if (geomChanged || propChanges.Count > 0)
                {
                    diff.ChangeKind = NwcChangeKind.Modified;
                    report.Modified.Add(diff);
                }
                else
                {
                    diff.ChangeKind = NwcChangeKind.Unchanged;
                    report.Unchanged.Add(diff);
                }
            }
        }

        // 3. Any baseline items not matched are deleted
        foreach (var b in baseItems)
        {
            if (!matchedBase.Contains(b) && PassesFilter(b, options.NameFilter))
            {
                report.Deleted.Add(new NwcElementDiff
                {
                    NodeId = b.Id,
                    Name = b.Name,
                    ClassName = b.ClassName,
                    ElementGuid = b.ElementGuid,
                    ChangeKind = NwcChangeKind.Deleted,
                    BaselineNode = b.Node,
                    BaselineMesh = b.Mesh,
                });
            }
        }
    }

    private static bool PassesFilter(DiffItem item, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        if (item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.ClassName is not null && item.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void CompareProperties(
        IReadOnlyList<NwcPropertyRecord> oldProps,
        IReadOnlyList<NwcPropertyRecord> newProps,
        List<NwcPropertyChange> changes)
    {
        var oldDict = new Dictionary<string, NwcPropertyRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in oldProps)
        {
            string key = $"{p.Class}::{p.Name}";
            oldDict[key] = p;
        }

        var matchedOld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var np in newProps)
        {
            string key = $"{np.Class}::{np.Name}";
            if (oldDict.TryGetValue(key, out var op))
            {
                matchedOld.Add(key);
                if (!string.Equals(op.Value, np.Value, StringComparison.Ordinal))
                {
                    changes.Add(new NwcPropertyChange(np.Class, np.Name, op.Value, np.Value, "Modified"));
                }
            }
            else
            {
                changes.Add(new NwcPropertyChange(np.Class, np.Name, null, np.Value, "Added"));
            }
        }

        foreach (var kvp in oldDict)
        {
            if (!matchedOld.Contains(kvp.Key))
            {
                var op = kvp.Value;
                changes.Add(new NwcPropertyChange(op.Class, op.Name, op.Value, null, "Removed"));
            }
        }
    }

    private static List<DiffItem> CollectItems(NwcDocument doc)
    {
        var items = new List<DiffItem>();
        var keyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (doc.RootNode is not null)
        {
            void Walk(NwcNode node, string path)
            {
                string currentPath = string.IsNullOrEmpty(path) ? node.Name : $"{path}/{node.Name}";

                // An item represents an element if it has GUID, Mesh, or properties and is a leaf or designated category
                bool isElement = node.ElementGuid.HasValue ||
                                 node.Mesh is not null ||
                                 (node.Children.Count == 0 && (node.Properties.Count > 0 || !string.IsNullOrEmpty(node.Name)));

                if (isElement)
                {
                    var bounds = BoundingBox3d.Empty;
                    if (node.Mesh is not null)
                    {
                        bounds = node.Mesh.GetWorldBoundingBox();
                    }

                    string rawKey = node.ElementGuid.HasValue
                        ? node.ElementGuid.Value.ToString("D")
                        : $"{node.Class}:{currentPath}";

                    keyCounts.TryGetValue(rawKey, out int count);
                    keyCounts[rawKey] = count + 1;
                    string matchKey = count == 0 ? rawKey : $"{rawKey}#{count}";

                    items.Add(new DiffItem
                    {
                        Id = node.Id,
                        Name = node.Name,
                        ClassName = node.Class,
                        ElementGuid = node.ElementGuid,
                        Node = node,
                        Mesh = node.Mesh,
                        Transform = node.Transform,
                        Properties = node.Properties,
                        WorldBounds = bounds,
                        MatchKey = matchKey,
                    });
                }

                for (int i = 0; i < node.Children.Count; i++)
                {
                    Walk(node.Children[i], currentPath);
                }
            }

            Walk(doc.RootNode, string.Empty);
        }

        // If tree had no geometric or property items, fallback to meshes directly
        if (items.Count == 0 && doc.Meshes.Count > 0)
        {
            for (int i = 0; i < doc.Meshes.Count; i++)
            {
                var m = doc.Meshes[i];
                string name = !string.IsNullOrEmpty(m.Name) ? m.Name : $"Mesh_{i + 1}";
                string rawKey = $"Mesh:{name}";
                keyCounts.TryGetValue(rawKey, out int count);
                keyCounts[rawKey] = count + 1;
                string matchKey = count == 0 ? rawKey : $"{rawKey}#{count}";

                items.Add(new DiffItem
                {
                    Id = m.Id,
                    Name = name,
                    ClassName = "MeshGeometry",
                    Mesh = m,
                    Transform = m.Transform,
                    WorldBounds = m.GetWorldBoundingBox(),
                    MatchKey = matchKey,
                });
            }
        }

        return items;
    }

    private static List<DiffItem> CollectAssemblyItems(NwcAssembly assembly)
    {
        var items = new List<DiffItem>();
        for (int i = 0; i < assembly.Models.Count; i++)
        {
            items.AddRange(CollectItems(assembly.Models[i]));
        }
        return items;
    }
}

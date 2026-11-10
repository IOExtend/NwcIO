using NwcIO.Internal.Export;
using NwcIO.Model.Sets;

namespace NwcIO.Model.Clash;

/// <summary>
/// Managed clash detection engine performing broad-phase bounding box tests
/// and narrow-phase 3D triangle-to-triangle intersection tests according to Navisworks rules.
/// </summary>
public static class NwcClashDetector
{
    /// <summary>
    /// Executes clash detection across an individual Navisworks document according to the specified test configuration.
    /// </summary>
    public static NwcClashTest Run(NwcDocument document, NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(test);

        var meshesA = ResolveSelection(document, test.SelectionA);
        var meshesB = ResolveSelection(document, test.SelectionB);

        RunCore(meshesA, meshesB, test, document.FileInfo.SourcePath ?? "Model");
        return test;
    }

    /// <summary>
    /// Executes clash detection across all contributing models of a federated Navisworks assembly.
    /// </summary>
    public static NwcClashTest Run(NwcAssembly assembly, NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(test);

        var itemsA = new List<(NwcMesh Mesh, string ModelName, string? Layer)>();
        var itemsB = new List<(NwcMesh Mesh, string ModelName, string? Layer)>();

        for (int m = 0; m < assembly.Models.Count; m++)
        {
            var doc = assembly.Models[m];
            string docName = Path.GetFileNameWithoutExtension(doc.FileInfo.SourcePath ?? $"Model_{m + 1}");

            var selA = ResolveSelection(doc, test.SelectionA);
            for (int i = 0; i < selA.Count; i++)
            {
                itemsA.Add((selA[i], docName, selA[i].Node?.Name));
            }

            var selB = ResolveSelection(doc, test.SelectionB);
            for (int i = 0; i < selB.Count; i++)
            {
                itemsB.Add((selB[i], docName, selB[i].Node?.Name));
            }
        }

        RunCoreMultiModel(itemsA, itemsB, test);
        return test;
    }

    private static void RunCore(
        IReadOnlyList<NwcMesh> meshesA,
        IReadOnlyList<NwcMesh> meshesB,
        NwcClashTest test,
        string modelName)
    {
        bool sameSelection = ReferenceEquals(meshesA, meshesB) ||
            (meshesA.Count == meshesB.Count && meshesA.Count > 0 && ReferenceEquals(meshesA[0], meshesB[0]));

        bool ignoreSameLayer = test.Rules.Any(r => r.Enabled && r.RuleType == NwcClashRuleType.IgnoreSameLayer);
        bool ignoreSameGroup = test.Rules.Any(r => r.Enabled && r.RuleType == NwcClashRuleType.IgnoreSameGroup);

        for (int i = 0; i < meshesA.Count; i++)
        {
            var meshA = meshesA[i];
            var boxA = meshA.GetWorldBoundingBox();
            if (boxA.IsEmpty) continue;

            int startJ = sameSelection ? i + 1 : 0;
            for (int j = startJ; j < meshesB.Count; j++)
            {
                var meshB = meshesB[j];
                if (ReferenceEquals(meshA, meshB) || meshA.Id == meshB.Id) continue;

                var boxB = meshB.GetWorldBoundingBox();
                if (boxB.IsEmpty) continue;

                // Rule filtering
                if (ignoreSameLayer && string.Equals(meshA.Node?.Parent?.Name, meshB.Node?.Parent?.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (ignoreSameGroup && meshA.Node?.Parent is not null && ReferenceEquals(meshA.Node.Parent, meshB.Node?.Parent))
                {
                    continue;
                }

                // Broad phase AABB
                double tol = Math.Max(0.0, test.Tolerance);
                var expandedA = tol > 0
                    ? new BoundingBox3d(boxA.Min - new Vector3d(tol, tol, tol), boxA.Max + new Vector3d(tol, tol, tol))
                    : boxA;

                if (!expandedA.Intersects(boxB)) continue;

                // Narrow phase: mesh triangle intersection
                if (CheckMeshIntersection(meshA, meshB, out var clashPoint, out double penetration))
                {
                    int clashIndex = test.Results.Count + 1;
                    string clashName = $"Clash{clashIndex}";

                    string item1Name = meshA.Node?.Name ?? $"Mesh #{meshA.Id}";
                    string item2Name = meshB.Node?.Name ?? $"Mesh #{meshB.Id}";

                    var obj1 = new NwcClashObject(item1Name, meshA.Id, meshA.Node?.Name ?? item1Name)
                    {
                        ModelName = modelName,
                        Layer = meshA.Node?.Parent?.Name ?? "Default",
                        Node = meshA.Node,
                        Mesh = meshA,
                        IfcGuid = IfcGuid.FromElementId(meshA.Id, "NwcMesh"),
                    };

                    var obj2 = new NwcClashObject(item2Name, meshB.Id, meshB.Node?.Name ?? item2Name)
                    {
                        ModelName = modelName,
                        Layer = meshB.Node?.Parent?.Name ?? "Default",
                        Node = meshB.Node,
                        Mesh = meshB,
                        IfcGuid = IfcGuid.FromElementId(meshB.Id, "NwcMesh"),
                    };

                    var unionBox = boxA.Union(boxB);
                    var vp = NwcSavedViewpoint.CreateDefault(unionBox, $"{clashName} View");

                    var result = new NwcClashResult(clashName, obj1, obj2, clashPoint, -penetration, NwcClashStatus.New)
                    {
                        Description = $"Hard clash between {item1Name} and {item2Name}",
                        Viewpoint = vp,
                    };

                    test.AddResult(result);
                }
            }
        }
    }

    private static void RunCoreMultiModel(
        List<(NwcMesh Mesh, string ModelName, string? Layer)> itemsA,
        List<(NwcMesh Mesh, string ModelName, string? Layer)> itemsB,
        NwcClashTest test)
    {
        bool ignoreSameFile = test.Rules.Any(r => r.Enabled && r.RuleType == NwcClashRuleType.IgnoreSameFile);
        bool ignoreSameLayer = test.Rules.Any(r => r.Enabled && r.RuleType == NwcClashRuleType.IgnoreSameLayer);

        for (int i = 0; i < itemsA.Count; i++)
        {
            var itemA = itemsA[i];
            var boxA = itemA.Mesh.GetWorldBoundingBox();
            if (boxA.IsEmpty) continue;

            for (int j = 0; j < itemsB.Count; j++)
            {
                var itemB = itemsB[j];
                if (ReferenceEquals(itemA.Mesh, itemB.Mesh)) continue;

                if (ignoreSameFile && string.Equals(itemA.ModelName, itemB.ModelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ignoreSameLayer && !string.IsNullOrEmpty(itemA.Layer) && string.Equals(itemA.Layer, itemB.Layer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var boxB = itemB.Mesh.GetWorldBoundingBox();
                if (boxB.IsEmpty) continue;

                double tol = Math.Max(0.0, test.Tolerance);
                var expandedA = tol > 0
                    ? new BoundingBox3d(boxA.Min - new Vector3d(tol, tol, tol), boxA.Max + new Vector3d(tol, tol, tol))
                    : boxA;

                if (!expandedA.Intersects(boxB)) continue;

                if (CheckMeshIntersection(itemA.Mesh, itemB.Mesh, out var clashPoint, out double penetration))
                {
                    int clashIndex = test.Results.Count + 1;
                    string clashName = $"Clash{clashIndex}";

                    string name1 = itemA.Mesh.Node?.Name ?? $"Mesh #{itemA.Mesh.Id}";
                    string name2 = itemB.Mesh.Node?.Name ?? $"Mesh #{itemB.Mesh.Id}";

                    var obj1 = new NwcClashObject(name1, itemA.Mesh.Id, $"{itemA.ModelName}/{name1}")
                    {
                        ModelName = itemA.ModelName,
                        Layer = itemA.Layer,
                        Node = itemA.Mesh.Node,
                        Mesh = itemA.Mesh,
                        IfcGuid = IfcGuid.FromElementId(itemA.Mesh.Id, "NwcMesh"),
                    };

                    var obj2 = new NwcClashObject(name2, itemB.Mesh.Id, $"{itemB.ModelName}/{name2}")
                    {
                        ModelName = itemB.ModelName,
                        Layer = itemB.Layer,
                        Node = itemB.Mesh.Node,
                        Mesh = itemB.Mesh,
                        IfcGuid = IfcGuid.FromElementId(itemB.Mesh.Id, "NwcMesh"),
                    };

                    var unionBox = boxA.Union(boxB);
                    var vp = NwcSavedViewpoint.CreateDefault(unionBox, $"{clashName} View");

                    var result = new NwcClashResult(clashName, obj1, obj2, clashPoint, -penetration, NwcClashStatus.New)
                    {
                        Description = $"Clash between {itemA.ModelName} ({name1}) and {itemB.ModelName} ({name2})",
                        Viewpoint = vp,
                    };

                    test.AddResult(result);
                }
            }
        }
    }

    private static bool CheckMeshIntersection(
        NwcMesh a,
        NwcMesh b,
        out Vector3d clashPoint,
        out double penetrationDepth)
    {
        clashPoint = Vector3d.Zero;
        penetrationDepth = 0.0;

        var vertsA = a.GetWorldVertices();
        var vertsB = b.GetWorldVertices();

        if (vertsA.Count < 3 || vertsB.Count < 3)
        {
            // Fallback to bounding box overlap
            var boxA = a.GetWorldBoundingBox();
            var boxB = b.GetWorldBoundingBox();
            if (boxA.Intersects(boxB))
            {
                var overlap = new BoundingBox3d(
                    new Vector3d(Math.Max(boxA.Min.X, boxB.Min.X), Math.Max(boxA.Min.Y, boxB.Min.Y), Math.Max(boxA.Min.Z, boxB.Min.Z)),
                    new Vector3d(Math.Min(boxA.Max.X, boxB.Max.X), Math.Min(boxA.Max.Y, boxB.Max.Y), Math.Min(boxA.Max.Z, boxB.Max.Z)));
                clashPoint = overlap.Center;
                penetrationDepth = Math.Min(overlap.Size.X, Math.Min(overlap.Size.Y, overlap.Size.Z));
                return true;
            }
            return false;
        }

        var indicesA = a.Indices.Count >= 3 ? a.Indices : CreateDefaultIndices(vertsA.Count);
        var indicesB = b.Indices.Count >= 3 ? b.Indices : CreateDefaultIndices(vertsB.Count);

        var intersectionPoints = new List<Vector3d>();

        for (int i = 0; i + 2 < indicesA.Count; i += 3)
        {
            int ia0 = indicesA[i];
            int ia1 = indicesA[i + 1];
            int ia2 = indicesA[i + 2];
            if (ia0 >= vertsA.Count || ia1 >= vertsA.Count || ia2 >= vertsA.Count) continue;

            var v0 = vertsA[ia0];
            var v1 = vertsA[ia1];
            var v2 = vertsA[ia2];

            for (int j = 0; j + 2 < indicesB.Count; j += 3)
            {
                int ib0 = indicesB[j];
                int ib1 = indicesB[j + 1];
                int ib2 = indicesB[j + 2];
                if (ib0 >= vertsB.Count || ib1 >= vertsB.Count || ib2 >= vertsB.Count) continue;

                var u0 = vertsB[ib0];
                var u1 = vertsB[ib1];
                var u2 = vertsB[ib2];

                if (NwcTriangleIntersection.TrianglesIntersect(v0, v1, v2, u0, u1, u2, out var pt))
                {
                    intersectionPoints.Add(pt);
                    if (intersectionPoints.Count >= 10) break; // Limit sampling for performance
                }
            }
            if (intersectionPoints.Count >= 10) break;
        }

        if (intersectionPoints.Count > 0)
        {
            var sum = Vector3d.Zero;
            for (int p = 0; p < intersectionPoints.Count; p++) sum += intersectionPoints[p];
            clashPoint = sum / (double)intersectionPoints.Count;

            var boxA = a.GetWorldBoundingBox();
            var boxB = b.GetWorldBoundingBox();
            var overlapSize = new Vector3d(
                Math.Max(0.0, Math.Min(boxA.Max.X, boxB.Max.X) - Math.Max(boxA.Min.X, boxB.Min.X)),
                Math.Max(0.0, Math.Min(boxA.Max.Y, boxB.Max.Y) - Math.Max(boxA.Min.Y, boxB.Min.Y)),
                Math.Max(0.0, Math.Min(boxA.Max.Z, boxB.Max.Z) - Math.Max(boxA.Min.Z, boxB.Min.Z)));
            penetrationDepth = Math.Min(overlapSize.X, Math.Min(overlapSize.Y, overlapSize.Z));
            return true;
        }

        return false;
    }

    private static List<int> CreateDefaultIndices(int count)
    {
        var indices = new List<int>(count);
        for (int i = 0; i < count; i++) indices.Add(i);
        return indices;
    }

    private static IReadOnlyList<NwcMesh> ResolveSelection(NwcDocument document, NwcClashSelection sel)
    {
        if (sel.ExplicitNodeIds.Count > 0)
        {
            var idSet = new HashSet<int>(sel.ExplicitNodeIds);
            return document.Meshes.Where(m => idSet.Contains(m.Id)).ToList();
        }

        if (sel.FilterCondition is not null)
        {
            var matchedNodes = document.FindNodes(sel.FilterCondition);
            var nodeSet = new HashSet<NwcNode>(matchedNodes);
            return document.Meshes.Where(m => m.Node is not null && nodeSet.Contains(m.Node)).ToList();
        }

        if (sel.SetNames.Count > 0)
        {
            var matchedMeshes = new List<NwcMesh>();
            for (int s = 0; s < sel.SetNames.Count; s++)
            {
                var set = document.FindSet(sel.SetNames[s]);
                if (set is NwcSelectionSet selSet)
                {
                    var nodes = selSet.ResolveNodes(document);
                    var nodeSet = new HashSet<NwcNode>(nodes);
                    matchedMeshes.AddRange(document.Meshes.Where(m => m.Node is not null && nodeSet.Contains(m.Node)));
                }
                else if (set is NwcSearchSet searchSet)
                {
                    var nodes = searchSet.Evaluate(document);
                    var nodeSet = new HashSet<NwcNode>(nodes);
                    matchedMeshes.AddRange(document.Meshes.Where(m => m.Node is not null && nodeSet.Contains(m.Node)));
                }
            }
            if (matchedMeshes.Count > 0) return matchedMeshes;
        }

        return document.Meshes;
    }
}

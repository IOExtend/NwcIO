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
    /// <exception cref="ArgumentException">An active set selection names a set that the document doesn't contain.</exception>
    public static NwcClashTest Run(NwcDocument document, NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(test);
        ThrowIfSetIsMissing([document], test);

        var meshesA = ResolveSelection(document, test.SelectionA);
        var meshesB = ResolveSelection(document, test.SelectionB);

        RunCore(meshesA, meshesB, test, document.FileInfo.SourcePath ?? "Model", document);
        return test;
    }

    /// <summary>
    /// Executes clash detection across all contributing models of a federated Navisworks assembly.
    /// </summary>
    /// <exception cref="ArgumentException">An active set selection names a set that none of the models contains.</exception>
    public static NwcClashTest Run(NwcAssembly assembly, NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(test);
        ThrowIfSetIsMissing(assembly.Models, test);

        var itemsA = new List<(NwcMesh Mesh, string ModelName, string? Layer, string ModelKey)>();
        var itemsB = new List<(NwcMesh Mesh, string ModelName, string? Layer, string ModelKey)>();
        var modelKeys = NwcElementGuid.ModelKeys(assembly.Models);

        for (int m = 0; m < assembly.Models.Count; m++)
        {
            var doc = assembly.Models[m];
            string docName = Path.GetFileNameWithoutExtension(doc.FileInfo.SourcePath ?? $"Model_{m + 1}");

            var selA = ResolveSelection(doc, test.SelectionA);
            for (int i = 0; i < selA.Count; i++)
            {
                itemsA.Add((selA[i], docName, selA[i].Node?.Parent?.Name, modelKeys[m]));
            }

            var selB = ResolveSelection(doc, test.SelectionB);
            for (int i = 0; i < selB.Count; i++)
            {
                itemsB.Add((selB[i], docName, selB[i].Node?.Parent?.Name, modelKeys[m]));
            }
        }

        RunCoreMultiModel(itemsA, itemsB, test);
        return test;
    }

    private static void RunCore(
        IReadOnlyList<NwcMesh> meshesA,
        IReadOnlyList<NwcMesh> meshesB,
        NwcClashTest test,
        string modelName,
        NwcDocument document)
    {
        bool sameSelection = ReferenceEquals(meshesA, meshesB) ||
            meshesA.SequenceEqual(meshesB, ReferenceEqualityComparer.Instance);
        Dictionary<NwcMesh, int>? firstIndexA = null;
        HashSet<NwcMesh>? meshesInB = null;
        if (!sameSelection)
        {
            firstIndexA = new Dictionary<NwcMesh, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < meshesA.Count; i++)
            {
                firstIndexA.TryAdd(meshesA[i], i);
            }
            meshesInB = new HashSet<NwcMesh>(meshesB, ReferenceEqualityComparer.Instance);
        }

        for (int i = 0; i < meshesA.Count; i++)
        {
            var meshA = meshesA[i];
            var boxA = meshA.GetWorldBoundingBox();
            if (boxA.IsEmpty) continue;
            bool canVisitReverse = !sameSelection && meshesInB!.Contains(meshA);

            for (var j = sameSelection ? i + 1 : 0; j < meshesB.Count; j++)
            {
                var meshB = meshesB[j];
                if (ReferenceEquals(meshA, meshB) || meshA.Id == meshB.Id || IsSameElement(meshA, meshB)) continue;
                // The reverse pair was already visited from the earlier mesh in A.
                if (canVisitReverse && firstIndexA!.TryGetValue(meshB, out int firstIndex) && firstIndex < i)
                {
                    continue;
                }

                var boxB = meshB.GetWorldBoundingBox();
                if (boxB.IsEmpty) continue;

                // Broad phase AABB
                double tol = Math.Max(0.0, test.Tolerance);
                var expandedA = tol > 0
                    ? new BoundingBox3d(boxA.Min - new Vector3d(tol, tol, tol), boxA.Max + new Vector3d(tol, tol, tol))
                    : boxA;

                if (!expandedA.Intersects(boxB)) continue;

                // Narrow phase: triangle intersection (Hard) or mesh distance (Clearance)
                if (TryGetClash(test, meshA, meshB, out var clashPoint, out double distance))
                {
                    int clashIndex = test.Results.Count + 1;
                    string clashName = $"Clash{clashIndex}";

                    string item1Name = meshA.Node?.Name ?? $"Mesh #{meshA.Id}";
                    string item2Name = meshB.Node?.Name ?? $"Mesh #{meshB.Id}";

                    var obj1 = new NwcClashObject(item1Name, meshA.Id, meshA.Node?.Name ?? item1Name)
                    {
                        ModelName = modelName,
                        Layer = meshA.Node?.Parent?.Name,
                        Node = meshA.Node,
                        Mesh = meshA,
                        IfcGuid = NwcElementGuid.ForMesh(meshA, document),
                    };

                    var obj2 = new NwcClashObject(item2Name, meshB.Id, meshB.Node?.Name ?? item2Name)
                    {
                        ModelName = modelName,
                        Layer = meshB.Node?.Parent?.Name,
                        Node = meshB.Node,
                        Mesh = meshB,
                        IfcGuid = NwcElementGuid.ForMesh(meshB, document),
                    };

                    var unionBox = boxA.Union(boxB);
                    var vp = NwcSavedViewpoint.CreateDefault(unionBox, $"{clashName} View");

                    var result = new NwcClashResult(clashName, obj1, obj2, clashPoint, distance, NwcClashStatus.New)
                    {
                        Description = test.TestType switch
                        {
                            NwcClashTestType.Clearance => $"Clearance between {item1Name} and {item2Name}",
                            NwcClashTestType.Duplicates => $"Duplicate geometry: {item1Name} and {item2Name}",
                            NwcClashTestType.Conservative => $"Conservative overlap between {item1Name} and {item2Name}",
                            _ => $"Hard clash between {item1Name} and {item2Name}",
                        },
                        Viewpoint = vp,
                    };

                    if (test.Rules.Any(rule => rule.ShouldIgnore(result)))
                    {
                        continue;
                    }

                    test.AddResult(result);
                }
            }
        }
    }

    private static void RunCoreMultiModel(
        List<(NwcMesh Mesh, string ModelName, string? Layer, string ModelKey)> itemsA,
        List<(NwcMesh Mesh, string ModelName, string? Layer, string ModelKey)> itemsB,
        NwcClashTest test)
    {
        bool sameSelection = itemsA.Select(item => item.Mesh)
            .SequenceEqual(itemsB.Select(item => item.Mesh), ReferenceEqualityComparer.Instance);
        Dictionary<NwcMesh, int>? firstIndexA = null;
        HashSet<NwcMesh>? meshesInB = null;
        if (!sameSelection)
        {
            firstIndexA = new Dictionary<NwcMesh, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < itemsA.Count; i++)
            {
                firstIndexA.TryAdd(itemsA[i].Mesh, i);
            }
            meshesInB = new HashSet<NwcMesh>(itemsB.Select(item => item.Mesh), ReferenceEqualityComparer.Instance);
        }
        for (int i = 0; i < itemsA.Count; i++)
        {
            var itemA = itemsA[i];
            var boxA = itemA.Mesh.GetWorldBoundingBox();
            if (boxA.IsEmpty) continue;
            bool canVisitReverse = !sameSelection && meshesInB!.Contains(itemA.Mesh);

            for (var j = sameSelection ? i + 1 : 0; j < itemsB.Count; j++)
            {
                var itemB = itemsB[j];
                if (ReferenceEquals(itemA.Mesh, itemB.Mesh) || IsSameElement(itemA.Mesh, itemB.Mesh)) continue;
                // Mesh IDs can repeat across models; only references identify pairs.
                if (canVisitReverse && firstIndexA!.TryGetValue(itemB.Mesh, out int firstIndex) && firstIndex < i)
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

                if (TryGetClash(test, itemA.Mesh, itemB.Mesh, out var clashPoint, out double distance))
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
                        IfcGuid = NwcElementGuid.ForMesh(itemA.Mesh, itemA.ModelKey),
                    };

                    var obj2 = new NwcClashObject(name2, itemB.Mesh.Id, $"{itemB.ModelName}/{name2}")
                    {
                        ModelName = itemB.ModelName,
                        Layer = itemB.Layer,
                        Node = itemB.Mesh.Node,
                        Mesh = itemB.Mesh,
                        IfcGuid = NwcElementGuid.ForMesh(itemB.Mesh, itemB.ModelKey),
                    };

                    var unionBox = boxA.Union(boxB);
                    var vp = NwcSavedViewpoint.CreateDefault(unionBox, $"{clashName} View");

                    var result = new NwcClashResult(clashName, obj1, obj2, clashPoint, distance, NwcClashStatus.New)
                    {
                        Description = test.TestType switch
                        {
                            NwcClashTestType.Clearance => $"Clearance between {itemA.ModelName} ({name1}) and {itemB.ModelName} ({name2})",
                            NwcClashTestType.Duplicates => $"Duplicate geometry: {itemA.ModelName} ({name1}) and {itemB.ModelName} ({name2})",
                            NwcClashTestType.Conservative => $"Conservative overlap between {itemA.ModelName} ({name1}) and {itemB.ModelName} ({name2})",
                            _ => $"Clash between {itemA.ModelName} ({name1}) and {itemB.ModelName} ({name2})",
                        },
                        Viewpoint = vp,
                    };

                    if (test.Rules.Any(rule => rule.ShouldIgnore(result)))
                    {
                        continue;
                    }

                    test.AddResult(result);
                }
            }
        }
    }

    /// <summary>
    /// Applies the test's tolerance after the narrow phase. Hard tests keep intersections with penetration at
    /// or above the tolerance, plus zero-depth (coplanar) overlaps; Clearance tests
    /// report meshes whose shortest distance is at or below the tolerance.
    /// </summary>
    private static bool TryGetClash(
        NwcClashTest test,
        NwcMesh meshA,
        NwcMesh meshB,
        out Vector3d clashPoint,
        out double distance)
    {
        double tolerance = Math.Max(0.0, test.Tolerance);
        clashPoint = Vector3d.Zero;
        distance = 0;
        switch (test.TestType)
        {
            case NwcClashTestType.Clearance:
            {
                // MeasureMeshToMesh reports zero distance at the origin for meshes without triangles.
                if (meshA.Indices.Count < 3 || meshB.Indices.Count < 3)
                {
                    return false;
                }

                var measurement = NwcIO.Model.Measure.NwcMeasureEngine.MeasureMeshToMesh(meshA, meshB);
                clashPoint = (measurement.PointA + measurement.PointB) * 0.5;
                distance = measurement.Distance;
                return measurement.Distance <= tolerance;
            }

            case NwcClashTestType.Duplicates:
                if (!IsDuplicate(meshA, meshB, tolerance))
                {
                    distance = 0;
                    return false;
                }

                clashPoint = (meshA.GetWorldBoundingBox().Center + meshB.GetWorldBoundingBox().Center) * 0.5;
                distance = 0;
                return true;

            case NwcClashTestType.Conservative:
            {
                var overlap = meshA.GetWorldBoundingBox().Intersect(meshB.GetWorldBoundingBox());
                if (overlap.IsEmpty)
                {
                    distance = 0;
                    return false;
                }

                clashPoint = overlap.Center;
                distance = -Math.Min(overlap.Size.X, Math.Min(overlap.Size.Y, overlap.Size.Z));
                return true;
            }

            default:
                if (CheckMeshIntersection(meshA, meshB, out clashPoint, out double penetration))
                {
                    // A zero-depth AABB overlap is a coplanar or touching surface; it is kept rather than
                    // classified as a below-tolerance penetration.
                    if (penetration > 0 && penetration < tolerance)
                    {
                        distance = 0;
                        return false;
                    }

                    distance = -penetration;
                    return true;
                }

                distance = 0;
                return false;
        }
    }

    /// <summary>
    /// Duplicate geometry: same world vertices within the tolerance and the same triangle/line topology.
    /// Transform, name, id and material are ignored.
    /// </summary>
    internal static bool IsDuplicate(NwcMesh meshA, NwcMesh meshB, double tolerance)
    {
        if (ReferenceEquals(meshA, meshB))
        {
            return true;
        }

        if (meshA.Indices.Count != meshB.Indices.Count ||
            meshA.LineIndices.Count != meshB.LineIndices.Count)
        {
            return false;
        }

        var verticesA = meshA.GetWorldVertices();
        var verticesB = meshB.GetWorldVertices();
        if (verticesA.Count != verticesB.Count)
        {
            return false;
        }

        double toleranceSquared = tolerance * tolerance;
        for (int i = 0; i < verticesA.Count; i++)
        {
            Vector3d delta = verticesA[i] - verticesB[i];
            // Negated form so a NaN tolerance fails closed.
            if (!(Vector3d.Dot(delta, delta) <= toleranceSquared))
            {
                return false;
            }
        }

        for (int i = 0; i < meshA.Indices.Count; i++)
        {
            if (meshA.Indices[i] != meshB.Indices[i])
            {
                return false;
            }
        }

        for (int i = 0; i < meshA.LineIndices.Count; i++)
        {
            if (meshA.LineIndices[i] != meshB.LineIndices[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool CheckMeshIntersection(
        NwcMesh a,
        NwcMesh b,
        out Vector3d clashPoint,
        out double penetrationDepth)
    {
        clashPoint = Vector3d.Zero;
        penetrationDepth = 0.0;

        // A line mesh has no triangles; its points must not trigger the bounding-box fallback.
        if ((a.Indices.Count < 3 && a.LineIndices.Count > 0) || (b.Indices.Count < 3 && b.LineIndices.Count > 0))
        {
            return false;
        }

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

    // Fragments of one element are separate meshes and never clash with each other. The element key works without
    // the scene tree; the node rule covers documents built in code.
    internal static bool IsSameElement(NwcMesh a, NwcMesh b) =>
        (a.ElementKey is not null && ReferenceEquals(a.ElementKey, b.ElementKey)) ||
        (a.Node is not null && ReferenceEquals(a.Node, b.Node));

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
            // Node ids and mesh ids are separate number spaces; a mesh without a node keeps its mesh id.
            return document.Meshes.Where(m => m.Node is not null ? idSet.Contains(m.Node.Id) : idSet.Contains(m.Id)).ToList();
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
            // Only the named sets, even when they match nothing: a model without them adds no meshes
            return matchedMeshes;
        }

        return document.Meshes;
    }

    /// <summary>
    /// Throws when a set named by a selection is in none of the models, so a misspelled name can't run against
    /// nothing without notice. A set that exists in only some models of an assembly is fine.
    /// </summary>
    private static void ThrowIfSetIsMissing(IReadOnlyList<NwcDocument> documents, NwcClashTest test)
    {
        foreach (var selection in new[] { test.SelectionA, test.SelectionB })
        {
            if (selection.ExplicitNodeIds.Count > 0 || selection.FilterCondition is not null) continue;

            foreach (string name in selection.SetNames)
            {
                if (!documents.Any(d => d.FindSet(name) is not null))
                {
                    throw new ArgumentException(
                        $"Clash test '{test.Name}' selects the set '{name}', which no model contains.", nameof(test));
                }
            }
        }
    }
}

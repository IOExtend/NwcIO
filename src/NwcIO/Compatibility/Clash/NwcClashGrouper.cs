using System.Globalization;

namespace NwcIO.Model.Clash;

/// <summary>
/// Provides intelligent automated clash grouping and clustering strategies for Navisworks Clash Detective tests.
/// Enables grouping thousands of raw clashes by spatial proximity, element identity, grid lines, and disciplines.
/// </summary>
public static class NwcClashGrouper
{
    /// <summary>
    /// Groups clashes by spatial 3D proximity. Clashes within <paramref name="clusterDistance"/> of each other
    /// are clustered into unified <see cref="NwcClashGroup"/> containers.
    /// </summary>
    /// <param name="test">The clash test whose standalone results will be grouped.</param>
    /// <param name="clusterDistance">Maximum distance in document length units (see <see cref="NwcDocument.LengthUnit"/>) between two clashes to belong to the same cluster (default: 1.0).</param>
    /// <param name="groupPrefix">Name prefix for created clash groups (default: "Cluster").</param>
    /// <param name="minClusterSize">Minimum number of clashes required to form a group (default: 2).</param>
    /// <returns>The number of new clash groups created.</returns>
    public static int GroupByProximity(
        NwcClashTest test,
        double clusterDistance = 1.0,
        string groupPrefix = "Cluster",
        int minClusterSize = 2)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (test.Results.Count < minClusterSize || clusterDistance <= 0.0)
        {
            return 0;
        }

        var results = test.Results.ToList();
        int n = results.Count;
        double distSqThreshold = clusterDistance * clusterDistance;

        // Disjoint-set union (Union-Find)
        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int i, int j)
        {
            int rootI = Find(i);
            int rootJ = Find(j);
            if (rootI != rootJ)
            {
                parent[rootI] = rootJ;
            }
        }

        var cells = new Dictionary<(long X, long Y, long Z), List<int>>();
        var unindexed = new List<int>();
        // The extra half-cell leaves room for rounding at an exact radius or a cell boundary.
        double cellSize = 2 * clusterDistance;
        for (int i = 0; i < n; i++)
        {
            var p = results[i].ClashPoint;
            if (!double.IsNormal(distSqThreshold) ||
                !TryCell(p.X, out long x) || !TryCell(p.Y, out long y) || !TryCell(p.Z, out long z))
            {
                // Preserve the distance comparison for extreme quotients or a squared radius
                // that overflowed or underflowed.
                for (int j = 0; j < i; j++) JoinIfClose(i, j);
                unindexed.Add(i);
                continue;
            }

            foreach (int j in unindexed) JoinIfClose(i, j);
            for (long cz = z - 1; cz <= z + 1; cz++)
            {
                for (long cy = y - 1; cy <= y + 1; cy++)
                {
                    for (long cx = x - 1; cx <= x + 1; cx++)
                    {
                        if (!cells.TryGetValue((cx, cy, cz), out var neighbors)) continue;
                        foreach (int j in neighbors) JoinIfClose(i, j);
                    }
                }
            }

            if (!cells.TryGetValue((x, y, z), out var bucket)) cells.Add((x, y, z), bucket = []);
            bucket.Add(i);
        }

        bool TryCell(double coordinate, out long cell)
        {
            double scaled = coordinate / cellSize;
            // At larger quotients the cell division can lose precision even before flooring.
            const double maxExactCell = 1L << 50;
            cell = 0;
            if (!(Math.Abs(scaled) < maxExactCell)) return false;
            cell = (long)Math.Floor(scaled);
            return true;
        }

        void JoinIfClose(int i, int j)
        {
            if ((results[j].ClashPoint - results[i].ClashPoint).LengthSquared <= distSqThreshold)
            {
                Union(j, i);
            }
        }

        // Group indices by root
        var clusters = new Dictionary<int, List<NwcClashResult>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!clusters.TryGetValue(root, out var list))
            {
                list = [];
                clusters[root] = list;
            }
            list.Add(results[i]);
        }

        int groupsCreated = 0;
        var toRemoveFromResults = new HashSet<NwcClashResult>();

        foreach (var kvp in clusters)
        {
            var clusterList = kvp.Value;
            if (clusterList.Count >= minClusterSize)
            {
                groupsCreated++;
                string groupName = $"{groupPrefix} {test.Groups.Count + 1}";
                var group = new NwcClashGroup(groupName, clusterList[0].Status)
                {
                    Description = $"Spatial cluster of {clusterList.Count} clashes within {clusterDistance:F2} document units proximity."
                };

                foreach (var clash in clusterList)
                {
                    group.AddClash(clash);
                    toRemoveFromResults.Add(clash);
                }

                // Create composite camera viewpoint for the group
                group.Viewpoint = CreateGroupViewpoint(group);
                test.Groups.Add(group);
            }
        }

        test.Results.RemoveAll(toRemoveFromResults.Contains);
        return groupsCreated;
    }

    /// <summary>
    /// Groups clashes by primary element (Item1), bundling all collisions involving the same pipe, duct, or beam into one group.
    /// </summary>
    public static int GroupByPrimaryElement(
        NwcClashTest test,
        string groupPrefix = "Element",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (test.Results.Count < minGroupSize) return 0;

        var buckets = new Dictionary<string, List<NwcClashResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var clash in test.Results)
        {
            string key = !string.IsNullOrEmpty(clash.Item1.IfcGuid)
                ? clash.Item1.IfcGuid
                : (!string.IsNullOrEmpty(clash.Item1.Name) ? clash.Item1.Name : "Unknown");

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(clash);
        }

        return ApplyBuckets(test, buckets, groupPrefix, minGroupSize);
    }

    /// <summary>
    /// Groups clashes by grid location (e.g. "Grid A-1", "Grid B-3").
    /// </summary>
    public static int GroupByGrid(
        NwcClashTest test,
        string groupPrefix = "Grid",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (test.Results.Count < minGroupSize) return 0;

        var buckets = new Dictionary<string, List<NwcClashResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var clash in test.Results)
        {
            string key = !string.IsNullOrEmpty(clash.GridLocation) ? clash.GridLocation : "Unassigned";

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(clash);
        }

        return ApplyBuckets(test, buckets, groupPrefix, minGroupSize);
    }

    /// <summary>
    /// Groups clashes by layer or file of the participating items.
    /// </summary>
    public static int GroupByLayer(
        NwcClashTest test,
        string groupPrefix = "Layer",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (test.Results.Count < minGroupSize) return 0;

        var buckets = new Dictionary<string, List<NwcClashResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var clash in test.Results)
        {
            string l1 = clash.Item1.Layer ?? clash.Item1.ModelName ?? "Default";
            string l2 = clash.Item2.Layer ?? clash.Item2.ModelName ?? "Default";
            string key = $"{l1} vs {l2}";

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(clash);
        }

        return ApplyBuckets(test, buckets, groupPrefix, minGroupSize);
    }

    /// <summary>
    /// Dissolves all groups in the clash test back into individual standalone results.
    /// </summary>
    public static void UngroupAll(NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(test);

        foreach (var group in test.Groups)
        {
            test.Results.AddRange(group.Clashes);
        }

        test.Groups.Clear();
    }

    private static int ApplyBuckets(
        NwcClashTest test,
        Dictionary<string, List<NwcClashResult>> buckets,
        string groupPrefix,
        int minGroupSize)
    {
        int groupsCreated = 0;
        var toRemove = new HashSet<NwcClashResult>();

        foreach (var (key, list) in buckets)
        {
            if (list.Count >= minGroupSize)
            {
                groupsCreated++;
                string groupName = $"{groupPrefix}: {key}";
                var group = new NwcClashGroup(groupName, list[0].Status)
                {
                    Description = $"Group of {list.Count} clashes for '{key}'."
                };

                foreach (var clash in list)
                {
                    group.AddClash(clash);
                    toRemove.Add(clash);
                }

                group.Viewpoint = CreateGroupViewpoint(group);
                test.Groups.Add(group);
            }
        }

        test.Results.RemoveAll(toRemove.Contains);
        return groupsCreated;
    }

    private static NwcSavedViewpoint CreateGroupViewpoint(NwcClashGroup group)
    {
        var centroid = group.ClashPoint;

        // Compute group bounding box
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        foreach (var c in group.Clashes)
        {
            if (c.ClashPoint.X < minX) minX = c.ClashPoint.X;
            if (c.ClashPoint.Y < minY) minY = c.ClashPoint.Y;
            if (c.ClashPoint.Z < minZ) minZ = c.ClashPoint.Z;
            if (c.ClashPoint.X > maxX) maxX = c.ClashPoint.X;
            if (c.ClashPoint.Y > maxY) maxY = c.ClashPoint.Y;
            if (c.ClashPoint.Z > maxZ) maxZ = c.ClashPoint.Z;
        }

        double span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        double dist = Math.Max(span * 1.5, 3.0);

        var camPos = new Vector3d(
            centroid.X + dist * 0.7071,
            centroid.Y - dist * 0.7071,
            centroid.Z + dist * 0.5);

        return new NwcSavedViewpoint
        {
            Id = Guid.NewGuid(),
            Name = $"{group.Name} View",
            Camera = new NwcCamera
            {
                Position = camPos,
                Rotation = Quaterniond.Identity,
                FocalDistance = dist,
                FieldOfView = 45.0,
                Projection = NwcCameraProjection.Perspective,
            }
        };
    }
}

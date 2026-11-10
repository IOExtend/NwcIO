using NwcIO.Model.Sets;

namespace NwcIO.Model.TimeLiner;

/// <summary>
/// Provides automated rule-based linking of 4D TimeLiner tasks to Navisworks scene graph geometry,
/// Selection Sets, and Search Sets.
/// </summary>
public static class NwcTimeLinerLinker
{
    /// <summary>
    /// Automatically links schedule tasks to model geometry using matching Selection Set or Search Set names.
    /// </summary>
    /// <param name="schedule">The 4D TimeLiner schedule containing tasks to link.</param>
    /// <param name="sets">Available Selection Sets and Search Sets.</param>
    /// <param name="rootNode">Optional root of the scene graph to evaluate dynamic search criteria.</param>
    /// <returns>The number of tasks successfully linked to model elements.</returns>
    public static int AutoLink(
        NwcTimeLinerSchedule schedule,
        IEnumerable<NwcSetItem> sets,
        NwcNode? rootNode = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(sets);

        var setList = sets.ToList();
        var setMap = new Dictionary<string, NwcSetItem>(StringComparer.OrdinalIgnoreCase);

        void IndexSets(IEnumerable<NwcSetItem> items)
        {
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.Name) && !setMap.ContainsKey(item.Name))
                {
                    setMap[item.Name] = item;
                }
                if (item is NwcSelectionSetFolder folder)
                {
                    IndexSets(folder.Children);
                }
            }
        }
        IndexSets(setList);

        int linkedCount = 0;

        foreach (var task in schedule.FlattenTasks())
        {
            string? targetSetName = !string.IsNullOrEmpty(task.AttachedSelectionSetName)
                ? task.AttachedSelectionSetName
                : task.Name;

            if (setMap.TryGetValue(targetSetName, out var matchedSet))
            {
                task.AttachedSelectionSetName = matchedSet.Name;

                if (matchedSet is NwcSelectionSet explicitSet)
                {
                    foreach (int id in explicitSet.NodeIds)
                    {
                        if (!task.AttachedNodeIds.Contains(id))
                        {
                            task.AttachedNodeIds.Add(id);
                        }
                    }
                    if (explicitSet.NodeIds.Count > 0)
                    {
                        linkedCount++;
                    }
                }
                else if (matchedSet is NwcSearchSet searchSet && rootNode is not null)
                {
                    var matchedNodes = FlattenNodes(rootNode).Where(n => searchSet.Matches(n)).ToList();
                    foreach (var n in matchedNodes)
                    {
                        if (!task.AttachedNodeIds.Contains(n.Id))
                        {
                            task.AttachedNodeIds.Add(n.Id);
                        }
                    }
                    if (matchedNodes.Count > 0)
                    {
                        linkedCount++;
                    }
                }
            }
        }

        return linkedCount;
    }

    /// <summary>
    /// Automatically links schedule tasks to model geometry using matching Selection Set or Search Set names in a document.
    /// </summary>
    public static int AutoLink(
        NwcTimeLinerSchedule schedule,
        IEnumerable<NwcSetItem> sets,
        NwcDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return AutoLink(schedule, sets, document.RootNode);
    }

    /// <summary>
    /// Automatically links schedule tasks directly to scene graph nodes matching node name or class.
    /// </summary>
    public static int LinkByNodeName(
        NwcTimeLinerSchedule schedule,
        NwcNode rootNode,
        bool exactMatch = false)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(rootNode);

        var allNodes = FlattenNodes(rootNode).ToList();
        int linkedCount = 0;

        foreach (var task in schedule.FlattenTasks())
        {
            bool anyMatched = false;
            foreach (var node in allNodes)
            {
                bool match = exactMatch
                    ? string.Equals(node.Name, task.Name, StringComparison.OrdinalIgnoreCase)
                    : (node.Name?.Contains(task.Name, StringComparison.OrdinalIgnoreCase) == true);

                if (match && !task.AttachedNodeIds.Contains(node.Id))
                {
                    task.AttachedNodeIds.Add(node.Id);
                    anyMatched = true;
                }
            }

            if (anyMatched) linkedCount++;
        }

        return linkedCount;
    }

    private static IEnumerable<NwcNode> FlattenNodes(NwcNode node)
    {
        yield return node;
        foreach (var c in node.Children)
        {
            foreach (var descendant in FlattenNodes(c))
            {
                yield return descendant;
            }
        }
    }
}

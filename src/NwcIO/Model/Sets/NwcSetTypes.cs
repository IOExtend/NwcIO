using System.Globalization;

namespace NwcIO.Model.Sets;

/// <summary>
/// Comparison operator used in dynamic Navisworks search set criteria.
/// </summary>
public enum NwcSearchOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    Wildcard,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
}

/// <summary>
/// Base class for items in the Navisworks Sets hierarchy (selection sets, search sets, and folders).
/// </summary>
public abstract class NwcSetItem
{
    public string Name { get; set; } = string.Empty;
    public Guid SetGuid { get; set; } = Guid.NewGuid();
    public string? Comment { get; set; }
}

/// <summary>
/// A folder / grouping node in the Navisworks Sets hierarchy containing child sets or subfolders.
/// </summary>
public sealed class NwcSelectionSetFolder : NwcSetItem
{
    public List<NwcSetItem> Children { get; } = [];

    public NwcSelectionSetFolder() { }

    public NwcSelectionSetFolder(string name)
    {
        Name = name;
    }
}

/// <summary>
/// An explicit selection set holding a static collection of model nodes selected by ID or path.
/// </summary>
public sealed class NwcSelectionSet : NwcSetItem
{
    public List<int> NodeIds { get; } = [];
    public List<string> NodePaths { get; } = [];

    public NwcSelectionSet() { }

    public NwcSelectionSet(string name, IEnumerable<int>? nodeIds = null)
    {
        Name = name;
        if (nodeIds is not null)
        {
            NodeIds.AddRange(nodeIds);
        }
    }

    /// <summary>
    /// Resolves the actual <see cref="NwcNode"/> instances from the document scene tree matching this selection set.
    /// </summary>
    public IReadOnlyList<NwcNode> ResolveNodes(NwcDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.RootNode is null) return Array.Empty<NwcNode>();

        var idSet = new HashSet<int>(NodeIds);
        var pathSet = new HashSet<string>(NodePaths, StringComparer.OrdinalIgnoreCase);
        var result = new List<NwcNode>();

        void Traverse(NwcNode current, string currentPath)
        {
            string fullPath = string.IsNullOrEmpty(currentPath) ? current.Name : $"{currentPath}/{current.Name}";

            if (idSet.Contains(current.Id) || pathSet.Contains(fullPath))
            {
                result.Add(current);
            }

            for (int i = 0; i < current.Children.Count; i++)
            {
                Traverse(current.Children[i], fullPath);
            }
        }

        Traverse(document.RootNode, string.Empty);
        return result;
    }
}

/// <summary>
/// A single condition criterion within a dynamic Navisworks search set.
/// </summary>
public sealed class NwcSearchCondition
{
    public string Category { get; set; } = "Item";
    public string Property { get; set; } = "Name";
    public NwcSearchOperator Operator { get; set; } = NwcSearchOperator.Equals;
    public string Value { get; set; } = string.Empty;

    public NwcSearchCondition() { }

    public NwcSearchCondition(string category, string property, NwcSearchOperator op, string value)
    {
        Category = category;
        Property = property;
        Operator = op;
        Value = value;
    }

    /// <summary>
    /// Evaluates whether a given scene graph node satisfies this search condition.
    /// </summary>
    public bool Evaluate(NwcNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // 1. Built-in node properties
        if (Category.Equals("Item", StringComparison.OrdinalIgnoreCase))
        {
            if (Property.Equals("Name", StringComparison.OrdinalIgnoreCase) && MatchString(node.Name, Value, Operator))
            {
                return true;
            }
            if ((Property.Equals("Class", StringComparison.OrdinalIgnoreCase) || Property.Equals("Type", StringComparison.OrdinalIgnoreCase)) && MatchString(node.Class, Value, Operator))
            {
                return true;
            }
            if (Property.Equals("Id", StringComparison.OrdinalIgnoreCase) && MatchNumeric(node.Id, Value, Operator))
            {
                return true;
            }
            if (Property.Equals("Guid", StringComparison.OrdinalIgnoreCase) && node.ElementGuid.HasValue && MatchString(node.ElementGuid.Value.ToString(), Value, Operator))
            {
                return true;
            }
        }

        // 2. Attached PropertyRecords
        for (int i = 0; i < node.Properties.Count; i++)
        {
            var p = node.Properties[i];
            bool categoryMatch = string.IsNullOrEmpty(Category) ||
                                 Category.Equals("Item", StringComparison.OrdinalIgnoreCase) ||
                                 p.Class.Contains(Category, StringComparison.OrdinalIgnoreCase);

            if (categoryMatch && p.Name.Equals(Property, StringComparison.OrdinalIgnoreCase))
            {
                if (p.Value is not null && MatchString(p.Value, Value, Operator))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchString(string actual, string target, NwcSearchOperator op)
    {
        var inv = StringComparison.OrdinalIgnoreCase;
        return op switch
        {
            NwcSearchOperator.Equals => actual.Equals(target, inv),
            NwcSearchOperator.NotEquals => !actual.Equals(target, inv),
            NwcSearchOperator.Contains => actual.Contains(target, inv),
            NwcSearchOperator.StartsWith => actual.StartsWith(target, inv),
            NwcSearchOperator.EndsWith => actual.EndsWith(target, inv),
            NwcSearchOperator.Wildcard => MatchesWildcard(actual, target),
            NwcSearchOperator.GreaterThan or NwcSearchOperator.GreaterThanOrEqual or
            NwcSearchOperator.LessThan or NwcSearchOperator.LessThanOrEqual => MatchNumericString(actual, target, op),
            _ => actual.Equals(target, inv),
        };
    }

    private static bool MatchNumeric(double actual, string target, NwcSearchOperator op)
    {
        if (!double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out double targetVal))
        {
            return false;
        }

        return op switch
        {
            NwcSearchOperator.Equals => Math.Abs(actual - targetVal) < 1e-9,
            NwcSearchOperator.NotEquals => Math.Abs(actual - targetVal) >= 1e-9,
            NwcSearchOperator.GreaterThan => actual > targetVal,
            NwcSearchOperator.GreaterThanOrEqual => actual >= targetVal,
            NwcSearchOperator.LessThan => actual < targetVal,
            NwcSearchOperator.LessThanOrEqual => actual <= targetVal,
            _ => false,
        };
    }

    private static bool MatchNumericString(string actual, string target, NwcSearchOperator op)
    {
        if (double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
            double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
        {
            return MatchNumeric(a, target, op);
        }
        return false;
    }

    private static bool MatchesWildcard(string text, string pattern)
    {
        // Simple glob * and ? matching
        int pIdx = 0, tIdx = 0;
        int starIdx = -1, matchIdx = 0;

        while (tIdx < text.Length)
        {
            if (pIdx < pattern.Length && (pattern[pIdx] == '?' || char.ToLowerInvariant(pattern[pIdx]) == char.ToLowerInvariant(text[tIdx])))
            {
                pIdx++;
                tIdx++;
            }
            else if (pIdx < pattern.Length && pattern[pIdx] == '*')
            {
                starIdx = pIdx++;
                matchIdx = tIdx;
            }
            else if (starIdx != -1)
            {
                pIdx = starIdx + 1;
                tIdx = ++matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (pIdx < pattern.Length && pattern[pIdx] == '*')
        {
            pIdx++;
        }

        return pIdx == pattern.Length;
    }
}

/// <summary>
/// A dynamic search set that evaluates search conditions against scene graph nodes.
/// </summary>
public sealed class NwcSearchSet : NwcSetItem
{
    public List<NwcSearchCondition> Conditions { get; } = [];
    public bool MatchAllConditions { get; set; } = true;

    public NwcSearchSet() { }

    public NwcSearchSet(string name, IEnumerable<NwcSearchCondition>? conditions = null, bool matchAll = true)
    {
        Name = name;
        MatchAllConditions = matchAll;
        if (conditions is not null)
        {
            Conditions.AddRange(conditions);
        }
    }

    /// <summary>
    /// Evaluates this search set across all nodes in the document scene tree and returns matching nodes.
    /// </summary>
    public IReadOnlyList<NwcNode> Evaluate(NwcDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.RootNode is null) return Array.Empty<NwcNode>();

        var result = new List<NwcNode>();

        void Walk(NwcNode node)
        {
            if (Matches(node))
            {
                result.Add(node);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Walk(node.Children[i]);
            }
        }

        Walk(document.RootNode);
        return result;
    }

    /// <summary>
    /// Checks whether an individual node matches all or any of the search set conditions.
    /// </summary>
    public bool Matches(NwcNode node)
    {
        if (Conditions.Count == 0) return true;

        if (MatchAllConditions)
        {
            for (int i = 0; i < Conditions.Count; i++)
            {
                if (!Conditions[i].Evaluate(node)) return false;
            }
            return true;
        }

        for (int i = 0; i < Conditions.Count; i++)
        {
            if (Conditions[i].Evaluate(node)) return true;
        }
        return false;
    }
}

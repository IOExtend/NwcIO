namespace NwcIO.Model;

/// <summary>
/// One property tab (category) of a node read through the DbCache directory of a Navisworks model file. A tab object
/// shared by several nodes, or referenced twice by one node, is one instance.
/// </summary>
public sealed class NwcPropertyTab
{
    /// <summary>The stored tab name, else the display name of its username, else ""; for the element id (class 66), a one-property tab named by its key, the display name of the key.</summary>
    public required string Name { get; init; }

    /// <summary>The internal name of the tab's class-52 username, for example <c>LcRevitData_Element</c>; for the element id (class 66), the internal name of its key, <c>LcRevitId</c>.</summary>
    public string? InternalName { get; init; }

    /// <summary>The tab's records in stored order.</summary>
    public required IReadOnlyList<NwcPropertyRecord> Properties { get; init; }
}

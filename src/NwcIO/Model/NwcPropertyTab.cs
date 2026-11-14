namespace NwcIO.Model;

/// <summary>
/// One property tab (category) of a node read through the DbCache directory of a Navisworks model file. A tab object
/// shared by several nodes, or referenced twice by one node, is one instance.
/// </summary>
public sealed class NwcPropertyTab
{
    /// <summary>The stored tab name, else the display name of its username, else "".</summary>
    public required string Name { get; init; }

    /// <summary>The internal name of the tab's class-52 username, for example <c>LcRevitData_Element</c>.</summary>
    public string? InternalName { get; init; }

    /// <summary>The tab's records in stored order.</summary>
    public required IReadOnlyList<NwcPropertyRecord> Properties { get; init; }
}

namespace NwcIO.Model;

/// <summary>
/// One record of the scene-tree/property stream. Records carry an item name, a class
/// (e.g. "LcRevitData_Element" for items, "LcRevitPropertyElementName" for properties)
/// and optionally a typed value.
/// </summary>
public sealed class NwcPropertyRecord
{
    public required int FirstInt { get; init; }

    public required int Tag { get; init; }

    public required string Name { get; init; }

    public required string Class { get; init; }

    /// <summary>Value type marker stored in the record (4 = string observed so far).</summary>
    public int ValueType { get; init; }

    /// <summary>String value when <see cref="ValueType"/> is a known text type; otherwise null.</summary>
    public string? Value { get; init; }

    /// <summary>The internal name of the property tab on node properties of the strict path; null on scanned records.</summary>
    public string? TabInternalName { get; init; }

    /// <summary>The internal name of the property key on node properties of the strict path; null on scanned records.</summary>
    public string? InternalName { get; init; }
}

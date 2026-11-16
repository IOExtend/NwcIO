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

    /// <summary>Value type marker stored in the record (matching <see cref="NwcVariantDataType"/>).</summary>
    public int ValueType { get; init; }

    /// <summary>String value when <see cref="ValueType"/> is a known text type; otherwise null.</summary>
    public string? Value { get; init; }

    /// <summary>The internal name of the property tab on node properties of the strict path; null on scanned records.</summary>
    public string? TabInternalName { get; init; }

    /// <summary>The internal name of the property key on node properties of the strict path; null on scanned records.</summary>
    public string? InternalName { get; init; }

    /// <summary>
    /// Parent node id for records written from an <see cref="NwcNode"/> tree by <c>NwcWriter</c>; null for records scanned
    /// from a stream, whose hierarchy is derived from collection names and record order.
    /// </summary>
    public int? ParentId { get; init; }
}

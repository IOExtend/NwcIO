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

    /// <summary>Value type marker stored in the record: 0/2/3 int, 1/6/7/10/11 double, 4/9 string, 5 DateTime, 8 reference, 12 Point3D, 13 Point2D.</summary>
    public int ValueType { get; init; }

    /// <summary>String value when <see cref="ValueType"/> is a known text type; otherwise null.</summary>
    public string? Value { get; init; }

    /// <summary>Typed integer value for types 0, 2, 3 and 5; null otherwise.</summary>
    public long? IntValue { get; init; }

    /// <summary>Typed floating-point value for types 1, 6, 7, 10 and 11; null otherwise.</summary>
    public double? DoubleValue { get; init; }

    /// <summary>Referenced id for type 8 records; null otherwise.</summary>
    public int? ReferenceId { get; init; }

    /// <summary>Referenced tag for type 8 records; null otherwise.</summary>
    public int? ReferenceTag { get; init; }

    /// <summary>Referenced class for type 8 records; null otherwise.</summary>
    public string? ReferenceClass { get; init; }

    /// <summary>The internal name of the property tab on node properties of the strict path; null on scanned records.</summary>
    public string? TabInternalName { get; init; }

    /// <summary>The internal name of the property key on node properties of the strict path; null on scanned records.</summary>
    public string? InternalName { get; init; }
}

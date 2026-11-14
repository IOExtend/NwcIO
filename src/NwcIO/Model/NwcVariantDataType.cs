using System.Diagnostics.CodeAnalysis;

namespace NwcIO.Model;

/// <summary>
/// Data type tags for Navisworks property values (<c>VariantDataType</c>).
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names", Justification = "Matches official Navisworks VariantDataType enum names")]
public enum NwcVariantDataType
{
    /// <summary>Unspecified or empty value.</summary>
    None = 0,

    /// <summary>Double-precision IEEE 754 floating point number.</summary>
    Double = 1,

    /// <summary>32-bit signed integer.</summary>
    Int32 = 2,

    /// <summary>Boolean flag (0 = false, non-zero = true).</summary>
    Boolean = 3,

    /// <summary>Display text string (UTF-8 or UTF-16).</summary>
    DisplayString = 4,

    /// <summary>Date and time stored as 64-bit Unix epoch seconds.</summary>
    DateTime = 5,

    /// <summary>Distance or length quantity in file linear units (double).</summary>
    DoubleLength = 6,

    /// <summary>Angular quantity in radians (double).</summary>
    DoubleAngle = 7,

    /// <summary>Named constant referencing user and internal names.</summary>
    NamedConstant = 8,

    /// <summary>Internal identifier string.</summary>
    IdentifierString = 9,

    /// <summary>Area quantity in square file units (double).</summary>
    DoubleArea = 10,

    /// <summary>Volume quantity in cubic file units (double).</summary>
    DoubleVolume = 11,

    /// <summary>3D point or coordinate vector (three doubles: X, Y, Z).</summary>
    Point3D = 12,

    /// <summary>2D point or UV coordinate (two doubles: X, Y).</summary>
    Point2D = 13,
}

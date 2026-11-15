namespace NwcIO;

/// <summary>The length unit of a document's coordinates and length values.</summary>
public enum NwcLengthUnit
{
    /// <summary>The unit is not known; no file names its unit.</summary>
    Unknown = 0,

    /// <summary>International foot, 0.3048 m. Feet when the cache was written by the Revit loader <c>LcNwcLoaderPlugin:lcldrevit</c> or the Revit NWC exporter <c>nwexportrevit</c>; no file names its unit.</summary>
    Feet = 1,

    /// <summary>Meter (SI base unit), 1.0 m.</summary>
    Meters = 2,

    /// <summary>Centimeter, 0.01 m.</summary>
    Centimeters = 3,

    /// <summary>Millimeter, 0.001 m.</summary>
    Millimeters = 4,

    /// <summary>Inch, 0.0254 m.</summary>
    Inches = 5,

    /// <summary>Yard, 0.9144 m.</summary>
    Yards = 6,

    /// <summary>Kilometer, 1000.0 m.</summary>
    Kilometers = 7,

    /// <summary>Mile, 1609.344 m.</summary>
    Miles = 8,

    /// <summary>Micrometer, 1e-6 m.</summary>
    Micrometers = 9,

    /// <summary>Mil (thou), 2.54e-5 m.</summary>
    Mils = 10,

    /// <summary>Microinch, 2.54e-8 m.</summary>
    Microinches = 11,
}

/// <summary>Extension methods for <see cref="NwcLengthUnit"/> conversions and display symbols.</summary>
public static class NwcLengthUnitExtensions
{
    /// <summary>Gets the exact conversion factor in metres per unit, or null if unknown.</summary>
    public static double? GetMetresPerUnit(this NwcLengthUnit unit) => unit switch
    {
        NwcLengthUnit.Feet => 0.3048,
        NwcLengthUnit.Meters => 1.0,
        NwcLengthUnit.Centimeters => 0.01,
        NwcLengthUnit.Millimeters => 0.001,
        NwcLengthUnit.Inches => 0.0254,
        NwcLengthUnit.Yards => 0.9144,
        NwcLengthUnit.Kilometers => 1000.0,
        NwcLengthUnit.Miles => 1609.344,
        NwcLengthUnit.Micrometers => 1e-6,
        NwcLengthUnit.Mils => 0.0254 / 1000.0,
        NwcLengthUnit.Microinches => 0.0254 / 1_000_000.0,
        _ => null,
    };

    /// <summary>Gets the standard unit symbol (e.g. "ft", "m", "mm"), or null if unknown.</summary>
    public static string? GetSymbol(this NwcLengthUnit unit) => unit switch
    {
        NwcLengthUnit.Feet => "ft",
        NwcLengthUnit.Meters => "m",
        NwcLengthUnit.Centimeters => "cm",
        NwcLengthUnit.Millimeters => "mm",
        NwcLengthUnit.Inches => "in",
        NwcLengthUnit.Yards => "yd",
        NwcLengthUnit.Kilometers => "km",
        NwcLengthUnit.Miles => "mi",
        NwcLengthUnit.Micrometers => "µm",
        NwcLengthUnit.Mils => "mil",
        NwcLengthUnit.Microinches => "µin",
        _ => null,
    };
}

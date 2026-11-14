namespace NwcIO;

/// <summary>The length unit of a document's coordinates and length values.</summary>
public enum NwcLengthUnit
{
    /// <summary>The unit is not known; no file names its unit.</summary>
    Unknown = 0,

    /// <summary>International foot, 0.3048 m. Feet when the cache was written by the Revit loader <c>LcNwcLoaderPlugin:lcldrevit</c>; no file names its unit.</summary>
    Feet = 1,
}

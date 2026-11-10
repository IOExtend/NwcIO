using NwcIO.Model.Sectioning;

namespace NwcIO;

/// <summary>
/// Supported IFC schema specifications for export.
/// </summary>
public enum NwcIfcSchemaVersion
{
    /// <summary>
    /// IFC2X3 Coordination View V2.0 (broadest compatibility with CAD/BIM tools).
    /// </summary>
    Ifc2x3,

    /// <summary>
    /// IFC4 Design Transfer View / Reference View (modern compact tessellation via IfcTriangulatedFaceSet).
    /// </summary>
    Ifc4,
}

/// <summary>
/// Length units supported for IFC export.
/// </summary>
public enum NwcIfcLengthUnit
{
    /// <summary>
    /// Length unit is millimetres (IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)).
    /// Default for Navisworks and Revit metric models.
    /// </summary>
    Millimetre,

    /// <summary>
    /// Length unit is metres (IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)).
    /// Vertices are scaled by 0.001 if source is in millimetres.
    /// </summary>
    Metre,
}

/// <summary>
/// Options controlling IFC (Industry Foundation Classes) export.
/// Supports IFC2x3 Coordination View V2.0 and IFC4 (ISO 10303-21 STEP physical file format).
/// </summary>
public sealed class NwcIfcExportOptions
{
    /// <summary>Default export configuration.</summary>
    public static NwcIfcExportOptions Default { get; } = new();

    /// <summary>Target IFC schema version (default: Ifc2x3).</summary>
    public NwcIfcSchemaVersion SchemaVersion { get; set; } = NwcIfcSchemaVersion.Ifc2x3;

    /// <summary>IFC Schema version string (e.g. "IFC2X3" or "IFC4").</summary>
    public string Schema
    {
        get => SchemaVersion == NwcIfcSchemaVersion.Ifc4 ? "IFC4" : "IFC2X3";
        set => SchemaVersion = value?.Equals("IFC4", StringComparison.OrdinalIgnoreCase) == true
            ? NwcIfcSchemaVersion.Ifc4
            : NwcIfcSchemaVersion.Ifc2x3;
    }

    /// <summary>Length unit for coordinates in the IFC model (default: Millimetre).</summary>
    public NwcIfcLengthUnit LengthUnit { get; set; } = NwcIfcLengthUnit.Millimetre;

    /// <summary>Convert millimetre source coordinates to metres when <see cref="LengthUnit"/> is Metre (default: true).</summary>
    public bool ConvertMmToMetres { get; set; } = true;

    /// <summary>Export surface materials and RGB colors as IfcSurfaceStyle / IfcStyledItem (default: true).</summary>
    public bool ExportMaterials { get; set; } = true;

    /// <summary>Export properties and metadata as IfcPropertySet and IfcPropertySingleValue (default: true).</summary>
    public bool ExportProperties { get; set; } = true;

    /// <summary>Export type objects (IfcTypeProduct, e.g. IfcWallType) and link via IfcRelDefinesByType (default: true).</summary>
    public bool ExportTypeObjects { get; set; } = true;

    /// <summary>When targeting IFC4, use compact IfcTriangulatedFaceSet instead of heavy IfcFacetedBrep (default: true).</summary>
    public bool UseTessellationInIfc4 { get; set; } = true;

    /// <summary>Export geometry in world coordinates applying 3D transformations (default: true).</summary>
    public bool WorldCoordinates { get; set; } = true;

    /// <summary>Share IfcCartesianPoint instances per mesh using mesh index triplets to minimize file size in IFC2x3 (default: true).</summary>
    public bool SharedVertices { get; set; } = true;

    /// <summary>Custom project name for IfcProject (defaults to document or assembly name).</summary>
    public string? ProjectName { get; set; }

    /// <summary>Custom building name for IfcBuilding (default: "Default Building").</summary>
    public string? BuildingName { get; set; }

    /// <summary>Custom storey name for IfcBuildingStorey (default: "Level 1").</summary>
    public string? StoreyName { get; set; }

    /// <summary>Author string for the STEP header (default: "mahach666").</summary>
    public string Author { get; set; } = "mahach666";

    /// <summary>Organization string for the STEP header (default: "NwcIO").</summary>
    public string Organization { get; set; } = "NwcIO";

    /// <summary>Application name for the STEP header (default: "NwcIO").</summary>
    public string Application { get; set; } = "NwcIO";

    /// <summary>Optional 3D section box to crop exported IFC geometry.</summary>
    public NwcSectionBox? SectionBox { get; set; }

    /// <summary>Optional active clipping section planes to crop exported IFC geometry.</summary>
    public IReadOnlyList<NwcSectionPlane>? SectionPlanes { get; set; }

    /// <summary>Whether to clip triangle geometry against active section planes/box (default: true).</summary>
    public bool ClipGeometryWithSectionPlanes { get; set; } = true;
}

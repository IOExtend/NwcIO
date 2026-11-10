using System.Globalization;
using System.Text;
using NwcIO.Model;
using NwcIO.Model.Sectioning;

namespace NwcIO.Internal.Export;

/// <summary>
/// Exports Navisworks models (<see cref="NwcDocument"/>) and federated assemblies (<see cref="NwcAssembly"/>)
/// to Industry Foundation Classes (IFC2x3 Coordination View V2.0) in ISO 10303-21 STEP physical file format.
/// </summary>
internal static class IfcExporter
{
    private const string DegreeInRadians = "0.017453292519943278";

    /// <summary>Writes a single <see cref="NwcDocument"/> to an IFC file.</summary>
    public static void WriteFile(NwcDocument doc, string path, NwcIfcExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(path);

        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8, bufferSize: 65536);
        Write(doc, writer, options, Path.GetFileName(path));
    }

    /// <summary>Writes a single <see cref="NwcDocument"/> to a <see cref="TextWriter"/>.</summary>
    public static void Write(
        NwcDocument doc,
        TextWriter writer,
        NwcIfcExportOptions? options = null,
        string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(writer);

        options ??= NwcIfcExportOptions.Default;
        fileName ??= Path.GetFileName(doc.FileInfo.SourcePath) ?? "model.ifc";

        var w = new StepWriter();

        // 1. Project infrastructure & Owner History
        int personId = w.Add("IFCPERSON", $"$,{StepWriter.Str(options.Author)},$,$,$,$,$,$");
        int orgId = w.Add("IFCORGANIZATION", $"$,{StepWriter.Str(options.Organization)},$,$,$");
        int personOrgId = w.Add("IFCPERSONANDORGANIZATION", $"{StepWriter.Ref(personId)},{StepWriter.Ref(orgId)},$");
        int appId = w.Add("IFCAPPLICATION", $"{StepWriter.Ref(orgId)},'0.1.0',{StepWriter.Str(options.Application)},'NwcIO'");

        int epochSeconds = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        int ownerHistoryId = w.Add("IFCOWNERHISTORY",
            $"{StepWriter.Ref(personOrgId)},{StepWriter.Ref(appId)},.READWRITE.,.NOCHANGE.,$,$,$,{epochSeconds}");

        // 2. Units & Geometric Context
        int lengthUnitId = options.LengthUnit == NwcIfcLengthUnit.Millimetre
            ? w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,.MILLI.,.METRE.")
            : w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,$,.METRE.");

        int radianId = w.Add("IFCSIUNIT", "*,.PLANEANGLEUNIT.,$,.RADIAN.");
        int dimExpId = w.Add("IFCDIMENSIONALEXPONENTS", "0,0,0,0,0,0,0");
        int angleMeasureId = w.Add("IFCMEASUREWITHUNIT", $"IFCPLANEANGLEMEASURE({DegreeInRadians}),{StepWriter.Ref(radianId)}");
        int degreeUnitId = w.Add("IFCCONVERSIONBASEDUNIT", $"{StepWriter.Ref(dimExpId)},.PLANEANGLEUNIT.,'DEGREE',{StepWriter.Ref(angleMeasureId)}");

        int areaUnitId = w.Add("IFCSIUNIT", "*,.AREAUNIT.,$,.SQUARE_METRE.");
        int volumeUnitId = w.Add("IFCSIUNIT", "*,.VOLUMEUNIT.,$,.CUBIC_METRE.");

        int unitAssignmentId = w.Add("IFCUNITASSIGNMENT",
            StepWriter.List(StepWriter.Ref(lengthUnitId), StepWriter.Ref(degreeUnitId), StepWriter.Ref(areaUnitId), StepWriter.Ref(volumeUnitId)));

        int originPointId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(0), StepWriter.Real(0), StepWriter.Real(0)));
        int axisZId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(0), StepWriter.Real(0), StepWriter.Real(1)));
        int axisXId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(1), StepWriter.Real(0), StepWriter.Real(0)));
        int worldPlacement3dId = w.Add("IFCAXIS2PLACEMENT3D",
            $"{StepWriter.Ref(originPointId)},{StepWriter.Ref(axisZId)},{StepWriter.Ref(axisXId)}");

        int mainContextId = w.Add("IFCGEOMETRICREPRESENTATIONCONTEXT",
            $"$,'Model',3,1.E-05,{StepWriter.Ref(worldPlacement3dId)},$");
        int bodySubContextId = w.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
            $"'Body','Model',*,*,*,*,{StepWriter.Ref(mainContextId)},$,.MODEL_VIEW.,$");

        // 3. Spatial Hierarchy: Project -> Site -> Building -> Storey
        string projectName = options.ProjectName
            ?? Path.GetFileNameWithoutExtension(doc.FileInfo.SourcePath)
            ?? "NwcIO Project";
        string buildingName = options.BuildingName ?? "Default Building";
        string storeyName = options.StoreyName ?? "Level 1";

        int projectId = w.Add("IFCPROJECT",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(projectName)},$,$,$,$,({StepWriter.Ref(mainContextId)}),{StepWriter.Ref(unitAssignmentId)}");

        int sitePlacementId = w.Add("IFCLOCALPLACEMENT", $"$,{StepWriter.Ref(worldPlacement3dId)}");
        int siteId = w.Add("IFCSITE",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},'Site',$,$,{StepWriter.Ref(sitePlacementId)},$,$,.ELEMENT.,$,$,$,$,$");

        int buildingPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(sitePlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int buildingId = w.Add("IFCBUILDING",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(buildingName)},$,$,{StepWriter.Ref(buildingPlacementId)},$,$,.ELEMENT.,$,$,$,$");

        int storeyPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(buildingPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int storeyId = w.Add("IFCBUILDINGSTOREY",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(storeyName)},$,$,{StepWriter.Ref(storeyPlacementId)},$,$,.ELEMENT.,{StepWriter.Real(0)}");

        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(projectId)},({StepWriter.Ref(siteId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(siteId)},({StepWriter.Ref(buildingId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(buildingId)},({StepWriter.Ref(storeyId)})");

        // 4. Materials & Styles Cache
        var styleCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (options.ExportMaterials)
        {
            foreach (var mat in doc.Materials)
            {
                GetOrCreateSurfaceStyle(w, mat, styleCache);
            }
        }

        // 5. Export Meshes as IFC Products
        bool isIfc4 = options.SchemaVersion == NwcIfcSchemaVersion.Ifc4;
        bool useTessellation = isIfc4 && options.UseTessellationInIfc4;
        string schemaName = isIfc4 ? "IFC4" : "IFC2X3";
        string viewDef = isIfc4 ? "ViewDefinition [DesignTransferView_1.0]" : "ViewDefinition [CoordinationView_V2.0]";

        double scale = (options.LengthUnit == NwcIfcLengthUnit.Metre && options.ConvertMmToMetres) ? 0.001 : 1.0;
        var elementIds = new List<int>(doc.Meshes.Count);

        var typeMap = new Dictionary<string, (int TypeId, List<int> ElementIds)>(StringComparer.OrdinalIgnoreCase);
        int typeIdSeed = 1000;

        var activePlanes = new List<NwcSectionPlane>();
        if (options.SectionPlanes is not null)
        {
            activePlanes.AddRange(options.SectionPlanes.Where(p => p.IsEnabled));
        }
        if (options.SectionBox is not null && options.SectionBox.IsEnabled)
        {
            activePlanes.AddRange(options.SectionBox.ToSectionPlanes());
        }

        for (int m = 0; m < doc.Meshes.Count; m++)
        {
            var mesh = doc.Meshes[m];
            if (activePlanes.Count > 0 && options.ClipGeometryWithSectionPlanes)
            {
                mesh = NwcMeshClipper.Clip(mesh, activePlanes);
            }

            var vertices = options.WorldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
            if (vertices.Count < 3 || mesh.Indices.Count < 3)
            {
                continue;
            }

            // Material style
            int? styleId = null;
            if (options.ExportMaterials && mesh.Material is not null)
            {
                styleId = GetOrCreateSurfaceStyle(w, mesh.Material, styleCache);
            }

            // Geometry: Tessellation for IFC4, Brep for IFC2x3
            int geomId;
            string repType;
            if (useTessellation)
            {
                geomId = WriteMeshTriangulatedFaceSet(w, vertices, mesh.Indices, scale);
                repType = "Tessellation";
            }
            else
            {
                geomId = WriteMeshFacetedBrep(w, vertices, mesh.Indices, scale, options.SharedVertices);
                repType = "Brep";
            }

            if (styleId.HasValue)
            {
                w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(geomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(mesh.Material?.Name)}");
            }

            int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},({StepWriter.Ref(geomId)})");
            int prodShapeId = w.Add("IFCPRODUCTDEFINITIONSHAPE",
                $"$,$,({StepWriter.Ref(shapeRepId)})");

            int elemPlacementId = w.Add("IFCLOCALPLACEMENT",
                $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

            string elemName = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{mesh.Id}" : mesh.Name;
            string elemGuid = IfcGuid.FromElementId(mesh.Id, "NwcMesh");
            string ifcType = ClassifyElement(elemName, mesh.Material?.Name);

            int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, elemName, elemPlacementId, prodShapeId, mesh.Id.ToString(CultureInfo.InvariantCulture));
            elementIds.Add(elemId);

            // Type assignment
            if (options.ExportTypeObjects)
            {
                string typeName = CleanTypeName(elemName, mesh.Material?.Name, ifcType);
                string typeKey = $"{ifcType}::{typeName}";
                if (!typeMap.TryGetValue(typeKey, out var entry))
                {
                    string typeGuid = IfcGuid.FromElementId(typeIdSeed++, "IfcType");
                    int typeId = AddIfcTypeProduct(w, ifcType, typeGuid, ownerHistoryId, typeName, isIfc4);
                    entry = (typeId, new List<int>());
                    typeMap[typeKey] = entry;
                }
                entry.ElementIds.Add(elemId);
            }

            // Export properties if requested
            if (options.ExportProperties)
            {
                WriteMeshProperties(w, elemId, mesh, ownerHistoryId);
            }
        }

        // 6. Type Definitions
        if (options.ExportTypeObjects)
        {
            foreach (var (_, (typeId, elemIds)) in typeMap)
            {
                if (elemIds.Count > 0)
                {
                    w.Add("IFCRELDEFINESBYTYPE",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elemIds.Select(StepWriter.Ref))},{StepWriter.Ref(typeId)}");
                }
            }
        }

        // 7. Spatial Containment
        if (elementIds.Count > 0)
        {
            w.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elementIds.Select(StepWriter.Ref))},{StepWriter.Ref(storeyId)}");
        }

        // 8. Write complete STEP physical file
        w.WriteTo(writer, fileName, schemaName, viewDef, options.Application, options.Organization);
    }

    /// <summary>Writes a federated <see cref="NwcAssembly"/> to an IFC file.</summary>
    public static void WriteAssembly(NwcAssembly assembly, string path, NwcIfcExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(path);

        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8, bufferSize: 65536);
        WriteAssembly(assembly, writer, options, Path.GetFileName(path));
    }

    /// <summary>Writes a federated <see cref="NwcAssembly"/> to a <see cref="TextWriter"/>.</summary>
    public static void WriteAssembly(
        NwcAssembly assembly,
        TextWriter writer,
        NwcIfcExportOptions? options = null,
        string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(writer);

        options ??= NwcIfcExportOptions.Default;
        fileName ??= $"{assembly.Name}.ifc";

        var w = new StepWriter();

        // 1. Project infrastructure & Owner History
        int personId = w.Add("IFCPERSON", $"$,{StepWriter.Str(options.Author)},$,$,$,$,$,$");
        int orgId = w.Add("IFCORGANIZATION", $"$,{StepWriter.Str(options.Organization)},$,$,$");
        int personOrgId = w.Add("IFCPERSONANDORGANIZATION", $"{StepWriter.Ref(personId)},{StepWriter.Ref(orgId)},$");
        int appId = w.Add("IFCAPPLICATION", $"{StepWriter.Ref(orgId)},'0.1.0',{StepWriter.Str(options.Application)},'NwcIO'");

        int epochSeconds = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        int ownerHistoryId = w.Add("IFCOWNERHISTORY",
            $"{StepWriter.Ref(personOrgId)},{StepWriter.Ref(appId)},.READWRITE.,.NOCHANGE.,$,$,$,{epochSeconds}");

        // 2. Units & Geometric Context
        int lengthUnitId = options.LengthUnit == NwcIfcLengthUnit.Millimetre
            ? w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,.MILLI.,.METRE.")
            : w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,$,.METRE.");

        int radianId = w.Add("IFCSIUNIT", "*,.PLANEANGLEUNIT.,$,.RADIAN.");
        int dimExpId = w.Add("IFCDIMENSIONALEXPONENTS", "0,0,0,0,0,0,0");
        int angleMeasureId = w.Add("IFCMEASUREWITHUNIT", $"IFCPLANEANGLEMEASURE({DegreeInRadians}),{StepWriter.Ref(radianId)}");
        int degreeUnitId = w.Add("IFCCONVERSIONBASEDUNIT", $"{StepWriter.Ref(dimExpId)},.PLANEANGLEUNIT.,'DEGREE',{StepWriter.Ref(angleMeasureId)}");

        int areaUnitId = w.Add("IFCSIUNIT", "*,.AREAUNIT.,$,.SQUARE_METRE.");
        int volumeUnitId = w.Add("IFCSIUNIT", "*,.VOLUMEUNIT.,$,.CUBIC_METRE.");

        int unitAssignmentId = w.Add("IFCUNITASSIGNMENT",
            StepWriter.List(StepWriter.Ref(lengthUnitId), StepWriter.Ref(degreeUnitId), StepWriter.Ref(areaUnitId), StepWriter.Ref(volumeUnitId)));

        int originPointId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(0), StepWriter.Real(0), StepWriter.Real(0)));
        int axisZId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(0), StepWriter.Real(0), StepWriter.Real(1)));
        int axisXId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(1), StepWriter.Real(0), StepWriter.Real(0)));
        int worldPlacement3dId = w.Add("IFCAXIS2PLACEMENT3D",
            $"{StepWriter.Ref(originPointId)},{StepWriter.Ref(axisZId)},{StepWriter.Ref(axisXId)}");

        int mainContextId = w.Add("IFCGEOMETRICREPRESENTATIONCONTEXT",
            $"$,'Model',3,1.E-05,{StepWriter.Ref(worldPlacement3dId)},$");
        int bodySubContextId = w.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
            $"'Body','Model',*,*,*,*,{StepWriter.Ref(mainContextId)},$,.MODEL_VIEW.,$");

        // 3. Project & Building
        string projectName = options.ProjectName ?? assembly.Name;
        string buildingName = options.BuildingName ?? assembly.Name;

        int projectId = w.Add("IFCPROJECT",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(projectName)},$,$,$,$,({StepWriter.Ref(mainContextId)}),{StepWriter.Ref(unitAssignmentId)}");

        int sitePlacementId = w.Add("IFCLOCALPLACEMENT", $"$,{StepWriter.Ref(worldPlacement3dId)}");
        int siteId = w.Add("IFCSITE",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},'Site',$,$,{StepWriter.Ref(sitePlacementId)},$,$,.ELEMENT.,$,$,$,$,$");

        int buildingPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(sitePlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int buildingId = w.Add("IFCBUILDING",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(buildingName)},$,$,{StepWriter.Ref(buildingPlacementId)},$,$,.ELEMENT.,$,$,$,$");

        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(projectId)},({StepWriter.Ref(siteId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(siteId)},({StepWriter.Ref(buildingId)})");

        // 4. Materials Cache
        var styleCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (options.ExportMaterials)
        {
            foreach (var mat in assembly.AllMaterials)
            {
                GetOrCreateSurfaceStyle(w, mat, styleCache);
            }
        }

        // 5. Storeys per Model or Global Storey
        bool isIfc4 = options.SchemaVersion == NwcIfcSchemaVersion.Ifc4;
        bool useTessellation = isIfc4 && options.UseTessellationInIfc4;
        string schemaName = isIfc4 ? "IFC4" : "IFC2X3";
        string viewDef = isIfc4 ? "ViewDefinition [DesignTransferView_1.0]" : "ViewDefinition [CoordinationView_V2.0]";

        double scale = (options.LengthUnit == NwcIfcLengthUnit.Metre && options.ConvertMmToMetres) ? 0.001 : 1.0;
        var storeyIds = new List<int>();

        var typeMap = new Dictionary<string, (int TypeId, List<int> ElementIds)>(StringComparer.OrdinalIgnoreCase);
        int typeIdSeed = 5000;

        for (int modelIdx = 0; modelIdx < assembly.Models.Count; modelIdx++)
        {
            var model = assembly.Models[modelIdx];
            string subStoreyName = !string.IsNullOrEmpty(model.FileInfo.SourcePath)
                ? Path.GetFileNameWithoutExtension(model.FileInfo.SourcePath)
                : $"Model_{modelIdx + 1}";

            int storeyPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(buildingPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
            int storeyId = w.Add("IFCBUILDINGSTOREY",
                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(subStoreyName)},$,$,{StepWriter.Ref(storeyPlacementId)},$,$,.ELEMENT.,{StepWriter.Real(0)}");
            storeyIds.Add(storeyId);

            var modelElementIds = new List<int>(model.Meshes.Count);

            var activePlanes = new List<NwcSectionPlane>();
            if (options.SectionPlanes is not null)
            {
                activePlanes.AddRange(options.SectionPlanes.Where(p => p.IsEnabled));
            }
            if (options.SectionBox is not null && options.SectionBox.IsEnabled)
            {
                activePlanes.AddRange(options.SectionBox.ToSectionPlanes());
            }

            for (int m = 0; m < model.Meshes.Count; m++)
            {
                var mesh = model.Meshes[m];
                if (activePlanes.Count > 0 && options.ClipGeometryWithSectionPlanes)
                {
                    mesh = NwcMeshClipper.Clip(mesh, activePlanes);
                }

                var vertices = options.WorldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
                if (vertices.Count < 3 || mesh.Indices.Count < 3)
                {
                    continue;
                }

                int? styleId = null;
                if (options.ExportMaterials && mesh.Material is not null)
                {
                    styleId = GetOrCreateSurfaceStyle(w, mesh.Material, styleCache);
                }

                int geomId;
                string repType;
                if (useTessellation)
                {
                    geomId = WriteMeshTriangulatedFaceSet(w, vertices, mesh.Indices, scale);
                    repType = "Tessellation";
                }
                else
                {
                    geomId = WriteMeshFacetedBrep(w, vertices, mesh.Indices, scale, options.SharedVertices);
                    repType = "Brep";
                }

                if (styleId.HasValue)
                {
                    w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(geomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(mesh.Material?.Name)}");
                }

                int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                    $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},({StepWriter.Ref(geomId)})");
                int prodShapeId = w.Add("IFCPRODUCTDEFINITIONSHAPE",
                    $"$,$,({StepWriter.Ref(shapeRepId)})");

                int elemPlacementId = w.Add("IFCLOCALPLACEMENT",
                    $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

                string elemName = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{mesh.Id}" : mesh.Name;
                string elemGuid = IfcGuid.FromElementId(mesh.Id + modelIdx * 1_000_000, "NwcAssemblyMesh");
                string ifcType = ClassifyElement(elemName, mesh.Material?.Name);

                int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, elemName, elemPlacementId, prodShapeId, mesh.Id.ToString(CultureInfo.InvariantCulture));
                modelElementIds.Add(elemId);

                // Type assignment
                if (options.ExportTypeObjects)
                {
                    string typeName = CleanTypeName(elemName, mesh.Material?.Name, ifcType);
                    string typeKey = $"{ifcType}::{typeName}";
                    if (!typeMap.TryGetValue(typeKey, out var entry))
                    {
                        string typeGuid = IfcGuid.FromElementId(typeIdSeed++, "IfcType");
                        int typeId = AddIfcTypeProduct(w, ifcType, typeGuid, ownerHistoryId, typeName, isIfc4);
                        entry = (typeId, new List<int>());
                        typeMap[typeKey] = entry;
                    }
                    entry.ElementIds.Add(elemId);
                }

                if (options.ExportProperties)
                {
                    WriteMeshProperties(w, elemId, mesh, ownerHistoryId);
                }
            }

            if (modelElementIds.Count > 0)
            {
                w.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(modelElementIds.Select(StepWriter.Ref))},{StepWriter.Ref(storeyId)}");
            }
        }

        if (options.ExportTypeObjects)
        {
            foreach (var (_, (typeId, elemIds)) in typeMap)
            {
                if (elemIds.Count > 0)
                {
                    w.Add("IFCRELDEFINESBYTYPE",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elemIds.Select(StepWriter.Ref))},{StepWriter.Ref(typeId)}");
                }
            }
        }

        if (storeyIds.Count > 0)
        {
            w.Add("IFCRELAGGREGATES",
                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(buildingId)},{StepWriter.List(storeyIds.Select(StepWriter.Ref))}");
        }

        w.WriteTo(writer, fileName, schemaName, viewDef, options.Application, options.Organization);
    }

    private static int WriteMeshFacetedBrep(
        StepWriter w,
        IReadOnlyList<Vector3d> vertices,
        IReadOnlyList<int> indices,
        double scale,
        bool sharedVertices)
    {
        var faceIds = new List<int>(indices.Count / 3);

        if (sharedVertices)
        {
            // Share point definitions per mesh
            var pointIds = new int[vertices.Count];
            for (int v = 0; v < vertices.Count; v++)
            {
                var pt = vertices[v];
                pointIds[v] = w.Add("IFCCARTESIANPOINT",
                    StepWriter.List(StepWriter.Real(pt.X * scale), StepWriter.Real(pt.Y * scale), StepWriter.Real(pt.Z * scale)));
            }

            for (int i = 0; i <= indices.Count - 3; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];

                if ((uint)i0 >= (uint)pointIds.Length || (uint)i1 >= (uint)pointIds.Length || (uint)i2 >= (uint)pointIds.Length)
                {
                    continue;
                }

                int loopId = w.Add("IFCPOLYLOOP",
                    StepWriter.List(StepWriter.Ref(pointIds[i0]), StepWriter.Ref(pointIds[i1]), StepWriter.Ref(pointIds[i2])));
                int boundId = w.Add("IFCFACEOUTERBOUND", $"{StepWriter.Ref(loopId)},.T.");
                faceIds.Add(w.Add("IFCFACE", StepWriter.List(StepWriter.Ref(boundId))));
            }
        }
        else
        {
            for (int i = 0; i <= indices.Count - 3; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];

                if ((uint)i0 >= (uint)vertices.Count || (uint)i1 >= (uint)vertices.Count || (uint)i2 >= (uint)vertices.Count)
                {
                    continue;
                }

                var p0 = vertices[i0];
                var p1 = vertices[i1];
                var p2 = vertices[i2];

                int pt0 = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(p0.X * scale), StepWriter.Real(p0.Y * scale), StepWriter.Real(p0.Z * scale)));
                int pt1 = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(p1.X * scale), StepWriter.Real(p1.Y * scale), StepWriter.Real(p1.Z * scale)));
                int pt2 = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(p2.X * scale), StepWriter.Real(p2.Y * scale), StepWriter.Real(p2.Z * scale)));

                int loopId = w.Add("IFCPOLYLOOP", StepWriter.List(StepWriter.Ref(pt0), StepWriter.Ref(pt1), StepWriter.Ref(pt2)));
                int boundId = w.Add("IFCFACEOUTERBOUND", $"{StepWriter.Ref(loopId)},.T.");
                faceIds.Add(w.Add("IFCFACE", StepWriter.List(StepWriter.Ref(boundId))));
            }
        }

        int shellId = w.Add("IFCCLOSEDSHELL", StepWriter.List(faceIds.Select(StepWriter.Ref)));
        return w.Add("IFCFACETEDBREP", StepWriter.Ref(shellId));
    }

    private static int GetOrCreateSurfaceStyle(StepWriter w, NwcMaterial mat, Dictionary<string, int> cache)
    {
        string key = $"{mat.Name}_{mat.Color.ToHex()}";
        if (cache.TryGetValue(key, out int existingId))
        {
            return existingId;
        }

        var col = mat.Color;
        int colorId = w.Add("IFCCOLOURRGB",
            $"$,{StepWriter.Real(col.R)},{StepWriter.Real(col.G)},{StepWriter.Real(col.B)}");
        int shadingId = w.Add("IFCSURFACESTYLESHADING", StepWriter.Ref(colorId));
        int styleId = w.Add("IFCSURFACESTYLE",
            $"{StepWriter.Str(mat.Name)},.BOTH.,({StepWriter.Ref(shadingId)})");

        cache[key] = styleId;
        return styleId;
    }

    private static int AddIfcProduct(
        StepWriter w,
        string ifcType,
        string guid,
        int ownerHistoryId,
        string name,
        int placementId,
        int shapeId,
        string tag)
    {
        string commonArgs = $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,{StepWriter.Ref(placementId)},{StepWriter.Ref(shapeId)},{StepWriter.Str(tag)}";

        return ifcType switch
        {
            "IFCWALL" => w.Add("IFCWALL", commonArgs),
            "IFCSLAB" => w.Add("IFCSLAB", $"{commonArgs},.FLOOR."),
            "IFCCOLUMN" => w.Add("IFCCOLUMN", commonArgs),
            "IFCBEAM" => w.Add("IFCBEAM", commonArgs),
            "IFCREINFORCINGBAR" => w.Add("IFCREINFORCINGBAR", $"{commonArgs},$,$,$,$,$"),
            "IFCWINDOW" => w.Add("IFCWINDOW", $"{commonArgs},$,$"),
            "IFCDOOR" => w.Add("IFCDOOR", $"{commonArgs},$,$,$"),
            "IFCFOOTING" => w.Add("IFCFOOTING", $"{commonArgs},.FOOTING_BEAM."),
            "IFCRAILING" => w.Add("IFCRAILING", $"{commonArgs},.GUARDRAIL."),
            "IFCSTAIR" => w.Add("IFCSTAIR", $"{commonArgs},.STRAIGHT_RUN_STAIR."),
            "IFCROOF" => w.Add("IFCROOF", $"{commonArgs},.FLAT_ROOF."),
            "IFCPIPESEGMENT" => w.Add("IFCPIPESEGMENT", commonArgs),
            "IFCDUCTSEGMENT" => w.Add("IFCDUCTSEGMENT", commonArgs),
            _ => w.Add("IFCBUILDINGELEMENTPROXY", $"{commonArgs},.ELEMENT."),
        };
    }

    private static void WriteMeshProperties(StepWriter w, int elementId, NwcMesh mesh, int ownerHistoryId)
    {
        var propIds = new List<int>();

        int p1 = w.Add("IFCPROPERTYSINGLEVALUE",
            $"'SourceMeshId',$,IFCINTEGER({mesh.Id}),$");
        propIds.Add(p1);

        int p2 = w.Add("IFCPROPERTYSINGLEVALUE",
            $"'TriangleCount',$,IFCINTEGER({mesh.TriangleCount}),$");
        propIds.Add(p2);

        int p3 = w.Add("IFCPROPERTYSINGLEVALUE",
            $"'VertexCount',$,IFCINTEGER({mesh.Vertices.Count}),$");
        propIds.Add(p3);

        if (mesh.Material is not null)
        {
            int pMat = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Material',$,IFCTEXT({StepWriter.Str(mesh.Material.Name)}),foo".Replace("foo", "$"));
            propIds.Add(pMat);
        }

        if (mesh.BoundingBox is { } bbox && !bbox.IsEmpty)
        {
            int pDimX = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Length_X',$,IFCREAL({StepWriter.Real(bbox.Size.X)}),$");
            int pDimY = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Width_Y',$,IFCREAL({StepWriter.Real(bbox.Size.Y)}),$");
            int pDimZ = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Height_Z',$,IFCREAL({StepWriter.Real(bbox.Size.Z)}),$");
            propIds.Add(pDimX);
            propIds.Add(pDimY);
            propIds.Add(pDimZ);
        }

        int psetId = w.Add("IFCPROPERTYSET",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},'Pset_NwcElementProperties',$,{StepWriter.List(propIds.Select(StepWriter.Ref))}");

        w.Add("IFCRELDEFINESBYPROPERTIES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(elementId)}),{StepWriter.Ref(psetId)}");
    }

    private static string ClassifyElement(string name, string? materialName)
    {
        string target = $"{name} {materialName}";

        if (ContainsAny(target, "стена", "стены", "wall", "walls"))
            return "IFCWALL";
        if (ContainsAny(target, "перекрытие", "перекрытия", "плита", "плиты", "floor", "floors", "slab", "slabs"))
            return "IFCSLAB";
        if (ContainsAny(target, "колонна", "колонны", "column", "columns"))
            return "IFCCOLUMN";
        if (ContainsAny(target, "балка", "балки", "beam", "beams", "ригель"))
            return "IFCBEAM";
        if (ContainsAny(target, "арматур", "rebar", "стержень", "каркас"))
            return "IFCREINFORCINGBAR";
        if (ContainsAny(target, "окно", "окна", "window", "windows"))
            return "IFCWINDOW";
        if (ContainsAny(target, "дверь", "двери", "door", "doors"))
            return "IFCDOOR";
        if (ContainsAny(target, "фундамент", "ростверк", "foundation", "footing"))
            return "IFCFOOTING";
        if (ContainsAny(target, "лестница", "лестницы", "stair", "stairs"))
            return "IFCSTAIR";
        if (ContainsAny(target, "ограждение", "ограждения", "railing", "railings", "перила"))
            return "IFCRAILING";
        if (ContainsAny(target, "кровля", "крыша", "roof", "roofs"))
            return "IFCROOF";
        if (ContainsAny(target, "труба", "трубы", "pipe", "pipes"))
            return "IFCPIPESEGMENT";
        if (ContainsAny(target, "воздуховод", "воздуховоды", "duct", "ducts"))
            return "IFCDUCTSEGMENT";

        return "IFCBUILDINGELEMENTPROXY";
    }

    private static int WriteMeshTriangulatedFaceSet(
        StepWriter w,
        IReadOnlyList<Vector3d> vertices,
        IReadOnlyList<int> indices,
        double scale)
    {
        // 1. Build IFCCARTESIANPOINTLIST3D
        var sbPoints = new StringBuilder(vertices.Count * 28 + 32);
        sbPoints.Append('(');
        for (int i = 0; i < vertices.Count; i++)
        {
            if (i > 0) sbPoints.Append(',');
            var v = vertices[i];
            sbPoints.Append('(');
            sbPoints.Append(StepWriter.Real(v.X * scale));
            sbPoints.Append(',');
            sbPoints.Append(StepWriter.Real(v.Y * scale));
            sbPoints.Append(',');
            sbPoints.Append(StepWriter.Real(v.Z * scale));
            sbPoints.Append(')');
        }
        sbPoints.Append(')');
        int pointListId = w.Add("IFCCARTESIANPOINTLIST3D", sbPoints.ToString());

        // 2. Build IFCTRIANGULATEDFACESET with 1-based index triples
        var sbIndices = new StringBuilder((indices.Count / 3) * 16 + 32);
        sbIndices.Append('(');
        bool firstFace = true;
        for (int i = 0; i <= indices.Count - 3; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];
            if ((uint)i0 >= (uint)vertices.Count || (uint)i1 >= (uint)vertices.Count || (uint)i2 >= (uint)vertices.Count)
            {
                continue;
            }

            if (!firstFace) sbIndices.Append(',');
            firstFace = false;

            sbIndices.Append('(');
            sbIndices.Append(i0 + 1);
            sbIndices.Append(',');
            sbIndices.Append(i1 + 1);
            sbIndices.Append(',');
            sbIndices.Append(i2 + 1);
            sbIndices.Append(')');
        }
        sbIndices.Append(')');

        return w.Add("IFCTRIANGULATEDFACESET", $"{StepWriter.Ref(pointListId)},$,.T.,{sbIndices},$");
    }

    private static int AddIfcTypeProduct(
        StepWriter w,
        string ifcType,
        string guid,
        int ownerHistoryId,
        string name,
        bool isIfc4)
    {
        // Attributes in order for IfcElementType and subtypes:
        // 1: GlobalId, 2: OwnerHistory, 3: Name, 4: Description, 5: ApplicableOccurrence,
        // 6: HasPropertySets, 7: RepresentationMaps, 8: Tag, 9: ElementType, 10: PredefinedType
        string prefix = $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,$,$,$,$";

        return ifcType switch
        {
            "IFCWALL" => w.Add("IFCWALLTYPE", $"{prefix},.NOTDEFINED."),
            "IFCSLAB" => w.Add("IFCSLABTYPE", $"{prefix},.NOTDEFINED."),
            "IFCCOLUMN" => w.Add("IFCCOLUMNTYPE", $"{prefix},.NOTDEFINED."),
            "IFCBEAM" => w.Add("IFCBEAMTYPE", $"{prefix},.NOTDEFINED."),
            "IFCFOOTING" => isIfc4
                ? w.Add("IFCFOOTINGTYPE", $"{prefix},.NOTDEFINED.")
                : w.Add("IFCBUILDINGELEMENTPROXYTYPE", $"{prefix},.NOTDEFINED."),
            "IFCRAILING" => w.Add("IFCRAILINGTYPE", $"{prefix},.NOTDEFINED."),
            "IFCROOF" => w.Add("IFCROOFTYPE", $"{prefix},.NOTDEFINED."),
            "IFCSTAIR" => w.Add("IFCSTAIRFLIGHTTYPE", $"{prefix},.NOTDEFINED."),
            "IFCPIPESEGMENT" => w.Add("IFCPIPESEGMENTTYPE", $"{prefix},.NOTDEFINED."),
            "IFCDUCTSEGMENT" => w.Add("IFCDUCTSEGMENTTYPE", $"{prefix},.NOTDEFINED."),
            "IFCREINFORCINGBAR" => isIfc4
                ? w.Add("IFCREINFORCINGBARTYPE", $"{prefix},.NOTDEFINED.")
                : w.Add("IFCBUILDINGELEMENTPROXYTYPE", $"{prefix},.NOTDEFINED."),
            "IFCWINDOW" => isIfc4
                ? w.Add("IFCWINDOWTYPE", $"{prefix},.NOTDEFINED.")
                : w.Add("IFCWINDOWSTYLE", $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,$,$,$,.NOTDEFINED.,.NOTDEFINED.,.F.,.F."),
            "IFCDOOR" => isIfc4
                ? w.Add("IFCDOORTYPE", $"{prefix},.NOTDEFINED.")
                : w.Add("IFCDOORSTYLE", $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,$,$,$,.NOTDEFINED.,.NOTDEFINED.,.F.,.F."),
            _ => w.Add("IFCBUILDINGELEMENTPROXYTYPE", $"{prefix},.NOTDEFINED."),
        };
    }

    private static string CleanTypeName(string elemName, string? matName, string ifcType)
    {
        if (!string.IsNullOrWhiteSpace(matName))
        {
            return matName.Trim();
        }

        if (elemName.StartsWith("Mesh_", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ifcType.Replace("IFC", "", StringComparison.OrdinalIgnoreCase)} Type";
        }

        int bracketIdx = elemName.IndexOf('[');
        if (bracketIdx > 0)
        {
            return elemName[..bracketIdx].Trim();
        }

        return elemName.Trim();
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (text.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

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

        // Output is streamed, so a failure part-way would leave a truncated IFC that looks valid; remove it.
        try
        {
            using var fs = File.Create(path);
            using var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 65536);
            Write(doc, writer, options, Path.GetFileName(path));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
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

        bool isIfc4 = options.SchemaVersion == NwcIfcSchemaVersion.Ifc4;
        string schemaName = isIfc4 ? "IFC4" : "IFC2X3";
        string viewDef = isIfc4 ? "ViewDefinition [DesignTransferView_1.0]" : "ViewDefinition [CoordinationView_V2.0]";
        var w = new StepWriter(writer, fileName, schemaName, viewDef, options.Application, options.Organization, options.Author);

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
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:project"))},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(projectName)},$,$,$,$,({StepWriter.Ref(mainContextId)}),{StepWriter.Ref(unitAssignmentId)}");

        int sitePlacementId = w.Add("IFCLOCALPLACEMENT", $"$,{StepWriter.Ref(worldPlacement3dId)}");
        int siteId = w.Add("IFCSITE",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:site"))},{StepWriter.Ref(ownerHistoryId)},'Site',$,$,{StepWriter.Ref(sitePlacementId)},$,$,.ELEMENT.,$,$,$,$,$");

        int buildingPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(sitePlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int buildingId = w.Add("IFCBUILDING",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:building"))},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(buildingName)},$,$,{StepWriter.Ref(buildingPlacementId)},$,$,.ELEMENT.,$,$,$");

        int storeyPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(buildingPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int storeyId = w.Add("IFCBUILDINGSTOREY",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:storey"))},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(storeyName)},$,$,{StepWriter.Ref(storeyPlacementId)},$,$,.ELEMENT.,{StepWriter.Real(0)}");

        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:project-site"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(projectId)},({StepWriter.Ref(siteId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:site-building"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(siteId)},({StepWriter.Ref(buildingId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:building-storey"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(buildingId)},({StepWriter.Ref(storeyId)})");

        // 4. Materials & Styles Cache
        bool isIfc4 = options.SchemaVersion == NwcIfcSchemaVersion.Ifc4;
        var styleCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (options.ExportMaterials)
        {
            foreach (var mat in doc.Materials)
            {
                GetOrCreateSurfaceStyle(w, mat, styleCache, isIfc4);
            }
        }

        // 5. Export Meshes as IFC Products
        bool useTessellation = isIfc4 && options.UseTessellationInIfc4;

        double scale = (options.LengthUnit == NwcIfcLengthUnit.Metre && options.ConvertMmToMetres) ? 0.001 : 1.0;
        var elementIds = new List<int>(doc.Meshes.Count);

        var typeMap = new Dictionary<string, (int TypeId, List<int> ElementIds)>(StringComparer.OrdinalIgnoreCase);

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
            var indices = options.WorldCoordinates ? mesh.GetWorldIndices() : mesh.Indices;
            if (vertices.Count < 3 || !HasValidTriangle(indices, vertices.Count))
            {
                continue;
            }

            bool closed = IsWatertight(indices, vertices);

            // Material style
            int? styleId = null;
            if (options.ExportMaterials && mesh.Material is not null)
            {
                styleId = GetOrCreateSurfaceStyle(w, mesh.Material, styleCache, isIfc4);
            }

            // Geometry: Tessellation for IFC4, Brep for IFC2x3
            int geomId;
            string repType;
            if (useTessellation)
            {
                geomId = WriteMeshTriangulatedFaceSet(w, vertices, indices, scale, closed);
                repType = "Tessellation";
            }
            else
            {
                geomId = WriteMeshFacetedBrep(w, vertices, indices, scale, options.SharedVertices, closed);
                repType = closed ? "Brep" : "SurfaceModel";
            }

            if (styleId.HasValue)
            {
                AddStyledItem(w, geomId, styleId.Value, mesh.Material?.Name, isIfc4);
            }

            int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},({StepWriter.Ref(geomId)})");
            int prodShapeId = w.Add("IFCPRODUCTDEFINITIONSHAPE",
                $"$,$,({StepWriter.Ref(shapeRepId)})");

            int elemPlacementId = w.Add("IFCLOCALPLACEMENT",
                $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

            var owner = mesh.Node?.Element ?? mesh.Node;
            string elemName = ElementName(mesh, owner);
            string elemGuid = NwcElementGuid.ForMesh(mesh, doc);
            string ifcType = ClassifyElement(elemName, owner);

            int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, elemName, elemPlacementId, prodShapeId, mesh.Id.ToString(CultureInfo.InvariantCulture), isIfc4);
            elementIds.Add(elemId);

            // Type assignment
            if (options.ExportTypeObjects)
            {
                string typeName = CleanTypeName(elemName, owner, ifcType);
                string typeKey = $"{ifcType}::{typeName}";
                if (!typeMap.TryGetValue(typeKey, out var entry))
                {
                    string typeGuid = IfcGuid.FromKey($"ifc:type:{ifcType}:{typeName}");
                    int typeId = AddIfcTypeProduct(w, ifcType, typeGuid, ownerHistoryId, typeName, isIfc4);
                    entry = (typeId, new List<int>());
                    typeMap[typeKey] = entry;
                }
                entry.ElementIds.Add(elemId);
            }

            // Export properties if requested
            if (options.ExportProperties)
            {
                WriteMeshProperties(w, elemId, elemGuid, mesh, ownerHistoryId, vertices, scale, isIfc4);
            }
        }

        // 6. Type Definitions
        if (options.ExportTypeObjects)
        {
            foreach (var (typeKey, (typeId, elemIds)) in typeMap)
            {
                if (elemIds.Count > 0)
                {
                    w.Add("IFCRELDEFINESBYTYPE",
                        $"{StepWriter.Str(IfcGuid.FromKey($"ifc:rel:type:{typeKey}"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elemIds.Select(StepWriter.Ref))},{StepWriter.Ref(typeId)}");
                }
            }
        }

        // 7. Spatial Containment
        if (elementIds.Count > 0)
        {
            w.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:contained"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elementIds.Select(StepWriter.Ref))},{StepWriter.Ref(storeyId)}");
        }

        // 8. Write complete STEP physical file
        w.Complete();
    }

    /// <summary>Writes a federated <see cref="NwcAssembly"/> to an IFC file.</summary>
    public static void WriteAssembly(NwcAssembly assembly, string path, NwcIfcExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            using var fs = File.Create(path);
            using var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 65536);
            WriteAssembly(assembly, writer, options, Path.GetFileName(path));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
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

        bool isIfc4 = options.SchemaVersion == NwcIfcSchemaVersion.Ifc4;
        string schemaName = isIfc4 ? "IFC4" : "IFC2X3";
        string viewDef = isIfc4 ? "ViewDefinition [DesignTransferView_1.0]" : "ViewDefinition [CoordinationView_V2.0]";
        var w = new StepWriter(writer, fileName, schemaName, viewDef, options.Application, options.Organization, options.Author);

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
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:project"))},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(projectName)},$,$,$,$,({StepWriter.Ref(mainContextId)}),{StepWriter.Ref(unitAssignmentId)}");

        int sitePlacementId = w.Add("IFCLOCALPLACEMENT", $"$,{StepWriter.Ref(worldPlacement3dId)}");
        int siteId = w.Add("IFCSITE",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:site"))},{StepWriter.Ref(ownerHistoryId)},'Site',$,$,{StepWriter.Ref(sitePlacementId)},$,$,.ELEMENT.,$,$,$,$,$");

        int buildingPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(sitePlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int buildingId = w.Add("IFCBUILDING",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:building"))},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(buildingName)},$,$,{StepWriter.Ref(buildingPlacementId)},$,$,.ELEMENT.,$,$,$");

        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:project-site"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(projectId)},({StepWriter.Ref(siteId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:site-building"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(siteId)},({StepWriter.Ref(buildingId)})");

        // 4. Materials Cache
        bool isIfc4 = options.SchemaVersion == NwcIfcSchemaVersion.Ifc4;
        var styleCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (options.ExportMaterials)
        {
            foreach (var mat in assembly.AllMaterials)
            {
                GetOrCreateSurfaceStyle(w, mat, styleCache, isIfc4);
            }
        }

        // 5. Storeys per Model or Global Storey
        bool useTessellation = isIfc4 && options.UseTessellationInIfc4;

        double scale = (options.LengthUnit == NwcIfcLengthUnit.Metre && options.ConvertMmToMetres) ? 0.001 : 1.0;
        var storeyIds = new List<int>();
        var modelKeys = NwcElementGuid.ModelKeys(assembly.Models);

        var typeMap = new Dictionary<string, (int TypeId, List<int> ElementIds)>(StringComparer.OrdinalIgnoreCase);

        for (int modelIdx = 0; modelIdx < assembly.Models.Count; modelIdx++)
        {
            var model = assembly.Models[modelIdx];
            string subStoreyName = !string.IsNullOrEmpty(model.FileInfo.SourcePath)
                ? Path.GetFileNameWithoutExtension(model.FileInfo.SourcePath)
                : $"Model_{modelIdx + 1}";

            int storeyPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(buildingPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
            int storeyId = w.Add("IFCBUILDINGSTOREY",
                $"{StepWriter.Str(IfcGuid.FromKey($"ifc:storey:{modelIdx.ToString(CultureInfo.InvariantCulture)}"))},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(subStoreyName)},$,$,{StepWriter.Ref(storeyPlacementId)},$,$,.ELEMENT.,{StepWriter.Real(0)}");
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
                var indices = options.WorldCoordinates ? mesh.GetWorldIndices() : mesh.Indices;
                if (vertices.Count < 3 || !HasValidTriangle(indices, vertices.Count))
                {
                    continue;
                }

                bool closed = IsWatertight(indices, vertices);

                int? styleId = null;
                if (options.ExportMaterials && mesh.Material is not null)
                {
                    styleId = GetOrCreateSurfaceStyle(w, mesh.Material, styleCache, isIfc4);
                }

                int geomId;
                string repType;
                if (useTessellation)
                {
                    geomId = WriteMeshTriangulatedFaceSet(w, vertices, indices, scale, closed);
                    repType = "Tessellation";
                }
                else
                {
                    geomId = WriteMeshFacetedBrep(w, vertices, indices, scale, options.SharedVertices, closed);
                    repType = closed ? "Brep" : "SurfaceModel";
                }

                if (styleId.HasValue)
                {
                    AddStyledItem(w, geomId, styleId.Value, mesh.Material?.Name, isIfc4);
                }

                int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                    $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},({StepWriter.Ref(geomId)})");
                int prodShapeId = w.Add("IFCPRODUCTDEFINITIONSHAPE",
                    $"$,$,({StepWriter.Ref(shapeRepId)})");

                int elemPlacementId = w.Add("IFCLOCALPLACEMENT",
                    $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

                var owner = mesh.Node?.Element ?? mesh.Node;
                string elemName = ElementName(mesh, owner);
                string elemGuid = NwcElementGuid.ForMesh(mesh, modelKeys[modelIdx]);
                string ifcType = ClassifyElement(elemName, owner);

                int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, elemName, elemPlacementId, prodShapeId, mesh.Id.ToString(CultureInfo.InvariantCulture), isIfc4);
                modelElementIds.Add(elemId);

                // Type assignment
                if (options.ExportTypeObjects)
                {
                    string typeName = CleanTypeName(elemName, owner, ifcType);
                    string typeKey = $"{ifcType}::{typeName}";
                    if (!typeMap.TryGetValue(typeKey, out var entry))
                    {
                        string typeGuid = IfcGuid.FromKey($"ifc:type:{ifcType}:{typeName}");
                        int typeId = AddIfcTypeProduct(w, ifcType, typeGuid, ownerHistoryId, typeName, isIfc4);
                        entry = (typeId, new List<int>());
                        typeMap[typeKey] = entry;
                    }
                    entry.ElementIds.Add(elemId);
                }

                if (options.ExportProperties)
                {
                    WriteMeshProperties(w, elemId, elemGuid, mesh, ownerHistoryId, vertices, scale, isIfc4);
                }
            }

            if (modelElementIds.Count > 0)
            {
                w.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                    $"{StepWriter.Str(IfcGuid.FromKey($"ifc:rel:contained:{modelIdx.ToString(CultureInfo.InvariantCulture)}"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(modelElementIds.Select(StepWriter.Ref))},{StepWriter.Ref(storeyId)}");
            }
        }

        if (options.ExportTypeObjects)
        {
            foreach (var (typeKey, (typeId, elemIds)) in typeMap)
            {
                if (elemIds.Count > 0)
                {
                    w.Add("IFCRELDEFINESBYTYPE",
                        $"{StepWriter.Str(IfcGuid.FromKey($"ifc:rel:type:{typeKey}"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elemIds.Select(StepWriter.Ref))},{StepWriter.Ref(typeId)}");
                }
            }
        }

        if (storeyIds.Count > 0)
        {
            w.Add("IFCRELAGGREGATES",
                $"{StepWriter.Str(IfcGuid.FromKey("ifc:rel:building-storeys"))},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(buildingId)},{StepWriter.List(storeyIds.Select(StepWriter.Ref))}");
        }

        w.Complete();
    }

    private static int WriteMeshFacetedBrep(
        StepWriter w,
        IReadOnlyList<Vector3d> vertices,
        IReadOnlyList<int> indices,
        double scale,
        bool sharedVertices,
        bool closed)
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

        if (closed)
        {
            int shellId = w.Add("IFCCLOSEDSHELL", StepWriter.List(faceIds.Select(StepWriter.Ref)));
            return w.Add("IFCFACETEDBREP", StepWriter.Ref(shellId));
        }

        // An unclosed mesh cannot be a solid: export it as an open shell surface model.
        int openShellId = w.Add("IFCOPENSHELL", StepWriter.List(faceIds.Select(StepWriter.Ref)));
        return w.Add("IFCSHELLBASEDSURFACEMODEL", StepWriter.List(StepWriter.Ref(openShellId)));
    }

    private static int GetOrCreateSurfaceStyle(StepWriter w, NwcMaterial mat, Dictionary<string, int> cache, bool isIfc4)
    {
        string key = $"{mat.Name}_{mat.Color.ToHex()}";
        if (cache.TryGetValue(key, out int existingId))
        {
            return existingId;
        }

        var col = mat.Color;
        int colorId = w.Add("IFCCOLOURRGB",
            $"$,{StepWriter.Real(col.R)},{StepWriter.Real(col.G)},{StepWriter.Real(col.B)}");
        int shadingId = isIfc4
            ? w.Add("IFCSURFACESTYLESHADING", $"{StepWriter.Ref(colorId)},$")
            : w.Add("IFCSURFACESTYLESHADING", StepWriter.Ref(colorId));
        int styleId = w.Add("IFCSURFACESTYLE",
            $"{StepWriter.Str(mat.Name)},.BOTH.,({StepWriter.Ref(shadingId)})");

        cache[key] = styleId;
        return styleId;
    }

    /// <summary>References a surface style from an <c>IFCSTYLEDITEM</c> using the schema's required select form.</summary>
    private static int AddStyledItem(StepWriter w, int itemId, int styleId, string? name, bool isIfc4)
    {
        // IFC2x3 restricts IfcStyledItem.Styles to IfcPresentationStyleAssignment; IFC4 accepts the style directly.
        int styleRef = isIfc4
            ? styleId
            : w.Add("IFCPRESENTATIONSTYLEASSIGNMENT", $"({StepWriter.Ref(styleId)})");
        return w.Add("IFCSTYLEDITEM",
            $"{StepWriter.Ref(itemId)},({StepWriter.Ref(styleRef)}),{StepWriter.Str(name)}");
    }

    private static int AddIfcProduct(
        StepWriter w,
        string ifcType,
        string guid,
        int ownerHistoryId,
        string name,
        int placementId,
        int shapeId,
        string tag,
        bool isIfc4)
    {
        string commonArgs = $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,{StepWriter.Ref(placementId)},{StepWriter.Ref(shapeId)},{StepWriter.Str(tag)}";

        return ifcType switch
        {
            // IFC4 added a PredefinedType to the 8 shared IfcProduct attributes; IFC2x3 IfcWall/IfcColumn/IfcBeam have none.
            "IFCWALL" => w.Add("IFCWALL", isIfc4 ? $"{commonArgs},.NOTDEFINED." : commonArgs),
            "IFCSLAB" => w.Add("IFCSLAB", $"{commonArgs},.FLOOR."),
            "IFCCOLUMN" => w.Add("IFCCOLUMN", isIfc4 ? $"{commonArgs},.NOTDEFINED." : commonArgs),
            "IFCBEAM" => w.Add("IFCBEAM", isIfc4 ? $"{commonArgs},.NOTDEFINED." : commonArgs),
            // 14 values: SteelGrade, NominalDiameter, CrossSectionArea, BarLength, BarRole/PredefinedType, BarSurface.
            // IFC2x3 makes NominalDiameter and CrossSectionArea mandatory and the NWC file has no such data, so the
            // placeholders 1 and 0 are written there (IFC4 leaves them '$'); see docs/format-notes.md.
            "IFCREINFORCINGBAR" => w.Add("IFCREINFORCINGBAR", isIfc4
                ? $"{commonArgs},$,$,$,$,$,$"
                : $"{commonArgs},$,{StepWriter.Real(1)},{StepWriter.Real(0)},$,.NOTDEFINED.,$"),
            "IFCWINDOW" => w.Add("IFCWINDOW", isIfc4
                ? $"{commonArgs},$,$,$,$,$"
                : $"{commonArgs},$,$"),
            "IFCDOOR" => w.Add("IFCDOOR", isIfc4
                ? $"{commonArgs},$,$,$,$,$"
                : $"{commonArgs},$,$"),
            "IFCFOOTING" => w.Add("IFCFOOTING", $"{commonArgs},.FOOTING_BEAM."),
            "IFCRAILING" => w.Add("IFCRAILING", $"{commonArgs},.GUARDRAIL."),
            "IFCSTAIR" => w.Add("IFCSTAIR", $"{commonArgs},.STRAIGHT_RUN_STAIR."),
            "IFCROOF" => w.Add("IFCROOF", $"{commonArgs},.FLAT_ROOF."),
            "IFCPIPESEGMENT" => isIfc4
                ? w.Add("IFCPIPESEGMENT", $"{commonArgs},.NOTDEFINED.")
                : w.Add("IFCFLOWSEGMENT", commonArgs),
            "IFCDUCTSEGMENT" => isIfc4
                ? w.Add("IFCDUCTSEGMENT", $"{commonArgs},.NOTDEFINED.")
                : w.Add("IFCFLOWSEGMENT", commonArgs),
            _ => w.Add("IFCBUILDINGELEMENTPROXY", $"{commonArgs},.ELEMENT."),
        };
    }

    private static void WriteMeshProperties(
        StepWriter w,
        int elementId,
        string elemGuid,
        NwcMesh mesh,
        int ownerHistoryId,
        IReadOnlyList<Vector3d> vertices,
        double scale,
        bool isIfc4 = false)
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

        var bounds = BoundingBox3d.Empty;
        foreach (var vertex in vertices)
        {
            bounds = bounds.Union(vertex);
        }

        if (!bounds.IsEmpty)
        {
            var size = bounds.Size * scale;
            var pDimX = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Length_X',$,IFCLENGTHMEASURE({StepWriter.Real(size.X)}),$");
            var pDimY = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Width_Y',$,IFCLENGTHMEASURE({StepWriter.Real(size.Y)}),$");
            var pDimZ = w.Add("IFCPROPERTYSINGLEVALUE",
                $"'Height_Z',$,IFCLENGTHMEASURE({StepWriter.Real(size.Z)}),$");
            propIds.Add(pDimX);
            propIds.Add(pDimY);
            propIds.Add(pDimZ);
        }

        int psetId = w.Add("IFCPROPERTYSET",
            $"{StepWriter.Str(IfcGuid.FromKey($"ifc:pset:{elemGuid}"))},{StepWriter.Ref(ownerHistoryId)},'Pset_NwcElementProperties',$,{StepWriter.List(propIds.Select(StepWriter.Ref))}");

        w.Add("IFCRELDEFINESBYPROPERTIES",
            $"{StepWriter.Str(IfcGuid.FromKey($"ifc:rel:props:{elemGuid}"))},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(elementId)}),{StepWriter.Ref(psetId)}");

        if (mesh.Node?.Hyperlinks is { Count: > 0 } hyperlinks)
        {
            foreach (var link in hyperlinks)
            {
                string docRefArgs = isIfc4
                    ? $"{StepWriter.Str(link.Url)},{StepWriter.Str(link.Name)},{StepWriter.Str(link.Category ?? "Hyperlink")},$,$"
                    : $"{StepWriter.Str(link.Url)},{StepWriter.Str(link.Name)},{StepWriter.Str(link.Category ?? "Hyperlink")}";
                int docRefId = w.Add("IFCDOCUMENTREFERENCE", docRefArgs);
                w.Add("IFCRELASSOCIATESDOCUMENT",
                    $"{StepWriter.Str(IfcGuid.FromKey($"ifc:reldoc:{elemGuid}:{link.Url}"))},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(elementId)}),{StepWriter.Ref(docRefId)}");
            }
        }
    }

    private static string ClassifyElement(string name, NwcNode? node)
    {
        foreach (string text in ClassificationTexts(node))
        {
            string? classified = ClassifyText(text);
            if (classified is not null)
            {
                return classified;
            }
        }

        return ClassifyText(name) ?? "IFCBUILDINGELEMENTPROXY";
    }

    private static IEnumerable<string> ClassificationTexts(NwcNode? node)
    {
        if (node is null)
        {
            yield break;
        }

        yield return node.Class;
        foreach (var property in node.Properties)
        {
            if (IsClassificationProperty(property.Name) && !string.IsNullOrWhiteSpace(property.Value))
            {
                yield return property.Value;
            }
        }

        yield return node.Name;
    }

    private static bool IsClassificationProperty(string name) =>
        name is "Category" or "Категория" or "Family" or "Семейство" or "Type" or "Тип"
            or "Element Type" or "Type Name" or "System Type" or "IFC Type" or "IfcType" or "IfcEntity";

    private static string? ClassifyText(string text)
    {
        if (text.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase) && text.Length > 3)
        {
            return ClassifyText(text[3..]);
        }

        if (HasTokenPrefix(text, "труб", "pipe"))
            return "IFCPIPESEGMENT";
        if (HasTokenPrefix(text, "воздуховод", "duct"))
            return "IFCDUCTSEGMENT";
        if (HasTokenPrefix(text, "стен", "wall"))
            return "IFCWALL";
        if (HasTokenPrefix(text, "перекрыт", "плит", "floor", "slab"))
            return "IFCSLAB";
        if (HasTokenPrefix(text, "колонн", "column"))
            return "IFCCOLUMN";
        if (HasTokenPrefix(text, "балк", "ригел", "beam"))
            return "IFCBEAM";
        if (HasTokenPrefix(text, "арматур", "rebar", "стерж", "каркас"))
            return "IFCREINFORCINGBAR";
        if (HasTokenPrefix(text, "окн", "window"))
            return "IFCWINDOW";
        if (HasTokenPrefix(text, "двер", "door"))
            return "IFCDOOR";
        if (HasTokenPrefix(text, "фундамент", "ростверк", "foundation", "footing"))
            return "IFCFOOTING";
        if (HasTokenPrefix(text, "лестниц", "stair"))
            return "IFCSTAIR";
        if (HasTokenPrefix(text, "огражден", "перил", "rail"))
            return "IFCRAILING";
        if (HasTokenPrefix(text, "кровл", "крыш", "roof"))
            return "IFCROOF";

        return null;
    }

    /// <summary>Words that merely start like a structural keyword (balcony vs beam, lid vs roof, tile vs slab); they are ignored, the rest of the text is still classified.</summary>
    private static bool IsNonStructuralToken(ReadOnlySpan<char> token) =>
        token.StartsWith("балкон", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("крышк", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("плитк", StringComparison.OrdinalIgnoreCase);

    private static string ElementName(NwcMesh mesh, NwcNode? owner) =>
        !string.IsNullOrEmpty(owner?.Name)
            ? owner.Name
            : string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{mesh.Id}" : mesh.Name;

    /// <summary>Matches a keyword only at the start of a word token, so "Drywall" is not a wall and "Outdoor" is not a door.</summary>
    private static bool HasTokenPrefix(string text, params string[] prefixes)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                i++;
            }

            var token = text.AsSpan(start, i - start);
            if (IsNonStructuralToken(token))
            {
                continue;
            }

            foreach (string prefix in prefixes)
            {
                if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int WriteMeshTriangulatedFaceSet(
        StepWriter w,
        IReadOnlyList<Vector3d> vertices,
        IReadOnlyList<int> indices,
        double scale,
        bool closed)
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

        // Closed is optional in IFC4: true only when watertightness was proven, otherwise leave it unknown
        // rather than asserting that a mesh we could not prove closed is definitely open.
        return w.Add("IFCTRIANGULATEDFACESET", $"{StepWriter.Ref(pointListId)},$,{(closed ? ".T." : "$")},{sbIndices},$");
    }

    private static bool HasValidTriangle(IReadOnlyList<int> indices, int vertexCount)
    {
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            if ((uint)indices[i] < (uint)vertexCount &&
                (uint)indices[i + 1] < (uint)vertexCount &&
                (uint)indices[i + 2] < (uint)vertexCount)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every undirected edge is shared by exactly two valid triangles, i.e. the surface is closed. Vertices
    /// are welded by position first: real NWC meshes store render vertices, so one position is repeated for
    /// every face normal and index-based edges would report every flat-shaded solid as open.
    /// </summary>
    private static bool IsWatertight(IReadOnlyList<int> indices, IReadOnlyList<Vector3d> vertices)
    {
        var canonical = new Dictionary<Vector3d, int>(vertices.Count);
        var ids = new int[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            if (!canonical.TryGetValue(vertices[i], out int id))
            {
                id = canonical.Count;
                canonical[vertices[i]] = id;
            }

            ids[i] = id;
        }

        var edges = new Dictionary<(int Low, int High), int>();
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];
            if ((uint)a >= (uint)vertices.Count || (uint)b >= (uint)vertices.Count || (uint)c >= (uint)vertices.Count)
            {
                continue;
            }

            CountEdge(edges, ids[a], ids[b]);
            CountEdge(edges, ids[b], ids[c]);
            CountEdge(edges, ids[c], ids[a]);
        }

        return edges.Count > 0 && edges.Values.All(count => count == 2);
    }

    private static void CountEdge(Dictionary<(int Low, int High), int> edges, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        edges[key] = edges.GetValueOrDefault(key) + 1;
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
            "IFCROOF" => isIfc4
                ? w.Add("IFCROOFTYPE", $"{prefix},.NOTDEFINED.")
                : w.Add("IFCBUILDINGELEMENTPROXYTYPE", $"{prefix},.NOTDEFINED."),
            "IFCSTAIR" => w.Add("IFCSTAIRFLIGHTTYPE", $"{prefix},.NOTDEFINED."),
            "IFCPIPESEGMENT" => w.Add("IFCPIPESEGMENTTYPE", $"{prefix},.NOTDEFINED."),
            "IFCDUCTSEGMENT" => w.Add("IFCDUCTSEGMENTTYPE", $"{prefix},.NOTDEFINED."),
            "IFCREINFORCINGBAR" => isIfc4
                ? w.Add("IFCREINFORCINGBARTYPE", $"{prefix},.NOTDEFINED.,$,$,$,$,$,$")
                : w.Add("IFCBUILDINGELEMENTPROXYTYPE", $"{prefix},.NOTDEFINED."),
            "IFCWINDOW" => isIfc4
                ? w.Add("IFCWINDOWTYPE", $"{prefix},.NOTDEFINED.,.NOTDEFINED.,.F.,$")
                : w.Add("IFCWINDOWSTYLE", $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,$,$,$,.NOTDEFINED.,.NOTDEFINED.,.F.,.F."),
            "IFCDOOR" => isIfc4
                ? w.Add("IFCDOORTYPE", $"{prefix},.NOTDEFINED.,.NOTDEFINED.,.F.,$")
                : w.Add("IFCDOORSTYLE", $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,$,$,$,.NOTDEFINED.,.NOTDEFINED.,.F.,.F."),
            _ => w.Add("IFCBUILDINGELEMENTPROXYTYPE", $"{prefix},.NOTDEFINED."),
        };
    }

    private static string CleanTypeName(string elemName, NwcNode? node, string ifcType)
    {
        string? familyOrType = NodeProperty(node, "Family", "Семейство", "Type", "Тип", "Element Type", "Type Name", "System Type");
        if (!string.IsNullOrWhiteSpace(familyOrType))
        {
            return familyOrType.Trim();
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

    private static string? NodeProperty(NwcNode? node, params string[] names)
    {
        if (node is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            foreach (var property in node.Properties)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(property.Value))
                {
                    return property.Value;
                }
            }
        }

        return null;
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

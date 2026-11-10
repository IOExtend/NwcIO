<p align="center">
  <img src="docs/images/nwcio-banner.png" alt="NwcIO" width="640">
</p>

# NwcIO

**NwcIO** is a fast, 100% managed .NET library and CLI tool that reads Autodesk Navisworks files (`.nwc`, `.nwd`, `.nwf`) directly from disk **without requiring Autodesk Navisworks or the Navisworks API to be installed**, and exports 3D geometry, materials, scene hierarchies, and properties to **Industry Foundation Classes (IFC2x3 Coordination View)**, **glTF 2.0 Binary (GLB)**, **Wavefront OBJ**, and **JSON**.

Built clean-room following the architectural principles of [mahach666/RvtIO](https://github.com/mahach666/RvtIO): zero native dependencies, zero external libraries, strict typing, high performance, and cross-platform compatibility.

---

## What It Does

- **Container Serialization & Decompression (`NwcWriter` / `NwcContainerReader`)**: Direct byte-stream writing and parsing of `#LcUStream-V1.1` containers, continuous zlib compression/decompression (`lichunk-008`, `lizlib`), and checksum gap framing without third-party libraries. Programmatically create, modify, and save `.nwc` files from code.
- **Fluent Document Builder (`NwcDocumentBuilder`)**: Construct Navisworks documents from scratch with 3D polygon meshes, spatial transformation matrices, hierarchy trees, materials, element GUIDs, and camera viewpoints.
- **Industry Foundation Classes (IFC2x3 & IFC4)**: Pure managed exporter generating ISO 10303-21 STEP physical files compliant with **IFC2x3 Coordination View V2.0** (`IfcFacetedBrep`, `IfcClosedShell`, `IfcFace`, `IfcPolyLoop`, `IfcCartesianPoint`) and modern **IFC4 Design Transfer / Reference View** with compact mesh tessellation (`IfcTriangulatedFaceSet`, `IfcCartesianPointList3D`) reducing file sizes by over 60%, spatial hierarchy (`IfcProject`, `IfcSite`, `IfcBuilding`, `IfcBuildingStorey`), element mapping (`IfcWall`, `IfcSlab`, `IfcColumn`, `IfcBeam`, `IfcReinforcingBar`, etc.), type product definitions (`IfcWallType`, `IfcSlabType`, `IfcColumnType`, `IfcReinforcingBarType`, etc. linked via `IfcRelDefinesByType`), surface styles (`IfcSurfaceStyle`, `IfcColourRgb`), property sets (`IfcPropertySet`, `IfcPropertySingleValue`), and buildingSMART 22-character GUID encoding.
- **Hierarchical Scene Graph**: Reconstructs Navisworks selection tree (Model &rarr; Layers &rarr; Categories &rarr; Families &rarr; Types &rarr; Composite Elements &rarr; Geometry) with bound parameters and GUIDs.
- **3D Spatial Transforms**: Decodes instance poses (double-precision translation vectors, unit rotation quaternions, local AABB bounding boxes) and generates analytic $4\times 4$ affine transformation matrices.
- **3D Polygon Meshes**: Reads geometry chunk streams via TOC (Table of Contents), unpacks IEEE 754 vertices, converts `TRIANGLE_STRIP` buffers with alternating normal windings, and transforms vertices to world coordinates.
- **Materials & RGBA Colors**: Extracts surface materials and color triples (`LcRevitElement`, `revit_Color`), assigns standard BIM category palettes (concrete, rebar, insulation), and computes vertex/face normal vectors for PBR rendering.
- **Binary glTF 2.0 (GLB) Exporter**: Generates specification-compliant `.glb` files with 12-byte header, 4-byte padded JSON chunk (`0x20`), 4-byte padded binary chunk (`0x00`), vertex positions, normals, indices, and PBR materials.
- **Wavefront OBJ Exporter**: Streams 3D meshes to `.obj` with 1-based indexing in local or project world coordinates.
- **Spatial Acceleration Index (BVH)**: Balanced Bounding Volume Hierarchy over all meshes for sub-millisecond range queries (`QueryBox`) and double-precision Möller–Trumbore raycasting (`Raycast`).
- **Navisworks Clash Detective & 3D Triangle Collision**: Complete clean-room collision engine (`NwcClashDetector`) executing broad-phase BVH and narrow-phase Möller 3D triangle-to-triangle intersection tests. Status lifecycles (`New`, `Active`, `Reviewed`, `Approved`, `Resolved`), test types (`Hard`, `Clearance`), and exclusion rules (`IgnoreSameLayer`, `IgnoreSameGroup`, `IgnoreSameFile`).
- **Intelligent Clash Grouping & Clustering (`NwcClashGrouper`)**: Automatically cluster thousands of raw collisions into manageable groups by spatial 3D proximity (Disjoint-Set algorithm), primary continuous MEP/structural element, structural grid line, or discipline layer. Full roundtrip `<clashgroup>` XML serialization and grouped BCF 2.1 issue export.
- **Standalone 3D WebGL HTML Viewer (`NwcHtmlViewerExporter`)**: Export any Navisworks document or federated assembly to an interactive, self-contained single-file 3D HTML viewer (`.html`) with zero external scripts, CDNs, or server dependencies. Features orbit camera controls, real-time fragment-shader section clipping planes (Cut X, Cut Y, Cut Z), saved viewpoint transitions, an integrated Clash Navigator, and element Property Inspector.
- **Quick Properties & Tooltips (`NwcQuickProperties`)**: Define and extract customizable element hover tooltips and quick properties with standard BIM presets and roundtrip Navisworks XML format exchange.
- **Autodesk Navisworks Clash Report XML Exchange**: Full reading and writing of official Navisworks Clash Report XML documents (`nw-clashreport-11.0.xsd`) including batch tests, clash tests, clash results, clash points, grid locations, and comments (`NwcXmlClashSerializer`).
- **Federated Multi-Model Assemblies (.nwf)**: Aggregates multiple discipline models into a unified coordination coordinate system, reads `.nwf` external references, and authors federated project manifests (`NwcNwfManifest`).
- **Saved Viewpoints & Cameras**: Extracts user saved viewpoints and camera views (`NwcCamera`, `NwcSavedViewpoint`) from stream tag 517 (double-precision eye coordinates, unit rotation quaternions, viewing direction, up vector, focal distance, FOV, and aspect ratio). Automatically embeds viewpoints as standard `cameras` and `nodes` in glTF 2.0 (GLB), outputs them in JSON, and exports to buildingSMART BCF 2.1 zip archives (`SaveViewpointsBcf`).
- **Dynamic 3D Sectioning & Mesh Clipping**: Supports up to 6 simultaneous clipping planes (`NwcSectionPlane`) aligned to Navisworks sectioning modes (`Top`, `Bottom`, `Left`, `Right`, `Front`, `Back`, `View`, `Custom`) and oriented section boxes (`NwcSectionBox`). Fast Sutherland-Hodgman polygon mesh clipping engine (`NwcMeshClipper`) in pure managed C#.
- **Redline Markup Annotations**: 2D and 3D visual review markups (`NwcRedlineText`, `NwcRedlineLine`, `NwcRedlineArrow`, `NwcRedlineCloud`, `NwcRedlineEllipse`, `NwcRedlineTag`) with custom colors, stroke thickness, authors, and review status workflows (`New`, `Active`, `Approved`, `Resolved`).
- **Autodesk Navisworks XML Viewpoints Exchange**: Full roundtrip serialization and deserialization of saved viewpoints, cameras, clipping planes, and redlines in official Navisworks XML format (`NwcXmlViewpointSerializer`, `SaveViewpointsXml`).
- **Selection Sets & Dynamic Search Sets**: Strongly-typed model hierarchy (`NwcSelectionSet`, `NwcSearchSet`, `NwcSelectionSetFolder`) with scene graph query evaluation. Dynamic search rules (`NwcSearchCondition`) with rich operators (`Equals`, `NotEquals`, `Contains`, `StartsWith`, `Wildcard`, `GreaterThan`, etc.) filtering by properties, categories, names, and GUIDs. Standard Autodesk Navisworks Selection Sets XML (`.xml`) reader and writer (`ExportSelectionSetsXml`).
- **4D TimeLiner Construction Schedules**: Work Breakdown Structure (WBS) task trees (`NwcTimeLinerSchedule`, `NwcTimeLinerTask`) with planned and actual schedules, budget tracking, and selection set bindings. 4D temporal simulation engine (`GetActiveTasksAt`, `GetSimulationProgressAt`, `GetNodeStateAt`) and standard TimeLiner CSV exchange (`ExportTimeLinerCsv`).
- **buildingSMART BCF 2.1 Collaboration**: Pure managed exporter generating standard BIM Collaboration Format (`.bcfzip`) archives with topic folders, `markup.bcf`, viewpoints (`viewpoint.bcfv`), perspective cameras focused on clash centers, and colored component highlights (`IfcGuid`).
- **Interactive HTML Clash Reports**: Standalone, offline HTML clash detection report with a discipline cross-clash matrix ($M_i \times M_j$ frequency), KPI summary cards, live search/filtering, and sortable clash details table.
- **Embedded DB Cache**: Decodes the internal `LcOpDBCache` virtual table directory (offsets, sizes, flags).

---

## Install

Install the command line tool:

```sh
dotnet tool install --global NwcIO.Cli
```

Add the library to a .NET project:

```sh
dotnet add package NwcIO
```

The library targets **.NET 8** and **.NET 10**. It is 100% managed code with zero native dependencies, running seamlessly on Windows, Linux, and macOS.

---

## Command Line (`nwcio`)

### Export to Industry Foundation Classes (IFC2x3 & IFC4)

```sh
# Export directly to IFC2x3 (Coordination View V2.0, Faceted BRep, Materials, Property Sets)
nwcio model.nwc model.ifc

# Export to modern compact IFC4 with IfcTriangulatedFaceSet and Type Objects
nwcio model.nwc model.ifc --ifc-schema IFC4

# Export without IFC type product definitions
nwcio model.nwc model.ifc --no-types

# Export with summary report to stderr
nwcio model.nwc model.ifc --ifc-schema IFC4 --report
```

### Export to Binary glTF 2.0 (GLB)

```sh
# Export directly to GLB (with meshes, normals, materials, and colors)
nwcio model.nwc model.glb

# Export with summary report to stderr
nwcio model.nwc model.glb --report
```

### Export to Wavefront OBJ

```sh
# Export 3D meshes to OBJ in world coordinate space
nwcio model.nwc model.obj

# Export in local coordinate space
nwcio model.nwc model.obj --local-coords
```

### Export to Structured JSON

```sh
# Export full scene tree, properties, transforms, GUIDs, materials, and meshes
nwcio model.nwc model.json

# Compact (non-indented) JSON
nwcio model.nwc model.json --no-indent

# Include raw container stream catalog (offsets, lengths, stream kinds)
nwcio model.nwc model.json --catalog
```

### Multi-Model Federated Assemblies & Clash Detection

```sh
# Load all .nwc models from a folder into a single federated assembly GLB
nwcio ./BuildingK4/ ./BuildingK4_assembly.glb --report

# Run cross-model geometric clash detection across contributing models
nwcio ./BuildingK4/ ./BuildingK4_assembly.glb --clashes --report

# Export detected clashes directly to buildingSMART BCF 2.1 zip archive
nwcio ./BuildingK4/ ./clashes.bcfzip

# Export interactive, self-contained HTML clash report with matrix
nwcio ./BuildingK4/ ./clash_report.html

# Run mesh-level clash detection and export Navisworks Clash Report XML
nwcio ./BuildingK4/ --clash-xml ClashReport.xml --clash-tolerance 0.005

# Run clash detection and export to buildingSMART BCF 2.1 zip archive
nwcio ./BuildingK4/ --clash-bcf Clashes.bcfzip

# Run clash detection with automated proximity grouping and export XML & BCF
nwcio ./BuildingK4/ --clash-xml Clashes.xml --clash-group proximity --clash-radius 1.5
nwcio ./BuildingK4/ --clash-bcf Clashes.bcfzip --clash-group element

# Export to standalone, interactive 3D WebGL HTML viewer (.html) with zero dependencies
nwcio model.nwc --html-viewer model_viewer.html
nwcio ./BuildingK4/ --html-viewer assembly_viewer.html

# Export federated coordination manifest (NWF XML)
nwcio ./BuildingK4/ --nwf-xml Campus.nwf.xml

# List all saved viewpoints and cameras to stdout
nwcio model.nwc --viewpoints

# Export saved viewpoints to buildingSMART BCF 2.1 zip archive
nwcio model.nwc --viewpoints-bcf ./viewpoints.bcfzip

# Export saved viewpoints, section planes, and redlines to Autodesk Navisworks XML format
nwcio model.nwc --viewpoints-xml ./viewpoints.xml

# Simultaneous multi-format export
nwcio ./BuildingK4/ ./assembly.glb --bcf ./clashes.bcfzip --clash-html ./report.html --report

# Inspect Bounding Volume Hierarchy (BVH) spatial index statistics
nwcio model.nwc model.glb --bvh-stats --report

# Test 3D raycast against scene geometry (originX, originY, originZ, dirX, dirY, dirZ)
nwcio model.nwc --raycast "0,0,100,0,0,-1"

# Export Selection Sets and Search Sets to Autodesk Navisworks XML format
nwcio model.nwc selection_sets.xml
nwcio model.nwc --sets-xml ./selection_sets.xml

# Export 4D TimeLiner schedule to CSV spreadsheet format
nwcio model.nwc timeliner.csv
nwcio model.nwc --timeliner-csv ./schedule.csv

# Simulate 4D construction progress at a milestone date
nwcio model.nwc --timeliner-simulate 2026-06-01 --report
```

### Navisworks Appearance Profiler & Color Overrides

```sh
# Apply Appearance Profiler XML rules to recolor model before GLB/IFC export
nwcio model.nwc model.glb --profiler rules.xml

# Export Appearance Profiler rules from document to XML
nwcio model.nwc --export-profiler rules.xml
```

### Exit Codes
- `0` — Success
- `1` — Usage error (missing or invalid arguments)
- `2` — File or directory not found
- `3` — Data format error or parsing failure

---

## Library Usage (C#)

### Reading a Navisworks Document

```csharp
using NwcIO;

// Open document (parses container, properties, GUIDs, transforms, meshes, materials)
var doc = NwcDocument.Open("model.nwc");

Console.WriteLine($"Source RVT:   {doc.FileInfo.SourcePath}");
Console.WriteLine($"Plugin:       {doc.FileInfo.LoaderPlugin}");
Console.WriteLine($"Meshes:       {doc.Meshes.Count}");
Console.WriteLine($"Materials:    {doc.Materials.Count}");
Console.WriteLine($"GUIDs:        {doc.Guids.Count}");
Console.WriteLine($"Transforms:   {doc.Transforms.Count}");

// Save or clone back to Autodesk Navisworks (.nwc)
doc.Save("cloned_model.nwc");

// Export directly to IFC2x3, IFC4, GLB, OBJ, or JSON
doc.SaveIfc("model.ifc"); // Default IFC2x3
doc.SaveIfc("model_ifc4.ifc", new NwcIfcExportOptions
{
    SchemaVersion = NwcIfcSchemaVersion.Ifc4,
    ExportTypeObjects = true,
    UseTessellationInIfc4 = true,
});
doc.SaveGlb("model.glb", worldCoordinates: true);
doc.SaveObj("model.obj", worldCoordinates: true);
doc.SaveJson("model.json");
```

### Programmatic Document Creation (`NwcDocumentBuilder`)

```csharp
using NwcIO;
using NwcIO.Model;

// Construct a new Navisworks document from scratch
var newDoc = new NwcDocumentBuilder()
    .SetFileInfo("ArchitecturalModel.rvt", "LcOpRevitExport")
    .AddMesh(new NwcMesh
    {
        Id = 1,
        Name = "Concrete Slab",
        Vertices = [new(0, 0, 0), new(10, 0, 0), new(10, 8, 0), new(0, 8, 0)],
        Indices = [0, 1, 2, 2, 1, 3],
        BoundingBox = new BoundingBox3d(new(0, 0, 0), new(10, 8, 0))
    })
    .AddTransform(new NwcTransform
    {
        Id = 100,
        Translation = new Vector3d(0, 0, 3.2),
        Rotation = Quaterniond.Identity
    })
    .AddSavedViewpoint(new NwcSavedViewpoint
    {
        Name = "Main Entry View",
        Camera = new NwcCamera
        {
            Position = new Vector3d(5, -15, 12),
            Rotation = Quaterniond.Identity,
            FocalDistance = 20.0
        }
    })
    .AddGuid(Guid.NewGuid())
    .Build();

// Save as a standard Navisworks .nwc file
newDoc.Save("GeneratedModel.nwc");
```

### 3D Raycasting with BVH Spatial Index

```csharp
// Spatial index is built lazily on first access and cached
var spatial = doc.SpatialIndex;

// Fast range query for meshes inside a bounding box
var hits = spatial.QueryBox(new BoundingBox3d(new(0, 0, 0), new(10, 10, 5)));

// Raycast against all polygon triangles in the scene
var origin = new Vector3d(5, 5, 20);
var direction = new Vector3d(0, 0, -1);

if (spatial.Raycast(origin, direction, out var hit))
{
    Console.WriteLine($"Hit mesh #{hit.Mesh.Id} at distance {hit.Distance:F2} m");
    Console.WriteLine($"Intersection point: {hit.Point}");
    Console.WriteLine($"Surface normal:     {hit.Normal}");
}
```

### Multi-Model Federated Coordination & Clash Detection

```csharp
using NwcIO.Model;

// Load an entire folder of models into a federated assembly
var assembly = NwcAssembly.LoadFromDirectory(@"C:\Projects\Campus", searchPattern: "*.nwc");

Console.WriteLine($"Models:   {assembly.Models.Count}");
Console.WriteLine($"Meshes:   {assembly.AllMeshes.Count}");
Console.WriteLine($"Bounds:   {assembly.BoundingBox}");

// Detect geometric clashes between elements from different models
var clashes = assembly.FindClashes(tolerance: 0.001);
foreach (var clash in clashes)
{
    Console.WriteLine($"Clash between {clash.ModelA.FileInfo.SourcePath} and {clash.ModelB.FileInfo.SourcePath}");
    Console.WriteLine($"Overlap volume: {clash.IntersectionBox}");
}

// Export the aggregated multi-discipline model to a single GLB or IFC file
assembly.SaveIfc("Campus_Coordination.ifc");
assembly.SaveGlb("Campus_Coordination.glb");

// Export clashes to standard buildingSMART BCF 2.1 archive (.bcfzip)
assembly.SaveBcf("Campus_Clashes.bcfzip", clashes);

// Export interactive, standalone HTML clash report with discipline matrix
assembly.SaveClashReportHtml("Campus_ClashReport.html", clashes);
```

### Saved Viewpoints & Cameras

```csharp
// Inspect saved viewpoints and camera parameters
foreach (var vp in doc.SavedViewpoints)
{
    var cam = vp.Camera;
    Console.WriteLine($"Viewpoint: {vp.Name} ({cam.Projection})");
    Console.WriteLine($"  Eye:        {cam.Position}");
    Console.WriteLine($"  Direction:  {cam.Direction}");
    Console.WriteLine($"  Target:     {cam.Target}");
    Console.WriteLine($"  Focal dist: {cam.FocalDistance:F2} m, FOV: {cam.FieldOfView:F1}°");
}

// Export saved viewpoints directly to buildingSMART BCF 2.1 zip archive
doc.SaveViewpointsBcf("viewpoints.bcfzip");
assembly.SaveViewpointsBcf("assembly_viewpoints.bcfzip");

// Export viewpoints, clipping planes, and redlines to Autodesk Navisworks XML
doc.SaveViewpointsXml("viewpoints.xml");
assembly.SaveViewpointsXml("assembly_viewpoints.xml");
```

### Dynamic Sectioning, Redlines & Viewpoint Exchange

```csharp
using NwcIO.Model.Redline;
using NwcIO.Model.Sectioning;

// Define dynamic clipping planes and bounding section box
var sectionPlane = NwcSectionPlane.CreateTop(zElevation: 15.0);
var sectionBox = new NwcSectionBox(new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(50, 50, 20)));

// Clip mesh geometry in pure managed C#
var clippedMesh = NwcMeshClipper.Clip(mesh, sectionBox);

// Create saved viewpoint with sectioning and redline markups
var vp = new NwcSavedViewpoint
{
    Name = "Level 2 HVAC Coordination",
    Camera = new NwcCamera
    {
        Position = new Vector3d(10, 20, 15),
        Rotation = Quaterniond.Identity,
        FocalDistance = 15.0,
    },
    SectionPlanes = [sectionPlane],
    SectionBox = sectionBox,
    Redlines =
    [
        new NwcRedlineText
        {
            Text = "Verify clash with concrete beam",
            Position = new Vector3d(10, 12, 14.5),
            Color = new RgbaColor(1f, 0f, 0f, 1f),
            Author = "BIM Coordinator",
        },
        new NwcRedlineArrow
        {
            StartPoint = new Vector3d(10, 12, 16),
            EndPoint = new Vector3d(10, 12, 14.5),
            Color = new RgbaColor(1f, 0f, 0f, 1f),
        },
        new NwcRedlineTag
        {
            TagNumber = 101,
            Position = new Vector3d(10, 12, 14.5),
            Comment = "RFI #42 pending structural approval",
            Status = NwcRedlineStatus.Active,
        }
    ]
};

// Export viewpoints to Navisworks XML exchange format (.xml)
NwcXmlViewpointSerializer.Save("coordination_viewpoints.xml", [vp]);

// Export section-cropped geometry to IFC
doc.SaveIfc("clipped_model.ifc", new NwcIfcExportOptions
{
    SectionBox = sectionBox,
    ClipGeometryWithSectionPlanes = true,
});
```

### Selection Sets & Dynamic Search Sets

```csharp
using NwcIO.Model.Sets;

// Add an explicit selection set
var selection = new NwcSelectionSet("Ground Columns", [10, 11, 12, 13]);

// Add a dynamic search set evaluating properties across the scene graph
var search = new NwcSearchSet("Structural Concrete",
[
    new NwcSearchCondition
    {
        Category = "Item",
        Property = "Type",
        Operator = NwcSearchOperator.Contains,
        Value = "Concrete"
    }
]);

// Evaluate search set dynamically against the document
var matchedNodes = search.Evaluate(doc);
Console.WriteLine($"Found {matchedNodes.Count} matching concrete elements.");

// Export selection sets and search sets to standard Navisworks XML
doc.ExportSelectionSetsXml("sets.xml");
```

### 4D TimeLiner Construction Scheduling & Simulation

```csharp
using NwcIO.Model.TimeLiner;

// Inspect 4D schedule
if (doc.TimeLiner != null)
{
    Console.WriteLine($"Total Planned Cost: {doc.TimeLiner.TotalPlannedCost:C}");
    Console.WriteLine($"Project Duration:   {doc.TimeLiner.OverallStart:d} - {doc.TimeLiner.OverallEnd:d}");

    // Simulate construction progress at a target date
    var milestone = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    double progress = doc.TimeLiner.GetSimulationProgressAt(milestone);
    var activeTasks = doc.TimeLiner.GetActiveTasksAt(milestone);

    Console.WriteLine($"Simulation Progress: {progress * 100:F1}%");
    Console.WriteLine($"Active Tasks:        {activeTasks.Count}");

    // Query node state (NotStarted, InProgress, Completed) for 4D visual simulation
    var state = doc.TimeLiner.GetNodeStateAt(nodeId: 42, milestone);
    Console.WriteLine($"Node #42 state: {state}");

    // Export schedule to standard TimeLiner CSV
    doc.ExportTimeLinerCsv("construction_schedule.csv");
}
```

### Autodesk Navisworks Appearance Profiler

Systematically override colors and transparencies across disciplines (HVAC, piping, architecture, structural):

```csharp
using NwcIO.Model.Appearance;
using NwcIO.Model.Sets;

var profiler = new NwcAppearanceProfiler();

// Add color overrides based on properties
profiler.AddRule(
    name: "Supply Air Blue",
    category: "Mechanical",
    property: "System",
    op: NwcSearchOperator.Equals,
    value: "Supply Air",
    color: new RgbaColor(0.0f, 0.45f, 0.85f, 1.0f),
    transparency: 0.1f);

profiler.AddRule(
    name: "Exhaust Air Magenta",
    category: "Mechanical",
    property: "System",
    op: NwcSearchOperator.Equals,
    value: "Exhaust",
    color: new RgbaColor(0.85f, 0.1f, 0.6f, 1.0f),
    transparency: 0.2f);

// Apply rules to document scene graph and 3D meshes
int overridden = doc.ApplyAppearanceProfiler(profiler);
Console.WriteLine($"Overrode appearance of {overridden} elements.");

// Export profiler rules to standard Autodesk Navisworks XML format (.xml)
doc.ExportAppearanceProfilerXml("appearance_rules.xml");

// Load external XML rules and apply to any model or assembly
var externalRules = NwcXmlAppearanceProfilerSerializer.Open("external_rules.xml");
assembly.ApplyAppearanceProfiler(externalRules);
```

### Navisworks Clash Detective & BCF 2.1 Coordination

```csharp
using NwcIO.Model.Clash;

// Configure a clash test with exclusion rules and tolerance
var test = new NwcClashTest("Structure vs MEP", NwcClashTestType.Hard, tolerance: 0.002);
test.Rules.Add(NwcClashRule.SameLayer());
test.Rules.Add(NwcClashRule.SameFile());

// Execute mesh-level 3D triangle collision detection
doc.RunClashDetection(test);

Console.WriteLine($"Total Clashes: {test.TotalCount} (New: {test.NewCount})");
foreach (var clash in test.Results)
{
    Console.WriteLine($"[{clash.Status}] {clash.Name}: {clash.Item1.Name} vs {clash.Item2.Name}");
    Console.WriteLine($"  Point:    {clash.ClashPoint}");
    Console.WriteLine($"  Distance: {clash.Distance:F3} m");
}

// Export official Autodesk Navisworks Clash Report XML
doc.ExportClashReportXml("ClashReport.xml");

// Export directly to buildingSMART BCF 2.1 archive for Revit / Solibri
doc.ExportClashBcf("ClashIssues.bcfzip");

// Export federated project coordination manifest (NWF XML)
assembly.SaveNwf("CoordinationProject.nwf.xml");
```

### Intelligent Clash Grouping & Clustering

```csharp
using NwcIO.Model.Clash;

// Run clash detection
var test = assembly.RunClashDetection("Federated Test", tolerance: 0.002);

// Automatically group raw clashes by spatial proximity (Union-Find clustering)
int proximityGroups = NwcClashGrouper.GroupByProximity(test, clusterDistance: 1.5, minClusterSize: 2);
Console.WriteLine($"Formed {proximityGroups} spatial clusters with auto-framed viewpoints.");

// Or group by primary continuous MEP element (e.g. pipe or duct run)
NwcClashGrouper.GroupByPrimaryElement(test);

// Export grouped clashes to Autodesk XML (<clashgroup>) and buildingSMART BCF 2.1
NwcXmlClashSerializer.Save("GroupedClashes.xml", [test]);
NwcClashBcfExporter.Save("GroupedIssues.bcfzip", test);
```

### Standalone 3D WebGL HTML Viewer Export

```csharp
using NwcIO.Model.Viewer;

// Export any model or federated assembly to an interactive, zero-dependency HTML viewer
doc.SaveHtmlViewer("BuildingModel.html", new NwcHtmlViewerOptions
{
    Title = "Building 4 Coordination Model",
    IncludeProperties = true,
    IncludeViewpoints = true,
    IncludeClashes = true,
    IncludeSectioning = true,
});

// Or export a multi-discipline assembly
assembly.SaveHtmlViewer("CampusFederated.html");
```

### Quick Properties & Smart Tooltips

```csharp
using NwcIO.Model.Sets;

// Configure quick property definitions
var qp = NwcQuickProperties.CreateDefault();
qp.Add("Revit", "Comments", "User Notes");

// Extract properties from any scene node
var tooltipData = qp.Extract(node);

// Save definitions to standard Navisworks XML format
string xml = qp.ToXml();
File.WriteAllText("quickproperties.xml", xml);
```

---

## Performance

Tested on representative multi-discipline BIM models and campus assemblies:

| Metric | Single Model (~250 KB) | Federated Assembly (~3 MB) | Multi-Building Campus (~10 MB) |
|---|---|---|---|
| **File size** | 250 KB `.nwc` | 2.8 MB (3 files) | ~10 MB (8 buildings) |
| **Meshes** | 573 | 2 861 | 20 000+ |
| **Vertices** | 19 930 | 95 000+ | 550 000+ |
| **Triangles** | 18 784 | 88 000+ | 520 000+ |
| **Parsing time** | **98 ms** | **572 ms** | **< 1.5 s** |
| **Export output** | 971 KB `.glb` | 5.01 MB `.glb` | ~30 MB `.glb` |
| **BVH Build** | < 2 ms | < 6 ms | < 25 ms |

---

## License and trademarks

NwcIO is licensed under the [MIT License](LICENSE). Copyright &copy; 2026 [mahach666](https://github.com/mahach666).

NwcIO is not affiliated with, endorsed by, or sponsored by Autodesk. Autodesk, Navisworks, and Revit are trademarks of Autodesk, Inc.

The file format is read by analysing binary stream structures for interoperability. No Autodesk code or binaries are included.

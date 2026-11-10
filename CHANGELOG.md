# Changelog

All notable changes to the `NwcIO` library and CLI are documented in this file.

## [0.9.0] - 2026-11-11

### Major Additions & Capabilities
- **4D TimeLiner XML Serialization & Exchange (`NwcTimeLinerXmlSerializer`)**:
  - Full reading and writing of Autodesk Navisworks TimeLiner Exchange XML format (`<exchange>` schema).
  - WBS task hierarchies with nested subtasks, planned/actual dates, duration tracking, planned and actual costs, and percentage progress.
  - Roundtrip persistence of attached Selection Set names and explicit attached node IDs.
  - Direct integration into `NwcDocument` (`ExportTimeLinerXml`, `ImportTimeLinerXml`) and `NwcAssembly` (`ExportTimeLinerXml`, `ImportTimeLinerXml`).
- **Automated Rule-Based Schedule Linking (`NwcTimeLinerLinker`)**:
  - `AutoLink`: Automated binding of 4D TimeLiner tasks to model geometry using matching Selection Set or Search Set names (`task.AttachedSelectionSetName` or `task.Name`).
  - Resolves explicit node IDs from `NwcSelectionSet` and evaluates dynamic condition queries from `NwcSearchSet`.
  - `LinkByNodeName`: Direct fuzzy or exact name matching against scene graph elements.
- **Multi-Discipline Coordination Clash Matrix (`NwcClashMatrix`, `NwcClashDiscipline`, `NwcClashMatrixCell`)**:
  - Execution of comprehensive $N \times M$ BIM clash coordination matrices across discipline models (Architecture, Structure, MEP, etc.).
  - Automated matrix population from federated assembly models (`NwcClashMatrix.CreateFromAssembly`).
  - Spatial pruning and double-precision AABB intersection penetration calculation.
  - Ignore rules evaluation (same layer, same file, tolerance thresholds).
  - Formatted Markdown coordination summary table generation (`ToMarkdownTable`).
  - Structured JSON report export (`SaveJson`, `ExportClashMatrixJson`).
  - Unified buildingSMART BCF 2.1 zip archive export (`SaveBcf`, `ExportClashMatrixBcf`), creating issue topics with 3D camera viewpoints and highlighted IFC components for coordination in Revit, Solibri, and BIMcollab.
- **Interactive 4D TimeLiner & Selection Sets HTML WebGL Viewer (`NwcHtmlViewerExporter`)**:
  - Integrated 4D TimeLiner playback controller with Play/Pause button, timeline slider scrubber, dynamic date label, and speed multiplier (1x, 5x, 15x, 60x).
  - Dynamic 3D simulation color coding: active construction glowing green (`[0.2, 0.95, 0.2]`), demolition highlighted red (`[0.95, 0.2, 0.2]`), temporary elements yellow, and unstarted elements hidden from the render loop.
  - Interactive Selection Sets sidebar tab (`#tab-sets`) rendering hierarchical folder trees and selection items with instant 3D model highlighting on click.
- **CLI Enhancements (`nwcio`)**:
  - Added `--timeliner-xml <file.xml>`: Export or import 4D TimeLiner schedule exchange XML.
  - Added `--link-timeliner`: Automatically links schedule tasks to Selection Sets, Search Sets, or element names.
  - Added `--clash-matrix <file.json>`: Executes multi-discipline coordination matrix and exports JSON report.
  - Added `--clash-matrix-md <file.md>`: Exports clash matrix coordination report as Markdown table.
  - Added `--clash-matrix-bcf <file.bcfzip>`: Exports clash matrix conflicts to buildingSMART BCF 2.1 zip archive.

## [0.8.0] - 2026-11-11

### Major Additions & Capabilities
- **Model Revision Comparison & Diff Engine (`NwcDiffEngine`, `NwcDiffTypes`)**:
  - Full revision comparison of Navisworks documents and federated assemblies (`baseline.Compare(revision, options)`).
  - Identification of added, deleted, modified, and unchanged BIM elements and scene nodes by GUID and composite hierarchy matching.
  - Double-precision 3D geometric deviation detection, measuring translation shift vectors and bounding box alterations beyond configurable tolerance (default 5mm).
  - Deep property record delta inspection, tracking added, removed, and modified parameter values across categories.
  - JSON revision delta report export (`report.SaveJson`, `report.ToJson`).
  - buildingSMART BCF 2.1 zip export (`report.SaveBcf`), generating issue topics with framed 3D viewpoints and highlighted IFC component GUIDs.
- **Spatial Grid System & Axis Management (`NwcGridSystem`, `NwcGridLine`, `NwcGridIntersection`)**:
  - Strongly typed object model for building axes and coordination grids (`NwcGridSystem`, `NwcGridLine`, `NwcGridIntersection`).
  - Automatic 2D/3D intersection calculation between crossing grid lines (e.g. "A / 1", "B / 4").
  - Point-to-grid spatial querying: `FindNearestGrid` and `FindNearestIntersection`.
  - Human-readable grid location formatting (`FormatLocation`), displaying exact grid intersection tags or signed perpendicular offsets with direction awareness (e.g. "A (+0.50m) / 1 (-0.20m)").
  - Automatic population of `clash.GridLocation` on `NwcClashResult` and `NwcClashGroup` (`doc.AssignGridLocations`, `assembly.AssignGridLocations`), seamlessly integrating with `NwcClashGrouper.GroupByGrid`.
  - Automatic extraction of grid systems from document hierarchy and metadata (`doc.ExtractGridSystem()`).
  - Full XML exchange serialization and deserialization (`SaveXml` / `LoadXml`).
- **High-Precision 3D Measure Engine (`NwcMeasureEngine`)**:
  - Point-to-point 3D Euclidean distance, coordinate deltas ($\Delta X, \Delta Y, \Delta Z$), horizontal distance, vertical height difference, pitch angle, and azimuth direction.
  - Point-to-mesh shortest distance with Christer Ericson's exact Voronoi-region closest point on triangle algorithm.
  - Mesh-to-mesh clearance measurement and closest vertex pair identification.
  - Cumulative polyline path length measurement.
  - Triangle surface area and unit normal vector calculation.
  - Mesh total surface area, closed manifold solid volume calculation ($\sum \frac{1}{6}(v_0 \cdot (v_1 \times v_2))$), and 3D centroid calculation.
  - Angle measurement between direction vectors and 3-point vertices.
- **Interactive HTML 3D Viewer Enhancements**:
  - Interactive 3D measurement tool: measure distances directly in the browser by clicking two points on the model, displaying dimension lines, distance badges, and coordinate deltas.
  - Spatial grid lines overlay: rendering grid lines with coordinate bubble labels directly over the 3D scene.
- **CLI Enhancements (`nwcio`)**:
  - Added `--diff <baseline.nwc>`: Compares current model against a baseline revision.
  - Added `--diff-json <file.json>`: Exports revision comparison report to JSON.
  - Added `--diff-bcf <file.bcfzip>`: Exports revision comparison issues to buildingSMART BCF 2.1 archive.
  - Added `--diff-tolerance <m>`: Sets geometric displacement tolerance for diff (default: 0.005m).
  - Added `--grids <file.xml>`: Loads a spatial grid system from XML and assigns grid locations.
  - Added `--export-grids <file.xml>`: Exports extracted or configured grid systems to XML.

### Major Additions & Capabilities
- **Intelligent Clash Grouping & Clustering Engine (`NwcClashGrouper`)**:
  - Spatial 3D proximity clustering (`GroupByProximity`) using Disjoint-Set (Union-Find) algorithm, grouping thousands of raw collisions into manageable spatial clusters with auto-calculated cluster centroids and optimal framing viewpoints.
  - Element-based grouping (`GroupByPrimaryElement`) bundling all collisions involving the same continuous MEP run (pipe, duct, cable tray) or structural beam into a single issue.
  - Structural grid grouping (`GroupByGrid`) clustering collisions by architectural and structural grid intersections.
  - Discipline & layer grouping (`GroupByLayer`) bundling clashes by layer or discipline pairs.
  - Reversible grouping lifecycle (`UngroupAll`) dissolving groups back into individual results when needed.
  - Full reading and writing of `<clashgroup>` elements in Autodesk Navisworks Clash Report XML (`NwcXmlClashSerializer`).
  - Grouped buildingSMART BCF 2.1 export (`NwcClashBcfExporter`), exporting clash groups as unified topics with consolidated viewpoints, descriptions, and participating IFC component GUID selections.
- **Standalone 3D WebGL HTML Viewer Export (`NwcHtmlViewerExporter`)**:
  - Direct export of Navisworks documents and federated assemblies to self-contained, interactive single-file 3D HTML viewers (`.html`) with zero external script, CDN, or server dependencies.
  - High-performance WebGL rendering with smooth orbit controls (rotate, pan, zoom), mesh instancing, material colors, and HUD scene statistics.
  - Dynamic multi-axis section clipping planes (Cut X, Cut Y, Cut Z sliders) running directly in WebGL fragment shaders.
  - Embedded saved viewpoints palette with instant camera fly-to transitions.
  - Integrated Clash Navigator allowing review of clash groups and collision points in interactive 3D.
  - Property Inspector panel displaying full hierarchical BIM properties of selected elements.
  - Added `NwcDocument.SaveHtmlViewer(...)` and `NwcAssembly.SaveHtmlViewer(...)`.
- **Quick Properties & Tooltip Rules (`NwcQuickProperties`)**:
  - Strongly typed object model for configuring Navisworks Quick Properties tooltips (`NwcQuickProperties`, `NwcQuickPropertyDefinition`).
  - Built-in standard BIM presets for element names, types, materials, layers, areas, and volumes.
  - Smart node extraction and full roundtrip Navisworks XML serialization (`ToXml` / `FromXml`).
- **CLI Enhancements (`nwcio`)**:
  - Added `--html-viewer <file.html>` / `--viewer <file.html>`: Exports models and assemblies to self-contained 3D WebGL HTML viewers.
  - Added `--clash-group <proximity|element|grid|layer>`: Automatically clusters raw clashes before exporting XML reports or BCF archives.
  - Added `--clash-radius <meters>`: Configurable spatial radius for proximity clustering (default: 1.0 m).

## [0.6.0] - 2026-11-10

### Major Additions & Capabilities
- **Dynamic 3D Sectioning & Mesh Clipping Engine**:
  - Strongly typed sectioning model (`NwcIO.Model.Sectioning`): `NwcSectionPlane`, `NwcSectionBox`, and `NwcSectionAlignment`.
  - Up to 6 simultaneous dynamic clipping planes conforming to Autodesk Navisworks sectioning modes (`Top`, `Bottom`, `Front`, `Back`, `Left`, `Right`, `View`, `Custom`).
  - High-performance 3D polygon clipping engine (`NwcMeshClipper`) based on Sutherland-Hodgman polygon clipping, splitting triangle meshes against arbitrary section planes and oriented section boxes in pure managed C#.
- **Redline Markup Annotations**:
  - Comprehensive redline markup object model (`NwcIO.Model.Redline`): `NwcRedline`, `NwcRedlineText`, `NwcRedlineLine`, `NwcRedlineArrow`, `NwcRedlineCloud`, `NwcRedlineEllipse`, `NwcRedlineTag`, `NwcRedlineStatus`.
  - Support for 2D/3D markup positions, custom RGBA colors, font size, stroke thickness, revision authors, timestamps, and review status workflows.
  - Linked to `NwcSavedViewpoint.Redlines` collection.
- **Autodesk Navisworks XML Viewpoint Exchange (`NwcXmlViewpointSerializer`)**:
  - Full reading and writing of Autodesk Navisworks saved viewpoints exchange XML (`viewpoints.xml`) conforming to `nw-exchange-12.0.xsd`.
  - Preserves camera projection, positions, orientation quaternions, active clipping section planes, and all redline markup annotations.
- **buildingSMART BCF 2.1 Viewpoint & Redline Bridge (`NwcViewpointBcfExporter`)**:
  - Exports saved viewpoints, clipping planes (`<ClippingPlanes>`), and redline line/markup annotations (`<Lines>`) directly to BCF 2.1 zip archives (`.bcfzip`).
  - Cross-platform BIM collaboration bridge enabling Navisworks markups and sections to be viewed directly in Revit, Solibri, Archicad, and BIMcollab.
- **Section-Aware IFC Export**:
  - Extended `NwcIfcExportOptions` with `SectionBox`, `SectionPlanes`, and `ClipGeometryWithSectionPlanes`.
  - Automatically clips 3D geometry during IFC export (`Ifc2x3` and `Ifc4`), generating clean cropped IFC models.
- **Document & Assembly API Integration**:
  - Added `NwcDocument.SaveViewpointsXml(...)` and `NwcDocument.ToViewpointsXml()`.
  - Added `NwcAssembly.SaveViewpointsXml(...)` and `NwcAssembly.ToViewpointsXml()`.
- **CLI Enhancements (`nwcio`)**:
  - Added `--viewpoints-xml <file.xml>`: exports saved viewpoints, active section planes, and redlines to Autodesk Navisworks XML format.

### Major Additions & Capabilities
- **Navisworks Clash Detective & 3D Triangle Collision Engine**:
  - High-performance, clean-room 3D clash detection engine (`NwcClashDetector`):
    - Broad-phase collision pruning using bounding volume hierarchy (BVH) and axis-aligned bounding boxes (AABB).
    - Narrow-phase exact triangle-to-triangle intersection testing using Möller's algorithm in pure managed C#.
    - Exact clash point calculation and penetration distance / clearance computation.
    - Automatic creation of isometric perspective viewpoints focused on each collision volume.
  - Strongly typed object model (`NwcIO.Model.Clash`): `NwcClashTest`, `NwcClashResult`, `NwcClashObject`, `NwcClashComment`, `NwcClashRule`, `NwcClashSelection`, `NwcClashBatch`.
  - Clash status lifecycle: `New`, `Active`, `Reviewed`, `Approved`, `Resolved`.
  - Test types: `Hard`, `Clearance`, `Duplicates`, `Conservative`.
  - Configurable exclusion rules: `IgnoreSameLayer`, `IgnoreSameGroup`, `IgnoreSameFile`.
- **Autodesk Navisworks Clash Report XML Exchange (`NwcXmlClashSerializer`)**:
  - Full reading and writing of Autodesk Navisworks Clash Report XML files (`nw-clashreport-11.0.xsd`).
  - Serializes `<batchtest>`, `<classtests>`, `<classtest>`, `<rules>`, `<summary>`, `<clashresults>`, `<clashresult>`, `<clashpoint>`, `<gridlocation>`, `<clashobjects>`, `<comments>`.
- **buildingSMART BCF 2.1 Issue Management (`NwcClashBcfExporter`)**:
  - Direct export of clash detection results to buildingSMART BCF 2.1 zip archives (`.bcfzip`) without external dependencies.
  - Each clash result is mapped to a `BcfTopic` with perspective camera parameters, visual framing, and component selections referencing 22-character IFC GUIDs.
  - Direct bridge between Navisworks clash results and open-standard BIM coordination tools (Revit, Solibri, Archicad, BIMcollab).
- **Navisworks Federated Project Manifest (NWF) (`NwcNwfManifest`)**:
  - Model coordination manifest representing federated NWF projects: external model links, saved viewpoints, selection/search sets, appearance profiler rules, and clash detective tests.
  - `assembly.SaveNwf("project.nwf.xml")` and `NwcAssembly.ToNwfManifest()`.
- **Document & Assembly API Integration**:
  - `NwcDocument.RunClashDetection(test)` and `NwcAssembly.RunClashDetection(testName, tolerance)`.
  - `NwcDocument.ExportClashReportXml(path)` and `NwcDocument.ExportClashBcf(path)`.
  - `NwcDocument.LoadClashReportXml(path)`.
  - `NwcDocument.FindSet(name)` and `NwcDocument.FindNodes(condition)`.
  - `NwcDocumentBuilder.AddClashTest(test)`.
- **CLI Enhancements (`nwcio`)**:
  - Added `--clash-xml <report.xml>`: runs clash detection and exports Navisworks Clash Report XML.
  - Added `--clash-bcf <issues.bcfzip>`: runs clash detection and exports buildingSMART BCF 2.1 archive.
  - Added `--clash-tolerance <meters>`: sets clash detection tolerance (default: 0.001 m).
  - Added `--nwf-xml <manifest.xml>`: exports federated coordination manifest in NWF XML format.

## [0.4.0] - 2026-11-10

### Major Additions & Capabilities
- **Autodesk Navisworks Appearance Profiler**:
  - Strongly typed appearance rule and profiler model: `NwcAppearanceRule` and `NwcAppearanceProfiler`.
  - Color overrides (RGB) and transparency levels (0.0 to 1.0) dynamically mapped to scene graph nodes and geometry meshes using `NwcSearchCondition` criteria.
  - Scene traversal engine: `NwcAppearanceProfiler.Apply(NwcDocument)` applies rules across the full scene tree and geometry meshes, updating element materials.
  - Multi-model assembly support: `NwcAssembly.ApplyAppearanceProfiler(NwcAppearanceProfiler)` applies rules across all contributing federated models.
  - XML exchange: `NwcXmlAppearanceProfilerSerializer` reading and writing Autodesk Navisworks Appearance Profiler rule files (`.xml`) adhering to the official `nw-exchange-12.0.xsd` schema.
- **Fluent Builder & Document Integration**:
  - Added `NwcDocument.ApplyAppearanceProfiler(...)` and `NwcDocument.ExportAppearanceProfilerXml(...)`.
  - Added `NwcDocumentBuilder.SetAppearanceProfiler(...)` and `NwcDocumentBuilder.AddAppearanceRule(...)`.
- **CLI Enhancements (`nwcio`)**:
  - Added `--profiler <rules.xml>`: loads appearance profiler XML rules and applies color/transparency overrides before exporting to GLB, IFC, OBJ, JSON, or NWC.
  - Added `--export-profiler <rules.xml>`: exports the document's appearance profiler rules to an XML file.
- **Cross-Ecosystem Interoperability (NwcIO &harr; IfcIO)**:
  - Integration testing between NwcIO and IfcIO verifying end-to-end NWC &rarr; IFC STEP physical file generation &rarr; `IfcModel.Open` deserialization of meshes, vertices, triangles, and property sets.

## [0.3.0] - 2026-11-10

### Major Additions & Capabilities
- **Selection Sets & Dynamic Search Sets**:
  - Strongly typed selection set object model: `NwcSelectionSet`, `NwcSearchSet`, `NwcSelectionSetFolder`, and base `NwcSetItem`.
  - Dynamic query evaluator: `NwcSearchCondition` with rich comparison operators (`Equals`, `NotEquals`, `Contains`, `StartsWith`, `EndsWith`, `Wildcard`, `GreaterThan`, `LessThan`, etc.) evaluating against node properties, categories, classes, names, and element GUIDs.
  - `NwcSearchSet.Evaluate(NwcDocument)` recurses through the full scene graph hierarchy to resolve matching nodes at runtime.
  - Standard Autodesk Navisworks Selection Sets XML (`.xml`) reader and writer (`NwcXmlSetSerializer`) for importing and exporting set exchange definitions (`<exchange><selectionsets>`).
- **4D TimeLiner Construction Schedules**:
  - Strongly typed schedule and task model: `NwcTimeLinerSchedule`, `NwcTimeLinerTask`, task types (`Construct`, `Demolish`, `Temporary`), and simulation states (`NotStarted`, `InProgress`, `Completed`).
  - Hierarchical WBS (Work Breakdown Structure) task trees with planned/actual start/end dates, planned/actual costs, and selection set bindings.
  - 4D Construction Simulation Engine: query temporal progress via `GetActiveTasksAt(DateTimeOffset)`, `GetSimulationProgressAt(DateTimeOffset)`, and `GetNodeStateAt(int nodeId, DateTimeOffset)`.
  - Standard TimeLiner CSV exchange (`NwcTimeLinerCsvSerializer`) supporting standard task schedule spreadsheet import and export.
- **Binary Partition Stream Serialization & Ingestion**:
  - Stream classifiers and binary raw stream handlers for Selection Sets (`tag 100 + 105` / XML payload) and TimeLiner (`tag 100 + 106` / CSV payload).
  - Roundtrip persistence of sets and schedules inside Navisworks `#LcUStream` containers via `NwcStreamSerializer`.
- **CLI Enhancements (`nwcio`)**:
  - Added `--sets-xml <output.xml>` option and automatic `.xml` extension routing for exporting Navisworks Selection Sets.
  - Added `--timeliner-csv <output.csv>` option and automatic `.csv` extension routing for exporting 4D TimeLiner schedules.
  - Added `--timeliner-simulate <YYYY-MM-DD>` option for querying construction progress, active tasks, and element states at a specific milestone date.
  - Extended `--report` output with Selection Sets hierarchy counts and TimeLiner schedule/budget summary metrics.

## [0.2.0] - 2026-11-10

### Major Additions & Capabilities
- **Programmatic NWC Generation & Serializer (`NwcWriter`)**:
  - Full reverse-engineered serialization of Autodesk Navisworks `#LcUStream` containers and stream partitions from pure managed C#.
  - Binary partition serialization: `PartitionHeader` (source metadata, loader plugin, timestamp, options), `SceneTree` (hierarchy nodes, tags 52/53, property types), `Transforms` (poses, quaternion rotations, bounding boxes), `Guids` (16-byte raw identifier tables), `Geometry` chunk records (IEEE 754 float vertices, record tags), and `Toc` (Table of Contents record lengths and chunk sizes).
  - Built-in compression using managed `ZLibStream` producing valid, specification-compliant back-to-back zlib stream containers.
  - Saved viewpoint and 3D camera serialization (tag 517).
- **Document Save & Fluent Builder API**:
  - `NwcDocument.Save(string path)` and `NwcDocument.Save(Stream stream)` methods for direct file output.
  - `NwcDocument.Create(...)` factory method for creating documents from in-memory objects.
  - `NwcDocumentBuilder` fluent builder pattern for easily creating `.nwc` documents from code.
  - `NwcDocument.Open(Stream stream)` for loading Navisworks documents directly from arbitrary streams.
- **CLI Enhancement (`nwcio`)**:
  - Added `--nwc <output.nwc>` option and automatic `.nwc` extension routing for converting, saving, and merging models and assemblies into `.nwc` files.
- **Full Verification**:
  - Comprehensive unit and integration test coverage verifying bit-level roundtripping (`Create -> Save -> Open -> Verify`) across synthetic models and real-world multi-discipline fixtures.

## [0.1.1] - 2026-11-10

### Security & Clean-Room Governance
- **Strict Clean-Room Policy**: Full abstraction of external test fixtures via `NWCIO_TEST_FILES` environment variable and optional uncommitted `test_files/` directory, mirroring the RvtIO standard.
- **Fixture Resolution Helper**: Introduced `TestFiles` helper class in `NwcIO.Tests` with zero hardcoded filesystem paths or machine-specific fallback directories.
- **Documentation**: Added `docs/test-fixtures.md` documenting clean-room testing principles, test skipping rules, and fixture expectations.
- **Anonymized Benchmarks**: Scrubbed all specific project references, real industrial facility names, and local test model filenames from README, format notes, and tests.

## [0.1.0] - 2026-11-10

### Initial Public Release

- **Pure Managed Navisworks Binary Engine**:
  - Full clean-room parser for Autodesk Navisworks binary streams (`.nwc`, `.nwd`, `.nwf`): `LcOStream`, `LcRawStream`, and compressed chunk hierarchies via internal Deflate decompression.
  - Complete hierarchical scene graph extraction (models, layers, partition nodes, composite instances, geometries).
  - Comprehensive parameter and property set extraction (`NwcPropertySet`, `NwcProperty`).
  - Target modern **.NET 8.0** and **.NET 10.0** with zero native dependencies or external libraries.
- **Advanced Multi-Format Exporters**:
  - **buildingSMART IFC (IFC2x3 & IFC4)**:
    - IFC2x3 via `IfcFacetedBrep` geometry representation.
    - IFC4 via compact modern tessellation: `IfcTriangulatedFaceSet` and `IfcCartesianPointList3D` under representation type `'Tessellation'` (>60% smaller file sizes, sub-second export).
    - IFC Type Products: automatic generation and association of `IfcTypeProduct` entities (`IfcWallType`, `IfcSlabType`, `IfcColumnType`, `IfcBeamType`, `IfcPipeSegmentType`, etc.) via `IfcRelDefinesByType`.
    - Semantic entity classification mapping Navisworks objects to standard IFC classes.
  - **glTF 2.0 Binary (GLB)**:
    - Interleaved binary buffers with PBR material definitions, per-element metadata, and embedded viewpoints / cameras.
  - **Wavefront OBJ**:
    - Direct export of textured geometry (`.obj` + `.mtl`).
  - **Structured JSON**:
    - Export of complete scene hierarchy, bounding boxes, property sets, clash test results, and camera definitions.
- **BCF 2.1 & Clash Reports**:
  - BuildingSMART compliant BIM Collaboration Format 2.1 zip export (`.bcfzip` / `.bcf`).
  - Interactive offline standalone single-file HTML clash matrix reports with responsive tables, severity badges, and 3D preview metadata.
- **Saved Viewpoints & 3D Cameras**:
  - Reverse-engineered Navisworks viewpoint stream format (stream tag 517).
  - Perspective and Orthographic camera models, focal distance, field of view, aspect ratio, camera look rotation math.
  - Automatic translation of viewpoints into glTF camera nodes, BCF viewpoints, and JSON.
- **Spatial Indexing & Clash Detection**:
  - Surface Area Heuristic (SAH) Bounding Volume Hierarchy (BVH) spatial index.
  - Fast AABB vs AABB, triangle-triangle, and raycasting acceleration for clash detection between federated models (`NwcAssembly`).
- **Cross-Platform CLI Tool (`nwcio`)**:
  - Available as a .NET Tool and executable CLI for `info`, `export`, `clash`, and `view` commands.

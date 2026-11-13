# Changelog

All notable changes to the `NwcIO` library and CLI are documented in this file.

## [Unreleased]

## [0.11.0] - 2026-11-14

### Changed
- **Breaking:** `NwcDocument.Open` (and `.nwf`/folder loading) reads Navisworks model files through the strict reader and throws `InvalidDataException` naming the segment when a file breaks the DbCache, hierarchy, fragment or geometry grammar, the record links or the input limits, where 0.10.0 returned a partial document. (#166)
- **Breaking:** Appearance-profiler XML now writes `greater_or_equal` and `less_or_equal` for the inclusive comparisons instead of `greater_equal` and `less_equal` (both XML formats read both spellings), and an unknown operator string throws `FormatException` instead of becoming `Equals`; a missing or empty operator still means `Equals`. Opening a file whose set XML holds an unknown operator now throws `FormatException` instead of loading the condition as `Equals`. (#114)
- **Breaking:** `NwcAssembly.OpenNwf` resolves relative references only under the `.nwf` directory (trailing subfolders first, then the bare file name); 0.10.0 checked the process working directory first. `NwcXRef.Exists(baseDirectory)` follows the same rule. References that resolve nowhere are reported in `UnresolvedXRefs` and the model is skipped. Models loaded from a `.nwf` can therefore differ from 0.10.0 (fewer duplicates, no unrelated working-directory files). (#124)
- **Breaking:** `NwcIfcExportOptions.Schema` now accepts only `"IFC2X3"` and `"IFC4"` (case-insensitive) and throws `ArgumentException` for anything else, including `null`, instead of silently selecting IFC2X3. (#125)
- **Breaking:** Write `NwcIfcExportOptions.Author` into the IFC `FILE_NAME` author list, which repeated `Organization` before. The `Author` default is now an empty string, so default exports have an empty author in the header and in the owner history's `IfcPerson`. (#128)
- **Breaking:** Both `NwcClashDetector.Run` overloads and `NwcDocument.RunClashDetection` throw `ArgumentException` naming the test and the set, for selections that use set names and no node ids or filter, when a named set exists in none of the models; a set present in only some models of an assembly does not throw. (#144)
- **Breaking:** Embed TimeLiner schedules in `.nwc` files as TimeLiner XML instead of CSV, which keeps the schedule name, task hierarchy, progress, node links and multi-line task names; files with the old CSV payload still open. Earlier NwcIO versions cannot read a schedule embedded by this version. Standalone CSV import and export are unchanged. (#150)
- **Breaking:** The container reader now checks the Adler-32 trailer of every zlib stream. A stream with a missing or mismatched trailer, such as one cut off at the end of the file, is dropped and its bytes are counted as a gap (`TrailingGap` at the end of the file); `Read` still does not throw. (#159)
- **Breaking:** `NwcWriter` validates the document before writing and throws `ArgumentException`, leaving the destination untouched, for input the reader would drop or change: an unrecognised header, meshes with a vertex count or coordinates outside the reader's limits, property records or scene-tree nodes with an empty name or class, and viewpoint names with unrecognised characters, an out-of-range UTF-8 length or leading/trailing spaces. A null or blank viewpoint name is still written with the `Viewpoint N` fallback. (#163)
- **Breaking:** NWF manifests now store `<Viewpoints>` and `<AppearanceRules>` as nested exchange XML and write the date in UTC; older files still load, but external consumers of the old flat `<Viewpoint>`/`<Rule>` elements must read the nested exchange XML. (#164)
- **Breaking:** BCF 2.1 exports from `NwcClashBcfExporter`, `NwcViewpointBcfExporter` and `BcfExporter` now validate against the official buildingSMART `markup.xsd` and `visinfo.xsd`: BCF namespaces and the placeholder `IfcProject` values are removed, elements are in schema order, `File/Date` is an element, `Components` are written before the camera, and `Lines` before the clipping planes. `NwcViewpointBcfExporter` keeps its `projectName` parameter but BCF 2.1 markup no longer stores it. (#165)
- **Breaking:** Model files now produce one `NwcMesh` and one `NwcTransform` per fragment. `Id` of both is the fragment index; node ids are depth-first preorder indices; `NwcMesh.Node` is its leaf, `NwcNode.Meshes` holds every placed mesh and `NwcNode.Mesh` the first. Records shared between fragments share arrays. Persisted ids and IFC/BCF GUIDs derived from them change for these files. (#166)
- **Breaking:** `NwcNode.Transform` and `NwcNode.ElementGuid` are null on the strict path. (#166)
- **Breaking:** Opening a `.nwd` whose hierarchy holds partition nodes (class 32) throws `NotSupportedException` unless `BuildSceneTree` is false. The CLI appends a `--no-tree` hint and still exits with code 3. `--no-tree` (which skips reading the scene tree and is required to open a `.nwd` with partition nodes) now also applies to `.nwf` input, folder input and the `--diff` baseline. (#166)
- **Breaking:** `NwcWriter` rejects, before writing, transforms with a scale other than 1, meshes that hold line segments and meshes whose triangles differ from the single strip the reader rebuilds over the positions, with `ArgumentException`. (#166)
- **Breaking:** Clash detection skips pairs of fragments that belong to the same element, with or without the scene tree, and so does the box-only clash matrix; federated clash items take the parent layer name as `Layer`, as single-document runs do; `ExplicitNodeIds` match node ids (mesh ids for meshes without a node). (#166)
- **Breaking:** `NwcDbCacheEntry.Offset` is an absolute file offset, `Extra` is `a + b*2^32`, and `NwcDbCacheDirectory.DatabaseName` is the name of the `LcOpDBCache` entry or `""`. The directory is parsed only when its head is -1 and its entries fill the stream exactly; other `LcOpDBCache` streams leave `NwcDocument.DbCache` null. (#166)
- **Breaking:** Clash XML written by `NwcXmlClashSerializer` now contains elements and attributes that are not in the Navisworks clash report schema (`selectiona`/`selectionb` in every test, `guid`, rule attributes, comment `status` and `date`, an `O`-format date `value`), so a consumer that validates it against that schema can reject it. (#161)
- GLB, OBJ and IFC export reverse triangles of mirrored placements, only when baking world coordinates. The triangle clash detector never tests line-only meshes as triangles; diff and appearance rules use every mesh of a node; grids take the first line mesh of a node. (#166)
- The JSON `transforms` block writes `"scale"` when the pose scale is not 1; unscaled output is unchanged. HTML viewer selection-set clicks match meshes by node id, and diff geometry counts cover every fragment of a node. (#166)
- `NwcReaderOptions.ReadPropertyRecords = false` leaves node properties empty on the strict path as well. (#166)
- Non-finite position floats and non-finite geometry box floats make a model file fail with `InvalidDataException`. (#166)
- The reader bounds crafted input: pointer nesting and tree height at most 256, at most 8,388,608 expanded tree nodes and 134,217,728 expanded property references, and at most one render vertex or corner per geometry-record byte. Input beyond these limits fails with `InvalidDataException`. (#166)

### Added
- Add `Quaterniond.Normalized()`, which returns a unit-length quaternion and falls back to `Identity` for a zero, near-zero or non-finite quaternion. (#113)
- Add `NwcAssembly.UnresolvedXRefs` and `NwcAssembly.SkippedXRefs` (references that are missing, or that resolve to files other than `.nwc`/`.nwd`) and `NwcXRef.TryResolve`. The CLI prints a warning for each of them and reports the component-file count next to `Loaded Models`. (#124)
- Strict geometry reader for Navisworks model files. It runs when section 1 of the container holds exactly one stream that parses as a DbCache directory naming `LcOpNwdLogicalHierarchy`, `LcOpNwdFragments` or `LcOpNwdGeometry`; every other container, including files written by NwcIO, keeps the previous reader. (#166)
- Triangle strips, polylines and stored normals are decoded from geometry records; node links, scaled poses and mirrored placements are read from the hierarchy and fragments. (#166)
- New public members: `NwcTransform.Scale`, `NwcMesh.Normals`, `NwcMesh.LineIndices` and `NwcNode.Meshes`. (#166)
- Node properties are read on the strict path: `Class` is the property tab name (category, for example `Element`), `Name` the property name and `FirstInt` the key object id. (#166)

### Fixed
- Normalize `Rotation` in `NwcTransform.ToMatrix4x4()` and `NwcCamera.Rotate()`, so non-unit quaternions give a pure rotation matrix and unit-length `Direction` and `Up`. (#113)
- Keep the `Wildcard` search operator through the appearance-profiler XML round-trip; it was written as `equals` and read back as `Equals`, so rules matched a different set of items. (#114)
- Stop `NwcHtmlViewerExporter.Save` (and `NwcDocument.SaveHtmlViewer` / `NwcAssembly.SaveHtmlViewer`) from writing document defaults into the caller's `NwcHtmlViewerOptions`, so reusing one options object no longer leaks the first document's grids, TimeLiner and selection sets into later exports. (#115)
- `NwcTimeLinerLinker.LinkByNodeName` skips tasks with null, empty or whitespace names instead of linking them to every node, which also affected the CLI `--link-timeliner` fallback. (#116)
- Write `ActualStart`, `ActualEnd` and `ActualCost` of a TimeLiner task to XML independently, so an in-progress task with a start or cost but no end is no longer dropped on save. (#116)
- Keep the winding of triangles that `NwcMeshClipper.Clip` cuts with two vertices kept; the output triangles were reversed, so they faced the wrong way. (#119)
- Accept a `SourceTimestampUtc` with `DateTimeKind.Local` when saving; it is converted to UTC instead of throwing `ArgumentException` on machines with a non-zero UTC offset. (#120)
- `NwcWriter.Write(document, path)` serializes the document before creating the file, so a failed save no longer creates or truncates the file. (#120)
- Stop the exported HTML viewer's properties pane from rendering model strings (element name, layer, property category, name and value) as HTML; the pane is now built with text nodes, so markup in an untrusted source model can no longer run script in the viewer. (#121)
- Resolve relative `.nwf` component references only under the federation directory, trailing subfolders first and then the bare file name, so a same-named file in the working directory no longer takes the place of the component. Load each resolved component once even when several references point to the same file. (#124)
- Import and link TimeLiner schedules before single-document CLI exports, including primary CSV output and `--timeliner-csv`. (#126)
- Count each mesh pair once when a federated clash test uses identical selections on both sides. (#126)
- Fail on invalid TimeLiner XML before an existing output file is overwritten. (#126)
- Read the XRef table of `.nwf` files whose binary prefix before the JSON contains a `{` byte; the parser now retries from each following `{` until the root object parses, instead of losing the table. (#127)
- Write `.ifc` and `.obj` files without a UTF-8 byte order mark, so IFC files start with `ISO-10303-21;`. (#128)
- Keep model names out of the HTML clash report matrix's `onclick` script: cells carry HTML-encoded `data-model-a`/`data-model-b` attributes, so names containing quotes, `<` or `&` can no longer break or inject script. (#129)
- Read back saved viewpoints whose camera rotation has W = 0 (for example a 180 degree camera), and classify streams that hold only such viewpoints as saved views. (#130)
- Fix `Raycast` when the ray origin lies inside a spatial index node: BVH nodes are now ordered and pruned by entry distance clamped to 0, so hits within a finite `maxDistance` are no longer missed and the closest hit is returned. (#131)
- Stop non-finite numbers from aborting GLB, JSON and HTML viewer exports. GLB leaves out viewpoints with a non-finite position or rotation and uses default camera parameters when they are not finite; JSON writes non-finite viewpoint camera fields as `null`; the HTML viewer omits a non-finite bounding box, writes non-finite vertex coordinates as 0 and drops the camera of a viewpoint with a non-finite position or target. (#132)
- Write an enabled section box in viewpoint XML only as `<section_box>`, not also as six `<clip_planes>`, so each save and reload no longer adds six section planes to `NwcXmlViewpointSerializer` round trips. XML written by 0.10.0 still reads back with the six extra planes until it is saved again. (#133)
- Keep same-named materials of different models distinct in federated GLB export: `NwcAssembly.AllMaterials` no longer drops later materials that share a name (for example `Concrete`) with an earlier one, and GLB export adds a material the caller did not list instead of falling back to material index 0; meshes without a material are written without one instead of taking material 0. (#134)
- Map folder, `.nwf` and `--profiler` failures to the documented CLI exit codes: a missing or empty folder exits with code 2, and a corrupt model in a folder, a corrupt `.nwf` or a malformed appearance profiler XML exits with code 3, each with a plain `Error: <message>` line instead of an unhandled exception and stack trace. (#135)
- Select only the meshes of the named sets in `NwcClashSelection`: a set name that resolves to no meshes, or an assembly model that lacks the set, now adds nothing instead of selecting every mesh, and only a selection with no sets, node ids or filter (such as `All()`) selects everything. (#144)
- Include grouped clashes in BCF exports (`ExportClashBcf`, `NwcClashMatrix.SaveBcf`, CLI `--clash-bcf` with `--clash-group`), with one topic per group and one per ungrouped clash, and in the clash matrix: `NwcClashMatrixCell.Clashes` and the `SaveJson` `clashes` array now cover every clash, matching `clashesCount`. (#145)
- Fix `NwcDiffEngine` reporting false `Deleted` items when several elements share a GUID, or when assembly models share a mesh name or GUID: each baseline item is now matched at most once, preferring the same model. (#146)
- Clip transformed meshes against section planes in world space; translated or rotated meshes were cut in the wrong place, which affected IFC export with `SectionPlanes`, `SectionBox` and `ClipGeometryWithSectionPlanes`. The result keeps its local vertices and `Transform`. (#147)
- `NwcMesh.GetWorldBoundingBox()` now reflects a replaced or removed `Transform`, and `NwcGridSystem.GetIntersections()` and `FormatLocation` reflect edits to grid line `Start`/`End` or to `Lines`, instead of returning stale cached values. (#148)
- Classify TOC and geometry streams before the GUID-table heuristic, so `.nwc` files written by NwcIO with three meshes or similar sizes no longer gain spurious GUIDs when reopened. (#149)
- IFC export computes `Length_X`/`Width_Y`/`Height_Z` from the exported geometry, so they follow the length unit, mesh transform, coordinate space and section clipping (clipped meshes no longer lose them), and are written as `IFCLENGTHMEASURE` instead of `IFCREAL`. (#151)
- Clash detection visits each pair of overlapping selections once: fix missed clashes for partially overlapping selections (for example A={1,2} against B={1,3}) and duplicate pairs in overlapping and federated runs. (#152)
- Container reader keeps scanning past gaps larger than 4096 bytes, so later streams and sections are no longer silently dropped. (#153)
- Triangle intersection test in clash detection uses scale-relative tolerances (small triangles no longer snap onto planes they miss), rejects zero-area triangles, and tests coplanar pairs by projected 2D overlap instead of bounding boxes. (#154)
- Fix `NwcSearchCondition.Evaluate`: the `Item` category no longer matches every property class, other categories match exactly (ignoring case) instead of by substring, and `NotEquals` matches only when a value exists and none equals the target, so a missing property no longer satisfies it. (#155)
- Write valid glTF from the GLB exporter for empty scenes: empty top-level arrays and the empty buffer and BIN chunk are omitted. Meshes with out-of-range indices keep only their whole valid triangles, and meshes with no valid triangle or with a non-finite position are skipped instead of being written as invalid data. (#156)
- Draw large meshes correctly in the HTML viewer: meshes with more than 65,535 vertices are split into 16-bit-index chunks inside one viewer entry per mesh, so hierarchy, selection and Fit Selection still treat it as one mesh, and wireframe mode draws real triangle edges instead of pairing consecutive indices. Invalid and incomplete triangles are skipped in split meshes. (#157)
- IFC2X3 export no longer writes IFC4-only entities: pipes and ducts are written as `IFCFLOWSEGMENT` and the roof type falls back to `IFCBUILDINGELEMENTPROXYTYPE`. IFC4 output is unchanged. (#158)
- `MeasureMeshToMesh` computes the real triangle-to-triangle distance instead of sampling vertices. Crossing meshes report distance 0 with the shared point, and the closest points between edges are found. (#160)
- `NwcXmlClashSerializer` keeps test, group and result GUIDs, rule types and parameters, comment ids and statuses, group metadata (assigned to, approved by, grid location, comments, created date) and `SelectionA`/`SelectionB` on a save/load round trip. Created dates keep their offset and fractional seconds instead of being read as the load time, and unknown or out-of-range rule types fall back to `Custom`. (#161)
- Malformed stream fields no longer make `NwcDocument.Open` throw framework exceptions. An out-of-range partition timestamp is ignored, overflowing DbCache name lengths skip the directory, malformed or overflowing geometry TOC records are bounded to the available bytes or stop mesh reading, and malformed binary-wrapped selection set XML skips the sets. (#162)
- `ToNwfManifest` links each model to the cache file it was loaded from (falling back to `SourcePath` for documents opened from a stream), and `SaveNwf` writes the links relative to the manifest folder. The NWF manifest also keeps assembly selection sets, appearance rules, viewpoints, the clash test type and the UTC date on a write/read round trip. (#164)
- Read every DbCache directory entry; the parser stopped at the first entry that was not 8-byte aligned. (#166)
- Read `.nwd` geometry with more than 5000 chunks. (#166)

### Documentation
- Correct the SDK version stated in the README (`global.json` pins 10.0.401) and remove the dead Code of Conduct link. (#122)
- Use one copyright holder, `mahach666`, in `LICENSE`, the package metadata and the README. Describe all CLI outputs (IFC, GLB, OBJ, JSON, BCF, HTML, NWC) in the `NwcIO.Cli` package description, and make the NuGet readme self-contained with absolute links. (#123)
- Fix the README and getting-started IFC samples to use `SchemaVersion = NwcIfcSchemaVersion.Ifc4`, and correct the glTF camera (`yfov`) and IFC2x3 Brep vertex-sharing descriptions in the format notes. (#125)
- Correct the README and getting-started API/CLI examples and the discipline-pair tolerance assignments. (#126)
- Rebuild `docs/public-api.md` from the actual public surface: remove types that do not exist, replace `NwcProperty`/`NwcPropertyCategory` with `NwcPropertyRecord`, add the missing federation, clash, TimeLiner, sets, diff, sectioning and other types, and state that `NwcIO.Internal.*` types are public but unsupported implementation surface. (#136)
- Add a "Scope and frozen features" section to the README listing the extras that now receive bug fixes only and their planned replacements. (#137)
- Rewrite the format notes for the logical hierarchy, fragments, table of contents, geometry records and DbCache directory, and update the README and public API notes for the new reader and the `.nwd` scene-tree limitation. (#166)

### Dependencies
- Bump `actions/setup-dotnet` from 4 to 6 in the CI and release workflows. (#138)
- Bump `actions/checkout` from 4 to 7 in the CI and release workflows. (#139)
- Bump `actions/upload-artifact` from 4 to 7 in the CI and release workflows. (#140)
- Bump the `IfcIO` test dependency from 0.3.0 to 0.10.0. (#141)
- Bump `Microsoft.NET.Test.Sdk` from 18.0.1 to 18.12.0 (test tooling only). (#142)
- Bump `xunit.runner.visualstudio` from 3.1.4 to 4.0.1 (test tooling only). (#143)

## [0.10.0] - 2026-11-11

### Major Additions & Capabilities
- **Microsoft Project XML 4D Schedule Serialization (`NwcTimeLinerMsProjectXmlSerializer`)**:
  - Full reading and writing of Microsoft Project XML schedule schema (`http://schemas.microsoft.com/project`).
  - Preserves hierarchical WBS outlines (`OutlineLevel`, `OutlineNumber`, `WBS`), planned and actual dates, ISO 8601 duration (`PT#H0M0S`), percent complete, and costs.
  - Roundtrips attached Selection Set names and `TaskType` through task `Notes`.
  - Direct integration into `NwcDocument` and `NwcAssembly` via `ExportTimeLinerMsProject` and `ImportTimeLinerMsProject`.
- **Primavera P6 XER 4D Schedule Serialization (`NwcTimeLinerPrimaveraXerSerializer`)**:
  - Full reading and writing of Primavera P6 tab-delimited XER exchange format (`ERMHDR`, `%T PROJECT`, `%T PROJWBS`, `%T TASK`).
  - Serializes task codes, activity names, target and actual start/finish timestamps, durations in hours, physical completion percentages, and planned/actual costs.
  - Direct integration into `NwcDocument` and `NwcAssembly` via `ExportTimeLinerPrimavera` and `ImportTimeLinerPrimavera`.
- **Interactive 3D WebGL Viewer Enhancements (`NwcHtmlViewerExporter`)**:
  - **6-Sided Section Box Volume**: Switchable sectioning mode between 3-axis cutting planes and a 6-sided clipping box. Includes interactive box scaling, invert mode (inside vs outside clipping volume), visual 3D wireframe bounding box overlay projection, and instant "Fit Selection" bounding box fitting.
  - **Smooth Camera Animations**: Cubic ease-in-out animated camera transitions when focusing saved viewpoints or clash detection conflict items.
  - **Turntable Presentation Mode**: 360° continuous turntable orbit animation for interactive model review and project presentations.
  - **Saved Viewpoints Walkthrough Tour**: Automated tour player (`Play Tour` / `Pause Tour`) cycling through all saved viewpoints with smooth camera transitions and pauses.
- **CLI Commands (`nwcio`)**:
  - Added `--timeliner-msproject <file.xml>`: Export or import 4D TimeLiner schedule in Microsoft Project XML format.
  - Added `--timeliner-primavera <file.xer>`: Export or import 4D TimeLiner schedule in Primavera P6 XER format.

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

## [0.7.0] - 2026-11-10
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

## [0.5.0] - 2026-11-10

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
  - Available as a .NET Tool and executable CLI taking an input file or federated folder plus an optional output path and long options (IFC/GLB/OBJ/JSON/BCF/HTML/NWC export, clash detection, clash matrix, TimeLiner, viewpoints, revision diff).

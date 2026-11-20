---
title: Public API
---

# Public API Surface

The supported surface is the `NwcIO` and `NwcIO.Model.*` namespaces listed here and the `NwcIO.Scene` package. The `NwcIO.Scene` package (`NwcSceneReader`, `NwcSceneOptions`, `NwcSceneResult`) is documented on the [scene adapter](scene.md) page. The `NwcIO.Internal.*` types (chunk decoders, byte streamers, container reader/writer, JSON/GLB/OBJ/IFC exporters) are `internal` implementation details: they are not part of the package API and are not covered by compatibility guarantees.

## Core Document & Hierarchy (`NwcIO`, `NwcIO.Model`)

| Type | Description |
|---|---|
| `NwcDocument` | Primary document container holding the root scene graph node, meshes, materials, viewpoints, selection sets, and TimeLiner schedules. Opens `.nwc`, `.nwd`, and `.nwf` files via `NwcDocument.Open`. `.nwd` scene trees are decoded: each appended model is a top-level partition node (class `LcOaPartition`) named after its source file. Trees that place one Revit element under two parents (linked models) are decoded: the repeated node is marked `IsLinkPlacement`. `IsRevitSource` is true when the loader plugin is exactly the Revit loader `LcNwcLoaderPlugin:lcldrevit` (Navisworks reading a `.rvt`) or the Revit NWC exporter `nwexportrevit`. `LengthUnit` and `MetresPerUnit` give the length unit of coordinates and length values: feet (0.3048 m) for those two loaders, otherwise `Unknown` and null. |
| `NwcProgress` | Progress payload for the cancellation-aware reader overloads and the `.nwc` and JSON export overloads (`export-nwc`, `export-json`): a phase name, completed units, and total units (0 when unknown). |
| `NwcDocumentBuilder` | Fluent builder API for creating Navisworks documents and geometry programmatically from code. |
| `NwcWriter` | Direct `#LcUStream-V1.1` binary container serializer and continuous zlib chunk writer. Throws `ArgumentException` before writing for input it cannot store faithfully: an unrecognised header, scaled transforms, line meshes, triangle indices outside the vertex buffer, out-of-range vertex counts or coordinates, records or nodes without a name/class, and viewpoint names the reader would rename. Mesh ids, names and triangle indices are stored; normals and materials are not. `Materials`, `XRefs`, `DbCache`, `AppearanceProfiler`, `ClashTests` and `GridSystem` are not written; the output is read back by NwcIO but has not been verified in Autodesk Navisworks. `WriteNative` writes an interoperable cache with independently owned triangle meshes or line segments and optional line vertex colours. A mesh must use a single topology. |
| `NwcReaderOptions` | Per-open options: which streams to read (property records, GUIDs, transforms, meshes, viewpoints, selection sets, TimeLiner), whether to read materials (`ReadMaterials`: decoded from the file on the strict path, named in property records elsewhere, with a neutral inferred colour unless a `revit_Color` record gives one), whether to build the scene tree (`BuildSceneTree`), catalog retention, and `Strict` (throw `InvalidDataException` when an error-level diagnostic is recorded). |
| `NwcAssembly` | Federated multi-model assembly: `Create`, `LoadFromDirectory` (several patterns, recursion), `OpenManifest` (`.nwf.xml`), `OpenNwf` (with `UnresolvedXRefs`/`SkippedXRefs` reporting), combined meshes/materials/bounds, per-assembly exports and clash runs. |
| `NwcDiagnostic` / `NwcDiagnosticSeverity` | Reader warning for skipped or substituted data (`Severity`, stable `Code`, `Message`, optional `StreamIndex` and `ByteOffset`), listed in `NwcDocument.Diagnostics` and `NwcAssembly.Diagnostics`. Codes: `meshes-skipped` (geometry records dropped because their vertex count is out of range or they are truncated or malformed; not raised on the strict DbCache path), `property-scan-failed` (the first malformed string met by the lenient property scan, with its stream index and byte offset; not raised on the strict DbCache path), `ContainerTrailingGap` (bytes after the last container stream, including a truncated zlib stream), `NwfUnresolvedXref` and `NwfSkippedXref` (from `NwcAssembly.OpenNwf`). They are included in JSON export (`diagnostics`) and printed by `nwcio --report`. |
| `NwcNode` | Scene graph tree node representing layers, categories, composite elements, and geometry instances with optional transformations and properties. For Navisworks model files read through their DbCache directory (the strict path), `Meshes` lists every mesh placed on a leaf and `Mesh` the first, ids are depth-first preorder indices, `Transform` is null; `IsElement` marks Revit element nodes, `ElementGuid` is their export GUID from `LcOpGuidStore`, `IfcGlobalId` is the GlobalId the Revit IFC export gives the element: its valid `IFC_GUID` parameter, else its export GUID in the IFC base-64 form (the rule of `RvtIO.Scene`), and `Element` points every node inside an element (and the second node of a sibling pair that repeats one element) to its element node; properties include the tabs stored in `LcOaPartitionProps`; other files leave `Meshes` empty and `Mesh` null. |
| `NwcPropertyRecord` | Property record (`Class`, `Name`, `Value`, `ValueType`, `Tag`, `FirstInt`; typed `IntValue`, `DoubleValue`, `ReferenceId`, `ReferenceTag`, `ReferenceClass`). On node properties of the strict path, `Class` is the property tab name (category, for example `Element`), `Name` the property name and `FirstInt` the key object id. Records scanned from property streams (`NwcDocument.PropertyRecords`, and node properties of other files) keep their internal class names. `TabInternalName` and `InternalName` are the internal names of the tab and the key on strict-path node properties, null on scanned records. |
| `NwcPropertyTab` | One property tab of a strict-path node (`NwcNode.PropertyTabs`, in stored slot order): display `Name`, `InternalName`, `Properties`. A tab object shared by several nodes is one instance. |
| `NwcMesh` | Triangle mesh with vertices, indices, `Normals` aligned with vertices, `LineIndices` for polylines, world-space transform, bounding box, and material. Model files hold one mesh per placed fragment; `Id` is the fragment index. Meshes of fragments that share a geometry record share their `Vertices`, `Indices`, `Normals`, `LineIndices` and `LineColors` arrays; do not mutate them. `GeometryIndex` (the geometry record; −1 outside model files) and `LineColors` (per vertex, line meshes of model files). |
| `NwcMaterial` | Colour and PBR material. On the strict path, `Name` comes from the nearest class-31 material on the mesh's leaf or an ancestor (`Default` when absent or empty), `Color` comes from the fragment's diffuse appearance with alpha `1 - transparency`, and `DiffuseTexture` comes from its Generic diffuse map. Each colour channel is rounded to a byte (halves away from zero). `Roughness` and `Metallic` keep their defaults. `NwcDocument.Materials` lists distinct name, RGBA and texture combinations in order of first use. With `BuildSceneTree = false`, appearances with a diffuse image still supply a material named `Default`. Elsewhere names come from material references in property records and the colour from a `revit_Color` record that follows the definition. A material without that record gets a neutral grey and `IsInferred = true`. Meshes without a material share one neutral `Default Material` per document with `IsInferred = true`. |
| `NwcTransform` | Parsed pose/transform records with quaternion rotation, uniform `Scale` (negative for a mirrored placement) and affine 4×4 matrix conversion. Model files hold one transform per fragment, with the same `Id` as its mesh. |
| `NwcFileInfo` | Source path, loader plugin, export timestamp from partition header, format version, text encoding, and chunk marker from section headers. |
| `NwcDbCacheDirectory` & `NwcDbCacheEntry` | The `LcOpDBCache` directory (`NwcDocument.DbCache`, null when the file has none or it cannot be parsed). `DatabaseName` is the name of the `LcOpDBCache` entry or `""`; `Entries` lists every entry in file order and `Find` looks one up by segment name (exact or suffix, case-insensitive). Each entry has `Name`, `Flags`, `Offset` (an absolute file offset), `Length` and `Extra` (`a + b*2^32` of its two trailing 32-bit fields). |
| `NwcXRef` & `NwcXRefTable` | External references with suffix-based resolution (`TryResolve`, `Exists`) and the parsed XRef table. |
| `NwcCamera`, `NwcSavedViewpoint`, `NwcCameraProjection` | Camera model (position, unit rotation quaternion, computed direction/up/target) and saved viewpoints. `Id` is null for viewpoints read from Navisworks files (none is stored). The camera's field of view of a viewpoint read from a Navisworks `.nwc`/`.nwd` is the 60° default, not the stored value, and only some real saved-view streams are recognised. Files written by `NwcWriter` carry a trailer that restores id, field of view, far distance, projection, comments and sectioning. |
| `Vector3d`, `Quaterniond`, `BoundingBox3d`, `RgbaColor` | Double-precision geometry primitives and RGBA colour. |
| `NwcClash` | Simple pairwise clash check between two meshes. Frozen: bug fixes only. |

`NwcDocument.GetLoadedPath()` returns the full path of a document opened by filename. It returns `null` for
documents opened from a stream or created in memory. `NwcFileInfo.SourcePath` is the source path stored in the file.

`NwcDocument.Open` overloads for paths and streams accept an `IProgress<NwcProgress>` and
`CancellationToken`. Cancellation is checked during container scanning and between parsing phases.
A non-seekable input uses a temporary file. With `KeepStreamCatalog = false`, stream payloads
are inflated on demand during parsing.

`NwcDocument.Save` and `SaveJson` accept cancellation and progress for paths and streams.
Path saves replace the destination after the output has been written successfully. A cancelled stream
save can leave partial output in the caller's stream. `OpenAsync`, `SaveAsync` and `SaveJsonAsync`
use asynchronous reads and writes for file and stream overloads and pass the cancellation token
to the underlying I/O. Parsing, serialization and compression perform synchronous CPU work between I/O operations.
Stream overloads leave the supplied stream open.

The document's `SaveObjAsync`, `SaveGlbAsync`, `SaveIfcAsync`, `SaveViewpointsBcfAsync`,
`SaveViewpointsXmlAsync` and `SaveHtmlViewerAsync` accept file paths or streams and a
`CancellationToken`. OBJ and IFC also accept a `TextWriter`. The compatibility formatters
run on a worker thread and forward writes and flushes to the destination's async methods with
the token. Successful path exports replace the destination atomically. Cancellation may leave
partial output in a caller-owned stream or writer, which remains open.

`NwcAssembly.LoadFromDirectoryAsync` and `NwcAssembly.OpenNwfAsync` read model files asynchronously
and pass cancellation through to each read. They load models sequentially in the same order as their
synchronous counterparts. Directory discovery and reference resolution use synchronous filesystem metadata.
The `load-models` phase reports each folder model, or the primary NWF and each of its reference entries,
including missing, duplicate and unsupported references.

`NwcDocument.LengthUnit` and `MetresPerUnit` describe the original coordinates. Set
`NwcReaderOptions.LengthUnit` to override inference. Clash detection, proximity grouping,
clash matrix cells and revision diff all interpret tolerances in the input coordinate units.
Grid locations and length measurements keep these values and display the corresponding unit
symbol. The measurement overloads accept a unit when their input is a standalone point or mesh.
IFC converts known coordinates to its selected output unit. BCF camera positions and orthogonal
view scales are exported in metres, using the original coordinates when the unit is unknown.
Viewpoint XML labels default to the document's unit when known and accept an explicit label.
`NwcAssembly.HasMixedLengthUnits` and the `MixedLengthUnits` diagnostic report mixed units.
Assembly geometry retains its original coordinates.

## Coordination & Clash Detection (`NwcIO.Model.Clash`)

| Type | Description |
|---|---|
| `NwcClashDetector` | Broad-phase bounding-box checks and narrow-phase 3D triangle-to-triangle collision detection (`Run` over a document or federated assembly, driven by an `NwcClashTest`). Frozen: bug fixes only. |
| `NwcClashTest`, `NwcClashSelection`, `NwcClashResult`, `NwcClashObject` | Clash test definition (type, tolerance, per-side selections), results, and the involved elements. Frozen: bug fixes only. |
| `NwcClashBatch`, `NwcClashRule`, `NwcClashComment` | Batch test container, filtering rules (ignore thresholds, custom predicates), and review comments. Frozen: bug fixes only. |
| `NwcClashGroup` | Grouped clash clusters produced by `NwcClashGrouper`. Frozen: bug fixes only. |
| `NwcClashGrouper` | Automated clustering of clashes by proximity, element, grid line, or layer. Frozen: bug fixes only. |
| `NwcClashMatrix`, `NwcClashDiscipline`, `NwcClashMatrixCell` | $N \times M$ multi-discipline coordination matrix with per-cell tolerance/type, automated execution, Markdown generation, and BCF 2.1 export. Frozen: bug fixes only. |
| `NwcClashBcfExporter` | buildingSMART BCF 2.1 export of clash tests and results. Frozen: bug fixes only. |
| `NwcXmlClashSerializer` | Roundtrip Navisworks Clash Report XML (`nw-clashreport-11.0.xsd`) reader and writer. Frozen: bug fixes only. |
| `NwcNwfManifest` | Federation manifest used by `NwcAssembly` tooling. |

`NwcClashDetector.Run` and the `RunClashDetection` methods on `NwcDocument` and `NwcAssembly`
offer overloads with `IProgress<NwcProgress>` and `CancellationToken`. The `clash-detection` phase
reports the number of meshes processed in the first selection, starting at zero and reaching the
total on completion. An empty selection reports `0/0` once. Cancellation is checked between meshes
and during triangle comparisons, including clearance tests. If cancelled, the run throws
`OperationCanceledException` and discards the results it added, preserving prior results and groups.
The convenience methods register a test only after it completes.

Clash tests and matrix cells use a spatial index to find candidate pairs in selection order.
Each run reuses transformed vertices, and hard clash tests cull triangles outside the shared
region before testing for intersection. Proximity grouping finds nearby clashes with a spatial grid.

## 4D TimeLiner, Selection Sets & Appearance (`NwcIO.Model.TimeLiner`, `.Sets`, `.Appearance`)

| Type | Description |
|---|---|
| `NwcTimeLinerSchedule` & `NwcTimeLinerTask` | WBS task hierarchy, planned/actual dates, costs, progress, and simulation queries. Frozen: bug fixes only. |
| `NwcTimeLinerXmlSerializer` | Full roundtrip serialization of Navisworks TimeLiner Exchange XML. Frozen: bug fixes only. |
| `NwcTimeLinerMsProjectXmlSerializer` | Microsoft Project XML schedule import/export. Project XML has no UTC offset field, so dates are written as UTC and read back as UTC. Frozen: bug fixes only. |
| `NwcTimeLinerPrimaveraXerSerializer` | Primavera P6 XER schedule import/export. XER has no UTC offset field, so dates are written as UTC and read back as UTC. Frozen: bug fixes only. |
| `NwcTimeLinerCsvSerializer` | CSV schedule export. CSV has no UTC offset field, so dates are written as UTC and read back as UTC. Frozen: bug fixes only. |
| `NwcTimeLinerLinker` | Rule-based automated linking of schedule tasks to geometry. Frozen: bug fixes only. |
| `NwcSelectionSet`, `NwcSearchSet`, `NwcSelectionSetFolder`, `NwcSetItem` | Manual selection sets, dynamic query search sets, and set folders. Frozen: bug fixes only. |
| `NwcSearchCondition` & `NwcSearchOperator` | Search criteria over node built-ins and property records (equals, contains, wildcard, comparisons). |
| `NwcXmlSetSerializer` | Navisworks selection-set XML exchange. Frozen: bug fixes only. |
| `NwcQuickProperties` | Curated quick-properties view over property records. |
| `NwcAppearanceProfiler`, `NwcAppearanceRule`, `NwcXmlAppearanceProfilerSerializer` | Rule-based colouring by search conditions and its XML exchange format. Frozen: bug fixes only. |

## 3D Geometry & Spatial Analysis (`NwcIO.Model.Grids`, `.Measure`, `.Diff`, `.Spatial`, `.Sectioning`, `.Redline`)

| Type | Description |
|---|---|
| `NwcGridSystem`, `NwcGridLine`, `NwcGridIntersection` | Building grid lines and axes with 2D/3D intersection calculations and spatial clash labelling. Frozen: bug fixes only. |
| `NwcMeasureEngine` | Double-precision 3D point-to-point, point-to-mesh and mesh-to-mesh distance, clearance, and area/volume calculations. Frozen: bug fixes only. |
| `NwcDiffEngine` (with `NwcDiffReport`, `NwcDiffOptions`, `NwcElementDiff`) | Revision comparison and differential geometry/property analysis. Frozen: bug fixes only. |
| `NwcSpatialIndex` | SAH bounding-volume hierarchy for raycasts and accelerated proximity queries (`RaycastHit`). Frozen: bug fixes only. |
| `NwcSectionPlane`, `NwcSectionBox`, `NwcSectionAlignment`, `NwcMeshClipper` | Section planes and six-sided section boxes with mesh clipping. Frozen: bug fixes only. |
| `NwcXmlViewpointSerializer`, `NwcViewpointBcfExporter` | Navisworks viewpoint XML exchange and BCF viewpoint export. Frozen: bug fixes only. |
| `NwcRedline*` (`NwcRedline`, `Arrow`, `Cloud`, `Ellipse`, `Line`, `Tag`, `Text`, `NwcRedlineStatus`) | Redline annotation records. Frozen: bug fixes only. |

## Export (`NwcIO`, `NwcIO.Model.Viewer`)

| Type | Description |
|---|---|
| `NwcDocument.SaveIfc` / `NwcAssembly.SaveIfc` | ISO 10303-21 STEP export supporting IFC2x3 Coordination View and modern IFC4 mesh tessellation (`IfcTriangulatedFaceSet`), configured via `NwcIfcExportOptions` (`NwcIfcSchemaVersion`, `NwcIfcLengthUnit`). Frozen: bug fixes only. |
| `NwcDocument.SaveGlb` / `SaveObj`, `NwcAssembly.SaveGlb` / `SaveObj` | Binary glTF 2.0 and Wavefront OBJ geometry export entry points. Frozen: bug fixes only. |
| `NwcDocument.SaveHtmlViewer` / `NwcAssembly.SaveHtmlViewer` | Interactive single-file 3D WebGL viewer exporter with 4D schedule playback (`NwcHtmlViewerExporter`, `NwcHtmlViewerOptions`). Frozen: bug fixes only. |
| `NwcJsonExportOptions` | Indentation and content toggles for JSON export (`NwcDocument` and the CLI). |

In the HTML viewer, use Tab to reach the viewpoint, clash, hierarchy and set buttons, then Enter or
Space to activate them. With focus on the 3D model view, the arrow keys orbit, + and - zoom, and
Home resets the camera. Escape clears the current measurement while Measure is active.

OBJ saves to a path, including `SaveObjAsync`, write a sibling `<name>.mtl` when exported geometry
has materials. `NwcMesh.TexCoords` holds optional per-vertex UVs with a lower-left origin. OBJ
writes `vt` and indexes it from faces, including when writing to a stream or `TextWriter`.
The material library stores diffuse RGB (`Kd`), opacity (`d`), and `map_Kd` for a diffuse image.
GLB writes `TEXCOORD_0` with a top-left origin, and `baseColorTexture` for external PNG or JPEG images.
`NwcMaterial.DiffuseTexture` holds the image reference, including any XRef remapping. Exporters
emit references to local paths. Keep the referenced images available at those paths. Relative
references are interpreted relative to the exported asset. Embedded image payloads and other
material channels remain outside this support.
An OBJ save with materials requires a sibling filename that can be used in an `mtllib` directive.
The export rejects a symbolic link at the material library path.

GLB geometry export requires a writable system temporary directory for its BIN chunk. The
exporter removes the temporary file when it closes. A path-based save replaces the destination
after the export succeeds. The complete GLB is limited to 4,294,967,292 bytes, including its
headers and 4-byte padding. [glTF 2.0 binary layout](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#binary-gltf-layout)

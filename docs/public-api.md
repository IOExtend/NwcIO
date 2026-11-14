---
title: Public API
---

# Public API Surface

The supported surface is the `NwcIO` and `NwcIO.Model.*` namespaces listed here. The `NwcIO.Internal.*` types (chunk decoders, byte streamers, container reader/writer, JSON/GLB/OBJ/IFC exporters) are also compiled as `public` — the tests and the CLI consume them directly — but they are implementation details: **they are not covered by compatibility guarantees and may change in any release.** Making them `internal` is tracked separately (#103).

## Core Document & Hierarchy (`NwcIO`, `NwcIO.Model`)

| Type | Description |
|---|---|
| `NwcDocument` | Primary document container holding the root scene graph node, meshes, materials, viewpoints, selection sets, and TimeLiner schedules. Opens `.nwc`, `.nwd`, and `.nwf` files via `NwcDocument.Open`; `.nwd` scene trees with partition nodes, and trees that place one Revit element under two parents (linked models), throw `NotSupportedException` unless `BuildSceneTree` is false. `LengthUnit` and `MetresPerUnit` give the length unit of coordinates and length values: feet (0.3048 m) for the Revit loader `LcNwcLoaderPlugin:lcldrevit`, otherwise `Unknown` and null. |
| `NwcDocumentBuilder` | Fluent builder API for creating Navisworks documents and geometry programmatically from code. |
| `NwcWriter` | Direct `#LcUStream-V1.1` binary container serializer and continuous zlib chunk writer. Throws `ArgumentException` before writing for input it cannot store faithfully: an unrecognised header, scaled transforms, line meshes, triangles other than the single strip the reader rebuilds, out-of-range vertex counts or coordinates, records or nodes without a name/class, and viewpoint names the reader would rename. Normals, materials and mesh names are not stored. |
| `NwcReaderOptions` | Per-open options: which streams to read (property records, GUIDs, transforms, meshes, viewpoints, selection sets, TimeLiner), whether to read materials (`ReadMaterials`: decoded from the file on the strict path, assigned by heuristics elsewhere), whether to build the scene tree (`BuildSceneTree`), catalog retention. |
| `NwcAssembly` | Federated multi-model assembly: `Create`, `LoadFromDirectory`, `OpenNwf` (with `UnresolvedXRefs`/`SkippedXRefs` reporting), combined meshes/materials/bounds, per-assembly exports and clash runs. |
| `NwcNode` | Scene graph tree node representing layers, categories, composite elements, and geometry instances with optional transformations and properties. For Navisworks model files read through their DbCache directory (the strict path), `Meshes` lists every mesh placed on a leaf and `Mesh` the first, ids are depth-first preorder indices, `Transform` is null; `IsElement` marks Revit element nodes, `ElementGuid` is their export GUID from `LcOpGuidStore`, `IfcGlobalId` is the GlobalId the Revit IFC export gives the element: its valid `IFC_GUID` parameter, else its export GUID in the IFC base-64 form (the rule of `RvtIO.Scene`), and `Element` points every node inside an element (and the second node of a sibling pair that repeats one element) to its element node; properties include the tabs stored in `LcOaPartitionProps`; other files leave `Meshes` empty and `Mesh` null. |
| `NwcPropertyRecord` | Property record (`Class`, `Name`, `Value`, `ValueType`, `Tag`, `FirstInt`). On node properties of the strict path, `Class` is the property tab name (category, for example `Element`), `Name` the property name and `FirstInt` the key object id. Records scanned from property streams (`NwcDocument.PropertyRecords`, and node properties of other files) keep their internal class names. `TabInternalName` and `InternalName` are the internal names of the tab and the key on strict-path node properties, null on scanned records. |
| `NwcPropertyTab` | One property tab of a strict-path node (`NwcNode.PropertyTabs`, in stored slot order): display `Name`, `InternalName`, `Properties`. A tab object shared by several nodes is one instance. |
| `NwcMesh` | Triangle mesh with vertices, indices, `Normals` aligned with vertices, `LineIndices` for polylines, world-space transform, bounding box, and material. Model files hold one mesh per placed fragment; `Id` is the fragment index. Meshes of fragments that share a geometry record share their `Vertices`, `Indices`, `Normals`, `LineIndices` and `LineColors` arrays; do not mutate them. `GeometryIndex` (the geometry record; −1 outside model files) and `LineColors` (per vertex, line meshes of model files). |
| `NwcMaterial` | Colour and PBR material. On the strict path, with meshes and the scene tree, each mesh's material is decoded from the file: `Name` is the name of the nearest class-31 material on its leaf or an ancestor (`Default` when there is none or it is empty), `Color` is the diffuse colour of its appearance with alpha `1 - transparency`, each channel rounded to a byte (halves away from zero), and `Roughness` and `Metallic` keep their defaults (the file's shininess is not mapped); one material per distinct name and colour bytes, `Id` in order of first use, and `NwcDocument.Materials` lists them; `NwcNode.Material` stays null. Without the scene tree there are no materials. Elsewhere materials are assigned by heuristics, not decoded from material data in the file: names come from material references in property records, colours are inferred from those names (or a `revit_Color` property), and each mesh gets a material by its mesh name and size. A default Concrete preset is added when no material is named `Concrete` (or `Бетон`), and a default Rebar preset when none is named `Rebar` (or `Арматура`); a mesh that matches no rule gets the first material. |
| `NwcTransform` | Parsed pose/transform records with quaternion rotation, uniform `Scale` (negative for a mirrored placement) and affine 4×4 matrix conversion. Model files hold one transform per fragment, with the same `Id` as its mesh. |
| `NwcFileInfo` | Source path, loader plugin, and export timestamp from the partition header. |
| `NwcDbCacheDirectory` & `NwcDbCacheEntry` | The `LcOpDBCache` directory (`NwcDocument.DbCache`, null when the file has none or it cannot be parsed). `DatabaseName` is the name of the `LcOpDBCache` entry or `""`; `Entries` lists every entry in file order and `Find` looks one up by segment name (exact or suffix, case-insensitive). Each entry has `Name`, `Flags`, `Offset` (an absolute file offset), `Length` and `Extra` (`a + b*2^32` of its two trailing 32-bit fields). |
| `NwcXRef` & `NwcXRefTable` | External references with suffix-based resolution (`TryResolve`, `Exists`) and the parsed XRef table. |
| `NwcCamera`, `NwcSavedViewpoint`, `NwcCameraProjection` | Camera model (position, unit rotation quaternion, computed direction/up/target) and saved viewpoints. |
| `Vector3d`, `Quaterniond`, `BoundingBox3d`, `RgbaColor` | Double-precision geometry primitives and RGBA colour. |
| `NwcClash` | Simple pairwise clash check between two meshes. |

## Coordination & Clash Detection (`NwcIO.Model.Clash`)

| Type | Description |
|---|---|
| `NwcClashDetector` | Broad-phase BVH and narrow-phase 3D triangle-to-triangle collision engine (`Run` over a document or federated assembly, driven by an `NwcClashTest`). |
| `NwcClashTest`, `NwcClashSelection`, `NwcClashResult`, `NwcClashObject` | Clash test definition (type, tolerance, per-side selections), results, and the involved elements. |
| `NwcClashBatch`, `NwcClashRule`, `NwcClashComment` | Batch test container, filtering rules (ignore thresholds, custom predicates), and review comments. |
| `NwcClashGroup` | Grouped clash clusters produced by `NwcClashGrouper`. |
| `NwcClashGrouper` | Automated clustering of clashes by proximity, element, grid line, or layer. |
| `NwcClashMatrix`, `NwcClashDiscipline`, `NwcClashMatrixCell` | $N \times M$ multi-discipline coordination matrix with per-cell tolerance/type, automated execution, Markdown generation, and BCF 2.1 export. |
| `NwcClashBcfExporter` | buildingSMART BCF 2.1 export of clash tests and results. |
| `NwcXmlClashSerializer` | Roundtrip Navisworks Clash Report XML (`nw-clashreport-11.0.xsd`) reader and writer. |
| `NwcNwfManifest` | Federation manifest used by `NwcAssembly` tooling. |

## 4D TimeLiner, Selection Sets & Appearance (`NwcIO.Model.TimeLiner`, `.Sets`, `.Appearance`)

| Type | Description |
|---|---|
| `NwcTimeLinerSchedule` & `NwcTimeLinerTask` | WBS task hierarchy, planned/actual dates, costs, progress, and simulation queries. |
| `NwcTimeLinerXmlSerializer` | Full roundtrip serialization of Navisworks TimeLiner Exchange XML. |
| `NwcTimeLinerMsProjectXmlSerializer` | Microsoft Project XML schedule import/export. |
| `NwcTimeLinerPrimaveraXerSerializer` | Primavera P6 XER schedule import/export. |
| `NwcTimeLinerCsvSerializer` | CSV schedule export. |
| `NwcTimeLinerLinker` | Rule-based automated linking of schedule tasks to geometry. |
| `NwcSelectionSet`, `NwcSearchSet`, `NwcSelectionSetFolder`, `NwcSetItem` | Manual selection sets, dynamic query search sets, and set folders. |
| `NwcSearchCondition` & `NwcSearchOperator` | Search criteria over node built-ins and property records (equals, contains, wildcard, comparisons). |
| `NwcXmlSetSerializer` | Navisworks selection-set XML exchange. |
| `NwcQuickProperties` | Curated quick-properties view over property records. |
| `NwcAppearanceProfiler`, `NwcAppearanceRule`, `NwcXmlAppearanceProfilerSerializer` | Rule-based colouring by search conditions and its XML exchange format. |

## 3D Geometry & Spatial Analysis (`NwcIO.Model.Grids`, `.Measure`, `.Diff`, `.Spatial`, `.Sectioning`, `.Redline`)

| Type | Description |
|---|---|
| `NwcGridSystem`, `NwcGridLine`, `NwcGridIntersection` | Building grid lines and axes with 2D/3D intersection calculations and spatial clash labelling. |
| `NwcMeasureEngine` | Double-precision 3D point-to-point, point-to-mesh and mesh-to-mesh distance, clearance, and area/volume calculations. |
| `NwcDiffEngine` (with `NwcDiffReport`, `NwcDiffOptions`, `NwcElementDiff`) | Revision comparison and differential geometry/property analysis. |
| `NwcSpatialIndex` | SAH bounding-volume hierarchy for raycasts and accelerated proximity queries (`RaycastHit`). |
| `NwcSectionPlane`, `NwcSectionBox`, `NwcSectionAlignment`, `NwcMeshClipper` | Section planes and six-sided section boxes with mesh clipping. |
| `NwcXmlViewpointSerializer`, `NwcViewpointBcfExporter` | Navisworks viewpoint XML exchange and BCF viewpoint export. |
| `NwcRedline*` (`NwcRedline`, `Arrow`, `Cloud`, `Ellipse`, `Line`, `Tag`, `Text`, `NwcRedlineStatus`) | Redline annotation records. |

## Export (`NwcIO`, `NwcIO.Model.Viewer`)

| Type | Description |
|---|---|
| `NwcDocument.SaveIfc` / `NwcAssembly.SaveIfc` | ISO 10303-21 STEP export supporting IFC2x3 Coordination View and modern IFC4 mesh tessellation (`IfcTriangulatedFaceSet`), configured via `NwcIfcExportOptions` (`NwcIfcSchemaVersion`, `NwcIfcLengthUnit`). |
| `NwcDocument.SaveGlb` / `SaveObj`, `NwcAssembly.SaveGlb` / `SaveObj` | Binary glTF 2.0 and Wavefront OBJ geometry export entry points. |
| `NwcDocument.SaveHtmlViewer` / `NwcAssembly.SaveHtmlViewer` | Interactive single-file 3D WebGL viewer exporter with 4D schedule playback (`NwcHtmlViewerExporter`, `NwcHtmlViewerOptions`). |
| `NwcJsonExportOptions` | Indentation and content toggles for JSON export (`NwcDocument` and the CLI). |
| `GlbExporter` & `ObjExporter` (`NwcIO.Internal.Export`) | Low-level public static exporters the `Save*` entry points delegate to; implementation surface, not covered by compatibility guarantees. |

## Container & Parsing (`NwcIO.Internal.*` — public but unsupported)

| Type | Description |
|---|---|
| `NwcContainerReader` / `NwcContainerWriter` | `#LcUStream-V1.1` section/chunk reader and writer. |
| `NwcContainer`, `LcRawStream`, `LcUStreamHeader`, `NwcStreamKind` | Decompressed stream records and their best-effort classification. |
| `StreamClassifier`, `PartitionHeaderParser`, `PropertyRecordScanner`, `SceneTreeBuilder`, `GuidTableParser`, `TransformParser`, `MeshParser`, `MaterialParser`, `DbCacheParser`, `XRefTableParser` | Stream classifiers and format decoders. |
| `JsonExporter` | Low-level JSON export helper. (The IFC and clash-report exporters are `internal`.) |

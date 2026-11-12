# Navisworks File Format Notes (.nwc / .nwd)

This document records reverse-engineering findings obtained from hex and stream analysis of real Autodesk Navisworks cache files (`.nwc`, version 448 / Navisworks 2021–2024 era) and clean-room study of format specifications.

## 1. High-Level Container: `#LcUStream`

Navisworks cache (`.nwc`) and document (`.nwd`) files are containerized streams introduced by ASCII header lines.

### 1.1 Section Header Syntax

Each section begins at an offset with an ASCII text line terminated by `\n` (`0x0A`):

```text
#LcUStream-V1.1 binary double release C lichunk-008
#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib
```

Key tokens:
- **Format identifier**: `#LcUStream-V1.1`
- **Double precision**: `double` (floating-point numbers in geometry and matrices are 64-bit doubles)
- **Character encoding**: `C` (8-bit locale-dependent ASCII) or `utf8`
- **Internal version**: `navisworks:448` (format version identifier, e.g. 448 in Navisworks 2021–2024)
- **Chunk marker**: `lichunk-008` (chunked container layout) or `lizlib` (raw zlib stream block)

A single `.nwc` file often contains multiple `#LcUStream` sections (e.g. partition 0 for model data, partition 1 for xrefs/caches).

### 1.2 Stream Structure

Following each section header is a series of back-to-back zlib compressed streams (RFC 1950, magic `0x78 0x01`, `0x78 0x5E`, `0x78 0x9C`, or `0x78 0xDA`).

Between some streams are framing blocks:
- 40-byte framing/hash blocks: a 20-byte or 40-byte checksum / hash before major partition boundaries.
- Trailing gap: optional footer bytes (e.g. SQLite DB cache metadata).

## 2. Decompressed Stream Classification

Once inflated, streams serve dedicated roles identified by their signature prefixes.

### 2.1 Partition Header (`PartitionHeader`)
- **Signature**: `[int64 7]` followed by length-prefixed strings.
- **Fields**:
  - `int64`: marker `7`
  - `int32 len` + UTF-8 string: full path of originating source document (e.g., `C:\Projects\Building_Structural.rvt`)
  - `int32 -1`
  - `int64`: modification timestamp as Unix epoch seconds
  - `int64`: source file size in bytes
  - `int32 len` + UTF-8 string: loader plugin ID (e.g., `LcNwcLoaderPlugin:lcldrevit`)
  - Subsequent length-prefixed UTF-8 strings: loader export configuration options.

### 2.2 Scene Tree & Property Stream (`SceneTree`)
- **Signatures**: starts with `[int32 100][int32 86]` or contains `LcRevit` hierarchy records.
- **Record Types**:
  - **Hierarchy Node (Tag 53 / `0x35`)**:
    - Layout: `[int32 id][int32 53][int32 nameLen][name string][padding to 4B]`
    - Represents scene grouping: levels (`<Нет уровня>`), categories (`Несущая арматура`), families (`Арматурный стержень`), types (`мп_А500 Ø10`).
  - **Property / Element Record (Tag 52 / `0x34`)**:
    - Layout:
      ```
      [int32 id]
      [int32 52]
      [int32 nameLen][UTF-8 name][4B padding]
      [int32 classLen][UTF-8 class][4B padding]
      [int32 valueType]
      [type-specific value payload]
      ```
    - **Value Types observed**:
      - `0`: Structural element / child count (e.g., `35` properties on this element)
      - `1, 2, 3`: 32-bit integers / booleans / Revit BuiltInCategory IDs (e.g., `-2009000` for `OST_Rebar`, Element IDs like `4917666`)
      - `4`: Length-prefixed UTF-8 string (padded to 4 bytes), e.g. `"мп_А500 Ø10 : Форма О_1"`
      - `5, 7`: 64-bit IEEE 754 double (coordinates, lengths, angles)
      - `8`: Embedded element reference `[int32 refId][int32 52][refName][refClass]`, e.g. `RebarShape "О_1", #4930621` of class `LcRevitElement`

### 2.3 GUID Table (`Guids`)
- **Signature**: `[int32 ?][int32 count]` where `(streamLength - 8) == count * 16`.
- **Payload**: sequential array of 16-byte raw little-endian GUIDs corresponding to element unique IDs.

### 2.4 Transforms (`Transforms`)
- **Signature**: `[int32 totalCount][int32 100][int32 13]`
- **Record Layout**:
  - `Tag 13`: Master transform entry `[int32 id][int32 13]`.
  - `Tag 15`: 3D Pose entry `[int32 id][int32 15]`:
    - Translation vector: 3 little-endian 64-bit IEEE 754 doubles `(X, Y, Z)` in document coordinates (typically millimeters).
    - Rotation unit quaternion: 4 little-endian 64-bit IEEE 754 doubles `(Qx, Qy, Qz, Qw)` satisfying `Qx^2 + Qy^2 + Qz^2 + Qw^2 ≈ 1.0`.
  - `Tag 93 (0x5D)`: Local Bounding Box `[int32 id][int32 93]`:
    - 6 little-endian 32-bit single-precision floats `(minX, minY, minZ, maxX, maxY, maxZ)` specifying the geometry extents in local coordinates.
- **Affine Matrix Conversion**: A 4x4 row-major matrix is computed directly from the quaternion rotation $R$ and translation vector $T$.

### 2.5 Table of Contents (`Toc`)
- **Signature & Layout**:
  - `[int32 recordCount]`: total number of indexed geometry records (e.g., 582, 2778, 4202).
  - `[int32 chunkCount]`: number of contiguous 64 KB uncompressed zlib chunks in Section 0 (e.g., 5, 25, 31).
  - `chunkCount x int32`: compressed byte size of each preceding chunk in the container.
  - `recordCount x int32`: byte length of each individual geometry record in the concatenated geometry buffer.
  - Stream byte length equals `8 + 4 * chunkCount + 4 * recordCount` (plus optional 4-byte padding).
- **Function**: Acts as the master index for the entire geometry stream, allowing random access and contiguous stream reassembly.

### 2.6 Geometry & 3D Mesh Records (`Geometry`)
- **Contiguous Block**: The `chunkCount` streams immediately preceding the TOC form a continuous binary data block containing 3D meshes and lines.
- **Record Headers**:
  - `Tag 94 (0x5E)` / `Tag 95 (0x5F)`: Mesh record container `[int32 recId][int32 94|95]`.
  - Nested `Tag 60 (0x3C)`: Vertex buffer descriptor `[int32 subId][int32 60][int32 p1][int32 p2]`.
- **Vertex Buffer Layout**:
  - If `p2 > 0 && p2 % 3 == 0`, `vertexCount = p2 / 3` (float count divided by 3).
  - Else if `p1 > 0`, `vertexCount = p1`.
  - Followed by `vertexCount` float triples `(X, Y, Z)` in IEEE 754 single-precision (12 bytes per vertex).
  - Terminated by `[int32 102][int32 100|58][int32 -indexCount]` and optional `0xD4` alignment padding.
- **Topology**:
  - Triangles are arranged in `TRIANGLE_STRIP` order: $(i, i+1, i+2)$ for even $i$, $(i+1, i, i+2)$ for odd $i$ to maintain outward normals.
  - Linear wire elements (e.g. rebar centerlines, grid axes) are indicated when `vCount = 2`.

### 2.7 DB Cache Directory (`DbCache` / `LcOpDBCache`)
- **Container Structure**:
  - `int32 magic = -1` (`0xFFFFFFFF`): stream indicator.
  - `int32 entryCount`: number of recorded table segments (e.g., 9, 19).
  - `int32 nameLen` + ASCII name (`LcOpDBCache`) + padding to 4 bytes.
  - `int64 v1`, `int64 v2`, `int64 v3`, `int64 v4`: 32 bytes of database partition metadata.
- **Segment Record Layout**:
  - `int32 segmentNameLen` + ASCII name (e.g. `cache\LcOpNwdGeometry`, `cache\LcOpGuidStore`, `LcOpXRefTable`) + padding to 4 bytes.
  - `int32 flags`: version / compression flags (e.g. 1, 3).
  - `int64 offset`: byte offset of segment payload.
  - `int64 length`: byte size of segment data.
  - `int64 extra`: segment alignment / format flags.
- **Role**: Functions as a virtual filesystem directory for embedded partition stores in Section 1/2.

### 2.8 External Reference Table (`XRefJson` / `XRefTable`)
- **Signature**: Stream beginning with or containing `{"Type":"XRefTable", ...}`.
- **JSON Structure**:
  ```json
  {
    "Type": "XRefTable",
    "Version": 1,
    "SavedFilename": "C:\\Projects\\Building_Structural.nwc",
    "Table": [
      {
        "OriginalPath": "C:/Program Files/Autodesk/Materials/Textures/1/Mats/124.png",
        "RemappedPath": "C:/Program Files/Autodesk/Materials/Textures/1/Mats/124.png"
      }
    ]
  }
  ```
- **Fields**:
  - `Type`: `"XRefTable"`
  - `Version`: schema version (integer 1)
  - `SavedFilename`: canonical original document path
  - `Table`: array of external file references (textures, linked `.nwc`/`.nwd` files) with `OriginalPath` and `RemappedPath`.

### 2.9 Materials, RGBA Colors & glTF 2.0 (GLB)
- **Material Records**:
  - `LcRevitElement` with prefix `Material "Name", #ElementId`: defines material instances.
  - `revit_Color` with property `Color`: stores comma-separated `"R, G, B"` integer triples in $0..255$ range (e.g. `"255, 128, 0"`, `"0, 255, 255"`).
  - Material parameters in elements (`ADSK_Материал`, `Материал`, `ADSK_Основной материал элемента`): link elements to material names.
- **glTF 2.0 Binary Container (`.glb`)**:
  - 12-byte header: magic `0x46546C67` (`glTF`), version 2, total byte length.
  - Chunk 0: JSON metadata padded with `0x20` space bytes to 4-byte alignment.
  - Chunk 1: Binary buffer (`BIN\0`, `0x004E4942`) padded with `0x00` bytes to 4-byte alignment.
  - Packed buffers include `POSITION` (VEC3 float), `NORMAL` (VEC3 computed smooth/flat normals), `indices` (SCALAR uint16/uint32), and PBR materials with `baseColorFactor`, `metallicFactor`, `roughnessFactor`.

### 2.10 Federated Multi-Model Assemblies (.nwf) & 3D Spatial Indexing (BVH)
- **NWF Assemblies**:
  - Navisworks files (`.nwf`) reference component models (`.nwc`, `.nwd`) without duplicating geometry.
  - `NwcAssembly` composites multiple documents into a unified coordinate system with hierarchical scene aggregation (`RootNode` -> contributing models -> categories -> families -> elements).
  - Merges meshes, materials, properties, and bounding boxes into a federated model with unified export to GLB, OBJ, and JSON.
- **Bounding Volume Hierarchy (BVH)**:
  - Balanced binary AABB tree constructed using centroid median splits along maximal extent axes ($X, Y, Z$).
  - Sub-millisecond spatial range queries (`QueryBox`) and frustum testing.
  - High-performance raycasting using branch-and-bound AABB traversal followed by double-precision Möller–Trumbore ray-triangle intersection tests with surface normal determination.
  - Multi-model clash detection (`FindClashes`) detecting spatial bounding box collisions across models.

### 2.11 Industry Foundation Classes (IFC2x3 & IFC4 Export)
- **Schema & Format**:
  - Implements buildingSMART ISO 10303-21 STEP physical file exchange format for both **IFC2x3 Coordination View V2.0** and **IFC4 Design Transfer View / Reference View**.
  - Header: `FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0]'),'2;1');`, `FILE_SCHEMA(('IFC2X3'));` or `FILE_DESCRIPTION(('ViewDefinition [DesignTransferView_1.0]'),'2;1');`, `FILE_SCHEMA(('IFC4'));`.
- **Character Encoding**:
  - Full Unicode support with ISO 10303-21 `\X2\HHHH...\X0\` escaping for Cyrillic and international characters (e.g., Russian BIM category and parameter names).
- **Geometric Representations**:
  - **IFC2x3 (Boundary Representation)**: Serialized as `IfcFacetedBrep` composed of `IfcClosedShell` containing `IfcFace` &rarr; `IfcFaceOuterBound` &rarr; `IfcPolyLoop` referencing `IfcCartesianPoint` coordinates, with one point shared per mesh vertex index when `SharedVertices` is enabled (the default); coincident coordinates at different vertex indices are not merged.
  - **IFC4 (Modern Tessellation)**: Serialized as `IfcTriangulatedFaceSet` referencing `IfcCartesianPointList3D` with representation type `'Tessellation'`. Reduces entity count and file size by over 60%, delivering sub-120 ms export times for full buildings.
- **Type Products & Occurrence Relations (`IfcRelDefinesByType`)**:
  - Generates type definition objects (`IfcTypeProduct` subtypes): `IFCWALLTYPE`, `IFCSLABTYPE`, `IFCCOLUMNTYPE`, `IFCBEAMTYPE`, `IFCREINFORCINGBARTYPE`, `IFCFOOTINGTYPE`, `IFCRAILINGTYPE`, `IFCROOFTYPE`, `IFCSTAIRFLIGHTTYPE`, `IFCPIPESEGMENTTYPE`, `IFCDUCTSEGMENTTYPE`, `IFCWINDOWTYPE`/`IFCWINDOWSTYLE`, `IFCDOORTYPE`/`IFCDOORSTYLE`, and `IFCBUILDINGELEMENTPROXYTYPE`.
  - Links occurrence instances to their corresponding type object using `IfcRelDefinesByType`.
- **Spatial Structure & Containment**:
  - Full IFC spatial containment graph: `IfcProject` &rarr; `IfcSite` &rarr; `IfcBuilding` &rarr; `IfcBuildingStorey`, linked via `IfcRelAggregates`.
  - Geometric elements are assigned to storeys via `IfcRelContainedInSpatialStructure`.
  - In federated assemblies (`NwcAssembly`), each sub-model forms an independent building storey.
- **Semantic Classification**:
  - Dynamic mapping of Navisworks elements, categories, and materials to standard IFC entities:
    - `IFCWALL`, `IFCSLAB`, `IFCCOLUMN`, `IFCBEAM`, `IFCREINFORCINGBAR`, `IFCWINDOW`, `IFCDOOR`, `IFCFOOTING`, `IFCSTAIR`, `IFCRAILING`, `IFCROOF`, `IFCPIPESEGMENT`, `IFCDUCTSEGMENT`, falling back to `IFCBUILDINGELEMENTPROXY`.
- **Materials & Properties**:
  - Shading styles serialized via `IfcSurfaceStyle`, `IfcSurfaceStyleShading`, and `IfcColourRgb`, assigned to geometry via `IfcStyledItem`.
  - Element metadata and dimensional bounding parameters exported as `IfcPropertySet` containing `IfcPropertySingleValue` linked to elements via `IfcRelDefinesByProperties`.
- **Global Identifiers**:
  - Deterministic 22-character buildingSMART base64 GUIDs generated via UUID/SHA-256 namespace hashing (`IfcGuid`), ensuring stable GUIDs across repeated exports.

### 2.12 BIM Collaboration Format (BCF 2.1) & Interactive Clash Reports
- **BCF 2.1 Container Specification**:
  - Formatted as a `.bcfzip` PKZip archive complying with buildingSMART BCF 2.1 specification.
  - Root entry `bcf.version` contains XML version declaration: `<Version VersionId="2.1" DetailedVersion="2.1" />`.
  - For each detected spatial clash, a GUID folder `{TopicGuid}/` contains:
    - `markup.bcf`: XML metadata including `<Topic Guid="..." TopicType="Clash" TopicStatus="Active">`, `<Title>`, `<Priority>`, `<CreationAuthor>NwcIO</CreationAuthor>`, `<CreationDate>`, `<Description>` detailing intersecting models and overlap volumes, and link to `<Viewpoints>`.
    - `viewpoint.bcfv`: XML visualization info including `<Components><Selection>` and `<Coloring>` highlighting element A in rose (`#E11D48`) and element B in azure (`#2563EB`) via buildingSMART 22-character `IfcGuid`, and `<PerspectiveCamera>` automatically aimed at the 3D clash intersection center.
- **Interactive Offline HTML Clash Reports**:
  - Standalone HTML document with zero external dependencies (no CDNs, works completely offline in air-gapped environments).
  - Responsive dark-theme BIM UI with Emerald (`#22C55E`) and Azure (`#3B82F6`) accent branding.
  - Discipline cross-clash matrix table ($M_i \times M_j$) showing pairwise conflict density across all contributing models, with clickable cell filtering.
  - Interactive table featuring live search filtering and client-side column sorting by clash index, model names, volumes, and bounding boxes.

### 2.13 Saved Viewpoints & 3D Camera Reverse Engineering
- **Container Identification**:
  - Saved viewpoint streams are identified by the presence of marker Tag `517` (`0x0205`), accompanied by consecutive IEEE 754 double sequences representing 3D coordinates and rotation quaternions.
- **Binary Layout of Viewpoint Records**:
  - String record prefix: Length-prefixed 32-bit integer followed by UTF-8 (or CP866 / CP1251 in localized releases) display name string (e.g., `"Общий вид здания"`, `"Navisworks коллизии"`).
  - Header tag: `517` (`0x00000205`), followed by `int32 1`, `int32 0`.
  - Eye position: 3 consecutive 64-bit IEEE 754 doubles at `offset + 12, +20, +28` ($X, Y, Z$ in model world coordinates).
  - Orientation quaternion: 4 consecutive 64-bit IEEE 754 doubles at `offset + 36, +44, +52, +60` ($Q_X, Q_Y, Q_Z, Q_W$), where $Q_X^2 + Q_Y^2 + Q_Z^2 + Q_W^2 \approx 1.0$.
  - Optical parameters:
    - `offset + 68`: Viewport aspect ratio (width / height, double).
    - `offset + 76`: Focal distance to target / look-at center point (double).
    - `offset + 84`: View height / frustum vertical extent (double).
    - `offset + 92`: Near clipping plane distance (double).
- **Coordinate Frame & Vector Algebra**:
  - Canonical view direction vector in camera local space is along $-Z$ (`(0, 0, -1)`).
  - Canonical up vector in camera local space is along $+Y$ (`(0, 1, 0)`).
  - World viewing direction: $\vec{D} = Q \cdot (0, 0, -1) \cdot Q^*$.
  - World up vector: $\vec{U} = Q \cdot (0, 1, 0) \cdot Q^*$.
  - 3D target / look-at point: $\vec{T} = \vec{P} + \vec{D} \times d_{\text{focal}}$.
- **glTF 2.0 & BCF Integration**:
  - Serialized into glTF 2.0 `cameras` array with perspective projection and vertical field of view (`yfov = FieldOfView * π / 180`, i.e. the camera's vertical FOV in degrees converted straight to radians, falling back to 1.04719755 rad ≈ 60° when not positive; no horizontal-to-vertical conversion is applied — `aspectRatio` is written separately).
  - Placed as camera scene nodes with `translation` ($\vec{P}$) and `rotation` ($Q$).
  - Exported to buildingSMART BCF 2.1 archives with `PerspectiveCamera` and `OrthogonalCamera` visualization blocks.

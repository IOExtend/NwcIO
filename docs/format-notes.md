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

### 2.2 Logical Hierarchy (`LcOpNwdLogicalHierarchy`)
- **Location**: the DbCache entry whose final segment is `LcOpNwdLogicalHierarchy` (one zlib stream). Older notes called it the scene tree and scanned it for tags 52/53; that scan misses every node that is only back-referenced.
- **Stream framing**: `[int32 k][k x int32] [int32 100] [int32 n] n x pointer [int32 1]`, zero-padded to a multiple of 8. `k` is the number of `LcOaPartitionProps` streams minus one; the `k` ints are not decoded. Id 100 is the implicit root and has no class or body.
- **Pointer rule**: a pointer is `0` (null), `[1][id]` (back-reference to an object whose body is complete) or `[nextId][classId][body]` (new object). `nextId` starts at 100; an object takes its id before its body is read, so nested new objects take the following ids in depth-first order. Any other first value, an unknown class, or a back-reference to an unfinished object is invalid. Ids are per stream: equal ids in different streams mean nothing.
- **Primitives**: strings are `[int32 len][UTF-8][zero pad to 4]` with `-1` for null; `f64` and `i64` values are aligned to 8 bytes from the stream start, with one zero `int32` of padding when needed.
- **Classes**:
  - `53` group: `[str name][ptr 52 username][i32 flags][i32 X][i32 Y][Y x attribute ptr][i32 c][c x child ptr]`. Children are class 22 or 53 and are never null.
  - `22` leaf: the same layout without `c` and children.
  - `32` (`LcOaPartition`, `.nwd` only): `[i32 0]`, a class-53 body, then a trailer that is not decoded. Class 32 nodes are the top-level children of a `.nwd`; class 53 nodes are the top-level children of a `.nwc`.
  - `52` display/internal name pair: `[str name][str internal]`. It is the node username, the property tab username and every property key.
- **Attributes** (slots may be null): `86` property tab `[str name][ptr 52][i32 flags][i32 n] n x ([ptr 52 key][i32 type][value])` with value by type `0` none, `1,5,6,7,10,11` `f64`, `2,3` `i32`, `4` string, `8` class-52 pointer; `31` material; `40` translation; `41` translation and quaternion (`x -> R(q)x + t`); `39` 3x3 column-major matrix and translation; `79` hyperlinks; `200` and `185` material JSON.
- **Shared subtrees**: a child pointer may back-reference an earlier node, so the hierarchy is a directed acyclic graph. A node reached through several parents appears once per path when the tree is expanded.
- **Preorder index (`seq`)**: walk from the implicit root in stored order, counting groups, with a shared subtree walked again at every place it occurs. The root has index 0. Every fragment's `seq` indexes this expanded sequence and lands on a leaf; `seq` never decreases along the fragments stream, and a leaf owns one contiguous run of fragments.

### 2.3 GUID Table (`Guids`)
- **Signature**: `[int32 ?][int32 count]` where `(streamLength - 8) == count * 16`.
- **Payload**: sequential array of 16-byte raw little-endian GUIDs corresponding to element unique IDs.

### 2.4 Fragments (`LcOpNwdFragments`)
- **Location**: the DbCache entry whose final segment is `LcOpNwdFragments` (one zlib stream). Framing: `[int32 n] n x pointer`, zero-padded to a multiple of 8. Pointers follow the rule in §2.2; every root pointer is a new class-13 object.
- **Classes**:
  - `13` fragment: `[f64 D][i32 a][i32 b][i32 c][i32 seq][ptr pose][ptr appearance][ptr geometry]`.
    - `seq`: preorder index of the owning leaf (§2.2).
    - `a`: triangle count Σ(L − 2) over the strip lengths `L` for a triangle record, segment count Σ(L − 1) over the polyline lengths `L` for a line record.
    - `D`, `b`, `c`: not decoded.
  - `15` pose: `[3 x f64 t][4 x f64 q]`, `q = (x, y, z, w)` a unit quaternion.
  - `16` pose with scale: class 15 plus `[f64 s]`. The scale is a uniform factor and can be negative.
  - `55` appearance: `[i32 flags][ptr 54]`, plus `[ptr 185]` when `flags & 32`. Class `54` is 14 `int32` values; neither is decoded.
  - `93` geometry reference: `[6 x f32 bbox][3 x f64 c][f32][i32 key][i32 rec]`. `rec` is the 1-based index of a geometry record (§2.5). The box equals the bounding box of the record's positions, and `c` is the offset of those positions.
- **Placement**: the fragment pose is the full world transform of the record's positions: `world = s·R(q)·v + t`, with `s = 1` for class 15. `R(q)` is the usual rotation matrix of the normalised quaternion. No unit conversion is applied.
- **Mirroring**: a pose with `s < 0` mirrors the geometry, so triangle winding must be reversed to keep faces outward in world space. Local vertices and indices are not changed.
- **Sharing**: several fragments may reference one geometry record, directly or through back-referenced class 93 objects. The pose is never shared.

### 2.5 Table of Contents (`Toc`) and Geometry Chunks
- **Layout**: `[int32 N][int32 ch][ch x int32 compressed chunk size][N x int32 record length]`, zero-padded to a multiple of 8. The padded length is `roundup8(8 + 4*ch + 4*N)`.
- **Chunk sizes** are compressed lengths: they equal the compressed lengths of the `ch` chunk streams.
- **Record cutting**: concatenate the inflated chunk streams. Record `k` starts at the sum of the preceding lengths and has length `len[k]`; the lengths sum to the block length, every start is a multiple of 8, and records may cross chunk boundaries. Fragments index records globally, 1-based.
- **Position of the chunk streams**: in a `.nwc` the chunks are container streams `1..ch` (stream 0 is the partition header). In a `.nwd` they are streams `0..ch-1` and the TOC is stream `ch`.
- **No chunk cap**: large `.nwd` files carry thousands of chunks, so the chunk count is bounded only by the stream length.
- **Segment**: the DbCache entry `LcOpNwdGeometry` is a 4-byte prefix (`int32 65536`), the chunk streams, then the TOC stream as the last stream of the segment.
- **Function**: the master index of the geometry block, giving random access to records.

### 2.6 Geometry Records
Each record is a strict grammar that ends exactly at its TOC length. `int` is a little-endian `int32`; every pad byte and unused packed bit is 0.

```text
RECORD := [100][cls] V UVSLOT NSLOT CSLOT P SFIELD? pad8
V      := [101][60][p1][p2][p2 x f32 xyz] VMAP?      (npos = p2/3; VMAP iff p1 != npos)
UVSLOT := 0 | [id][61] UVBODY
NSLOT  := 0 | [id][100][-p1][pal] PACKED(3*pal, B) NTOK?   (NTOK iff pal != p1)
CSLOT  := 0 | [id][58][5634][-p1][pal][pal x 4 bytes] CTOK?  (CTOK iff pal != p1)
P      := [n][bits][n][ceil(n*bits/32) words]         (strip or polyline lengths L)
SFIELD := [S] TOKENS(S, p1)?                          (class 94 only; tokens iff S > 0)
UVBODY := [p1][fc][fc x f32] TOKENS(p1, fc/2)?        (raw; tokens iff fc/2 != p1)
        | [-p1][4 x f32][w][m][ceil(m*(bu+bv)/32) words] TOKENS(p1, m)?   (quantised; tokens iff m != p1)
```

- **Class**: `94` triangles, `95` lines. Class 94 always has a normal slot and never a colour slot; class 95 has only the colour slot. Object ids run 100, 101, then 102, 103 over the slots present.
- **`p1` is the render-vertex count**, not the position count. `p2/3` positions are stored; `VMAP` maps each render vertex to a position and is the identity when absent. Quantised positions (negative `p1`) and point records (class 96) are not supported.
- **PACKED(count, w)**: bit `j` of the stream is bit `31 - j%32` of little-endian word `j/32`; value `k` occupies bits `k*w` to `k*w+w-1`, most significant first.
- **TOKENS(count, fresh)**: one byte code for every map. State: `out = []`, `next = 0`; read lead bytes until `count` values exist.

  | Lead | Bytes | Meaning |
  | --- | --- | --- |
  | `00-3F` | 1 | run: append `next .. next+b`, then `next += b+1` |
  | `40-7F` | 1 | copy `out[n-d]` with `d = b-0x40` |
  | `80-BF` | 1 | repeat the previous value `b-0x7F` times |
  | `D0-D5` | 1 | normal streams only: axis code (`-1` to `-6`) |
  | `E0-EF xx` | 2 | copy `out[n-d]` with `d = ((b&15)<<8)\|xx` |
  | `F0 hh ll` | 3 | literal `(hh<<8)\|ll`, below `next` |

  At the end `next == fresh`, and the stream is padded to 4 bytes.
- **Normals** (class 94): a palette entry is three two's-complement values of width `B`, divided by `2^(B-1)-1` and normalised; a zero vector means no normal. `B` is not stored in the record: it is the width under which every record of the file parses (16 for `.nwc`, 8 for `.nwd`). Render vertex `r` takes `palette[NIdx[r]]`, or an axis vector for an axis code (`D0` +X, `D1` -X, `D2` +Y, `D3` -Y, `D4` +Z, `D5` -Z).
- **Triangles** (class 94): corners are `0..p1-1` when `S = 0`, otherwise `TOKENS(S, p1)`. They are split by `L` into consecutive strips; in each strip, triangle `i` is `(c[i], c[i+1], c[i+2])` for even `i` and `(c[i+1], c[i], c[i+2])` for odd `i`. Parity restarts in every strip. The triangle count is the sum of `L-2`. Front faces are counter-clockwise.
- **Polylines** (class 95): render vertices `0..p1-1` are split by `L` into open polylines; the segment count is the sum of `L-1`. There is no implicit closing segment.
- **UVBODY**: the raw form has a non-negative first int `p1` and `fc` floats (two per UV pair, `fc/2` pairs). The quantised form has `-p1`, four floats, then `w`, `m` and the packed words; `bu = w & 0xFF` and `bv = (w >> 8) & 0xFF` are the bit widths of the two coordinates, so the packed block is `ceil(m*(bu+bv)/32)` words. The token stream maps each of the `p1` render vertices to one of the `fc/2` or `m` stored pairs.
- **Not decoded**: UV values and colour channels. Only their lengths and maps are read, to find the next field.

### 2.7 DB Cache Directory (`DbCache` / `LcOpDBCache`)
The directory is the only stream of section 1. It lists every other segment and tiles section 0 exactly.

```text
DIRECTORY := [int32 head = -1][int32 n] ENTRY{n}      (ends exactly at the stream end)
ENTRY     := [str name] [int32 flags] PAD8 [int64 offset] [int64 length] [int32 a] [int32 b]
PAD8      := one zero int32 if the cursor (from stream start) is 4 mod 8, else nothing
```

- **Names**: `[int32 len][ASCII or UTF-8][zero pad to 4]`. Match a segment by the text after the last `\` (exact, ordinal), for example `LcOpNwdGeometry`. A `.nwc` uses `cache\<Name>` or a bare name; a `.nwd` uses a path-like prefix. A `.nwd` has no `LcOpDBCache` entry.
- **`offset`** is an absolute file offset and **`length`** the byte length in the file. Entries are listed in file order: the first starts right after the section-0 header line and each next one starts where the previous ends.
- **`flags`**: `1` zlib streams, `3` raw bytes.
- **`(a, b)` shapes**:
  - `(0, 0)`: one zlib stream starts at `offset` and its compressed length is `length`;
  - `(4, b)`: a 4-byte prefix sits at `offset`, then consecutive zlib streams follow with no gaps; the last stream's compressed length is `b`, and the segment ends at `offset + length`.
- **Segments the reader uses**: `LcOpNwdLogicalHierarchy` (§2.2) and `LcOpNwdFragments` (§2.4) are `(0, 0)`; `LcOpNwdGeometry` (§2.5) is `(4, b)`. `LcOpNwdGeometryCompress` is 40 raw bytes and is not decoded. Other entries are listed but not parsed.
- **Strict path**: a container is read through these segments when section 1 holds exactly one stream, the stream parses as this directory and it names `LcOpNwdLogicalHierarchy`, `LcOpNwdFragments` or `LcOpNwdGeometry`. Any other container keeps the previous reader. NwcIO's writer still emits its own single-section layout, which the reader keeps.
- **Model API**: `NwcDbCacheEntry.Offset` is the absolute file offset, `Extra` equals `a + b * 2^32`, and `DatabaseName` is the name of the `LcOpDBCache` entry, or `""` when there is none.

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

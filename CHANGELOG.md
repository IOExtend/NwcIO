# Changelog

All notable changes to the `NwcIO` library and CLI are documented in this file.

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

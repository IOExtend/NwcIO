---
title: Public API
---

# Public API Surface

The `NwcIO` package is the supported surface listed here. Everything else (internal chunk decoders, byte streamers, raw Huffman tables) is `internal` and visible only to the test and tool projects through `InternalsVisibleTo`.

## Core Document & Hierarchy

| Type | Description |
|---|---|
| `NwcDocument` | Primary document container holding root scene graph nodes, meshes, materials, viewpoints, selection sets, and TimeLiner schedules. Opens `.nwc`, `.nwd`, and `.nwf` files via `NwcDocument.Open`. |
| `NwcNode` | Scene graph tree node representing layers, categories, composite elements, and geometry instances with hierarchical transformations and parameters. |
| `NwcProperty` & `NwcPropertyCategory` | Property key-value pairs (strings, doubles, booleans, dates, GUIDs) grouped by category. |
| `NwcDocumentBuilder` | Fluent builder API for creating Navisworks documents and geometry programmatically from code. |
| `NwcWriter` | Direct `#LcUStream-V1.1` binary container serializer and continuous zlib chunk writer. |

## Coordination & Clash Detection

| Type | Description |
|---|---|
| `NwcClashMatrix` | $N \times M$ multi-discipline clash coordination matrix with automated matrix execution, bounding box filtering, Markdown generation, and BCF 2.1 export. |
| `NwcClashDetector` | Broad-phase BVH and narrow-phase Möller 3D triangle-to-triangle collision engine. Supports hard clashes, clearance clashes, and duplicate detection. |
| `NwcClashGrouper` | Automated clustering of clashes by proximity, element, grid line, or discipline layer. |
| `NwcXmlClashSerializer` | Roundtrip Navisworks Clash Report XML (`nw-clashreport-11.0.xsd`) reader and writer. |

## 4D TimeLiner & Selection Sets

| Type | Description |
|---|---|
| `NwcTimeLinerSchedule` & `NwcTimeLinerTask` | WBS task hierarchy, planned/actual dates, costs, and progress tracking. |
| `NwcTimeLinerXmlSerializer` | Full roundtrip serialization of Navisworks TimeLiner Exchange XML format. |
| `NwcTimeLinerLinker` | Rule-based automated linking of schedule tasks to geometry. |
| `NwcSelectionSet` & `NwcSearchSet` | Manual selection sets and dynamic query search sets with rich condition evaluation (`NwcSearchCondition`). |

## 3D Geometry & Spatial Analysis

| Type | Description |
|---|---|
| `NwcGridSystem` | Building grid lines and axes with 2D/3D intersection calculations and spatial clash labeling. |
| `NwcMeasureEngine` | Double-precision 3D point-to-point, point-to-mesh shortest distance (Voronoi algorithm), clearance, and area/volume calculations. |
| `NwcDiffEngine` | Revision comparison and differential geometry/property analysis. |
| `NwcHtmlViewerExporter` | Interactive single-file 3D WebGL viewer exporter with 4D schedule playback. |
| `NwcIfcExporter` | ISO 10303-21 STEP exporter supporting IFC2x3 Coordination View and modern IFC4 mesh tessellation (`IfcTriangulatedFaceSet`). |
| `NwcGlbExporter` & `NwcObjExporter` | Binary glTF 2.0 and Wavefront OBJ geometry exporters. |

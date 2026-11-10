---
title: NwcIO
---

# NwcIO

**NwcIO** is a fast, 100% managed .NET library and CLI tool that reads Autodesk Navisworks files (`.nwc`, `.nwd`, `.nwf`) directly from disk **without requiring Autodesk Navisworks or the Navisworks API to be installed**, and exports 3D geometry, materials, scene hierarchies, and properties to **Industry Foundation Classes (IFC2x3 & IFC4)**, **glTF 2.0 Binary (GLB)**, **Wavefront OBJ**, and **JSON**.

- **Multi-Discipline Coordination Clash Matrix (`NwcClashMatrix`)**: Comprehensive $N \times M$ coordination clash matrix execution across disciplines with automated matrix population from federated assemblies, spatial bounding box pruning, configurable ignore rules, Markdown tables, JSON reports, and buildingSMART BCF 2.1 zip package export.
- **4D TimeLiner XML Serialization & Automated Linking (`NwcTimeLinerXmlSerializer` / `NwcTimeLinerLinker`)**: Full roundtrip reading and writing of Autodesk Navisworks TimeLiner Exchange XML format (`<exchange>`), supporting WBS hierarchies, planned/actual dates, costs, and progress. Automated rule-based schedule linking binding tasks to model geometry via Selection Sets, Search Sets, or element names.
- **Interactive 4D TimeLiner & Selection Sets HTML WebGL Viewer (`NwcHtmlViewerExporter`)**: Standalone, single-file interactive 3D WebGL viewer with built-in 4D TimeLiner playback controller and interactive Selection Sets sidebar tab.
- **Model Revision Comparison & Diff Engine (`NwcDiffEngine`)**: Compare revisions of Navisworks documents and federated assemblies to detect added, deleted, modified, and unchanged elements, 3D translation shifts, and property alterations.
- **Spatial Grid System & Axis Management (`NwcGridSystem`)**: Manage building grid lines and axes, calculate 2D/3D intersections, and automatically assign grid locations to detected clashes.
- **High-Precision 3D Measure Engine (`NwcMeasureEngine`)**: Double-precision point-to-point 3D distance and coordinate projections, point-to-mesh shortest distance with Voronoi-region closest point on triangle algorithm, mesh-to-mesh clearance, and polyline path measurement.
- **Navisworks Clash Detective & 3D Triangle Collision (`NwcClashDetector`)**: Broad-phase BVH and narrow-phase Möller 3D triangle-to-triangle intersection tests, clustering, and BCF 2.1 export.
- **Direct Container Writing & Fluent Document Builder (`NwcWriter` / `NwcDocumentBuilder`)**: Create and save valid `#LcUStream-V1.1` Navisworks files directly from code.

The library targets .NET 8 and .NET 10, uses 100% pure managed code with zero native dependencies, and supports Windows, Linux, and macOS.

## Where to go next

- [Getting started](getting-started.md): install the package or CLI tool and run a first export or clash test.
- [Public API](public-api.md): supported surface and key entry points of the library.
- [Binary Format Notes](format-notes.md): technical deep dive into the `#LcUStream-V1.1` binary container format.
- [API reference](api/NwcIO.yml): complete API documentation generated directly from XML source docstrings.
- [Source on GitHub](https://github.com/IOExtend/NwcIO) and [DeepWiki](https://deepwiki.com/IOExtend/NwcIO) for an automated architectural overview of the repository.

NwcIO is licensed under the MIT License and is not affiliated with, endorsed by, or sponsored by Autodesk. Autodesk and Navisworks are registered trademarks of Autodesk, Inc.

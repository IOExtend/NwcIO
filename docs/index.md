---
title: NwcIO
---

# NwcIO

**NwcIO** is a fast, 100% managed .NET library and CLI tool that reads Autodesk Navisworks files (`.nwc`, `.nwd`, `.nwf`) directly from disk **without requiring Autodesk Navisworks or the Navisworks API to be installed**.

- **Reading the format (`NwcDocument`)**: the scene hierarchy, geometry meshes, transforms, GUID tables, materials, and property categories of a Navisworks cache or document.
- **Writing `.nwc` (`NwcWriter` / `NwcDocumentBuilder`)**: create and save valid `#LcUStream-V1.1` Navisworks files directly from code.
- **JSON export (`NwcDocument.SaveJson`)**: writes structured JSON with the file information, property records, GUIDs, transforms, scene tree (each node with its id, name, class, value, element GUID, transform, properties, and hyperlinks), materials, XRefs, DbCache entries, and saved viewpoints, plus optional mesh summaries (id, name, vertex and triangle counts, material, bounding box) and a catalog of the container's sections and streams. It is core and grows with the reader.
- **Scene adapter (`NwcIO.Scene`)**: turns a Navisworks model into an `IOScene` document for IOViewer. See the [scene adapter](scene.md).

The HTML viewer, IFC, GLB and OBJ export, clash, measure, grids, diff, BCF, TimeLiner, selection and search sets, appearance profiler, mesh clipper, spatial index with raycast, section planes and boxes, redlines, and Navisworks XML exchange are frozen: bug fixes only. See [Scope and frozen features](https://github.com/IOExtend/NwcIO#scope-and-frozen-features).

The library targets .NET 8 and .NET 10, uses 100% pure managed code with zero native dependencies, and supports Windows, Linux, and macOS.

## Where to go next

- [Getting started](getting-started.md): install the package or CLI tool and run a first read, export or clash test.
- [Public API](public-api.md): supported surface and key entry points of the library.
- [Scene adapter](scene.md): convert a Navisworks model into an `IOScene` document with `NwcIO.Scene`.
- [Binary Format Notes](format-notes.md): technical deep dive into the `#LcUStream-V1.1` binary container format.
- [API reference](api/NwcIO.yml): complete API documentation generated directly from XML source docstrings.
- [Source on GitHub](https://github.com/IOExtend/NwcIO) and [DeepWiki](https://deepwiki.com/IOExtend/NwcIO) for an automated architectural overview of the repository.

NwcIO is licensed under the MIT License and is not affiliated with, endorsed by, or sponsored by Autodesk. Autodesk and Navisworks are registered trademarks of Autodesk, Inc.

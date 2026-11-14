---
title: Getting started
---

# Getting Started with NwcIO

## Installation

### NuGet Package

Install the `NwcIO` library package in your .NET project:

```bash
dotnet add package NwcIO
```

### Global CLI Tool

Install the `nwcio` command line tool globally:

```bash
dotnet tool install --global NwcIO.Cli
```

---

## Quick Examples

### Reading a Navisworks File

```csharp
using NwcIO;

// Open a .nwc file directly from disk. A .nwd whose scene tree holds partition nodes, and a linked
// Revit model (a tree that places one element under two parents), need
// new NwcReaderOptions { BuildSceneTree = false }, because such trees are not decoded yet.
var doc = NwcDocument.Open("model.nwc");

Console.WriteLine($"Source: {doc.FileInfo?.SourcePath}");
Console.WriteLine($"Total 3D meshes: {doc.Meshes.Count}");

foreach (var node in doc.RootNode.Children)
{
    Console.WriteLine($"Node: {node.Name} (Class: {node.Class})");
}
```

### Exporting to IFC4, glTF 2.0 (GLB), and OBJ

```csharp
using NwcIO;

var doc = NwcDocument.Open("federated_model.nwc");

// Modern IFC4 with compact IfcTriangulatedFaceSet mesh tessellation
doc.SaveIfc("output.ifc", new NwcIfcExportOptions
{
    SchemaVersion = NwcIfcSchemaVersion.Ifc4,
    ProjectName = "Coordination Project"
});

// Binary glTF 2.0 for 3D web engines (Three.js, Babylon.js, Cesium)
doc.SaveGlb("output.glb");

// Wavefront OBJ
doc.SaveObj("output.obj");
```

### Multi-Discipline Clash Matrix

```csharp
using System.Linq;
using NwcIO;
using NwcIO.Model;
using NwcIO.Model.Clash;

// Federate every model of the site folder into one assembly
var assembly = NwcAssembly.LoadFromDirectory("./site");

// Create a coordination clash matrix; disciplines separate meshes by model name
var matrix = new NwcClashMatrix("Coordination Matrix");
matrix.AddDiscipline("Architecture", "ARCH");
matrix.AddDiscipline("Structure", "STRUCT");
matrix.AddDiscipline("MEP", "MEP");
matrix.PopulateStandardMatrix(defaultTolerance: 0.002);

// Configure tolerance per cell (in meters); PopulateStandardMatrix fills cells in
// discipline order: [0]=Architecture–Structure, [1]=Architecture–MEP, [2]=Structure–MEP
matrix.Cells.Single(c => c.DisciplineA == "Architecture" && c.DisciplineB == "Structure").Tolerance = 0.005; // 5mm
matrix.Cells.Single(c => c.DisciplineA == "Structure" && c.DisciplineB == "MEP").Tolerance = 0.010;          // 10mm

// Execute the entire coordination matrix
matrix.Execute(assembly);

Console.WriteLine($"Total coordination clashes: {matrix.TotalClashesCount}");
Console.WriteLine(matrix.ToMarkdownTable());

// Save to buildingSMART BCF 2.1 zip package for issue tracking
matrix.SaveBcf("CoordinationIssues.bcfzip");
```

### 4D TimeLiner & Interactive 3D Viewer

```csharp
using NwcIO;
using NwcIO.Model.Viewer;

// A .nwd is opened without its scene tree: partition nodes in the tree are not decoded yet.
var doc = NwcDocument.Open("construction_site.nwd", new NwcReaderOptions { BuildSceneTree = false });

// Export self-contained single-file HTML WebGL viewer with 4D schedule playback
doc.SaveHtmlViewer("interactive_site.html", new NwcHtmlViewerOptions
{
    IncludeTimeLiner = true,
    IncludeSelectionSets = true
});
```

---

## CLI Usage

Run standard coordination and export tasks from your terminal or CI/CD pipelines:

```bash
# Inspect scene graph and metadata (diagnostic report goes to stderr)
nwcio model.nwc --report

# Convert to IFC4 and glTF
nwcio model.nwc model.ifc --ifc-schema IFC4
nwcio model.nwc model.glb

# Multi-discipline clash matrix across a folder of federated models
nwcio ./site --clash-matrix matrix.json --clash-matrix-md matrix.md --clash-matrix-bcf matrix.bcfzip

# 4D TimeLiner: import schedule.xml (when it exists), link it and simulate a date
nwcio model.nwc --timeliner-xml schedule.xml --link-timeliner --timeliner-simulate 2026-06-01 --timeliner-csv sim.csv

# Standalone 3D HTML WebGL viewer
nwcio model.nwc --html-viewer viewer.html
```

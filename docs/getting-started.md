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

// Open any .nwc or .nwd file directly from disk
var doc = NwcDocument.Open("model.nwc");

Console.WriteLine($"Root nodes: {doc.RootNodes.Count}");
Console.WriteLine($"Total 3D meshes: {doc.Meshes.Count}");

foreach (var node in doc.RootNodes)
{
    Console.WriteLine($"Node: {node.Name} (Category: {node.Category})");
}
```

### Exporting to IFC4, glTF 2.0 (GLB), and OBJ

```csharp
using NwcIO;

var doc = NwcDocument.Open("federated_model.nwc");

// Modern IFC4 with compact IfcTriangulatedFaceSet mesh tessellation
doc.ExportIfc("output.ifc", new NwcIfcExportOptions
{
    Schema = NwcIfcSchema.Ifc4,
    ProjectName = "Coordination Project"
});

// Binary glTF 2.0 for 3D web engines (Three.js, Babylon.js, Cesium)
doc.ExportGlb("output.glb");

// Wavefront OBJ
doc.ExportObj("output.obj");
```

### Multi-Discipline Clash Matrix

```csharp
using NwcIO;

// Create a coordination clash matrix across discipline files
var matrix = new NwcClashMatrix("Coordination Matrix");

var arch = matrix.AddDiscipline("Architecture", NwcDocument.Open("ARCH.nwc"));
var structDoc = matrix.AddDiscipline("Structure", NwcDocument.Open("STRUCT.nwc"));
var mep = matrix.AddDiscipline("MEP", NwcDocument.Open("MEP.nwc"));

// Configure tolerance and exclusion rules per cell
matrix.SetTolerance(arch, structDoc, 0.005); // 5mm
matrix.SetTolerance(structDoc, mep, 0.010);  // 10mm

// Execute the entire coordination matrix
var result = matrix.Execute();

Console.WriteLine($"Total coordination clashes: {result.TotalClashes}");
Console.WriteLine(matrix.ToMarkdownTable());

// Save to buildingSMART BCF 2.1 zip package for issue tracking
matrix.SaveBcf("CoordinationIssues.bcfzip");
```

### 4D TimeLiner & Interactive 3D Viewer

```csharp
using NwcIO;

var doc = NwcDocument.Open("construction_site.nwd");

// Export self-contained single-file HTML WebGL viewer with 4D schedule playback
NwcHtmlViewerExporter.Export(doc, "interactive_site.html", new NwcHtmlViewerOptions
{
    EnableTimeLiner = true,
    EnableSelectionSets = true
});
```

---

## CLI Usage

Run standard coordination and export tasks from your terminal or CI/CD pipelines:

```bash
# Inspect scene graph and metadata
nwcio info model.nwc

# Export to IFC4 and glTF
nwcio export model.nwc --ifc model.ifc --glb model.glb

# Multi-discipline clash matrix
nwcio matrix ARCH.nwc STRUCT.nwc MEP.nwc --out-json matrix.json --bcf matrix.bcfzip

# 4D TimeLiner linking & simulation
nwcio timeliner model.nwc --schedule schedule.xml --link auto --export-csv sim.csv

# Standalone 3D HTML WebGL viewer
nwcio view model.nwc --output viewer.html --timeliner --selection-sets
```

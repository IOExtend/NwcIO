<p align="center">
  <img src="docs/images/nwcio-banner.png" alt="NwcIO" width="640">
</p>

# NwcIO

[![Docs](https://img.shields.io/badge/Docs-Online-3B82F6?style=flat-square&logo=github)](https://ioextend.github.io/NwcIO/)
[![NuGet](https://img.shields.io/nuget/v/NwcIO?style=flat-square&color=3B82F6&logo=nuget)](https://www.nuget.org/packages/NwcIO)
[![Release](https://img.shields.io/github/v/release/IOExtend/NwcIO?style=flat-square&color=3B82F6)](https://github.com/IOExtend/NwcIO/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

NwcIO is a fast, 100% managed .NET library and command line tool that reads Autodesk Navisworks cache and document files (`.nwc`, `.nwd`, `.nwf`) directly from disk without Navisworks or the Navisworks API, and exports IFC, glTF, OBJ, HTML, and JSON.

Built clean-room under the [IOExtend](https://github.com/IOExtend) organization alongside [RvtIO](https://github.com/IOExtend/RvtIO) and [IfcIO](https://github.com/IOExtend/IfcIO).

## What it does

- Reads scene hierarchy, geometry meshes, materials, transforms, GUIDs, and property categories.
- Exports to standard Industry Foundation Classes (IFC2X3 & compact IFC4), binary glTF 2.0 (`.glb`), Wavefront OBJ, and structured JSON.
- Coordinates multi-model federated assemblies with 3D geometric clash detection, proximity clustering, and clash matrix execution.
- Manages saved viewpoints, dynamic section planes, redlines, selection sets, and search sets with Navisworks XML and buildingSMART BCF 2.1 exchange.
- Supports 4D TimeLiner construction simulation, appearance profiling, spatial grids, 3D measurement, and revision comparison.
- Generates standalone, zero-dependency interactive 3D WebGL HTML viewers (`.html`).

## Install

Install the command line tool:

```sh
dotnet tool install --global NwcIO.Cli
```

Add the library to a .NET project:

```sh
dotnet add package NwcIO
```

The library targets .NET 8 and .NET 10. It uses managed code without native dependencies or Windows-only APIs and supports Windows, Linux, and macOS.

## Command line

Export to IFC, glTF GLB, Wavefront OBJ, or JSON:

```sh
# Convert to IFC2X3 or modern compact IFC4
nwcio model.nwc model.ifc --ifc4

# Convert to binary glTF 2.0 (with PBR materials and vertex normals)
nwcio model.nwc model.glb

# Export 3D meshes to Wavefront OBJ
nwcio model.nwc model.obj

# Export scene hierarchy and properties to JSON
nwcio model.nwc model.json
```

Coordinate models, detect clashes, and export BCF:

```sh
# Run clash detection between models and export buildingSMART BCF 2.1
nwcio clash arch.nwc mep.nwc --bcf clashes.bcfzip --tolerance 0.01

# Execute multi-discipline clash matrix across a federated folder
nwcio clash-matrix ./models/ -o matrix.json --export-markdown matrix.md --bcf matrix.bcfzip

# Export standalone 3D WebGL HTML viewer with sectioning and measure tools
nwcio view model.nwc -o viewer.html --grids grids.xml
```

TimeLiner, viewpoints, search sets, and revision diff:

```sh
# Simulate 4D construction schedule at a specific date
nwcio schedule model.nwc --sim-date 2026-06-01 --export-csv schedule.csv

# Export saved viewpoints and section planes to BCF 2.1 or Navisworks XML
nwcio export-viewpoints model.nwc --bcf viewpoints.bcfzip --xml viewpoints.xml

# Compare two model revisions (added, deleted, modified geometry & properties)
nwcio diff baseline.nwc revision.nwc -o diff.json --bcf diff.bcfzip
```

Diagnostics and progress go to standard error. Run `nwcio --help` for the full list of options.

## Library

Open a Navisworks document, inspect elements, and export to GLB or IFC:

```csharp
using NwcIO;
using NwcIO.Exporters;

// Open document directly from disk
var doc = NwcDocument.Open("model.nwc");

Console.WriteLine($"Model: {doc.ModelName}");
Console.WriteLine($"Nodes: {doc.RootNodes.Count}, Triangles: {doc.TotalTriangleCount:N0}");

// Query nodes by property category
foreach (var node in doc.FindNodesByProperty("Item", "Type", "Wall"))
{
    Console.WriteLine($"Node: {node.Name} (Path: {node.Path})");
}

// Export to binary glTF 2.0 and IFC4
NwcGlbExporter.Export(doc, "model.glb");
NwcIfcExporter.Export(doc, "model.ifc", new NwcIfcExportOptions { Schema = NwcIfcSchema.Ifc4 });
```

Run 3D geometric clash detection between documents:

```csharp
using NwcIO;
using NwcIO.Clash;

var archDoc = NwcDocument.Open("Architecture.nwc");
var mepDoc = NwcDocument.Open("MEP.nwc");

// Detect interferences with broad-phase AABB and narrow-phase triangle intersection
var report = NwcClashDetector.DetectClashes(archDoc, mepDoc, new NwcClashOptions
{
    Tolerance = 0.01, // 10mm tolerance
    ClashType = NwcClashType.Hard
});

// Group clashes by spatial 3D proximity and export BCF 2.1
report.GroupByProximity(clusterDistance: 1.5);
report.SaveBcf("Clashes.bcfzip");
```

See the [public API](docs/public-api.md) for the supported surface and [getting started guide](docs/getting-started.md) for detailed workflows.

## Documentation

The documentation site with API references and guides is at <https://ioextend.github.io/NwcIO/>. [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/IOExtend/NwcIO) offers an automatically generated overview of the repository.

## Supported files

Reads binary Navisworks Cache (`.nwc`), Navisworks Document (`.nwd`), and Navisworks Federated (`.nwf`) files. Tested against models saved across Navisworks 2016 through 2026.

## Limitations

- Version 0.4.0 is a preview. The API can evolve before 1.0.
- Geometry extraction supports triangle meshes, line sets, cylinders, and point clouds; proprietary custom parametric solids are tessellated.
- Writing native `.nwd` or `.nwc` files is not supported (export targets open standards: IFC, GLB, OBJ, JSON, HTML, BCF, and Navisworks XML exchange).

## Build from source and tests

Use the .NET SDK selected by `global.json`, which specifies 10.0.100 with `latestFeature` roll-forward.

From the repository root:

```sh
dotnet build NwcIO.slnx -c Release
dotnet test
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md). Release changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## Security

Report vulnerabilities through GitHub private vulnerability reporting in the repository's Security tab. Do not open public issues for security vulnerabilities. See [SECURITY.md](SECURITY.md).

## License and trademarks

NwcIO is licensed under the [MIT License](LICENSE). Copyright holder: mahach666.

NwcIO is not affiliated with, endorsed by or sponsored by Autodesk. Autodesk and Navisworks are trademarks of Autodesk, Inc.

The file format is read by analysing binary streams for interoperability. No Autodesk code or binaries are included.

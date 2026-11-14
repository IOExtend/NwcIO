using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using NwcIO;
using NwcIO.Internal.Json;
using NwcIO.Model;
using NwcIO.Model.Spatial;

namespace NwcIO.Cli;

internal static class Program
{
    private const string UsageText = """
        Usage: nwcio <input.nwc|input.nwd|input.nwf|input_dir> [output.json|output.obj|output.glb|output.ifc|output.bcfzip|output.html|output.nwc] [options]
               nwcio --help | --version

        Reads Autodesk Navisworks files (.nwc, .nwd, .nwf) or assemblies of models
        and exports scene trees, properties, 3D transforms, materials, and 3D polygon meshes
        (IFC/GLB/OBJ/JSON/BCF/HTML/NWC) with BVH spatial acceleration, without requiring Navisworks.

        Arguments:
          <input>            Path to .nwc, .nwd, .nwf file or a folder of .nwc models.
          [output]           Destination path for exported file (.json, .obj, .glb, .ifc, .bcfzip, .html, or .nwc).
                             Default: <input-without-ext>.json (or standard output if '-').

        Options:
          --nwc <file.nwc>   Save or convert to Autodesk Navisworks format (.nwc)
          --sets-xml <file.xml> Export selection sets and search sets to Navisworks XML format (.xml)
          --timeliner-csv <file.csv> Export 4D TimeLiner schedule to CSV format (.csv)
          --timeliner-xml <file.xml> Export or import 4D TimeLiner schedule exchange XML (.xml)
          --timeliner-msproject <file.xml> Export or import 4D TimeLiner schedule in Microsoft Project XML (.xml)
          --timeliner-primavera <file.xer> Export or import 4D TimeLiner schedule in Primavera P6 XER (.xer)
          --link-timeliner   Auto-link schedule tasks to Selection Sets, Search Sets, or element names
          --timeliner-simulate <date> Run 4D simulation at specified date (e.g. 2026-06-15)
          --profiler <file.xml> Apply Navisworks Appearance Profiler color/transparency rules (.xml)
          --export-profiler <file.xml> Export Appearance Profiler rules to Navisworks XML format (.xml)
          --clash-xml <file.xml> Run clash detection and export Navisworks Clash Report XML (.xml)
          --clash-bcf <file.bcfzip> Run clash detection and export buildingSMART BCF 2.1 archive (.bcfzip)
          --clash-matrix <file.json> Execute multi-discipline coordination matrix and export JSON report
          --clash-matrix-md <file.md> Export clash matrix coordination report as Markdown table
          --clash-matrix-bcf <file.bcfzip> Export clash matrix conflicts to buildingSMART BCF 2.1 zip
          --clash-group <mode> Group clashes: proximity, element, grid, layer
          --clash-radius <m>   Cluster radius in meters for proximity grouping (default: 1.0)
          --clash-tolerance <m> Set clash detection tolerance in meters (default: 0.001)
          --diff <baseline.nwc> Compare model against baseline revision
          --diff-json <file.json> Export revision comparison report to JSON
          --diff-bcf <file.bcfzip> Export revision comparison to buildingSMART BCF 2.1 zip
          --diff-tolerance <m> Geometric displacement tolerance for diff (default: 0.005m)
          --grids <file.xml>   Load spatial grid system from XML (.xml)
          --export-grids <file.xml> Export extracted or loaded grid system to XML (.xml)
          --nwf-xml <file.xml> Export Navisworks federated coordination manifest (.nwf.xml)
          --ifc <file.ifc>   Export 3D geometry, materials, and properties to IFC (IFC2x3 / IFC4)
          --ifc-schema <schema> Target IFC schema: IFC2X3 (default) or IFC4 (Tessellation)
          --no-types         Exclude IFC type product definitions (IfcTypeProduct)
          --glb <file.glb>   Export 3D geometry and materials to glTF 2.0 Binary format (GLB)
          --obj <file.obj>   Export 3D geometry to Wavefront OBJ format
          --bcf <file.bcfzip> Export cross-model clashes to buildingSMART BCF 2.1 zip archive
          --viewpoints-bcf <file.bcfzip> Export saved viewpoints to buildingSMART BCF 2.1 zip archive
          --viewpoints-xml <file.xml>    Export saved viewpoints and redlines to Navisworks XML format
          --viewpoints       List saved viewpoints and cameras to standard output
          --html-viewer <file.html> Export standalone interactive 3D WebGL viewer (.html)
          --clash-html <file.html> Export interactive HTML clash detection report with matrix
          --local-coords     Export geometry in local coordinate space (default: world coordinates)
          --no-meshes        Disable 3D mesh geometry extraction
          --no-materials     Disable material and surface color extraction
          --catalog          Include raw container stream catalog (offsets, sizes, kinds)
          --no-transforms    Exclude 3D transform matrices from JSON export
          --no-tree          Skip reading the scene tree and exclude it from JSON export (required to open a .nwd with partition nodes or a linked Revit model)
          --no-indent        Write compact JSON instead of pretty-printed indented JSON
          --bvh-stats        Include Bounding Volume Hierarchy (BVH) spatial index statistics
          --raycast <coords> Perform 3D raycast test: "originX,originY,originZ,dirX,dirY,dirZ"
          --clashes          Run cross-model clash detection for multi-model assemblies
          --report           Print summary report to standard error
          -h, --help         Show this help message and exit
          -v, --version      Show version information and exit

        Exit codes:
          0  Success
          1  Usage error (missing or invalid arguments)
          2  File or folder not found
          3  Data format error or parsing failure
        """;

    // The reader throws NotSupportedException only for a scene tree it cannot convert (class-32 partition nodes, linked Revit models); --no-tree reads around both.
    private const string NoTreeHint = " Use --no-tree to read geometry and placement without the scene tree.";

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(UsageText);
            return 1;
        }

        string? inputPath = null;
        string? outputPath = null;
        string? explicitObjPath = null;
        string? explicitGlbPath = null;
        string? explicitIfcPath = null;
        string? explicitNwcPath = null;
        string? explicitSetsXmlPath = null;
        string? explicitTimeLinerCsvPath = null;
        string? explicitTimeLinerXmlPath = null;
        string? explicitTimeLinerMsProjectPath = null;
        string? explicitTimeLinerPrimaveraPath = null;
        bool linkTimeLiner = false;
        string? explicitClashMatrixJsonPath = null;
        string? explicitClashMatrixMdPath = null;
        string? explicitClashMatrixBcfPath = null;
        string? simulateDateStr = null;
        string? explicitProfilerPath = null;
        string? explicitExportProfilerPath = null;
        string? explicitClashXmlPath = null;
        string? explicitClashBcfPath = null;
        string? explicitNwfXmlPath = null;
        double clashTolerance = 0.001;
        string? explicitBcfPath = null;
        string? explicitViewpointsBcfPath = null;
        string? explicitViewpointsXmlPath = null;
        string? explicitClashHtmlPath = null;
        string? explicitHtmlViewerPath = null;
        string? explicitDiffBaseline = null;
        string? explicitDiffJsonPath = null;
        string? explicitDiffBcfPath = null;
        string? explicitGridsPath = null;
        string? explicitExportGridsPath = null;
        double diffTolerance = 0.005;
        bool diffToleranceSpecified = false;
        string? clashGroupMode = null;
        double clashRadius = 1.0;
        string? raycastArg = null;
        NwcIfcSchemaVersion ifcSchema = NwcIfcSchemaVersion.Ifc2x3;
        bool exportIfcTypes = true;
        bool checkClashes = false;
        bool listViewpoints = false;
        bool bvhStats = false;
        bool worldCoords = true;
        bool readMeshes = true;
        bool readMaterials = true;
        bool includeCatalog = false;
        bool includeTransforms = true;
        bool includeTree = true;
        bool indent = true;
        bool printReport = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    Console.WriteLine(UsageText);
                    return 0;
                case "--version":
                case "-v":
                    var version = typeof(Program).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? typeof(Program).Assembly.GetName().Version?.ToString()
                        ?? "0.1.0";
                    Console.WriteLine($"nwcio {version}");
                    return 0;
                case "--catalog":
                    includeCatalog = true;
                    break;
                case "--no-transforms":
                    includeTransforms = false;
                    break;
                case "--no-tree":
                    includeTree = false;
                    break;
                case "--no-meshes":
                    readMeshes = false;
                    break;
                case "--no-materials":
                    readMaterials = false;
                    break;
                case "--local-coords":
                    worldCoords = false;
                    break;
                case "--bvh-stats":
                    bvhStats = true;
                    break;
                case "--clashes":
                    checkClashes = true;
                    break;
                case "--raycast":
                    if (!TryConsumeValue(args, ref i, "--raycast", out raycastArg)) return 1;
                    var rayParts = raycastArg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (rayParts.Length != 6 ||
                        !double.TryParse(rayParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                        !double.TryParse(rayParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                        !double.TryParse(rayParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                        !double.TryParse(rayParts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                        !double.TryParse(rayParts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                        !double.TryParse(rayParts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        Console.Error.WriteLine($"Error: Invalid raycast format '{raycastArg}'. Expected: ox,oy,oz,dx,dy,dz");
                        return 1;
                    }
                    break;
                case "--nwc":
                    if (!TryConsumeValue(args, ref i, "--nwc", out explicitNwcPath)) return 1;
                    break;
                case "--sets-xml":
                    if (!TryConsumeValue(args, ref i, "--sets-xml", out explicitSetsXmlPath)) return 1;
                    break;
                case "--timeliner-csv":
                    if (!TryConsumeValue(args, ref i, "--timeliner-csv", out explicitTimeLinerCsvPath)) return 1;
                    break;
                case "--timeliner-xml":
                    if (!TryConsumeValue(args, ref i, "--timeliner-xml", out explicitTimeLinerXmlPath)) return 1;
                    break;
                case "--timeliner-msproject":
                case "--timeliner-msp":
                case "--export-msproject":
                    if (!TryConsumeValue(args, ref i, arg, out explicitTimeLinerMsProjectPath)) return 1;
                    break;
                case "--timeliner-primavera":
                case "--timeliner-p6":
                case "--export-primavera":
                    if (!TryConsumeValue(args, ref i, arg, out explicitTimeLinerPrimaveraPath)) return 1;
                    break;
                case "--link-timeliner":
                    linkTimeLiner = true;
                    break;
                case "--clash-matrix":
                    if (!TryConsumeValue(args, ref i, "--clash-matrix", out explicitClashMatrixJsonPath)) return 1;
                    break;
                case "--clash-matrix-md":
                    if (!TryConsumeValue(args, ref i, "--clash-matrix-md", out explicitClashMatrixMdPath)) return 1;
                    break;
                case "--clash-matrix-bcf":
                    if (!TryConsumeValue(args, ref i, "--clash-matrix-bcf", out explicitClashMatrixBcfPath)) return 1;
                    break;
                case "--timeliner-simulate":
                    if (!TryConsumeValue(args, ref i, "--timeliner-simulate", out simulateDateStr)) return 1;
                    if (!DateTimeOffset.TryParse(simulateDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                    {
                        Console.Error.WriteLine($"Error: Unable to parse simulation date '{simulateDateStr}'. Expected format 'yyyy-MM-dd'.");
                        return 1;
                    }
                    break;
                case "--profiler":
                    if (!TryConsumeValue(args, ref i, "--profiler", out explicitProfilerPath)) return 1;
                    break;
                case "--export-profiler":
                    if (!TryConsumeValue(args, ref i, "--export-profiler", out explicitExportProfilerPath)) return 1;
                    break;
                case "--clash-xml":
                    if (!TryConsumeValue(args, ref i, "--clash-xml", out explicitClashXmlPath)) return 1;
                    break;
                case "--clash-bcf":
                    if (!TryConsumeValue(args, ref i, "--clash-bcf", out explicitClashBcfPath)) return 1;
                    break;
                case "--clash-group":
                    if (!TryConsumeValue(args, ref i, "--clash-group", out clashGroupMode)) return 1;
                    if (clashGroupMode != "proximity" && clashGroupMode != "element" && clashGroupMode != "grid" && clashGroupMode != "layer")
                    {
                        Console.Error.WriteLine($"Error: Invalid clash group mode '{clashGroupMode}'. Supported modes: proximity, element, grid, layer.");
                        return 1;
                    }
                    break;
                case "--clash-radius":
                    if (!TryConsumeValue(args, ref i, "--clash-radius", out string clashRadiusStr) ||
                        !double.TryParse(clashRadiusStr, NumberStyles.Float, CultureInfo.InvariantCulture, out clashRadius) ||
                        double.IsNaN(clashRadius) || double.IsInfinity(clashRadius) || clashRadius <= 0)
                    {
                        Console.Error.WriteLine($"Error: Invalid value for --clash-radius: '{clashRadiusStr}'. Expected positive number.");
                        return 1;
                    }
                    break;
                case "--html-viewer":
                case "--viewer":
                    if (!TryConsumeValue(args, ref i, arg, out explicitHtmlViewerPath)) return 1;
                    break;
                case "--clash-tolerance":
                    if (!TryConsumeValue(args, ref i, "--clash-tolerance", out string clashTolStr) ||
                        !double.TryParse(clashTolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out clashTolerance) ||
                        double.IsNaN(clashTolerance) || double.IsInfinity(clashTolerance) || clashTolerance < 0)
                    {
                        Console.Error.WriteLine($"Error: Invalid value for --clash-tolerance: '{clashTolStr}'. Expected non-negative number.");
                        return 1;
                    }
                    break;
                case "--diff":
                    if (!TryConsumeValue(args, ref i, "--diff", out explicitDiffBaseline)) return 1;
                    break;
                case "--diff-json":
                    if (!TryConsumeValue(args, ref i, "--diff-json", out explicitDiffJsonPath)) return 1;
                    break;
                case "--diff-bcf":
                    if (!TryConsumeValue(args, ref i, "--diff-bcf", out explicitDiffBcfPath)) return 1;
                    break;
                case "--diff-tolerance":
                    diffToleranceSpecified = true;
                    if (!TryConsumeValue(args, ref i, "--diff-tolerance", out string diffTolStr) ||
                        !double.TryParse(diffTolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out diffTolerance) ||
                        double.IsNaN(diffTolerance) || double.IsInfinity(diffTolerance) || diffTolerance < 0)
                    {
                        Console.Error.WriteLine($"Error: Invalid value for --diff-tolerance: '{diffTolStr}'. Expected non-negative number.");
                        return 1;
                    }
                    break;
                case "--grids":
                    if (!TryConsumeValue(args, ref i, "--grids", out explicitGridsPath)) return 1;
                    break;
                case "--export-grids":
                    if (!TryConsumeValue(args, ref i, "--export-grids", out explicitExportGridsPath)) return 1;
                    break;
                case "--nwf-xml":
                    if (!TryConsumeValue(args, ref i, "--nwf-xml", out explicitNwfXmlPath)) return 1;
                    break;
                case "--ifc":
                    if (!TryConsumeValue(args, ref i, "--ifc", out explicitIfcPath)) return 1;
                    break;
                case "--ifc-schema":
                    if (!TryConsumeValue(args, ref i, "--ifc-schema", out string schemaStr)) return 1;
                    if (schemaStr.Equals("IFC4", StringComparison.OrdinalIgnoreCase))
                    {
                        ifcSchema = NwcIfcSchemaVersion.Ifc4;
                    }
                    else if (schemaStr.Equals("IFC2X3", StringComparison.OrdinalIgnoreCase))
                    {
                        ifcSchema = NwcIfcSchemaVersion.Ifc2x3;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Invalid IFC schema '{schemaStr}'. Expected 'IFC2X3' or 'IFC4'.");
                        return 1;
                    }
                    break;
                case "--no-types":
                    exportIfcTypes = false;
                    break;
                case "--obj":
                    if (!TryConsumeValue(args, ref i, "--obj", out explicitObjPath)) return 1;
                    break;
                case "--glb":
                    if (!TryConsumeValue(args, ref i, "--glb", out explicitGlbPath)) return 1;
                    break;
                case "--bcf":
                    if (!TryConsumeValue(args, ref i, "--bcf", out explicitBcfPath)) return 1;
                    break;
                case "--viewpoints-bcf":
                    if (!TryConsumeValue(args, ref i, "--viewpoints-bcf", out explicitViewpointsBcfPath)) return 1;
                    break;
                case "--viewpoints-xml":
                    if (!TryConsumeValue(args, ref i, "--viewpoints-xml", out explicitViewpointsXmlPath)) return 1;
                    break;
                case "--viewpoints":
                    listViewpoints = true;
                    break;
                case "--clash-html":
                    if (!TryConsumeValue(args, ref i, "--clash-html", out explicitClashHtmlPath)) return 1;
                    break;
                case "--no-indent":
                    indent = false;
                    break;
                case "--report":
                    printReport = true;
                    break;
                default:
                    if (arg != "-" && arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Error: Unrecognized option '{arg}'.");
                        Console.Error.WriteLine(UsageText);
                        return 1;
                    }

                    if (inputPath is null)
                    {
                        inputPath = arg;
                    }
                    else if (outputPath is null)
                    {
                        outputPath = arg;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Unexpected extra argument '{arg}'.");
                        return 1;
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            Console.Error.WriteLine("Error: Missing input file path.");
            Console.Error.WriteLine(UsageText);
            return 1;
        }

        if (explicitDiffBaseline is null && (explicitDiffJsonPath != null || explicitDiffBcfPath != null || diffToleranceSpecified))
        {
            Console.Error.WriteLine("Error: --diff-json, --diff-bcf, and --diff-tolerance require --diff <baseline>.");
            return 1;
        }

        bool isDirectory = Directory.Exists(inputPath);
        bool isFile = File.Exists(inputPath);

        if (isDirectory || inputPath.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase))
        {
            if (explicitDiffBaseline != null || explicitDiffJsonPath != null || explicitDiffBcfPath != null || diffToleranceSpecified)
            {
                Console.Error.WriteLine("Error: --diff, --diff-json, --diff-bcf, and --diff-tolerance are not supported for folder or .nwf assembly inputs.");
                return 1;
            }
            if (!string.IsNullOrEmpty(simulateDateStr))
            {
                Console.Error.WriteLine("Error: --timeliner-simulate is not supported for folder or .nwf assembly inputs.");
                return 1;
            }
        }

        if (!isDirectory && !isFile)
        {
            Console.Error.WriteLine($"Error: Input '{inputPath}' does not exist as file or directory.");
            return 2;
        }

        var sw = Stopwatch.StartNew();

        var ifcExportOptions = new NwcIfcExportOptions
        {
            SchemaVersion = ifcSchema,
            ExportTypeObjects = exportIfcTypes,
            WorldCoordinates = worldCoords,
            ExportMaterials = readMaterials,
        };

        // Handle multi-model directory assembly and NWF assembly files with the same
        // exit-code mapping as the single-file path: a missing folder or a folder with
        // no models is a level-2 error, anything thrown while reading model data is a
        // data format error (3) — never an unhandled exception with a stack trace.
        try
        {
            if (isDirectory)
            {
                return ProcessAssemblyDirectory(
                    inputPath, outputPath, explicitGlbPath, explicitObjPath, explicitIfcPath, explicitNwcPath,
                    explicitBcfPath, explicitViewpointsBcfPath, explicitViewpointsXmlPath, explicitClashHtmlPath, explicitProfilerPath,
                    worldCoords, checkClashes, listViewpoints, bvhStats, raycastArg, printReport, indent, ifcExportOptions, sw,
                    explicitClashXmlPath, explicitClashBcfPath, explicitNwfXmlPath, clashTolerance, explicitHtmlViewerPath, clashGroupMode, clashRadius,
                    explicitTimeLinerXmlPath, linkTimeLiner, explicitClashMatrixJsonPath, explicitClashMatrixMdPath, explicitClashMatrixBcfPath,
                    explicitTimeLinerMsProjectPath, explicitTimeLinerPrimaveraPath, includeTree);
            }

            if (inputPath.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessNwfAssembly(
                    inputPath, outputPath, explicitGlbPath, explicitObjPath, explicitIfcPath, explicitNwcPath,
                    explicitBcfPath, explicitViewpointsBcfPath, explicitViewpointsXmlPath, explicitClashHtmlPath, explicitProfilerPath,
                    worldCoords, checkClashes, listViewpoints, bvhStats, raycastArg, printReport, indent, ifcExportOptions, sw,
                    explicitClashXmlPath, explicitClashBcfPath, explicitNwfXmlPath, clashTolerance, explicitHtmlViewerPath, clashGroupMode, clashRadius,
                    explicitTimeLinerXmlPath, linkTimeLiner, explicitClashMatrixJsonPath, explicitClashMatrixMdPath, explicitClashMatrixBcfPath,
                    explicitTimeLinerMsProjectPath, explicitTimeLinerPrimaveraPath, includeTree);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}{(ex is NotSupportedException ? NoTreeHint : "")}");
            return ex is DirectoryNotFoundException or FileNotFoundException ? 2 : 3;
        }

        // Single NWC/NWD document
        outputPath ??= Path.ChangeExtension(inputPath, ".json");

        NwcDocument doc;
        try
        {
            doc = NwcDocument.Open(inputPath, new NwcReaderOptions
            {
                ReadMeshes = readMeshes,
                ReadMaterials = readMaterials,
                ReadPropertyRecords = true,
                ReadGuids = true,
                ReadTransforms = includeTransforms,
                BuildSceneTree = includeTree,
                KeepStreamCatalog = includeCatalog,
            });
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' is not a valid Navisworks container: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading '{inputPath}': {ex.Message}{(ex is NotSupportedException ? NoTreeHint : "")}");
            return 3;
        }

        bool outputIsIfc = outputPath.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase);
        bool outputIsObj = outputPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);
        bool outputIsGlb = outputPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        bool outputIsBcf = outputPath.EndsWith(".bcf", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".bcfzip", StringComparison.OrdinalIgnoreCase);
        bool outputIsHtml = outputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
        bool outputIsNwc = outputPath.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase);
        bool outputIsXml = outputPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        bool outputIsCsv = outputPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        NwcAssembly? singleAssembly = null;
        NwcAssembly GetSingleAssembly() => singleAssembly ??= NwcAssembly.Create(Path.GetFileNameWithoutExtension(inputPath), doc);

        if (!string.IsNullOrEmpty(explicitProfilerPath))
        {
            if (!File.Exists(explicitProfilerPath))
            {
                Console.Error.WriteLine($"Error: Appearance profiler file not found: {explicitProfilerPath}");
                return 2;
            }

            try
            {
                var profiler = Model.Appearance.NwcXmlAppearanceProfilerSerializer.Open(explicitProfilerPath);
                int overridden = doc.ApplyAppearanceProfiler(profiler);
                if (printReport)
                {
                    Console.Error.WriteLine($"Appearance profiler applied: {overridden} elements overridden.");
                }
            }
            catch (Exception ex)
            {
                // Malformed profiler XML (XmlException) or unknown operators (FormatException).
                Console.Error.WriteLine($"Error: Invalid appearance profiler file '{explicitProfilerPath}': {ex.Message}");
                return 3;
            }
        }

        try
        {
            // TimeLiner exchange imports/exports and linking run BEFORE any schedule export,
            // so --timeliner-csv and a .csv primary output see the imported, linked schedule.
            if (!string.IsNullOrEmpty(explicitTimeLinerXmlPath))
            {
                if (File.Exists(explicitTimeLinerXmlPath))
                {
                    doc.ImportTimeLinerXml(explicitTimeLinerXmlPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"TimeLiner schedule loaded from XML: '{explicitTimeLinerXmlPath}'.");
                    }
                }
                else
                {
                    EnsureParentDirectory(explicitTimeLinerXmlPath);
                    doc.ExportTimeLinerXml(explicitTimeLinerXmlPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"TimeLiner schedule exported to XML: '{explicitTimeLinerXmlPath}'.");
                    }
                }
            }

            if (!string.IsNullOrEmpty(explicitTimeLinerMsProjectPath))
            {
                if (File.Exists(explicitTimeLinerMsProjectPath))
                {
                    doc.ImportTimeLinerMsProject(explicitTimeLinerMsProjectPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"TimeLiner schedule loaded from MS Project XML: '{explicitTimeLinerMsProjectPath}'.");
                    }
                }
                else
                {
                    EnsureParentDirectory(explicitTimeLinerMsProjectPath);
                    doc.ExportTimeLinerMsProject(explicitTimeLinerMsProjectPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"TimeLiner schedule exported to MS Project XML: '{explicitTimeLinerMsProjectPath}'.");
                    }
                }
            }

            if (!string.IsNullOrEmpty(explicitTimeLinerPrimaveraPath))
            {
                if (File.Exists(explicitTimeLinerPrimaveraPath))
                {
                    doc.ImportTimeLinerPrimavera(explicitTimeLinerPrimaveraPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"TimeLiner schedule loaded from Primavera P6 XER: '{explicitTimeLinerPrimaveraPath}'.");
                    }
                }
                else
                {
                    EnsureParentDirectory(explicitTimeLinerPrimaveraPath);
                    doc.ExportTimeLinerPrimavera(explicitTimeLinerPrimaveraPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"TimeLiner schedule exported to Primavera P6 XER: '{explicitTimeLinerPrimaveraPath}'.");
                    }
                }
            }

            if (linkTimeLiner && doc.TimeLiner is not null)
            {
                int linked = Model.TimeLiner.NwcTimeLinerLinker.AutoLink(doc.TimeLiner, doc.SelectionSets, doc);
                if (linked == 0 && doc.RootNode is not null)
                {
                    linked = Model.TimeLiner.NwcTimeLinerLinker.LinkByNodeName(doc.TimeLiner, doc.RootNode);
                }
                Console.WriteLine($"Linked {linked} TimeLiner tasks to model elements.");
            }

            if (outputIsBcf)
            {
                EnsureParentDirectory(outputPath);
                GetSingleAssembly().SaveBcf(outputPath);
            }
            else if (outputIsHtml)
            {
                EnsureParentDirectory(outputPath);
                GetSingleAssembly().SaveClashReportHtml(outputPath);
            }
            else if (outputIsIfc)
            {
                EnsureParentDirectory(outputPath);
                doc.SaveIfc(outputPath, ifcExportOptions);
            }
            else if (outputIsGlb)
            {
                EnsureParentDirectory(outputPath);
                doc.SaveGlb(outputPath, worldCoords);
            }
            else if (outputIsObj)
            {
                EnsureParentDirectory(outputPath);
                doc.SaveObj(outputPath, worldCoords);
            }
            else if (outputIsNwc)
            {
                EnsureParentDirectory(outputPath);
                doc.Save(outputPath);
            }
            else if (outputIsXml)
            {
                EnsureParentDirectory(outputPath);
                doc.ExportSelectionSetsXml(outputPath);
            }
            else if (outputIsCsv)
            {
                EnsureParentDirectory(outputPath);
                doc.ExportTimeLinerCsv(outputPath);
            }
            else
            {
                var exportOptions = new NwcJsonExportOptions
                {
                    Indented = indent,
                    IncludeTransforms = includeTransforms,
                    IncludeSceneTree = includeTree,
                    IncludeMeshes = readMeshes,
                    IncludeMaterials = readMaterials,
                    IncludeStreamCatalog = includeCatalog,
                };

                if (outputPath == "-")
                {
                    using var stdout = Console.OpenStandardOutput();
                    JsonExporter.Write(doc, stdout, exportOptions);
                }
                else
                {
                    EnsureParentDirectory(outputPath);
                    JsonExporter.WriteFile(doc, outputPath, exportOptions);
                }
            }

            if (!string.IsNullOrEmpty(explicitIfcPath))
            {
                EnsureParentDirectory(explicitIfcPath);
                doc.SaveIfc(explicitIfcPath, ifcExportOptions);
            }

            if (!string.IsNullOrEmpty(explicitNwcPath))
            {
                EnsureParentDirectory(explicitNwcPath);
                doc.Save(explicitNwcPath);
            }

            if (!string.IsNullOrEmpty(explicitGlbPath))
            {
                EnsureParentDirectory(explicitGlbPath);
                doc.SaveGlb(explicitGlbPath, worldCoords);
            }

            if (!string.IsNullOrEmpty(explicitObjPath))
            {
                EnsureParentDirectory(explicitObjPath);
                doc.SaveObj(explicitObjPath, worldCoords);
            }

            if (!string.IsNullOrEmpty(explicitBcfPath))
            {
                EnsureParentDirectory(explicitBcfPath);
                GetSingleAssembly().SaveBcf(explicitBcfPath);
            }

            if (!string.IsNullOrEmpty(explicitViewpointsBcfPath))
            {
                EnsureParentDirectory(explicitViewpointsBcfPath);
                doc.SaveViewpointsBcf(explicitViewpointsBcfPath);
            }

            if (!string.IsNullOrEmpty(explicitViewpointsXmlPath))
            {
                EnsureParentDirectory(explicitViewpointsXmlPath);
                doc.SaveViewpointsXml(explicitViewpointsXmlPath);
            }

            if (!string.IsNullOrEmpty(explicitClashHtmlPath))
            {
                EnsureParentDirectory(explicitClashHtmlPath);
                GetSingleAssembly().SaveClashReportHtml(explicitClashHtmlPath);
            }

            if (!string.IsNullOrEmpty(explicitSetsXmlPath))
            {
                EnsureParentDirectory(explicitSetsXmlPath);
                doc.ExportSelectionSetsXml(explicitSetsXmlPath);
            }

            if (!string.IsNullOrEmpty(explicitExportProfilerPath))
            {
                if (doc.AppearanceProfiler is null)
                {
                    Console.Error.WriteLine("Error: Document has no Appearance Profiler rules to export.");
                    return 1;
                }
                EnsureParentDirectory(explicitExportProfilerPath);
                doc.ExportAppearanceProfilerXml(explicitExportProfilerPath);
            }

            if (!string.IsNullOrEmpty(explicitClashXmlPath))
            {
                EnsureParentDirectory(explicitClashXmlPath);
                var test = new Model.Clash.NwcClashTest("Clash Test", Model.Clash.NwcClashTestType.Hard, clashTolerance);
                doc.RunClashDetection(test);
                ApplyClashGrouping(test, clashGroupMode, clashRadius, printReport);
                doc.ExportClashReportXml(explicitClashXmlPath);
                if (printReport)
                {
                    Console.Error.WriteLine($"Clash detection finished: {test.TotalClashCount} clashes ({test.Groups.Count} groups) exported to '{explicitClashXmlPath}'.");
                }
            }

            if (!string.IsNullOrEmpty(explicitClashBcfPath))
            {
                EnsureParentDirectory(explicitClashBcfPath);
                if (doc.ClashTests.Count == 0)
                {
                    var test = new Model.Clash.NwcClashTest("Clash Test", Model.Clash.NwcClashTestType.Hard, clashTolerance);
                    doc.RunClashDetection(test);
                    ApplyClashGrouping(test, clashGroupMode, clashRadius, printReport);
                }
                doc.ExportClashBcf(explicitClashBcfPath);
                if (printReport)
                {
                    Console.Error.WriteLine($"Clash BCF 2.1 exported to '{explicitClashBcfPath}'.");
                }
            }

            if (!string.IsNullOrEmpty(explicitHtmlViewerPath))
            {
                EnsureParentDirectory(explicitHtmlViewerPath);
                doc.SaveHtmlViewer(explicitHtmlViewerPath);
            }

            if (!string.IsNullOrEmpty(explicitGridsPath))
            {
                if (!File.Exists(explicitGridsPath))
                {
                    Console.Error.WriteLine($"Error: Grids file '{explicitGridsPath}' not found.");
                    return 2;
                }
                var grids = Model.Grids.NwcGridSystem.LoadXml(explicitGridsPath);
                doc.GridSystem = grids;
                int assigned = doc.AssignGridLocations(grids);
                if (printReport)
                {
                    Console.Error.WriteLine($"Loaded grid system '{grids.Name}' with {grids.Lines.Count} lines. Assigned grid locations to {assigned} clashes.");
                }
            }

            if (!string.IsNullOrEmpty(explicitExportGridsPath))
            {
                EnsureParentDirectory(explicitExportGridsPath);
                var grids = doc.GridSystem ?? doc.ExtractGridSystem();
                grids.SaveXml(explicitExportGridsPath);
                if (printReport)
                {
                    Console.Error.WriteLine($"Exported grid system '{grids.Name}' ({grids.Lines.Count} lines) to '{explicitExportGridsPath}'.");
                }
            }

            if (!string.IsNullOrEmpty(explicitDiffBaseline))
            {
                if (!File.Exists(explicitDiffBaseline))
                {
                    Console.Error.WriteLine($"Error: Baseline file '{explicitDiffBaseline}' not found.");
                    return 2;
                }
                var baseDoc = NwcDocument.Open(explicitDiffBaseline, new NwcReaderOptions { BuildSceneTree = includeTree });
                var diffReport = baseDoc.Compare(doc, new Model.Diff.NwcDiffOptions { GeometricTolerance = diffTolerance });

                if (!string.IsNullOrEmpty(explicitDiffJsonPath))
                {
                    EnsureParentDirectory(explicitDiffJsonPath);
                    diffReport.SaveJson(explicitDiffJsonPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"Revision diff exported to JSON: '{explicitDiffJsonPath}'.");
                    }
                }

                if (!string.IsNullOrEmpty(explicitDiffBcfPath))
                {
                    EnsureParentDirectory(explicitDiffBcfPath);
                    diffReport.SaveBcf(explicitDiffBcfPath);
                    if (printReport)
                    {
                        Console.Error.WriteLine($"Revision diff exported to BCF 2.1: '{explicitDiffBcfPath}'.");
                    }
                }

                Console.WriteLine($"Model Revision Comparison ({diffReport.BaselineName} vs {diffReport.RevisionName}):");
                Console.WriteLine($"  Added:     {diffReport.Added.Count}");
                Console.WriteLine($"  Deleted:   {diffReport.Deleted.Count}");
                Console.WriteLine($"  Modified:  {diffReport.Modified.Count}");
                Console.WriteLine($"  Unchanged: {diffReport.Unchanged.Count}");
                Console.WriteLine($"  Total:     {diffReport.TotalChangesCount} changes");
            }

            if (!string.IsNullOrEmpty(explicitTimeLinerCsvPath))
            {
                EnsureParentDirectory(explicitTimeLinerCsvPath);
                doc.ExportTimeLinerCsv(explicitTimeLinerCsvPath);
            }

            bool needDocClashMatrix = !string.IsNullOrEmpty(explicitClashMatrixJsonPath) ||
                                      !string.IsNullOrEmpty(explicitClashMatrixMdPath) ||
                                      !string.IsNullOrEmpty(explicitClashMatrixBcfPath);
            if (needDocClashMatrix)
            {
                var singleAss = GetSingleAssembly();
                singleAss.ClashMatrix ??= Model.Clash.NwcClashMatrix.CreateFromAssembly(singleAss);
                singleAss.ClashMatrix.Execute(singleAss);

                if (!string.IsNullOrEmpty(explicitClashMatrixJsonPath))
                {
                    EnsureParentDirectory(explicitClashMatrixJsonPath);
                    singleAss.ExportClashMatrixJson(explicitClashMatrixJsonPath, indent);
                }
                if (!string.IsNullOrEmpty(explicitClashMatrixMdPath))
                {
                    EnsureParentDirectory(explicitClashMatrixMdPath);
                    singleAss.ExportClashMatrixMarkdown(explicitClashMatrixMdPath);
                }
                if (!string.IsNullOrEmpty(explicitClashMatrixBcfPath))
                {
                    EnsureParentDirectory(explicitClashMatrixBcfPath);
                    singleAss.ExportClashMatrixBcf(explicitClashMatrixBcfPath);
                }

                Console.WriteLine(singleAss.ClashMatrix.ToMarkdownTable());
            }

            if (!string.IsNullOrEmpty(simulateDateStr))
            {
                if (doc.TimeLiner is null || doc.TimeLiner.Tasks.Count == 0)
                {
                    Console.Error.WriteLine("Notice: Document has no TimeLiner schedule tasks to simulate.");
                }
                else if (DateTimeOffset.TryParse(simulateDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var simDate))
                {
                    Console.WriteLine($"4D TimeLiner Simulation at {simDate:yyyy-MM-dd HH:mm:ss}:");
                    Console.WriteLine($"  Overall Progress: {doc.TimeLiner.GetSimulationProgressAt(simDate):P1}");
                    var active = doc.TimeLiner.GetActiveTasksAt(simDate);
                    Console.WriteLine($"  Active Tasks ({active.Count}):");
                    foreach (var t in active)
                    {
                        Console.WriteLine($"    - [{t.Id}] {t.Name} ({t.TaskType}) from {t.PlannedStart:yyyy-MM-dd} to {t.PlannedEnd:yyyy-MM-dd}");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Error: Unable to parse simulation date '{simulateDateStr}'. Expected format 'yyyy-MM-dd'.");
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error exporting to '{outputPath}': {ex.Message}");
            return 3;
        }

        // Raycast test if requested
        if (!string.IsNullOrEmpty(raycastArg))
        {
            ExecuteRaycastTest(doc.SpatialIndex, raycastArg);
        }

        if (listViewpoints)
        {
            PrintViewpoints(doc.SavedViewpoints);
        }

        sw.Stop();

        if (printReport)
        {
            int treeNodeCount = CountNodes(doc.RootNode);
            int totalVertices = doc.Meshes.Sum(m => m.Vertices.Count);
            int totalTriangles = doc.Meshes.Sum(m => m.TriangleCount);

            Console.Error.WriteLine("=== NwcIO Execution Report ===");
            Console.Error.WriteLine($"Input:            {inputPath}");
            Console.Error.WriteLine($"Source document:  {doc.FileInfo.SourcePath ?? "<unknown>"}");
            Console.Error.WriteLine($"Loader plugin:    {doc.FileInfo.LoaderPlugin ?? "<unknown>"}");
            Console.Error.WriteLine($"Timestamp:        {doc.FileInfo.SourceTimestampUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? "<unknown>"}");
            Console.Error.WriteLine($"Scene tree nodes: {treeNodeCount}");
            Console.Error.WriteLine($"Property records: {doc.PropertyRecords.Count}");
            Console.Error.WriteLine($"GUIDs:            {doc.Guids.Count}");
            Console.Error.WriteLine($"3D Transforms:    {doc.Transforms.Count}");
            Console.Error.WriteLine($"3D Meshes:        {doc.Meshes.Count}");
            Console.Error.WriteLine($"Vertices:         {totalVertices:N0}");
            Console.Error.WriteLine($"Triangles:        {totalTriangles:N0}");
            Console.Error.WriteLine($"Materials:        {doc.Materials.Count}");
            Console.Error.WriteLine($"Saved viewpoints: {doc.SavedViewpoints.Count}");
            if (doc.SelectionSets.Count > 0)
            {
                Console.Error.WriteLine($"Selection Sets:   {doc.SelectionSets.Count}");
            }
            if (doc.TimeLiner is not null)
            {
                Console.Error.WriteLine($"TimeLiner Tasks:  {doc.TimeLiner.Tasks.Count} (Planned Budget: {doc.TimeLiner.TotalPlannedCost:C0})");
            }
            if (doc.XRefs is not null)
            {
                Console.Error.WriteLine($"XRefs:            {doc.XRefs.References.Count}");
            }
            if (doc.DbCache is not null)
            {
                Console.Error.WriteLine($"DbCache Entries:  {doc.DbCache.Entries.Count}");
            }
            if (bvhStats && doc.Meshes.Count > 0)
            {
                var idx = doc.SpatialIndex;
                Console.Error.WriteLine($"BVH Nodes:        {idx.NodeCount} ({idx.LeafCount} leaves, max depth {idx.MaxDepth})");
                Console.Error.WriteLine($"Bounding box:     {idx.Bounds}");
            }
            Console.Error.WriteLine($"Elapsed:          {sw.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"Output:           {outputPath}");
        }
        else
        {
            Console.WriteLine(FormattableString.Invariant(
                $"Exported {doc.PropertyRecords.Count} records, {doc.Guids.Count} GUIDs, {doc.Transforms.Count} transforms, {doc.Meshes.Count} meshes, {doc.Materials.Count} materials to '{outputPath}' in {sw.ElapsedMilliseconds} ms."));
        }

        return 0;
    }

    private static int ProcessAssemblyDirectory(
        string dirPath,
        string? outputPath,
        string? explicitGlbPath,
        string? explicitObjPath,
        string? explicitIfcPath,
        string? explicitNwcPath,
        string? explicitBcfPath,
        string? explicitViewpointsBcfPath,
        string? explicitViewpointsXmlPath,
        string? explicitClashHtmlPath,
        string? explicitProfilerPath,
        bool worldCoords,
        bool checkClashes,
        bool listViewpoints,
        bool bvhStats,
        string? raycastArg,
        bool printReport,
        bool indent,
        NwcIfcExportOptions ifcOptions,
        Stopwatch sw,
        string? explicitClashXmlPath = null,
        string? explicitClashBcfPath = null,
        string? explicitNwfXmlPath = null,
        double clashTolerance = 0.001,
        string? explicitHtmlViewerPath = null,
        string? clashGroupMode = null,
        double clashRadius = 1.0,
        string? explicitTimeLinerXmlPath = null,
        bool linkTimeLiner = false,
        string? explicitClashMatrixJsonPath = null,
        string? explicitClashMatrixMdPath = null,
        string? explicitClashMatrixBcfPath = null,
        string? explicitTimeLinerMsProjectPath = null,
        string? explicitTimeLinerPrimaveraPath = null,
        bool includeTree = true)
    {
        outputPath ??= Path.Combine(dirPath, Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + "_assembly.glb");

        var assembly = NwcAssembly.LoadFromDirectory(dirPath, options: new NwcReaderOptions { BuildSceneTree = includeTree });

        if (!string.IsNullOrEmpty(explicitTimeLinerXmlPath))
        {
            if (File.Exists(explicitTimeLinerXmlPath))
            {
                assembly.ImportTimeLinerXml(explicitTimeLinerXmlPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule loaded from XML: '{explicitTimeLinerXmlPath}'.");
            }
            else
            {
                EnsureParentDirectory(explicitTimeLinerXmlPath);
                assembly.ExportTimeLinerXml(explicitTimeLinerXmlPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule exported to XML: '{explicitTimeLinerXmlPath}'.");
            }
        }

        if (!string.IsNullOrEmpty(explicitTimeLinerMsProjectPath))
        {
            if (File.Exists(explicitTimeLinerMsProjectPath))
            {
                assembly.ImportTimeLinerMsProject(explicitTimeLinerMsProjectPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule loaded from MS Project XML: '{explicitTimeLinerMsProjectPath}'.");
            }
            else
            {
                EnsureParentDirectory(explicitTimeLinerMsProjectPath);
                assembly.ExportTimeLinerMsProject(explicitTimeLinerMsProjectPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule exported to MS Project XML: '{explicitTimeLinerMsProjectPath}'.");
            }
        }

        if (!string.IsNullOrEmpty(explicitTimeLinerPrimaveraPath))
        {
            if (File.Exists(explicitTimeLinerPrimaveraPath))
            {
                assembly.ImportTimeLinerPrimavera(explicitTimeLinerPrimaveraPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule loaded from Primavera P6 XER: '{explicitTimeLinerPrimaveraPath}'.");
            }
            else
            {
                EnsureParentDirectory(explicitTimeLinerPrimaveraPath);
                assembly.ExportTimeLinerPrimavera(explicitTimeLinerPrimaveraPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule exported to Primavera P6 XER: '{explicitTimeLinerPrimaveraPath}'.");
            }
        }

        if (linkTimeLiner && assembly.TimeLiner is not null)
        {
            int linked = Model.TimeLiner.NwcTimeLinerLinker.AutoLink(assembly.TimeLiner, assembly.SelectionSets);
            Console.WriteLine($"Linked {linked} TimeLiner tasks to assembly elements.");
        }

        if (!string.IsNullOrEmpty(explicitProfilerPath))
        {
            if (!File.Exists(explicitProfilerPath))
            {
                Console.Error.WriteLine($"Error: Appearance profiler file not found: {explicitProfilerPath}");
                return 2;
            }
            var profiler = Model.Appearance.NwcXmlAppearanceProfilerSerializer.Open(explicitProfilerPath);
            int overridden = assembly.ApplyAppearanceProfiler(profiler);
            if (printReport)
            {
                Console.Error.WriteLine($"Appearance profiler applied to assembly: {overridden} elements overridden.");
            }
        }

        IReadOnlyList<NwcClash>? clashes = null;
        bool needClashes = checkClashes ||
            !string.IsNullOrEmpty(explicitBcfPath) ||
            !string.IsNullOrEmpty(explicitClashHtmlPath) ||
            outputPath.EndsWith(".bcf", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".bcfzip", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

        if (needClashes)
        {
            clashes = assembly.FindClashes();
        }

        if (outputPath.EndsWith(".bcf", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".bcfzip", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveBcf(outputPath, clashes);
        }
        else if (outputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveClashReportHtml(outputPath, clashes);
        }
        else if (outputPath.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveIfc(outputPath, ifcOptions);
        }
        else if (outputPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveGlb(outputPath, worldCoords);
        }
        else if (outputPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveObj(outputPath, worldCoords);
        }
        else if (outputPath.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            var merged = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = assembly.Name + ".rvt" },
                meshes: assembly.AllMeshes);
            merged.Save(outputPath);
        }
        else
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveJson(outputPath, new NwcJsonExportOptions { Indented = indent });
        }

        if (!string.IsNullOrEmpty(explicitIfcPath)) { EnsureParentDirectory(explicitIfcPath); assembly.SaveIfc(explicitIfcPath, ifcOptions); }
        if (!string.IsNullOrEmpty(explicitNwcPath))
        {
            EnsureParentDirectory(explicitNwcPath);
            var merged = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = assembly.Name + ".rvt" },
                meshes: assembly.AllMeshes);
            merged.Save(explicitNwcPath);
        }
        if (!string.IsNullOrEmpty(explicitGlbPath)) { EnsureParentDirectory(explicitGlbPath); assembly.SaveGlb(explicitGlbPath, worldCoords); }
        if (!string.IsNullOrEmpty(explicitObjPath)) { EnsureParentDirectory(explicitObjPath); assembly.SaveObj(explicitObjPath, worldCoords); }
        if (!string.IsNullOrEmpty(explicitBcfPath)) { EnsureParentDirectory(explicitBcfPath); assembly.SaveBcf(explicitBcfPath, clashes); }
        if (!string.IsNullOrEmpty(explicitViewpointsBcfPath)) { EnsureParentDirectory(explicitViewpointsBcfPath); assembly.SaveViewpointsBcf(explicitViewpointsBcfPath); }
        if (!string.IsNullOrEmpty(explicitViewpointsXmlPath)) { EnsureParentDirectory(explicitViewpointsXmlPath); assembly.SaveViewpointsXml(explicitViewpointsXmlPath); }
        if (!string.IsNullOrEmpty(explicitClashHtmlPath)) { EnsureParentDirectory(explicitClashHtmlPath); assembly.SaveClashReportHtml(explicitClashHtmlPath, clashes); }
        if (!string.IsNullOrEmpty(explicitClashXmlPath))
        {
            EnsureParentDirectory(explicitClashXmlPath);
            var test = assembly.RunClashDetection("Federated Clash Test", clashTolerance);
            ApplyClashGrouping(test, clashGroupMode, clashRadius, printReport);
            assembly.ExportClashReportXml(explicitClashXmlPath);
        }
        if (!string.IsNullOrEmpty(explicitClashBcfPath))
        {
            EnsureParentDirectory(explicitClashBcfPath);
            if (assembly.ClashTests.Count == 0)
            {
                var test = assembly.RunClashDetection("Federated Clash Test", clashTolerance);
                ApplyClashGrouping(test, clashGroupMode, clashRadius, printReport);
            }
            assembly.ExportClashBcf(explicitClashBcfPath);
        }
        if (!string.IsNullOrEmpty(explicitHtmlViewerPath))
        {
            EnsureParentDirectory(explicitHtmlViewerPath);
            assembly.SaveHtmlViewer(explicitHtmlViewerPath);
        }
        if (!string.IsNullOrEmpty(explicitNwfXmlPath))
        {
            EnsureParentDirectory(explicitNwfXmlPath);
            assembly.SaveNwf(explicitNwfXmlPath);
        }

        bool needMatrix = !string.IsNullOrEmpty(explicitClashMatrixJsonPath) ||
                          !string.IsNullOrEmpty(explicitClashMatrixMdPath) ||
                          !string.IsNullOrEmpty(explicitClashMatrixBcfPath);

        if (needMatrix)
        {
            assembly.ClashMatrix ??= Model.Clash.NwcClashMatrix.CreateFromAssembly(assembly);
            assembly.ClashMatrix.Execute(assembly);

            if (!string.IsNullOrEmpty(explicitClashMatrixJsonPath))
            {
                EnsureParentDirectory(explicitClashMatrixJsonPath);
                assembly.ExportClashMatrixJson(explicitClashMatrixJsonPath, indent);
                if (printReport) Console.Error.WriteLine($"Clash matrix exported to JSON: '{explicitClashMatrixJsonPath}'.");
            }
            if (!string.IsNullOrEmpty(explicitClashMatrixMdPath))
            {
                EnsureParentDirectory(explicitClashMatrixMdPath);
                assembly.ExportClashMatrixMarkdown(explicitClashMatrixMdPath);
                if (printReport) Console.Error.WriteLine($"Clash matrix summary exported to Markdown: '{explicitClashMatrixMdPath}'.");
            }
            if (!string.IsNullOrEmpty(explicitClashMatrixBcfPath))
            {
                EnsureParentDirectory(explicitClashMatrixBcfPath);
                assembly.ExportClashMatrixBcf(explicitClashMatrixBcfPath);
                if (printReport) Console.Error.WriteLine($"Clash matrix conflicts exported to BCF 2.1: '{explicitClashMatrixBcfPath}'.");
            }

            Console.WriteLine(assembly.ClashMatrix.ToMarkdownTable());
        }

        if (!string.IsNullOrEmpty(raycastArg))
        {
            ExecuteRaycastTest(assembly.SpatialIndex, raycastArg);
        }

        if (listViewpoints)
        {
            PrintViewpoints(assembly.SavedViewpoints);
        }

        sw.Stop();

        if (printReport)
        {
            Console.Error.WriteLine("=== NwcIO Assembly Report ===");
            Console.Error.WriteLine($"Assembly:         {assembly.Name}");
            Console.Error.WriteLine($"Models:           {assembly.Models.Count}");
            Console.Error.WriteLine($"Total Meshes:     {assembly.AllMeshes.Count}");
            Console.Error.WriteLine($"Total Materials:  {assembly.AllMaterials.Count}");
            Console.Error.WriteLine($"Saved viewpoints: {assembly.SavedViewpoints.Count}");
            Console.Error.WriteLine($"Bounding Box:     {assembly.BoundingBox}");
            if (bvhStats)
            {
                var idx = assembly.SpatialIndex;
                Console.Error.WriteLine($"BVH Nodes:        {idx.NodeCount} ({idx.LeafCount} leaves, max depth {idx.MaxDepth})");
            }
            if (clashes is not null)
            {
                Console.Error.WriteLine($"Clashes Detected: {clashes.Count}");
            }
            Console.Error.WriteLine($"Elapsed:          {sw.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"Output:           {outputPath}");
        }
        else
        {
            Console.WriteLine(FormattableString.Invariant(
                $"Assembly '{assembly.Name}': {assembly.Models.Count} models, {assembly.AllMeshes.Count} meshes, {assembly.AllMaterials.Count} materials exported to '{outputPath}' in {sw.ElapsedMilliseconds} ms."));
        }

        return 0;
    }

    private static int ProcessNwfAssembly(
        string nwfPath,
        string? outputPath,
        string? explicitGlbPath,
        string? explicitObjPath,
        string? explicitIfcPath,
        string? explicitNwcPath,
        string? explicitBcfPath,
        string? explicitViewpointsBcfPath,
        string? explicitViewpointsXmlPath,
        string? explicitClashHtmlPath,
        string? explicitProfilerPath,
        bool worldCoords,
        bool checkClashes,
        bool listViewpoints,
        bool bvhStats,
        string? raycastArg,
        bool printReport,
        bool indent,
        NwcIfcExportOptions ifcOptions,
        Stopwatch sw,
        string? explicitClashXmlPath = null,
        string? explicitClashBcfPath = null,
        string? explicitNwfXmlPath = null,
        double clashTolerance = 0.001,
        string? explicitHtmlViewerPath = null,
        string? clashGroupMode = null,
        double clashRadius = 1.0,
        string? explicitTimeLinerXmlPath = null,
        bool linkTimeLiner = false,
        string? explicitClashMatrixJsonPath = null,
        string? explicitClashMatrixMdPath = null,
        string? explicitClashMatrixBcfPath = null,
        string? explicitTimeLinerMsProjectPath = null,
        string? explicitTimeLinerPrimaveraPath = null,
        bool includeTree = true)
    {
        outputPath ??= Path.ChangeExtension(nwfPath, ".glb");
        var assembly = NwcAssembly.OpenNwf(nwfPath, new NwcReaderOptions { BuildSceneTree = includeTree });

        if (!string.IsNullOrEmpty(explicitTimeLinerXmlPath))
        {
            if (File.Exists(explicitTimeLinerXmlPath))
            {
                assembly.ImportTimeLinerXml(explicitTimeLinerXmlPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule loaded from XML: '{explicitTimeLinerXmlPath}'.");
            }
            else
            {
                EnsureParentDirectory(explicitTimeLinerXmlPath);
                assembly.ExportTimeLinerXml(explicitTimeLinerXmlPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule exported to XML: '{explicitTimeLinerXmlPath}'.");
            }
        }

        if (!string.IsNullOrEmpty(explicitTimeLinerMsProjectPath))
        {
            if (File.Exists(explicitTimeLinerMsProjectPath))
            {
                assembly.ImportTimeLinerMsProject(explicitTimeLinerMsProjectPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule loaded from MS Project XML: '{explicitTimeLinerMsProjectPath}'.");
            }
            else
            {
                EnsureParentDirectory(explicitTimeLinerMsProjectPath);
                assembly.ExportTimeLinerMsProject(explicitTimeLinerMsProjectPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule exported to MS Project XML: '{explicitTimeLinerMsProjectPath}'.");
            }
        }

        if (!string.IsNullOrEmpty(explicitTimeLinerPrimaveraPath))
        {
            if (File.Exists(explicitTimeLinerPrimaveraPath))
            {
                assembly.ImportTimeLinerPrimavera(explicitTimeLinerPrimaveraPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule loaded from Primavera P6 XER: '{explicitTimeLinerPrimaveraPath}'.");
            }
            else
            {
                EnsureParentDirectory(explicitTimeLinerPrimaveraPath);
                assembly.ExportTimeLinerPrimavera(explicitTimeLinerPrimaveraPath);
                if (printReport) Console.Error.WriteLine($"TimeLiner schedule exported to Primavera P6 XER: '{explicitTimeLinerPrimaveraPath}'.");
            }
        }

        if (linkTimeLiner && assembly.TimeLiner is not null)
        {
            int linked = Model.TimeLiner.NwcTimeLinerLinker.AutoLink(assembly.TimeLiner, assembly.SelectionSets);
            Console.WriteLine($"Linked {linked} TimeLiner tasks to assembly elements.");
        }

        if (!string.IsNullOrEmpty(explicitProfilerPath))
        {
            if (!File.Exists(explicitProfilerPath))
            {
                Console.Error.WriteLine($"Error: Appearance profiler file not found: {explicitProfilerPath}");
                return 2;
            }
            var profiler = Model.Appearance.NwcXmlAppearanceProfilerSerializer.Open(explicitProfilerPath);
            int overridden = assembly.ApplyAppearanceProfiler(profiler);
            if (printReport)
            {
                Console.Error.WriteLine($"Appearance profiler applied to assembly: {overridden} elements overridden.");
            }
        }

        IReadOnlyList<NwcClash>? clashes = null;
        bool needClashes = checkClashes ||
            !string.IsNullOrEmpty(explicitBcfPath) ||
            !string.IsNullOrEmpty(explicitClashHtmlPath) ||
            outputPath.EndsWith(".bcf", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".bcfzip", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

        if (needClashes)
        {
            clashes = assembly.FindClashes();
        }

        if (outputPath.EndsWith(".bcf", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".bcfzip", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveBcf(outputPath, clashes);
        }
        else if (outputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveClashReportHtml(outputPath, clashes);
        }
        else if (outputPath.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveIfc(outputPath, ifcOptions);
        }
        else if (outputPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveGlb(outputPath, worldCoords);
        }
        else if (outputPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveObj(outputPath, worldCoords);
        }
        else if (outputPath.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParentDirectory(outputPath);
            var merged = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = assembly.Name + ".rvt" },
                meshes: assembly.AllMeshes);
            merged.Save(outputPath);
        }
        else
        {
            EnsureParentDirectory(outputPath);
            assembly.SaveJson(outputPath, new NwcJsonExportOptions { Indented = indent });
        }

        if (!string.IsNullOrEmpty(explicitIfcPath)) { EnsureParentDirectory(explicitIfcPath); assembly.SaveIfc(explicitIfcPath, ifcOptions); }
        if (!string.IsNullOrEmpty(explicitNwcPath))
        {
            EnsureParentDirectory(explicitNwcPath);
            var merged = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = assembly.Name + ".rvt" },
                meshes: assembly.AllMeshes);
            merged.Save(explicitNwcPath);
        }
        if (!string.IsNullOrEmpty(explicitGlbPath)) { EnsureParentDirectory(explicitGlbPath); assembly.SaveGlb(explicitGlbPath, worldCoords); }
        if (!string.IsNullOrEmpty(explicitObjPath)) { EnsureParentDirectory(explicitObjPath); assembly.SaveObj(explicitObjPath, worldCoords); }
        if (!string.IsNullOrEmpty(explicitBcfPath)) { EnsureParentDirectory(explicitBcfPath); assembly.SaveBcf(explicitBcfPath, clashes); }
        if (!string.IsNullOrEmpty(explicitViewpointsBcfPath)) { EnsureParentDirectory(explicitViewpointsBcfPath); assembly.SaveViewpointsBcf(explicitViewpointsBcfPath); }
        if (!string.IsNullOrEmpty(explicitViewpointsXmlPath)) { EnsureParentDirectory(explicitViewpointsXmlPath); assembly.SaveViewpointsXml(explicitViewpointsXmlPath); }
        if (!string.IsNullOrEmpty(explicitClashHtmlPath)) { EnsureParentDirectory(explicitClashHtmlPath); assembly.SaveClashReportHtml(explicitClashHtmlPath, clashes); }
        if (!string.IsNullOrEmpty(explicitClashXmlPath))
        {
            EnsureParentDirectory(explicitClashXmlPath);
            var test = assembly.RunClashDetection("Federated Clash Test", clashTolerance);
            ApplyClashGrouping(test, clashGroupMode, clashRadius, printReport);
            assembly.ExportClashReportXml(explicitClashXmlPath);
        }
        if (!string.IsNullOrEmpty(explicitClashBcfPath))
        {
            EnsureParentDirectory(explicitClashBcfPath);
            if (assembly.ClashTests.Count == 0)
            {
                var test = assembly.RunClashDetection("Federated Clash Test", clashTolerance);
                ApplyClashGrouping(test, clashGroupMode, clashRadius, printReport);
            }
            assembly.ExportClashBcf(explicitClashBcfPath);
        }
        if (!string.IsNullOrEmpty(explicitHtmlViewerPath))
        {
            EnsureParentDirectory(explicitHtmlViewerPath);
            assembly.SaveHtmlViewer(explicitHtmlViewerPath);
        }
        if (!string.IsNullOrEmpty(explicitNwfXmlPath))
        {
            EnsureParentDirectory(explicitNwfXmlPath);
            assembly.SaveNwf(explicitNwfXmlPath);
        }

        bool needMatrix = !string.IsNullOrEmpty(explicitClashMatrixJsonPath) ||
                          !string.IsNullOrEmpty(explicitClashMatrixMdPath) ||
                          !string.IsNullOrEmpty(explicitClashMatrixBcfPath);

        if (needMatrix)
        {
            assembly.ClashMatrix ??= Model.Clash.NwcClashMatrix.CreateFromAssembly(assembly);
            assembly.ClashMatrix.Execute(assembly);

            if (!string.IsNullOrEmpty(explicitClashMatrixJsonPath))
            {
                EnsureParentDirectory(explicitClashMatrixJsonPath);
                assembly.ExportClashMatrixJson(explicitClashMatrixJsonPath, indent);
                if (printReport) Console.Error.WriteLine($"Clash matrix exported to JSON: '{explicitClashMatrixJsonPath}'.");
            }
            if (!string.IsNullOrEmpty(explicitClashMatrixMdPath))
            {
                EnsureParentDirectory(explicitClashMatrixMdPath);
                assembly.ExportClashMatrixMarkdown(explicitClashMatrixMdPath);
                if (printReport) Console.Error.WriteLine($"Clash matrix summary exported to Markdown: '{explicitClashMatrixMdPath}'.");
            }
            if (!string.IsNullOrEmpty(explicitClashMatrixBcfPath))
            {
                EnsureParentDirectory(explicitClashMatrixBcfPath);
                assembly.ExportClashMatrixBcf(explicitClashMatrixBcfPath);
                if (printReport) Console.Error.WriteLine($"Clash matrix conflicts exported to BCF 2.1: '{explicitClashMatrixBcfPath}'.");
            }

            Console.WriteLine(assembly.ClashMatrix.ToMarkdownTable());
        }

        if (!string.IsNullOrEmpty(raycastArg))
        {
            ExecuteRaycastTest(assembly.SpatialIndex, raycastArg);
        }

        if (listViewpoints)
        {
            PrintViewpoints(assembly.SavedViewpoints);
        }

        sw.Stop();

        if (printReport)
        {
            Console.Error.WriteLine("=== NwcIO NWF Assembly Report ===");
            Console.Error.WriteLine($"NWF File:         {nwfPath}");
            // Models[0] is the .nwf document itself; report the component files separately.
            Console.Error.WriteLine($"Loaded Models:    {assembly.Models.Count} (component files: {assembly.Models.Count - 1})");
            Console.Error.WriteLine($"Total Meshes:     {assembly.AllMeshes.Count}");
            Console.Error.WriteLine($"Saved viewpoints: {assembly.SavedViewpoints.Count}");
            Console.Error.WriteLine($"Bounding Box:     {assembly.BoundingBox}");
            foreach (var xref in assembly.UnresolvedXRefs)
            {
                Console.Error.WriteLine($"Warning: unresolved reference, component model missing: '{xref.EffectivePath}'");
            }
            foreach (var xref in assembly.SkippedXRefs)
            {
                Console.Error.WriteLine($"Warning: skipped unsupported reference (not .nwc/.nwd): '{xref.EffectivePath}'");
            }
            if (clashes is not null)
            {
                Console.Error.WriteLine($"Clashes Detected: {clashes.Count}");
            }
            Console.Error.WriteLine($"Elapsed:          {sw.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"Output:           {outputPath}");
        }
        else
        {
            Console.WriteLine(FormattableString.Invariant(
                $"NWF Assembly '{assembly.Name}': {assembly.Models.Count} models, {assembly.AllMeshes.Count} meshes exported to '{outputPath}' in {sw.ElapsedMilliseconds} ms."));
        }

        return 0;
    }

    private static void PrintViewpoints(IReadOnlyList<NwcSavedViewpoint> viewpoints)
    {
        Console.WriteLine($"=== Saved Viewpoints ({viewpoints.Count}) ===");
        if (viewpoints.Count == 0)
        {
            Console.WriteLine("No saved viewpoints found.");
            return;
        }

        for (int i = 0; i < viewpoints.Count; i++)
        {
            var vp = viewpoints[i];
            var cam = vp.Camera;
            Console.WriteLine(FormattableString.Invariant(
                $"[{i + 1}] \"{vp.Name}\" ({cam.Projection}) Pos: {cam.Position} Dir: {cam.Direction} Up: {cam.Up} Target: {cam.Target} FOV: {cam.FieldOfView:F1}° FocalDist: {cam.FocalDistance:F2}"));
            if (vp.Comments.Count > 0)
            {
                Console.WriteLine($"     Comments: {string.Join("; ", vp.Comments)}");
            }
        }
    }

    private static void ExecuteRaycastTest(NwcSpatialIndex index, string coords)
    {
        var parts = coords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 ||
            !double.TryParse(parts[0], CultureInfo.InvariantCulture, out double ox) ||
            !double.TryParse(parts[1], CultureInfo.InvariantCulture, out double oy) ||
            !double.TryParse(parts[2], CultureInfo.InvariantCulture, out double oz) ||
            !double.TryParse(parts[3], CultureInfo.InvariantCulture, out double dx) ||
            !double.TryParse(parts[4], CultureInfo.InvariantCulture, out double dy) ||
            !double.TryParse(parts[5], CultureInfo.InvariantCulture, out double dz))
        {
            Console.Error.WriteLine($"Warning: Invalid raycast format '{coords}'. Expected: ox,oy,oz,dx,dy,dz");
            return;
        }

        var origin = new Vector3d(ox, oy, oz);
        var dir = new Vector3d(dx, dy, dz);

        if (index.Raycast(origin, dir, out var hit))
        {
            Console.WriteLine(FormattableString.Invariant(
                $"Raycast HIT at distance {hit.Distance:F3}: Point={hit.Point}, Normal={hit.Normal}, MeshId={hit.Mesh.Id}, Triangle={hit.TriangleIndex}"));
        }
        else
        {
            Console.WriteLine("Raycast MISS: no intersecting surface found along the ray.");
        }
    }

    private static void ApplyClashGrouping(Model.Clash.NwcClashTest test, string? mode, double radius, bool printReport)
    {
        if (string.IsNullOrEmpty(mode)) return;
        int grps = 0;
        if (mode.Equals("proximity", StringComparison.OrdinalIgnoreCase))
        {
            grps = Model.Clash.NwcClashGrouper.GroupByProximity(test, radius);
        }
        else if (mode.Equals("element", StringComparison.OrdinalIgnoreCase))
        {
            grps = Model.Clash.NwcClashGrouper.GroupByPrimaryElement(test);
        }
        else if (mode.Equals("grid", StringComparison.OrdinalIgnoreCase))
        {
            grps = Model.Clash.NwcClashGrouper.GroupByGrid(test);
        }
        else if (mode.Equals("layer", StringComparison.OrdinalIgnoreCase))
        {
            grps = Model.Clash.NwcClashGrouper.GroupByLayer(test);
        }
        if (printReport && grps > 0)
        {
            Console.Error.WriteLine($"Grouped clashes into {grps} groups using mode '{mode}'.");
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static int CountNodes(NwcNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        int count = 1;
        foreach (var child in node.Children)
        {
            count += CountNodes(child);
        }

        return count;
    }

    private static bool TryConsumeValue(string[] args, ref int i, string optionName, out string value)
    {
        if (i + 1 >= args.Length || (args[i + 1].StartsWith('-') && args[i + 1] != "-"))
        {
            Console.Error.WriteLine($"Error: Missing argument for {optionName}.");
            value = string.Empty;
            return false;
        }
        value = args[++i];
        return true;
    }
}

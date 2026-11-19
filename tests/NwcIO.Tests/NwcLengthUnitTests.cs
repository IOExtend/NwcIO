using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using NwcIO.Model;
using NwcIO.Model.Clash;
using NwcIO.Model.Diff;
using NwcIO.Model.Grids;
using NwcIO.Model.Measure;
using Xunit;

namespace NwcIO.Tests;

public class NwcLengthUnitTests
{
    [Theory]
    [InlineData(NwcLengthUnit.Feet, 0.3048, "ft")]
    [InlineData(NwcLengthUnit.Meters, 1.0, "m")]
    [InlineData(NwcLengthUnit.Centimeters, 0.01, "cm")]
    [InlineData(NwcLengthUnit.Millimeters, 0.001, "mm")]
    [InlineData(NwcLengthUnit.Inches, 0.0254, "in")]
    [InlineData(NwcLengthUnit.Yards, 0.9144, "yd")]
    [InlineData(NwcLengthUnit.Kilometers, 1000.0, "km")]
    [InlineData(NwcLengthUnit.Miles, 1609.344, "mi")]
    public void LengthUnits_ReturnCanonicalMetresFactorAndSymbol(NwcLengthUnit unit, double expectedMetres, string expectedSymbol)
    {
        Assert.Equal(expectedMetres, unit.GetMetresPerUnit());
        Assert.Equal(expectedSymbol, unit.GetSymbol());
    }

    [Fact]
    public void Document_LengthUnit_InfersFeetForRevitAndUnknownOtherwise()
    {
        var revit = NwcDocument.Open(new MemoryStream(NwcDocument.Create(fileInfo: new NwcFileInfo
        {
            SourcePath = "model.rvt",
            LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit",
        }).ToByteArray()));
        Assert.Equal(NwcLengthUnit.Feet, revit.LengthUnit);
        Assert.Equal(0.3048, revit.MetresPerUnit);

        var other = NwcDocument.Open(new MemoryStream(NwcDocument.Create(fileInfo: new NwcFileInfo
        {
            SourcePath = "model.dwg",
            LoaderPlugin = "LcNwcLoaderPlugin:lcldwg",
        }).ToByteArray()));
        Assert.Equal(NwcLengthUnit.Unknown, other.LengthUnit);
        Assert.Null(other.MetresPerUnit);
    }

    [Fact]
    public void ReaderOptions_LengthUnit_OverridesInference()
    {
        byte[] file = NwcDocument.Create(fileInfo: new NwcFileInfo
        {
            SourcePath = "model.rvt",
            LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit",
        }).ToByteArray();

        var doc = NwcDocument.Open(new MemoryStream(file), new NwcReaderOptions { LengthUnit = NwcLengthUnit.Meters });

        Assert.Equal(NwcLengthUnit.Meters, doc.LengthUnit);
        Assert.Equal(1.0, doc.MetresPerUnit);
    }

    [Fact]
    public void Assembly_ReportsMixedLengthUnits()
    {
        var feet = NwcDocument.Create(fileInfo: new NwcFileInfo { LengthUnit = NwcLengthUnit.Feet });
        var meters = NwcDocument.Create(fileInfo: new NwcFileInfo { LengthUnit = NwcLengthUnit.Meters });
        var same = NwcDocument.Create(fileInfo: new NwcFileInfo { LengthUnit = NwcLengthUnit.Meters });

        var unknown = NwcDocument.Create(fileInfo: new NwcFileInfo { LengthUnit = NwcLengthUnit.Unknown });

        var mixed = NwcAssembly.Create("mixed", feet, meters);
        Assert.True(mixed.HasMixedLengthUnits);
        Assert.Equal(NwcDiagnosticSeverity.Warning, Assert.Single(mixed.Diagnostics, d => d.Code == "MixedLengthUnits").Severity);
        var homogeneous = NwcAssembly.Create("same", meters, same);
        Assert.False(homogeneous.HasMixedLengthUnits);
        Assert.DoesNotContain(homogeneous.Diagnostics, d => d.Code == "MixedLengthUnits");
        Assert.False(NwcAssembly.Create("nwf", unknown, feet, feet).HasMixedLengthUnits);
        Assert.False(NwcAssembly.Create("unknown", unknown, unknown).HasMixedLengthUnits);
    }

    [Fact]
    public void IfcExport_ConvertsKnownFeetCoordinatesToTheOutputUnit()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };
        var doc = NwcDocument.Create(
            fileInfo: new NwcFileInfo { LengthUnit = NwcLengthUnit.Feet },
            meshes: [mesh]);

        string ifc = doc.ToIfcString(new NwcIfcExportOptions { LengthUnit = NwcIfcLengthUnit.Millimetre });

        Assert.Contains("304.8", ifc, StringComparison.Ordinal);
    }

    [Fact]
    public void IfcExport_KeepsMetreCoordinatesOfAMetresDocumentWithMetreOutput()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1234, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };
        var doc = NwcDocument.Create(
            fileInfo: new NwcFileInfo { LengthUnit = NwcLengthUnit.Meters },
            meshes: [mesh]);

        string ifc = doc.ToIfcString(new NwcIfcExportOptions { LengthUnit = NwcIfcLengthUnit.Metre });

        Assert.Contains("1234", ifc, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NwcLengthUnit.Meters, 1)]
    [InlineData(NwcLengthUnit.Millimeters, 1000)]
    public void IfcExport_ConvertsKnownCoordinatesToMetres(NwcLengthUnit unit, double unitsPerMetre)
    {
        var doc = Document(unit, Triangle(1, unitsPerMetre, 0));
        var options = new NwcIfcExportOptions { LengthUnit = NwcIfcLengthUnit.Metre };

        Assert.Contains("IFCCARTESIANPOINT((2.,0.,0.))", doc.ToIfcString(options), StringComparison.Ordinal);
        using var assemblyIfc = new StringWriter();
        NwcAssembly.Create("Model", doc).SaveIfc(assemblyIfc, options);
        Assert.Contains("IFCCARTESIANPOINT((2.,0.,0.))", assemblyIfc.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NwcLengthUnit.Meters, 1)]
    [InlineData(NwcLengthUnit.Millimeters, 1000)]
    public void ClashAndDiff_UseTolerancesInDocumentUnits(NwcLengthUnit unit, double unitsPerMetre)
    {
        double shift = 0.002 * unitsPerMetre;
        var a = Triangle(1, unitsPerMetre, 0);
        var b = Triangle(2, unitsPerMetre, shift);
        var document = Document(unit, a, b);

        var belowDistance = new NwcClashTest("Below", NwcClashTestType.Clearance, shift / 2);
        var aboveDistance = new NwcClashTest("Above", NwcClashTestType.Clearance, shift * 2);
        Assert.Empty(NwcClashDetector.Run(document, belowDistance).Results);
        Assert.Equal(shift, Assert.Single(NwcClashDetector.Run(document, aboveDistance).Results).Distance, 8);

        var baseline = Document(unit, a);
        var revision = Document(unit, Triangle(1, unitsPerMetre, shift));
        Assert.Single(NwcDiffEngine.Compare(baseline, revision, new NwcDiffOptions { GeometricTolerance = shift / 2 }).Modified);
        Assert.Single(NwcDiffEngine.Compare(baseline, revision, new NwcDiffOptions { GeometricTolerance = shift * 2 }).Unchanged);
    }

    [Theory]
    [InlineData(NwcLengthUnit.Meters, 1, "m", "+1.00m", "1.000 m")]
    [InlineData(NwcLengthUnit.Millimeters, 1000, "mm", "+1000.00mm", "1000.000 mm")]
    public async Task GridMeasurementsAndViewpointXml_UseSourceUnits(
        NwcLengthUnit unit, double unitsPerMetre, string symbol, string offset, string distance)
    {
        var doc = Document(unit, Triangle(1, unitsPerMetre, 0));
        var grid = doc.ExtractGridSystem();
        grid.AddLine("A", new(0, -2 * unitsPerMetre, 0), new(0, 2 * unitsPerMetre, 0));
        Assert.Equal(unit, grid.LengthUnit);
        Assert.Equal($"Grid A ({offset})", grid.FormatLocation(new Vector3d(unitsPerMetre, 0, 0)));
        using var gridXml = new MemoryStream();
        grid.SaveXml(gridXml);
        gridXml.Position = 0;
        Assert.Equal(unit, NwcGridSystem.ReadXml(gridXml).LengthUnit);

        Assert.Contains(distance, NwcMeasureEngine.MeasureDistance(Vector3d.Zero, new(unitsPerMetre, 0, 0), unit).ToString(),
            StringComparison.Ordinal);
        var b = Triangle(2, unitsPerMetre, unitsPerMetre);
        Assert.Contains(distance, NwcMeasureEngine.MeasurePointToMesh(Vector3d.Zero, b, true, unit).ToString(), StringComparison.Ordinal);
        Assert.Contains(distance, NwcMeasureEngine.MeasureMeshToMesh(doc.Meshes[0], b, true, unit).ToString(), StringComparison.Ordinal);

        Assert.Equal(symbol, XDocument.Parse(doc.ToViewpointsXml()).Root!.Attribute("units")!.Value);
        Assert.Equal("ft", XDocument.Parse(doc.ToViewpointsXml("ft")).Root!.Attribute("units")!.Value);
        using var viewpointXml = new MemoryStream();
        await doc.SaveViewpointsXmlAsync(viewpointXml);
        viewpointXml.Position = 0;
        Assert.Equal(symbol, XDocument.Load(viewpointXml).Root!.Attribute("units")!.Value);

        using var html = new MemoryStream();
        await doc.SaveHtmlViewerAsync(html);
        Assert.Contains($"\"lengthUnit\":\"{symbol}\"", Encoding.UTF8.GetString(html.ToArray()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NwcLengthUnit.Meters, 1)]
    [InlineData(NwcLengthUnit.Millimeters, 1000)]
    public async Task BcfCameraPositionsAndViewScale_AreInMetres(NwcLengthUnit unit, double unitsPerMetre)
    {
        var a = Triangle(1, unitsPerMetre, 0);
        var b = Triangle(2, unitsPerMetre, 0.002 * unitsPerMetre);
        var baseline = Document(unit, a);
        var revision = Document(unit, a, b);

        using var diffBcf = new MemoryStream();
        NwcDiffEngine.Compare(baseline, revision).SaveBcf(diffBcf);
        Assert.Equal(new Vector3d(6, -4, 4.002), ReadCameraPoint(ReadBcfCamera(diffBcf)));

        var test = new NwcClashTest("Clearance", NwcClashTestType.Clearance, 0.004 * unitsPerMetre);
        var clash = Assert.Single(NwcClashDetector.Run(revision, test).Results);
        using var clashBcf = new MemoryStream();
        NwcClashBcfExporter.Write(clashBcf, test);
        Assert.Equal(clash.ClashPoint / unitsPerMetre + new Vector3d(5, -5, 3.5), ReadCameraPoint(ReadBcfCamera(clashBcf)));

        var group = new NwcClashGroup("Clearance group");
        group.AddClash(clash);
        test.Groups.Add(group);
        test.Results.Clear();
        using var groupBcf = new MemoryStream();
        NwcClashBcfExporter.Write(groupBcf, test);
        Assert.Equal(clash.ClashPoint / unitsPerMetre + new Vector3d(8, -8, 5), ReadCameraPoint(ReadBcfCamera(groupBcf)));

        var overlap = new BoundingBox3d(new Vector3d(1, 2, 3) * unitsPerMetre, new Vector3d(2, 3, 4) * unitsPerMetre);
        using var spatialBcf = new MemoryStream();
        NwcAssembly.Create("Assembly", baseline, revision).SaveBcf(spatialBcf, [new NwcClash(baseline, a, revision, b, overlap)]);
        var actual = ReadCameraPoint(ReadBcfCamera(spatialBcf));
        var expected = new Vector3d(1.5, 2.5, 3.5) + new Vector3d(1, -1, 0.75).Normalize() * 4;
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);

        var viewpoint = new NwcSavedViewpoint
        {
            Name = "Orthogonal",
            Camera = new NwcCamera
            {
                Position = new Vector3d(2, 3, 4) * unitsPerMetre,
                Projection = NwcCameraProjection.Orthographic,
                FocalDistance = 6 * unitsPerMetre,
            },
        };
        var withViewpoint = NwcDocument.Create(fileInfo: new NwcFileInfo { LengthUnit = unit }, savedViewpoints: [viewpoint]);
        using var viewpointsBcf = new MemoryStream();
        await withViewpoint.SaveViewpointsBcfAsync(viewpointsBcf);
        XElement camera = ReadBcfCamera(viewpointsBcf);
        Assert.Equal(new Vector3d(2, 3, 4), ReadCameraPoint(camera));
        Assert.Equal(6.0, (double)camera.Element("ViewToWorldScale")!);
    }

    [Fact]
    public void UnknownGridAndMeasurement_DoNotClaimMetres()
    {
        var grid = new NwcGridSystem();
        grid.AddLine("A", new(0, -2, 0), new(0, 2, 0));

        Assert.Equal("Grid A (+1.00 units)", grid.FormatLocation(new Vector3d(1, 0, 0)));
        Assert.Contains("1.000 units", NwcMeasureEngine.MeasureDistance(Vector3d.Zero, new(1, 0, 0)).ToString(), StringComparison.Ordinal);
    }

    private static NwcDocument Document(NwcLengthUnit unit, params NwcMesh[] meshes) =>
        NwcDocument.Create(fileInfo: new NwcFileInfo { LengthUnit = unit }, meshes: meshes);

    private static NwcMesh Triangle(int id, double scale, double z) => new()
    {
        Id = id,
        Name = $"Triangle {id}",
        Vertices = [new(0, 0, z), new(2 * scale, 0, z), new(0, 2 * scale, z)],
        Indices = [0, 1, 2],
    };

    private static XElement ReadBcfCamera(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = Assert.Single(archive.Entries, e => e.Name == "viewpoint.bcfv");
        using var data = entry.Open();
        var root = XDocument.Load(data).Root!;
        return root.Element("PerspectiveCamera") ?? root.Element("OrthogonalCamera")!;
    }

    private static Vector3d ReadCameraPoint(XElement camera)
    {
        var p = camera.Element("CameraViewPoint")!;
        return new Vector3d((double)p.Element("X")!, (double)p.Element("Y")!, (double)p.Element("Z")!);
    }

    [Fact]
    public void Unknown_ReturnsNullFactorAndSymbol()
    {
        Assert.Null(NwcLengthUnit.Unknown.GetMetresPerUnit());
        Assert.Null(NwcLengthUnit.Unknown.GetSymbol());
    }
}

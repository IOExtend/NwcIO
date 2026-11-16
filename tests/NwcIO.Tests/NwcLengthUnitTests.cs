using NwcIO.Model;
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
    public void Document_LengthUnit_InfersFeetForRevitAndMillimetresOtherwise()
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

        Assert.True(NwcAssembly.Create("mixed", feet, meters).HasMixedLengthUnits);
        Assert.False(NwcAssembly.Create("same", meters, same).HasMixedLengthUnits);
    }

    [Fact]
    public void Unknown_ReturnsNullFactorAndSymbol()
    {
        Assert.Null(NwcLengthUnit.Unknown.GetMetresPerUnit());
        Assert.Null(NwcLengthUnit.Unknown.GetSymbol());
    }
}

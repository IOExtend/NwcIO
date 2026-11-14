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
    public void Unknown_ReturnsNullFactorAndSymbol()
    {
        Assert.Null(NwcLengthUnit.Unknown.GetMetresPerUnit());
        Assert.Null(NwcLengthUnit.Unknown.GetSymbol());
    }
}

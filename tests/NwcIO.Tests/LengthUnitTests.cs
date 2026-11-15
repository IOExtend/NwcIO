using Xunit;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Tests;

public class LengthUnitTests
{
    [Theory]
    [InlineData("LcNwcLoaderPlugin:lcldrevit", NwcLengthUnit.Feet, 0.3048, true)]
    [InlineData("LcNwcLoaderPlugin:mock", NwcLengthUnit.Unknown, null, false)]
    [InlineData("nwexportrevit", NwcLengthUnit.Feet, 0.3048, true)]
    [InlineData("NWEXPORTREVIT", NwcLengthUnit.Unknown, null, false)]
    [InlineData("nwexportrevit2", NwcLengthUnit.Unknown, null, false)]
    public void Open_LoaderPlugin_DecidesTheUnit(string plugin, NwcLengthUnit unit, double? metres, bool revit)
    {
        var doc = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write([SyntheticNwcFile.PartitionHeader("a.rvt", 1, plugin)])));
        Assert.Equal(unit, doc.LengthUnit);
        Assert.Equal(metres, doc.MetresPerUnit);
        Assert.Equal(revit, doc.IsRevitSource);
    }

    [Fact]
    public void Create_FollowsTheFileInfo()
    {
        Assert.Equal(NwcLengthUnit.Unknown, NwcDocument.Create().LengthUnit);   // default loader LcOpRevitExport
        Assert.Equal(NwcLengthUnit.Feet, NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }).LengthUnit);
    }
}

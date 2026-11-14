using Xunit;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Tests;

public class LengthUnitTests
{
    [Theory]
    [InlineData("LcNwcLoaderPlugin:lcldrevit", NwcLengthUnit.Feet, 0.3048)]
    [InlineData("LcNwcLoaderPlugin:mock", NwcLengthUnit.Unknown, null)]
    public void Open_LoaderPlugin_DecidesTheUnit(string plugin, NwcLengthUnit unit, double? metres)
    {
        var doc = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write([SyntheticNwcFile.PartitionHeader("a.rvt", 1, plugin)])));
        Assert.Equal(unit, doc.LengthUnit);
        Assert.Equal(metres, doc.MetresPerUnit);
    }

    [Fact]
    public void Create_FollowsTheFileInfo()
    {
        Assert.Equal(NwcLengthUnit.Unknown, NwcDocument.Create().LengthUnit);   // default loader LcOpRevitExport
        Assert.Equal(NwcLengthUnit.Feet, NwcDocument.Create(new NwcFileInfo { LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" }).LengthUnit);
    }
}

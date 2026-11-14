using Xunit;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Tests;

public class PartitionHeaderParserTests
{
    [Theory]
    [InlineData("a.rv")]       // no path pad; -1 ends at 20, so a 4-byte pad precedes the timestamp
    [InlineData("ab.rvt")]     // 2-byte path pad; the timestamp needs no pad
    [InlineData("Tower.rvt")]  // 3-byte path pad and the 4-byte pad
    public void Open_AlignedHeader_ReadsPathTimestampAndPlugin(string path)
    {
        var doc = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write(
            [SyntheticNwcFile.PartitionHeader(path, 1767225600, "LcNwcLoaderPlugin:lcldrevit")])));

        Assert.Equal(path, doc.FileInfo.SourcePath);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), doc.FileInfo.SourceTimestampUtc);
        Assert.Equal("LcNwcLoaderPlugin:lcldrevit", doc.FileInfo.LoaderPlugin);
    }

    [Fact]   // guard: 14cc597 also stops here
    public void Open_NonZeroPathPad_KeepsOnlyThePath()
    {
        var header = SyntheticNwcFile.PartitionHeader("ab.rvt", 1767225600, "LcNwcLoaderPlugin:lcldrevit");
        header[18] = 1;   // first pad byte after "ab.rvt" (8 + 4 + 6)

        var doc = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write([header])));

        Assert.Equal("ab.rvt", doc.FileInfo.SourcePath);
        Assert.Null(doc.FileInfo.SourceTimestampUtc);
        Assert.Null(doc.FileInfo.LoaderPlugin);
    }
}

using Xunit;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
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
            [SyntheticNwcFile.PartitionHeader(path, 1767225600, "LcNwcLoaderPlugin:lcldrevit", 123456789)])));

        Assert.Equal(path, doc.FileInfo.SourcePath);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), doc.FileInfo.SourceTimestampUtc);
        Assert.Equal(123456789L, doc.FileInfo.SourceByteSize);
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
        Assert.Null(doc.FileInfo.SourceByteSize);
        Assert.Null(doc.FileInfo.LoaderPlugin);
    }

    [Fact]   // the Revit NWC exporter's header: [i64 7][i32 -1][i32 -1][i64 0][i64 0][i32 13]"nwexportrevit" + pad
    public void Open_NullSourcePathHeader_ReadsLoaderAndUnit()
    {
        var doc = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write([SyntheticNwcFile.PartitionHeader(null, 0, "nwexportrevit")])));

        Assert.Null(doc.FileInfo.SourcePath);
        Assert.Null(doc.FileInfo.SourceTimestampUtc);
        Assert.Equal("nwexportrevit", doc.FileInfo.LoaderPlugin);
        Assert.Equal(NwcLengthUnit.Feet, doc.LengthUnit);
        Assert.True(doc.IsRevitSource);
    }

    [Fact]
    public void Save_NullSourcePath_RoundtripsAsNull()
    {
        var original = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write([SyntheticNwcFile.PartitionHeader(null, 0, "nwexportrevit")])));

        using var ms = new MemoryStream();
        original.Save(ms);
        ms.Position = 0;

        var roundtripped = NwcDocument.Open(ms);
        Assert.Null(roundtripped.FileInfo.SourcePath);
        Assert.Equal("nwexportrevit", roundtripped.FileInfo.LoaderPlugin);
    }

    [Fact]
    public void Save_BuilderWithoutFileInfo_ReopensWithNullSourcePath()
    {
        using var ms = new MemoryStream();
        new NwcDocumentBuilder().Build().Save(ms);
        ms.Position = 0;

        var roundtripped = NwcDocument.Open(ms);
        Assert.Null(roundtripped.FileInfo.SourcePath);
        Assert.Equal("LcOpRevitExport", roundtripped.FileInfo.LoaderPlugin);
    }

    [Fact]   // guard: only -1 is a null path, in the classifier and in the parser
    public void Open_SourcePathLengthMinus2_IsNotAHeader()
    {
        var header = SyntheticNwcFile.PartitionHeader(null, 0, "nwexportrevit");
        header[8] = 0xFE;   // path length -1 -> -2

        var doc = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write([header])));
        var parsed = PartitionHeaderParser.Parse(new LcRawStream { Index = 0, FileOffset = 0, CompressedLength = 0, GapBefore = 0, Data = header, SectionIndex = 0 });

        Assert.Null(doc.FileInfo.LoaderPlugin);
        Assert.Null(parsed.SourcePath);
        Assert.Null(parsed.LoaderPlugin);
    }

    [Fact]
    public void Save_SourceByteSize_RoundtripsSuccessfully()
    {
        var original = NwcDocument.Open(new MemoryStream(NwcContainerWriter.Write(
            [SyntheticNwcFile.PartitionHeader("model.rvt", 1767225600, "LcNwcLoaderPlugin:lcldrevit", 123456789)])));

        Assert.Equal(123456789L, original.FileInfo.SourceByteSize);

        using var ms = new MemoryStream();
        original.Save(ms);
        ms.Position = 0;

        var roundtripped = NwcDocument.Open(ms);
        Assert.Equal(123456789L, roundtripped.FileInfo.SourceByteSize);
        Assert.Equal("model.rvt", roundtripped.FileInfo.SourcePath);
        Assert.Equal("LcNwcLoaderPlugin:lcldrevit", roundtripped.FileInfo.LoaderPlugin);
    }
}

using NwcIO.Internal.Container;
using Xunit;

namespace NwcIO.Tests;

public class ContainerHeaderTests
{
    [Fact]
    public void Parse_Lichunk008Header_ParsesTokensCorrectly()
    {
        var raw = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lichunk-008\nremaining");
        var header = LcUStreamHeader.Parse(raw, 0);

        Assert.Equal(0, header.FileOffset);
        Assert.Equal(52, header.HeaderLength);
        Assert.Equal("LcUStream-V1.1", header.Format);
        Assert.Equal("C", header.Encoding);
        Assert.Equal("lichunk-008", header.Marker);
        Assert.Null(header.InternalVersion);
        Assert.Equal(5, header.Tokens.Count);
    }

    [Fact]
    public void Parse_NavisworksVersionHeader_ParsesVersionCorrectly()
    {
        var raw = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib\n");
        var header = LcUStreamHeader.Parse(raw, 0);

        Assert.Equal(0, header.FileOffset);
        Assert.Equal("utf8", header.Encoding);
        Assert.Equal("lizlib", header.Marker);
        Assert.Equal(448, header.InternalVersion);
    }

    [Fact]
    public void Parse_HeaderNotTerminated_ThrowsInvalidDataException()
    {
        var raw = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 without newline");
        Assert.Throws<InvalidDataException>(() => LcUStreamHeader.Parse(raw, 0));
    }

    [Fact]
    public void Parse_InvalidMagic_ThrowsInvalidDataException()
    {
        var raw = System.Text.Encoding.ASCII.GetBytes("#NotLcUStream line\n");
        Assert.Throws<InvalidDataException>(() => LcUStreamHeader.Parse(raw, 0));
    }

    [Fact]
    public void Open_BothHeaderStyles_PopulatesFileInfoFormatMetadataAndJsonExport()
    {
        // 1. Modern header: navisworks:448, utf8, lizlib
        var modernBytes = NwcContainerWriter.Write([], "#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib\n");
        var docModern = NwcDocument.Open(new MemoryStream(modernBytes));

        Assert.Equal(448, docModern.FileInfo.FormatVersion);
        Assert.Equal("utf8", docModern.FileInfo.Encoding);
        Assert.Equal("lizlib", docModern.FileInfo.ChunkMarker);

        using var jsonDoc = System.Text.Json.JsonDocument.Parse(docModern.ToJson());
        var fileEl = jsonDoc.RootElement.GetProperty("file");
        Assert.Equal(448, fileEl.GetProperty("formatVersion").GetInt32());
        Assert.Equal("utf8", fileEl.GetProperty("encoding").GetString());
        Assert.Equal("lizlib", fileEl.GetProperty("chunkMarker").GetString());

        // 2. Legacy header: C, lichunk-008, no format version
        var legacyBytes = NwcContainerWriter.Write([], "#LcUStream-V1.1 binary double release C lichunk-008\n");
        var docLegacy = NwcDocument.Open(new MemoryStream(legacyBytes));

        Assert.Null(docLegacy.FileInfo.FormatVersion);
        Assert.Equal("C", docLegacy.FileInfo.Encoding);
        Assert.Equal("lichunk-008", docLegacy.FileInfo.ChunkMarker);

        using var jsonLegacy = System.Text.Json.JsonDocument.Parse(docLegacy.ToJson());
        var fileLegacyEl = jsonLegacy.RootElement.GetProperty("file");
        Assert.False(fileLegacyEl.TryGetProperty("formatVersion", out _));
        Assert.Equal("C", fileLegacyEl.GetProperty("encoding").GetString());
        Assert.Equal("lichunk-008", fileLegacyEl.GetProperty("chunkMarker").GetString());
    }
}

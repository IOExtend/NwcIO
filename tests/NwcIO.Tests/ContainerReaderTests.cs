using System.IO.Compression;
using NwcIO.Internal.Container;
using Xunit;

namespace NwcIO.Tests;

public class ContainerReaderTests
{
    [Fact]
    public void Read_SyntheticBackToBackStreams_InflatesAndPreservesLengths()
    {
        using var ms = new MemoryStream();
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lichunk-008\n");
        ms.Write(headerBytes);

        var payload1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var comp1 = CompressZlib(payload1);
        ms.Write(comp1);

        var payload2 = System.Text.Encoding.UTF8.GetBytes("Hello Navisworks stream reader!");
        var comp2 = CompressZlib(payload2);
        ms.Write(comp2);

        var container = NwcContainerReader.Read(ms.ToArray());

        Assert.Single(container.Sections);
        Assert.Equal(2, container.Streams.Count);

        Assert.Equal(payload1, container.Streams[0].Data);
        Assert.Equal(headerBytes.Length, container.Streams[0].FileOffset);
        Assert.Equal(comp1.Length, container.Streams[0].CompressedLength);

        Assert.Equal(payload2, container.Streams[1].Data);
        Assert.Equal(headerBytes.Length + comp1.Length, container.Streams[1].FileOffset);
        Assert.Equal(comp2.Length, container.Streams[1].CompressedLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Read_LargeGaps_PreservesLaterStreamsSectionsAndGapAccounting(int trailingGap)
    {
        using var ms = new MemoryStream();
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lichunk-008\n");
        var payload1 = new byte[100];
        var payload2 = new byte[200];
        var payload3 = new byte[50];
        var gap = new byte[5000];

        ms.Write(headerBytes);
        ms.Write(CompressZlib(payload1));
        ms.Write(gap);
        ms.Write(CompressZlib(payload2));
        ms.Write(gap);
        ms.Write(headerBytes);
        ms.Write(CompressZlib(payload3));
        ms.Write(new byte[trailingGap]);

        var container = NwcContainerReader.Read(ms.ToArray());

        Assert.Equal(2, container.Sections.Count);
        Assert.Equal(3, container.Streams.Count);
        Assert.Equal(payload1, container.Streams[0].Data);
        Assert.Equal(payload2, container.Streams[1].Data);
        Assert.Equal(payload3, container.Streams[2].Data);
        Assert.Equal(0, container.Streams[0].SectionIndex);
        Assert.Equal(0, container.Streams[1].SectionIndex);
        Assert.Equal(1, container.Streams[2].SectionIndex);
        Assert.Equal(0, container.Streams[0].GapBefore);
        Assert.Equal(gap.Length, container.Streams[1].GapBefore);
        Assert.Equal(0, container.Streams[2].GapBefore);
        Assert.Equal(trailingGap, container.TrailingGap);
    }

    [Fact]
    public void Read_InvalidHeader_ThrowsInvalidDataException()
    {
        var invalid = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        Assert.Throws<InvalidDataException>(() => NwcContainerReader.Read(invalid));
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var outMs = new MemoryStream();
        using (var zlib = new ZLibStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return outMs.ToArray();
    }
}

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

    [Theory]
    [InlineData(1)]
    [InlineData(200000)]
    public void Read_CompleteZlibStream_PreservesPayloadAndLength(int payloadLength)
    {
        var payload = new byte[payloadLength];
        Array.Fill(payload, (byte)'A');
        var compressed = CompressZlib(payload);
        using var ms = new MemoryStream();
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n");
        ms.Write(headerBytes);
        ms.Write(compressed);

        var container = NwcContainerReader.Read(ms.ToArray());

        var stream = Assert.Single(container.Streams);
        Assert.Equal(payload, stream.Data);
        Assert.Equal(headerBytes.Length, stream.FileOffset);
        Assert.Equal(compressed.Length, stream.CompressedLength);
        Assert.Equal(0, container.TrailingGap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_TruncatedZlibStream_RejectsStreamAndAccountsForTrailingGap(bool truncateBody)
    {
        var payload = new byte[200000];
        Array.Fill(payload, (byte)'A');
        var compressed = CompressZlib(payload);
        var truncatedLength = truncateBody ? compressed.Length / 2 : compressed.Length - 4;
        using var ms = new MemoryStream();
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n");
        ms.Write(headerBytes);
        ms.Write(compressed, 0, truncatedLength);

        var container = NwcContainerReader.Read(ms.ToArray());

        Assert.Single(container.Sections);
        Assert.Empty(container.Streams);
        Assert.Equal(truncatedLength, container.TrailingGap);
    }

    [Fact]
    public void Read_InvalidHeader_ThrowsInvalidDataException()
    {
        var invalid = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        Assert.Throws<InvalidDataException>(() => NwcContainerReader.Read(invalid));
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(300_000)]
    public void Read_StreamLongerThanOneBlock_PreservesExactLengthAndFollowingStream(int payloadLength)
    {
        var payload = new byte[payloadLength];
        new Random(42).NextBytes(payload);
        var compressed = CompressZlib(payload);
        var nextPayload = System.Text.Encoding.UTF8.GetBytes("second stream");
        var nextCompressed = CompressZlib(nextPayload);

        using var ms = new MemoryStream();
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n");
        ms.Write(headerBytes);
        ms.Write(compressed);
        ms.Write(nextCompressed);

        var container = NwcContainerReader.Read(ms.ToArray());

        Assert.Equal(2, container.Streams.Count);
        Assert.Equal(payload, container.Streams[0].Data);
        Assert.Equal(headerBytes.Length, container.Streams[0].FileOffset);
        Assert.Equal(compressed.Length, container.Streams[0].CompressedLength);
        Assert.Equal((long)headerBytes.Length + compressed.Length, container.Streams[1].FileOffset);
        Assert.Equal(nextCompressed.Length, container.Streams[1].CompressedLength);
        Assert.Equal(nextPayload, container.Streams[1].Data);
        Assert.Equal(0, container.TrailingGap);
    }

    [Theory]
    [InlineData(8_182)]
    [InlineData(8_183)]
    [InlineData(8_184)]
    public void Read_TrailerAcrossInflateBlockBoundary_KeepsBothStreams(int payloadLength)
    {
        var payload = new byte[payloadLength];
        new Random(payloadLength).NextBytes(payload);
        var compressed = CompressZlib(payload);
        var nextPayload = System.Text.Encoding.UTF8.GetBytes("after boundary");
        var nextCompressed = CompressZlib(nextPayload);

        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        ms.Write(compressed);
        ms.Write(nextCompressed);

        var container = NwcContainerReader.Read(ms.ToArray());

        Assert.Equal(2, container.Streams.Count);
        Assert.Equal(payload, container.Streams[0].Data);
        Assert.Equal(compressed.Length, container.Streams[0].CompressedLength);
        Assert.Equal(nextPayload, container.Streams[1].Data);
    }

    [Fact]
    public void Read_ThreeIdenticalStreams_KeepsEachCompressedLength()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("same payload");
        var compressed = CompressZlib(payload);

        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        for (int i = 0; i < 3; i++)
        {
            ms.Write(compressed);
        }

        var container = NwcContainerReader.Read(ms.ToArray());

        Assert.Equal(3, container.Streams.Count);
        Assert.All(container.Streams, s =>
        {
            Assert.Equal(compressed.Length, s.CompressedLength);
            Assert.Equal(payload, s.Data);
        });
        Assert.Equal(0, container.TrailingGap);
    }

    [Fact]
    public void Read_MemoryStreamAtEnd_DoesNotReadFromTheStart()
    {
        var compressed = CompressZlib(System.Text.Encoding.UTF8.GetBytes("payload"));
        var file = FileBytes(compressed);
        using var stream = new MemoryStream(file);
        stream.Position = stream.Length;

        Assert.Throws<InvalidDataException>(() => NwcContainerReader.Read(stream));
    }

    [Fact]
    public void Read_SeekableStreamWithSpareCapacity_ReadsOnlyItsContent()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("stream payload");
        var compressed = CompressZlib(payload);
        var file = FileBytes(compressed);

        using var stream = new MemoryStream(file.Length + 1024);
        stream.Write(file);
        stream.Position = 0;

        var container = NwcContainerReader.Read(stream);

        Assert.Equal(payload, Assert.Single(container.Streams).Data);
    }

    private static byte[] FileBytes(byte[] compressedPayload)
    {
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        ms.Write(compressedPayload);
        return ms.ToArray();
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

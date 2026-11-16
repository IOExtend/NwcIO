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

    [Fact]
    public void Read_StreamBeyondPerStreamLimit_ThrowsInvalidDataException()
    {
        var payload = new byte[1 << 20];
        var bytes = ContainerWith(CompressZlib(payload));

        var ex = Assert.Throws<InvalidDataException>(
            () => NwcContainerReader.Read(bytes, 64 * 1024, 1L << 30));

        Assert.Contains("per-stream limit", ex.Message);
        Assert.Contains("offset", ex.Message);
    }

    [Fact]
    public void Read_StreamsBeyondTotalLimit_ThrowsInvalidDataException()
    {
        var payload = new byte[100_000];
        Array.Fill(payload, (byte)'A');
        var compressed = CompressZlib(payload);
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        ms.Write(compressed);
        ms.Write(compressed);

        var ex = Assert.Throws<InvalidDataException>(
            () => NwcContainerReader.Read(ms.ToArray(), 1L << 20, 150_000));

        Assert.Contains("total limit", ex.Message);
    }

    [Fact]
    public void Read_StreamAtExactLimit_IsAccepted()
    {
        var payload = new byte[4096];
        var bytes = ContainerWith(CompressZlib(payload));

        var container = NwcContainerReader.Read(bytes, payload.Length, payload.Length);

        Assert.Equal(payload, Assert.Single(container.Streams).Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Read_NonPositiveLimits_ThrowArgumentOutOfRange(long limit)
    {
        var payload = new byte[16];
        var bytes = ContainerWith(CompressZlib(payload));

        Assert.Throws<ArgumentOutOfRangeException>(() => NwcContainerReader.Read(bytes, limit, 1L << 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => NwcContainerReader.Read(bytes, 1L << 20, limit));
    }

    [Fact]
    public void Open_DecompressionLimit_ThrowsInvalidDataException()
    {
        var payload = new byte[1 << 20];
        var bytes = ContainerWith(CompressZlib(payload));

        var options = new NwcReaderOptions { MaxDecompressedStreamBytes = 64 * 1024 };

        Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(bytes), options));
    }

    [Fact]
    public void Open_TotalLimit_IsPassedToTheContainerReader()
    {
        var payload = new byte[100_000];
        Array.Fill(payload, (byte)'A');
        var compressed = CompressZlib(payload);
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        ms.Write(compressed);
        ms.Write(compressed);

        var options = new NwcReaderOptions { MaxDecompressedStreamBytes = 1L << 30, MaxTotalDecompressedBytes = 150_000 };

        var ex = Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(ms.ToArray()), options));
        Assert.Contains("total limit", ex.Message);
    }

    [Fact]
    public void Read_RejectedStreamsCountTowardTotalLimit()
    {
        var bomb = CompressZlibWithBadTrailer(100_000);
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        ms.Write(bomb);
        ms.Write(bomb);

        var ex = Assert.Throws<InvalidDataException>(
            () => NwcContainerReader.Read(ms.ToArray(), 1L << 20, 150_000));

        Assert.Contains("total limit", ex.Message);
    }

    [Fact]
    public void Read_RejectedStreamAfterGap_IsInflatedOnce()
    {
        var bomb = CompressZlibWithBadTrailer(100_000);
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("#LcUStream-V1.1 binary double release C lizlib\n"));
        ms.Write(new byte[] { 0xFF, 0xFF, 0xFF });
        ms.Write(bomb);

        var container = NwcContainerReader.Read(ms.ToArray(), 1L << 20, 150_000);

        Assert.Empty(container.Streams);
    }

    private static byte[] CompressZlibWithBadTrailer(int size)
    {
        var payload = new byte[size];
        Array.Fill(payload, (byte)'A');
        var compressed = CompressZlib(payload);
        compressed[^1] ^= 0xFF;
        return compressed;
    }

    private static byte[] ContainerWith(byte[] compressedPayload)
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

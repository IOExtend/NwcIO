using System.Buffers.Binary;
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
    public void Read_MissingZlibTrailerWithMatchingPayloadSuffix_RejectsStream()
    {
        // A final DEFLATE stored block whose payload ends with its own Adler-32 (0x00000100).
        const ushort payloadLength = 64_249;
        var compressed = new byte[7 + payloadLength];
        compressed[0] = 0x78;
        compressed[1] = 0x01;
        compressed[2] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(compressed.AsSpan(3), payloadLength);
        BinaryPrimitives.WriteUInt16LittleEndian(compressed.AsSpan(5), unchecked((ushort)~payloadLength));
        compressed[^5] = 254;
        compressed[^2] = 1;

        var complete = NwcContainerReader.Read(ContainerWith([.. compressed, 0, 0, 1, 0]));
        Assert.Equal(payloadLength, Assert.Single(complete.Streams).Data.Length);

        var truncated = NwcContainerReader.Read(ContainerWith(compressed));
        Assert.Empty(truncated.Streams);
        Assert.Equal(compressed.Length, truncated.TrailingGap);
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

    [Theory]
    [InlineData(8_180, false)]
    [InlineData(8_181, false)]
    [InlineData(8_182, false)]
    [InlineData(12_000, true)]
    public void Read_HuffmanStreamWithSplitOrRepeatedTrailer_PreservesExactBoundary(int payloadLength, bool repeatTrailer)
    {
        // Non-final stored block with zero bytes, followed by an empty final fixed-Huffman block.
        var compressed = new byte[13 + payloadLength];
        compressed[0] = 0x78;
        compressed[1] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(compressed.AsSpan(3), (ushort)payloadLength);
        BinaryPrimitives.WriteUInt16LittleEndian(compressed.AsSpan(5), unchecked((ushort)~payloadLength));
        compressed[^6] = 0x03;
        BinaryPrimitives.WriteUInt32BigEndian(compressed.AsSpan(compressed.Length - 4), ((uint)payloadLength << 16) | 1);
        byte[] gap = repeatTrailer ? compressed[^4..] : [];
        var next = CompressZlib("next"u8.ToArray());

        var container = NwcContainerReader.Read(ContainerWith([.. compressed, .. gap, .. next]));

        Assert.Equal(2, container.Streams.Count);
        Assert.Equal(new byte[payloadLength], container.Streams[0].Data);
        Assert.Equal(compressed.Length, container.Streams[0].CompressedLength);
        Assert.Equal(gap.Length, container.Streams[1].GapBefore);
        Assert.Equal("next"u8.ToArray(), container.Streams[1].Data);
        Assert.Equal(0, container.TrailingGap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Adler32_IncrementalAcrossVectorAndScalarLengths_MatchesZlib(bool maximumBytes)
    {
        var payload = new byte[100_003];
        if (maximumBytes)
        {
            Array.Fill(payload, byte.MaxValue);
        }
        else
        {
            new Random(27).NextBytes(payload);
        }

        uint a = 1, b = 0;
        NwcContainerReader.UpdateAdler32(payload.AsSpan(0, 257), ref a, ref b);
        NwcContainerReader.UpdateAdler32(payload.AsSpan(257), ref a, ref b);
        uint expected = BinaryPrimitives.ReadUInt32BigEndian(CompressZlib(payload).AsSpan()[^4..]);

        Assert.Equal(expected, (b << 16) | a);
    }

    [Fact]
    public void Read_VisibleMemoryStreamWithOffsetAndPosition_UsesTheSelectedRange()
    {
        byte[] payload = "selected range"u8.ToArray();
        byte[] file = ContainerWith(CompressZlib(payload));
        byte[] bytes = [1, 2, 3, .. file, 4, 5, 6];
        using var input = new MemoryStream(bytes, 2, file.Length + 1, writable: false, publiclyVisible: true);
        input.Position = 1;

        var container = NwcContainerReader.Read(input);

        Assert.Equal(payload, Assert.Single(container.Streams).Data);
        Assert.Equal(file.Length, container.FileLength);
        Assert.Equal(0, container.TrailingGap);
        Assert.Equal(input.Length, input.Position);
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
    public void Read_SeekableStreamAboveTwoGiB_PreservesLongOffsetsAndGap()
    {
        byte[] header = "#LcUStream-V1.1 binary double release C lizlib\n"u8.ToArray();
        byte[] payload = "after the gap"u8.ToArray();
        byte[] compressed = CompressZlib(payload);
        const long gap = (long)int.MaxValue + 16;
        using var input = new SegmentedInput(header, gap, compressed);

        var container = NwcContainerReader.Read(input);

        var stream = Assert.Single(container.Streams);
        Assert.Equal(gap + header.Length, stream.FileOffset);
        Assert.Equal(gap, stream.GapBefore);
        Assert.Equal(compressed.Length, stream.CompressedLength);
        Assert.Equal(payload, stream.Data);
        Assert.Equal(input.Length, container.FileLength);
        Assert.Equal(input.Length, input.Position);
        Assert.Equal(0, container.TrailingGap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_WithoutStreamCatalog_ReadsPayloadsBeforeClosingBackingStream(bool seekable)
    {
        var document = NwcDocument.Create(fileInfo: new NwcIO.Model.NwcFileInfo { SourcePath = "model.rvt" });
        using var input = new SegmentedInput(document.ToByteArray(), 0, [], seekable);

        var reopened = NwcDocument.Open(input, new NwcReaderOptions { KeepStreamCatalog = false });

        Assert.Equal("model.rvt", reopened.FileInfo.SourcePath);
        Assert.Empty(reopened.Container.Streams);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_CancelledWhileReadingContainer_ThrowsBeforeReadingAnotherBlock(bool seekable)
    {
        byte[] payload = new byte[256 * 1024];
        new Random(42).NextBytes(payload);
        using var cancellation = new CancellationTokenSource();
        using var input = new SegmentedInput(ContainerWith(CompressZlib(payload)), 0, [], seekable, cancellation.Cancel);

        Assert.Throws<OperationCanceledException>(() => NwcDocument.Open(input, null, null, null, cancellation.Token));
        Assert.Equal(1, input.ReadCount);
    }

    [Fact]
    public void Read_MemoryStreamAtEnd_DoesNotReadFromTheStart()
    {
        var compressed = CompressZlib(System.Text.Encoding.UTF8.GetBytes("payload"));
        var file = ContainerWith(compressed);
        using var stream = new MemoryStream(file);
        stream.Position = stream.Length;

        Assert.Throws<InvalidDataException>(() => NwcContainerReader.Read(stream));
    }

    [Fact]
    public void Read_SeekableStreamWithSpareCapacity_ReadsOnlyItsContent()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("stream payload");
        var compressed = CompressZlib(payload);
        var file = ContainerWith(compressed);

        using var stream = new MemoryStream(file.Length + 1024);
        stream.Write(file);
        stream.Position = 0;

        var container = NwcContainerReader.Read(stream);

        Assert.Equal(payload, Assert.Single(container.Streams).Data);
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
        ms.Write(bomb); // the inflater rejects the checksum on its last read, so each rejected stream charges its earlier 64 KiB chunks

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

    private sealed class SegmentedInput(
        byte[] prefix, long gap, byte[] suffix, bool seekable = true, Action? afterRead = null) : Stream
    {
        public int ReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => seekable ? prefix.Length + gap + suffix.Length : throw new NotSupportedException();
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            long suffixStart = prefix.Length + gap;
            int read;
            if (Position < prefix.Length)
            {
                read = Math.Min(count, prefix.Length - (int)Position);
                prefix.AsSpan((int)Position, read).CopyTo(buffer.AsSpan(offset));
            }
            else if (Position < suffixStart)
            {
                read = (int)Math.Min(count, suffixStart - Position);
                buffer.AsSpan(offset, read).Clear();
            }
            else
            {
                int start = (int)(Position - suffixStart);
                read = Math.Min(count, suffix.Length - start);
                suffix.AsSpan(start, read).CopyTo(buffer.AsSpan(offset));
            }

            Position += read;
            afterRead?.Invoke();
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}

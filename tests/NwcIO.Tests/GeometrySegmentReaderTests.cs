using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using Xunit;

namespace NwcIO.Tests;

public class GeometrySegmentReaderTests
{
    // Compressed lengths are arbitrary here; only the TOC sizes must match them.
    private static LcRawStream Chunk(byte[] data, int compressedLength) => new()
    {
        Index = 0,
        FileOffset = 0,
        CompressedLength = compressedLength,
        GapBefore = 0,
        Data = data,
        SectionIndex = 0,
    };

    // [N][ch][ch x size][N x len], zero padded to a multiple of 8 when padTo8.
    private static LcRawStream Toc(int n, int[] sizes, int[] lengths, bool padTo8)
    {
        int body = 8 + (4 * sizes.Length) + (4 * lengths.Length);
        var data = new byte[padTo8 ? (body + 7) / 8 * 8 : body];
        BinaryPrimitives.WriteInt32LittleEndian(data, n);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), sizes.Length);
        int pos = 8;
        foreach (int value in sizes.Concat(lengths))
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(pos), value);
            pos += 4;
        }

        return Chunk(data, data.Length);
    }

    private static LcRawStream Patched(LcRawStream toc, int offset, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return toc with { Data = FormatVectors.Patched(toc.Data, offset, bytes) };
    }

    [Fact]
    public void Read_ThreeRecordsAcrossTwoChunks_DecodesInOrder()
    {
        byte[] block = [.. FormatVectors.R1, .. FormatVectors.R2, .. FormatVectors.R3];      // 392 bytes; R2 crosses byte 200
        var records = GeometrySegmentReader.Read([Chunk(block[..200], 82), Chunk(block[200..], 119)], Toc(3, [82, 119], [120, 152, 120], padTo8: true));
        Assert.Equal(3, records.Length);
        Assert.Equal([2, 3, 4], records.Select(r => r.PrimitiveCount));
        Assert.Equal([false, false, true], records.Select(r => r.IsLines));
        Assert.Equal([0, 1, 2, 2, 1, 3, 4, 5, 6], records[1].Indices);
    }

    // TOC defects (Toc() emits [3][2][82][119][120][152][120] + 4 zero bytes = 32 bytes)
    [Theory]
    [InlineData("pad")]        // byte 28 = 1
    [InlineData("ch")]         // ch = 3
    [InlineData("size")]       // size[0] = 83
    [InlineData("sum")]        // len[2] = 128
    [InlineData("align")]      // lens 124, 148, 120 (sum unchanged, start not 8-aligned)
    [InlineData("unpadded")]   // TOC of 28 bytes
    [InlineData("hugeN")]      // N = 0x7FFFFFFF
    public void Read_TocDefect_Throws(string defect)
    {
        byte[] block = [.. FormatVectors.R1, .. FormatVectors.R2, .. FormatVectors.R3];
        var good = Toc(3, [82, 119], [120, 152, 120], padTo8: true);
        var toc = defect switch
        {
            "pad" => Patched(good, 28, 1),
            "ch" => Patched(good, 4, 3),
            "size" => Patched(good, 8, 83),
            "sum" => Patched(good, 24, 128),
            "align" => Toc(3, [82, 119], [124, 148, 120], padTo8: true),
            "unpadded" => Toc(3, [82, 119], [120, 152, 120], padTo8: false),
            "hugeN" => Patched(good, 0, 0x7FFFFFFF),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        var ex = Assert.Throws<InvalidDataException>(() => GeometrySegmentReader.Read([Chunk(block[..200], 82), Chunk(block[200..], 119)], toc));
        Assert.StartsWith("LcOpNwdGeometry TOC", ex.Message);
    }

    [Fact]
    public void Read_EightBitPaletteFile_SelectsEightBits()
    {
        var r = GeometrySegmentReader.Read([Chunk([.. FormatVectors.R1, .. FormatVectors.R5], 1)], Toc(2, [1], [120, 128], padTo8: true));
        Assert.Equal(0.707107, r[1].Normals![0].X, 6);
    }

    [Fact]
    public void Read_RecordsNeedingDifferentWidths_ThrowsLaterError()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GeometrySegmentReader.Read([Chunk([.. FormatVectors.R2, .. FormatVectors.R5], 1)], Toc(2, [1], [152, 128], padTo8: true)));
        Assert.StartsWith("LcOpNwdGeometry record 1: ", ex.Message);   // 16 fails at 0-based record 1, 8 at record 0
    }

    [Fact]
    public void Read_MoreThan5000Chunks_ReadsEveryRecord()
    {
        // 6000 chunks, each one R1 (compressed length 1); TOC [6000][6000][6000 x 1][6000 x 120] (48,008 bytes)
        var chunks = Enumerable.Range(0, 6000).Select(_ => Chunk(FormatVectors.R1, 1)).ToArray();
        var toc = Toc(6000, Enumerable.Repeat(1, 6000).ToArray(), Enumerable.Repeat(120, 6000).ToArray(), padTo8: true);
        Assert.Equal(48_008, toc.Data.Length);
        var records = GeometrySegmentReader.Read(chunks, toc);
        Assert.Equal(6000, records.Length);
    }

    [Fact]
    public void Read_RecordSpanningThreeChunks_Decodes()
    {
        var r2 = FormatVectors.R2;
        var records = GeometrySegmentReader.Read([Chunk(r2[..50], 1), Chunk(r2[50..100], 1), Chunk(r2[100..], 1)], Toc(1, [1, 1, 1], [152], padTo8: true));
        Assert.Equal([0, 1, 2, 2, 1, 3, 4, 5, 6], records.Single().Indices);
    }

    [Fact]
    public void Read_ChunkBoundaryAtRecordStart_Decodes()
    {
        var records = GeometrySegmentReader.Read([Chunk(FormatVectors.R1, 1), Chunk(FormatVectors.R3, 1)], Toc(2, [1, 1], [120, 120], padTo8: true));
        Assert.Equal([false, true], records.Select(r => r.IsLines));
    }
}

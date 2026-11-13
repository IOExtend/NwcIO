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

    // A class-94 record that parses under both widths (312 bytes): R1's head with p1 = 32 over its four positions, VMAP 03 9B,
    // no UV, then [102][100][-32][pal = 32] (identity form) and the palette at 96. B = 8 reads 96 palette bytes, then at 192
    // [0][1][7][1][L = 104][S = 104] and 104 corner tokens at 216: 04, 71 x 80 and the bytes at 288..307. B = 16 reads 192
    // palette bytes, then at 288 [0][1][6][1][L = 32][S = 0]: one strip over the 32 render vertices.
    private static byte[] BuildBothWidths()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(FormatVectors.R1, 0, 16);
            w.Write(32);
            w.Write(FormatVectors.R1, 20, 52);
            w.Write(new byte[] { 0x03, 0x9B, 0, 0 });
            foreach (int value in new[] { 0, 102, 100, -32, 32 })
            {
                w.Write(value);
            }

            w.Write(new byte[96]);
            foreach (int value in new[] { 0, 1, 7, 1, 104 << 25, 104 })
            {
                w.Write(value);
            }

            w.Write((byte)0x04);
            w.Write(Enumerable.Repeat((byte)0x80, 71).ToArray());
            foreach (int value in new[] { 0, 1, 6, 1, 32 << 26, 0 })
            {
                w.Write(value);
            }
        }

        return stream.ToArray();
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
    [InlineData("pad", "pad byte is not zero at +28")]                                    // byte 28 = 1
    [InlineData("ch", "chunk count does not match the chunk streams at +4")]              // ch = 3
    [InlineData("size", "chunk size does not match the compressed length at +8")]         // size[0] = 83
    [InlineData("sum", "record lengths do not sum to the inflated chunk length")]         // len[2] = 128
    [InlineData("align", "record length is not a positive multiple of 8 at +16")]         // lens 124, 148, 120 (sum unchanged, start not 8-aligned)
    [InlineData("zeroLength", "record length is not a positive multiple of 8 at +16")]    // len[0] = 0
    [InlineData("unpadded", "length is not 8 + 4ch + 4N rounded up to 8")]                // TOC of 28 bytes
    [InlineData("hugeN", "length is not 8 + 4ch + 4N rounded up to 8")]                   // N = 0x7FFFFFFF
    [InlineData("negativeN", "record count is negative at +0")]                           // N = -1
    [InlineData("short", "header passes the end at +0")]                                  // TOC of 4 bytes
    public void Read_TocDefect_Throws(string defect, string rule)
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
            "zeroLength" => Patched(good, 16, 0),
            "unpadded" => Toc(3, [82, 119], [120, 152, 120], padTo8: false),
            "hugeN" => Patched(good, 0, 0x7FFFFFFF),
            "negativeN" => Patched(good, 0, -1),
            "short" => Chunk(good.Data[..4], 4),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        var ex = Assert.Throws<InvalidDataException>(() => GeometrySegmentReader.Read([Chunk(block[..200], 82), Chunk(block[200..], 119)], toc));
        Assert.Equal("LcOpNwdGeometry TOC: " + rule, ex.Message);
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
    public void Read_BothWidthsFailingAtOneRecord_ReportsSixteenBits()
    {
        // R5 + 8 zero bytes: B = 16 fails in its palette, B = 8 at its end
        byte[] record = [.. FormatVectors.R5, .. new byte[8]];
        var ex = Assert.Throws<InvalidDataException>(() => GeometrySegmentReader.Read([Chunk(record, 1)], Toc(1, [1], [136], padTo8: true)));
        Assert.Equal("LcOpNwdGeometry record 0: unused packed bit is set at +96", ex.Message);
    }

    [Fact]
    public void Read_RecordValidUnderBothWidths_DecodesSixteenBits()
    {
        var record = BuildBothWidths();
        Assert.Equal(3 * 102, GeometryRecordParser.Parse(record, 8).Indices.Length);
        var decoded = GeometrySegmentReader.Read([Chunk(record, 1)], Toc(1, [1], [record.Length], padTo8: true)).Single();
        Assert.Equal(3 * 30, decoded.Indices.Length);
        Assert.Equal(GeometryRecordParser.Parse(record, 16).Normals, decoded.Normals);
    }

    [Fact]
    public void Read_EmptyChunks_AreSkipped()
    {
        var first = GeometrySegmentReader.Read([Chunk([], 1), Chunk(FormatVectors.R1, 2)], Toc(1, [1, 2], [120], padTo8: true));
        Assert.Equal([0, 1, 2, 2, 1, 3], first.Single().Indices);

        var r2 = FormatVectors.R2;
        var crossing = GeometrySegmentReader.Read([Chunk(r2[..50], 1), Chunk([], 2), Chunk(r2[50..], 3)], Toc(1, [1, 2, 3], [152], padTo8: true));
        Assert.Equal([0, 1, 2, 2, 1, 3, 4, 5, 6], crossing.Single().Indices);
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

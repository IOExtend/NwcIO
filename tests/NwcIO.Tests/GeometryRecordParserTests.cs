using System.Buffers.Binary;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class GeometryRecordParserTests
{
    private static void AssertR1(GeometryRecord g)
    {
        Assert.False(g.IsLines);
        Assert.Equal([new Vector3d(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)], g.Vertices);
        Assert.Equal([0, 1, 2, 2, 1, 3], g.Indices);
        Assert.Empty(g.LineIndices);
        Assert.All(g.Normals!, n => Assert.Equal(new Vector3d(0, 0, 1), n));
        Assert.Equal(new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)), g.Bounds);
        Assert.Equal(2, g.PrimitiveCount);
    }

    private static byte[] Vector(string name) => name switch
    {
        "R1" => FormatVectors.R1,
        "R2" => FormatVectors.R2,
        "R3" => FormatVectors.R3,
        "R4" => FormatVectors.R4,
        "R5" => FormatVectors.R5,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // R1q (168 bytes) = R1[0..72) + i32 102, 61, -4; f32 0,0,1,1; i32 0x0808, 2; u32 0x0000FFFF; bytes 01 80 F0 00 00 00 00 00
    //                 + R1[76..120) with its first int 102 -> 103 + 4 zero bytes
    private static byte[] BuildR1q()
    {
        var r1 = FormatVectors.R1;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(r1, 0, 72);
            w.Write(102);
            w.Write(61);
            w.Write(-4);
            w.Write(0f);
            w.Write(0f);
            w.Write(1f);
            w.Write(1f);
            w.Write(0x0808);
            w.Write(2);
            w.Write(0x0000FFFFu);
            w.Write(Convert.FromHexString("0180F00000000000"));
            w.Write(103);
            w.Write(r1, 80, 40);
            w.Write(0);
        }

        return stream.ToArray();
    }

    private static byte[] Words(uint[] words)
    {
        var data = new byte[4 * words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4 * i), words[i]);
        }

        return data;
    }

    [Theory]
    [InlineData(16)]
    [InlineData(8)]
    public void Parse_R1AxisQuad_DecodesTrianglesAndAxisNormals(int bits) => AssertR1(GeometryRecordParser.Parse(FormatVectors.R1, bits));

    // REC Normals: D0 +X, D1 -X, D2 +Y, D3 -Y, D4 +Z, D5 -Z; R1 bytes 92..95 are its four NTOK codes
    [Theory]
    [InlineData("D0D0D0D0", 1, 0, 0)]
    [InlineData("D1D1D1D1", -1, 0, 0)]
    [InlineData("D2D2D2D2", 0, 1, 0)]
    [InlineData("D3D3D3D3", 0, -1, 0)]
    [InlineData("D4D4D4D4", 0, 0, 1)]
    [InlineData("D5D5D5D5", 0, 0, -1)]
    public void Parse_R1AxisCode_MapsToAxis(string codes, double x, double y, double z)
    {
        var g = GeometryRecordParser.Parse(FormatVectors.Patched(FormatVectors.R1, 92, Convert.FromHexString(codes)), 16);
        Assert.All(g.Normals!, n => Assert.Equal(new Vector3d(x, y, z), n));
    }

    [Fact]
    public void Parse_R2VmapPaletteAndCornerList_DecodesThreeStrips()
    {
        var g = GeometryRecordParser.Parse(FormatVectors.R2, 16);
        Assert.Equal([new Vector3d(0, 0, 0), new(0, 1, 0), new(1, 0, 0), new(1, 1, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)], g.Vertices);
        Assert.Equal([0, 1, 2, 2, 1, 3, 4, 5, 6], g.Indices); // parity restarts per strip: second triangle is (2,1,3)
        for (int r = 0; r < 4; r++)
        {
            Assert.Equal(new Vector3d(0, 0, -1), g.Normals![r]);
        }

        for (int r = 4; r < 7; r++)
        {
            Assert.Equal(0.57735, g.Normals![r].X, 5);
            Assert.Equal(0.57735, g.Normals[r].Y, 5);
            Assert.Equal(0.57735, g.Normals[r].Z, 5);
        }

        Assert.Equal(new BoundingBox3d(new(0, 0, 0), new(1, 1, 1)), g.Bounds);
        Assert.Equal(3, g.PrimitiveCount);
    }

    [Fact]
    public void Parse_R2WithEightBitNormals_Throws() => Assert.Throws<InvalidDataException>(() => GeometryRecordParser.Parse(FormatVectors.R2, 8));

    [Fact]
    public void Parse_R3ClosedPolyline_DecodesSegments()
    {
        var g = GeometryRecordParser.Parse(FormatVectors.R3, 16);
        Assert.True(g.IsLines);
        Assert.Equal([new Vector3d(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], g.Vertices);
        Assert.Equal([0, 1, 1, 2, 2, 3, 4, 5], g.LineIndices); // no implicit closing segment
        Assert.Empty(g.Indices);
        Assert.Null(g.Normals);
        Assert.Equal(4, g.PrimitiveCount);
        Assert.Equal(new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)), g.Bounds);
    }

    [Fact]
    public void Parse_R4RawUvSlot_ShiftsNormalIdAndDecodesAsR1()
    {
        Assert.Equal(160, FormatVectors.R4.Length);
        AssertR1(GeometryRecordParser.Parse(FormatVectors.R4, 16));
    }

    [Fact]
    public void Parse_QuantisedUvSlot_DecodesAsR1() => AssertR1(GeometryRecordParser.Parse(BuildR1q(), 16));

    // R1q header: w at +100, m at +104 (RS §7 UV quantised)
    [Theory]
    [InlineData(100, "08000000")] // bv = 0
    [InlineData(100, "08080100")] // w >> 16 != 0
    [InlineData(104, "00000000")] // m = 0
    [InlineData(104, "05000000")] // m > p1
    public void Parse_QuantisedUvHeaderDefect_Throws(int offset, string bytes)
        => Assert.Throws<InvalidDataException>(() => GeometryRecordParser.Parse(FormatVectors.Patched(BuildR1q(), offset, Convert.FromHexString(bytes)), 16));

    // REC Triangles: S > p1. R1 with S = 4 at +116, token 03 (-> [0,1,2,3]) and 7 zero pad bytes (128 bytes)
    [Fact]
    public void Parse_CornerCountNotAboveP1_Throws()
        => Assert.Throws<InvalidDataException>(() => GeometryRecordParser.Parse([.. FormatVectors.R1[..116], 4, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0], 16));

    [Fact]
    public void Parse_R5EightBitPalette_DecodesDiagonalNormal()
    {
        var g = GeometryRecordParser.Parse(FormatVectors.R5, 8);
        Assert.All(g.Normals!, n =>
        {
            Assert.Equal(0.707107, n.X, 6);
            Assert.Equal(-0.707107, n.Y, 6);
            Assert.Equal(0, n.Z, 12);
        });
        Assert.Throws<InvalidDataException>(() => GeometryRecordParser.Parse(FormatVectors.R5, 16));
    }

    // RS §12 mutation table: (vector, byte offset, new little-endian bytes as hex)
    [Theory]
    [InlineData("R1", 92, "D6")]
    [InlineData("R1", 92, "C0")]                // undefined NTOK lead
    [InlineData("R1", 112, "01000040")]         // unused packed bit
    [InlineData("R1", 76, "67000000")]          // N id 103: id order
    [InlineData("R1", 4, "60000000")]           // class 96
    [InlineData("R1", -1, "0000000000000000")]  // -1 = append: end != TOC length
    [InlineData("R2", 122, "01")]               // non-zero pad4
    [InlineData("R2", 84, "D5")]                // axis code outside N
    [InlineData("R2", 150, "47")]               // back-reference d=7 > n=4
    [InlineData("R3", 63, "03")]                // F0 literal >= next
    [InlineData("R3", 84, "03160000")]          // colour flag 5635
    public void Parse_Mutation_Throws(string vector, int offset, string bytes)
    {
        var record = FormatVectors.Patched(Vector(vector), offset, Convert.FromHexString(bytes));
        Assert.Throws<InvalidDataException>(() => GeometryRecordParser.Parse(record, 16));
    }

    // Further grammar rules of RS §7, as R1/R2/R3/R4 byte edits; the first three are crafted counts (Review Focus 2)
    [Theory]
    [InlineData("R1", 16, "FFFFFF7F")]  // p1 = int.MaxValue: VMAP count beyond 64 x remaining bytes
    [InlineData("R1", 20, "FEFFFF7F")]  // p2 = 2147483646: positions past the end
    [InlineData("R2", 144, "FFFFFF7F")] // S = int.MaxValue
    [InlineData("R1", 84, "FDFFFFFF")]  // N count -3 != -p1
    [InlineData("R3", 88, "FBFFFFFF")]  // C count -5 != -p1
    [InlineData("R1", 104, "20000000")] // P bits = 32
    [InlineData("R3", 116, "00000043")] // class 95 L = [4,3]: ΣL != p1
    [InlineData("R3", 116, "00000041")] // class 95 L = [4,1]: L < 2
    [InlineData("R4", 84, "03000000")]  // raw UV fc odd
    [InlineData("R4", 84, "00000000")]  // raw UV fc = 0
    [InlineData("R1", 20, "0D000000")]  // p2 % 3 != 0
    [InlineData("R1", 16, "03000000")]  // p1 < npos
    [InlineData("R1", 16, "FCFFFFFF")]  // negative p1 (quantised positions: never seen)
    [InlineData("R1", 112, "00000020")] // L = [2] < 3 for class 94
    [InlineData("R1", 112, "00000050")] // L = [5]: ΣL != corner count
    [InlineData("R1", 108, "02000000")] // second P count differs
    [InlineData("R1", 104, "00000000")] // P bits = 0
    [InlineData("R1", 116, "FFFFFFFF")] // S < 0
    [InlineData("R1", 4, "5F000000")]   // class 95 with an N slot: slot set
    [InlineData("R4", 80, "05000000")]  // UV count neither p1 nor -p1
    [InlineData("R3", 92, "00000000")]  // colour palette size 0
    public void Parse_GrammarViolation_Throws(string vector, int offset, string bytes)
    {
        var record = FormatVectors.Patched(Vector(vector), offset, Convert.FromHexString(bytes));
        Assert.Throws<InvalidDataException>(() => GeometryRecordParser.Parse(record, 16));
    }

    [Theory]
    [InlineData("0480", 6, 5, false, new[] { 0, 1, 2, 3, 4, 4 })]
    [InlineData("0242", 4, 3, false, new[] { 0, 1, 2, 1 })]
    [InlineData("D0D1D5", 3, 0, true, new[] { -1, -2, -6 })]
    public void DecodeTokens_Vector_ReturnsValues(string hex, int count, int fresh, bool axis, int[] expected)
    {
        int pos = 0;
        Assert.Equal(expected, GeometryRecordParser.DecodeTokens(Convert.FromHexString(hex), ref pos, count, fresh, axis));
        Assert.Equal(hex.Length / 2, pos);
    }

    [Fact]
    public void DecodeTokens_EFormDistance64_ReturnsValues()
    {
        int pos = 0;
        Assert.Equal([.. Enumerable.Range(0, 65), 1], GeometryRecordParser.DecodeTokens(Convert.FromHexString("3F00E040"), ref pos, 66, 65, false));
        Assert.Equal(4, pos);
    }

    [Fact]
    public void DecodeTokens_Distance4096_NeedsLiteral()
    {
        byte[] data = [.. Enumerable.Repeat((byte)0x3F, 64), 0x00, 0xF0, 0x00, 0x01];
        int pos = 0;
        Assert.Equal([.. Enumerable.Range(0, 4097), 1], GeometryRecordParser.DecodeTokens(data, ref pos, 4098, 4097, false));
        Assert.Equal(data.Length, pos);
    }

    [Theory]
    [InlineData("D0D1D5", 3, 0, false)]
    [InlineData("0040", 2, 1, false)]
    [InlineData("80", 1, 0, false)]
    [InlineData("01", 1, 2, false)]
    [InlineData("00", 1, 2, false)]
    [InlineData("00F1", 2, 1, true)]
    [InlineData("00FF", 2, 1, true)]
    [InlineData("00C0", 2, 1, true)]
    [InlineData("00DF", 2, 1, true)]
    [InlineData("00D6", 2, 1, true)]
    [InlineData("0081", 2, 1, false)]   // repeat passes count
    [InlineData("00E000", 2, 1, false)] // E form d = 0
    public void DecodeTokens_Invalid_Throws(string hex, int count, int fresh, bool axis)
    {
        var data = Convert.FromHexString(hex);
        Assert.Throws<InvalidDataException>(() =>
        {
            int pos = 0;
            GeometryRecordParser.DecodeTokens(data, ref pos, count, fresh, axis);
        });
    }

    [Theory]
    [InlineData(new uint[] { 0x6D800000 }, 3, 3, new[] { 3, 3, 3 })]
    [InlineData(new uint[] { 0x49E649E6, 0x49E60000 }, 3, 16, new[] { 18918, 18918, 18918 })]
    [InlineData(new uint[] { 0x7FFF0000, 0 }, 3, 16, new[] { 32767, 0, 0 })]
    [InlineData(new uint[] { 0x80010000, 0 }, 3, 16, new[] { 32769, 0, 0 })]
    [InlineData(new uint[] { 0x7F810000 }, 3, 8, new[] { 127, 129, 0 })]
    [InlineData(new uint[] { 0x80000000 }, 3, 8, new[] { 128, 0, 0 })]
    public void DecodePacked_Vector_ReturnsValues(uint[] words, int count, int width, int[] expected)
    {
        int pos = 0;
        Assert.Equal(expected, GeometryRecordParser.DecodePacked(Words(words), ref pos, count, width));
        Assert.Equal(4 * words.Length, pos);
    }

    [Fact]
    public void DecodePacked_NonZeroTailBit_Throws()
    {
        var data = Words([0x6D800001]);
        Assert.Throws<InvalidDataException>(() =>
        {
            int pos = 0;
            GeometryRecordParser.DecodePacked(data, ref pos, 3, 3);
        });
    }

    [Theory]
    [InlineData(18918, 18918, 18918, 16, 0.57735, 0.57735, 0.57735)]
    [InlineData(32767, 0, 0, 16, 1, 0, 0)]
    [InlineData(32769, 0, 0, 16, -1, 0, 0)]
    [InlineData(0, 0, 0, 16, 0, 0, 0)] // no normal
    [InlineData(127, 129, 0, 8, 0.707107, -0.707107, 0)]
    [InlineData(128, 0, 0, 8, -1, 0, 0)]
    public void DecodePaletteEntry_Vector_ReturnsUnitOrZero(int x, int y, int z, int bits, double ex, double ey, double ez)
    {
        var n = GeometryRecordParser.DecodePaletteEntry(x, y, z, bits);
        Assert.Equal(ex, n.X, 5);
        Assert.Equal(ey, n.Y, 5);
        Assert.Equal(ez, n.Z, 5);
    }
}

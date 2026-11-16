using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class TransformParserTests
{
    [Fact]
    public void TransformLinkParser_ReadsTransformIndices()
    {
        var data = new byte[8 + 4 * 3];
        BinaryPrimitives.WriteInt32LittleEndian(data, TransformStreamFormat.LinksMagic);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 3);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 2);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), -1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 0);

        var links = TransformLinkParser.TryParse(RawStream(data));

        Assert.Equal([2, -1, 0], links);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void TransformLinkParser_TruncatedOrNegativeCount_ReturnsNull(int count)
    {
        var data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data, TransformStreamFormat.LinksMagic);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), count);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);

        Assert.Null(TransformLinkParser.TryParse(RawStream(data)));
    }

    private static LcRawStream RawStream(byte[] data) => new()
    {
        Index = 0,
        FileOffset = 0,
        CompressedLength = data.Length,
        GapBefore = 0,
        Data = data,
        SectionIndex = 0,
    };

    [Fact]
    public void Parse_SyntheticTransformStream_DecodesPoseAndBBox()
    {
        using var ms = new MemoryStream();

        // Total count header
        WriteInt32(ms, 1);

        // Record: id=100, tag=13 (TagTransform)
        WriteInt32(ms, 100);
        WriteInt32(ms, 13);

        // Child tag 15 (TagPose): id=101, tag=15
        WriteInt32(ms, 101);
        WriteInt32(ms, 15);
        WriteDouble(ms, 100.0); // Tx
        WriteDouble(ms, 200.0); // Ty
        WriteDouble(ms, 300.0); // Tz
        WriteDouble(ms, 0.0);   // Qx
        WriteDouble(ms, 0.0);   // Qy
        WriteDouble(ms, 0.7071067811865475); // Qz
        WriteDouble(ms, 0.7071067811865476); // Qw

        // Child tag 93 (TagBoundingBox): id=102, tag=93
        WriteInt32(ms, 102);
        WriteInt32(ms, 93);
        WriteFloat(ms, -10.0f); // minX
        WriteFloat(ms, -20.0f); // minY
        WriteFloat(ms, -30.0f); // minZ
        WriteFloat(ms, 10.0f);  // maxX
        WriteFloat(ms, 20.0f);  // maxY
        WriteFloat(ms, 30.0f);  // maxZ

        var transforms = TransformParser.Parse(ms.ToArray());

        Assert.Single(transforms);
        var t = transforms[0];

        Assert.Equal(100, t.Id);
        Assert.Equal(new Vector3d(100.0, 200.0, 300.0), t.Translation);
        Assert.True(t.Rotation.IsNormalized());

        Assert.NotNull(t.BoundingBox);
        Assert.Equal(-10.0, t.BoundingBox.Value.Min.X, 4);
        Assert.Equal(10.0, t.BoundingBox.Value.Max.X, 4);

        // Check 4x4 matrix
        double[] matrix = t.ToMatrix4x4();
        Assert.Equal(16, matrix.Length);
        Assert.Equal(100.0, matrix[12]);
        Assert.Equal(200.0, matrix[13]);
        Assert.Equal(300.0, matrix[14]);
        Assert.Equal(1.0, matrix[15]);
    }

    private static void WriteInt32(MemoryStream ms, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteDouble(MemoryStream ms, double value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteFloat(MemoryStream ms, float value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buf, value);
        ms.Write(buf);
    }
}
